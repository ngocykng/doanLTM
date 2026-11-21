using System.Text.RegularExpressions;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BilingualVocabApi.Models;

namespace BilingualVocabApi.Services;

public class AiFallbackService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly IConfiguration _configuration;

    public AiFallbackService(IConfiguration configuration) => _configuration = configuration;

    public async Task<List<VocabCard>> GenerateVocabAsync(
        string topic,
        string level,
        string topicId,
        AiConfig config,
        IEnumerable<string>? excludeWords = null,
        int batchIndex = 1,
        int startIndex = 0,
        CancellationToken cancellationToken = default)
    {
        var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? _configuration["GEMINI_API_KEY"];
        if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("Chưa cấu hình GEMINI_API_KEY trong file .env.");

        var preferredModel = string.IsNullOrWhiteSpace(config.Model_Name)
            || config.Model_Name.Equals("gemini-2.5-flash", StringComparison.OrdinalIgnoreCase)
            ? "gemini-3.8-flash"
            : config.Model_Name.Trim();
        var models = new[] { preferredModel, "gemini-3.5-flash", "gemini-3.1-flash-lite-preview", "gemini-2.5-flash" }
            .Where(model => !string.IsNullOrWhiteSpace(model))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var excluded = excludeWords?.Where(word => !string.IsNullOrWhiteSpace(word)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new();
        var excludePrompt = excluded.Count == 0
            ? string.Empty
            : $" Không được dùng lại các từ đã có: {string.Join(", ", excluded.Take(100))}.";
        var prompt = $"Tạo đúng 15 flashcard từ vựng tiếng Anh mới về chủ đề '{topic}', trình độ CEFR {level}, cho đợt #{batchIndex}. " +
            "Chỉ trả JSON array, không markdown. Mỗi object có các trường: word_en, ipa_en, meaning_vi, example_en, example_meaning_vi, difficulty. " +
            $"Nghĩa và câu ví dụ phải chính xác, phù hợp trình độ. Không lặp từ.{excludePrompt}";
        var requestBody = JsonSerializer.Serialize(new
        {
            contents = new[] { new { parts = new[] { new { text = prompt } } } },
            generationConfig = new
            {
                responseMimeType = "application/json",
                temperature = Math.Clamp(config.Temperature, 0, 2),
                maxOutputTokens = Math.Clamp(config.Max_Output_Tokens, 256, 8192)
            }
        });
        HttpResponseMessage? successResponse = null;
        string responseText = string.Empty;
        string? lastError = null;
        foreach (var model in models)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent");
                request.Headers.Add("x-goog-api-key", apiKey);
                request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
                using var response = await Http.SendAsync(request, cancellationToken);
                responseText = await response.Content.ReadAsStringAsync(cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    successResponse = new HttpResponseMessage(response.StatusCode);
                    break;
                }

                lastError = TryReadApiError(responseText) ?? $"Gemini trả về HTTP {(int)response.StatusCode}.";
                if ((int)response.StatusCode is 400 or 401 or 403)
                    break;
            }
            catch (TaskCanceledException)
            {
                lastError = $"Model Gemini {model} không phản hồi kịp thời.";
            }
            catch (HttpRequestException ex)
            {
                lastError = $"Không kết nối được Gemini model {model}: {ex.Message}";
            }
        }
        if (successResponse is null)
            throw new HttpRequestException(lastError ?? "Gemini không trả về kết quả.");
        successResponse.Dispose();

        using var document = JsonDocument.Parse(responseText);
        var generatedText = document.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
        if (string.IsNullOrWhiteSpace(generatedText)) throw new JsonException("Gemini trả về nội dung rỗng.");
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        var generated = JsonSerializer.Deserialize<List<GeneratedCard>>(generatedText, options);
        if (generated is null || generated.Count == 0) throw new JsonException("Không đọc được danh sách từ vựng Gemini trả về.");

        return generated.Select((card, index) => new VocabCard
        {
            Id = $"card-{topicId}-{startIndex + index + 1}",
            Topic_Id = topicId,
            Word_En = card.Word_En?.Trim() ?? string.Empty,
            Ipa_En = card.Ipa_En,
            Meaning_Vi = card.Meaning_Vi?.Trim() ?? string.Empty,
            Example_En = card.Example_En?.Trim() ?? string.Empty,
            Example_Meaning_Vi = card.Example_Meaning_Vi?.Trim() ?? string.Empty,
            Difficulty = string.IsNullOrWhiteSpace(card.Difficulty) ? level : card.Difficulty,
            Learned = false
        }).Where(card => !string.IsNullOrWhiteSpace(card.Word_En)
            && !string.IsNullOrWhiteSpace(card.Meaning_Vi)
            && !excluded.Contains(card.Word_En, StringComparer.OrdinalIgnoreCase))
            .GroupBy(card => card.Word_En.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static string? TryReadApiError(string body)
    {
        try { using var doc = JsonDocument.Parse(body); return doc.RootElement.GetProperty("error").GetProperty("message").GetString(); }
        catch { return null; }
    }

    private sealed class GeneratedCard
    {
        [JsonPropertyName("word_en")]
        public string? Word_En { get; set; }
        [JsonPropertyName("ipa_en")]
        public string? Ipa_En { get; set; }
        [JsonPropertyName("meaning_vi")]
        public string? Meaning_Vi { get; set; }
        [JsonPropertyName("example_en")]
        public string? Example_En { get; set; }
        [JsonPropertyName("example_meaning_vi")]
        public string? Example_Meaning_Vi { get; set; }
        [JsonPropertyName("difficulty")]
        public string? Difficulty { get; set; }
    }

    public List<VocabCard> GenerateVocab(string topic, string level, string topicId)
    {
        var baseWords = new[]
        {
            ("resilience","/rɪˈzɪl.jəns/","韧性","rèn xìng","Sự kiên cường"),
            ("meticulous","/məˈtɪk.jə.ləs/","一丝不苟","yī sī bù gǒu","Tỉ mỉ"),
            ("enhance","/ɪnˈhɑːns/","提升","tí shēng","Nâng cao"),
            ("sustainable","/səˈsteɪ.nə.bəl/","可持续","kě chí xù","Bền vững"),
            ("innovative","/ˈɪn.ə.veɪ.tɪv/","创新的","chuàng xīn de","Đổi mới")
        };

        return baseWords.Select((w, i) => new VocabCard
        {
            Id = $"card-{topicId}-{i + 1}",
            Topic_Id = topicId,
            Word_En = w.Item1,
            Ipa_En = w.Item2,
            Word_Zh = w.Item3,
            Pinyin = w.Item4,
            Meaning_Vi = w.Item5,
            Example_En = $"This {w.Item1} concept is useful in {topic}.",
            Example_Zh = $"这个{w.Item3}概念在{topic}中很有用。",
            Example_Meaning_Vi = $"Khái niệm {w.Item1} hữu ích trong chủ đề {topic}.",
            Difficulty = string.IsNullOrWhiteSpace(level) ? "B1" : level,
            Learned = false
        }).ToList();
    }

    public VocabCard AutoFill(string input)
    {
        return new VocabCard
        {
            Word_En = input,
            Ipa_En = "/.../",
            Word_Zh = input,
            Pinyin = string.Empty,
            Meaning_Vi = $"Nghĩa tiếng Việt của {input}",
            Example_En = $"I use the word {input} in a sentence.",
            Example_Zh = $"我在句子中使用{input}这个词。",
            Example_Meaning_Vi = $"Tôi dùng từ {input} trong một câu.",
            Difficulty = "B1"
        };
    }

    public object Lookup(string text, string sourceLang)
    {
        return new
        {
            word_en = text,
            word_zh = text,
            pinyin = sourceLang == "zh" ? "" : "",
            meaning_vi = $"Giải nghĩa tham khảo cho '{text}'",
            example_sentence = $"This is an example sentence with {text}."
        };
    }

    public string? ExtractYouTubeId(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        input = input.Trim();
        if (Regex.IsMatch(input, "^[a-zA-Z0-9_-]{11}$")) return input;
        var match = Regex.Match(input, "(?:youtube\\.com\\/(?:.*[?&]v=|shorts\\/)|youtu\\.be\\/)([a-zA-Z0-9_-]{11})");
        return match.Success ? match.Groups[1].Value : null;
    }
}
