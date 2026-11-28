using System.Text.RegularExpressions;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BilingualVocabApi.Models;

namespace BilingualVocabApi.Services;

public class AiFallbackService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(45) };
    private static readonly HashSet<string> RetiredModels = new(StringComparer.OrdinalIgnoreCase)
    {
        "gemini-3.8-flash", "gemini-3.5-flash", "gemini-3.1-flash-lite-preview", "gemini-2.0-flash"
    };
    private readonly IConfiguration _configuration;
    private readonly ILogger<AiFallbackService>? _logger;

    public AiFallbackService(IConfiguration configuration, ILogger<AiFallbackService>? logger = null)
    {
        _configuration = configuration;
        _logger = logger;
    }

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

        var models = GetCandidateModels(config, includeLitePreview: true);
        var excluded = excludeWords?.Where(word => !string.IsNullOrWhiteSpace(word)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new();
        var excludePrompt = excluded.Count == 0
            ? string.Empty
            : $" Không được dùng lại các từ đã có: {string.Join(", ", excluded.Take(100))}.";
        var prompt = $"Tạo đúng 15 flashcard từ vựng tiếng Anh mới về chủ đề '{topic}', trình độ CEFR {level}, cho đợt #{batchIndex}. " +
            "Chỉ trả JSON array, không markdown. Mỗi object có các trường: word_en, ipa_en, meaning_vi, example_en, example_meaning_vi, difficulty. " +
            $"Nghĩa và câu ví dụ phải chính xác, phù hợp trình độ. Không lặp từ.{excludePrompt}";
        var requestBody = BuildGeminiRequestBody(prompt, config, minTokens: 4096, maxTokens: 8192);
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
        var generatedText = StripMarkdownJsonFences(document.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString());
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

    public async Task<ExamStructure> GenerateExamAsync(
        User student,
        List<Topic> topics,
        List<VocabCard> cards,
        ExamGenerationRequestDto req,
        AiConfig config,
        CancellationToken cancellationToken = default)
    {
        var topicParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(req.Custom_Topic))
            topicParts.Add(req.Custom_Topic.Trim());
        topicParts.AddRange(topics.Select(t => t.Name).Where(n => !string.IsNullOrWhiteSpace(n)));
        var topicNames = topicParts.Count > 0 ? string.Join(", ", topicParts.Distinct(StringComparer.OrdinalIgnoreCase)) : "General English";
        var cardWords = cards.Take(25).Select(c => $"{c.Word_En} ({c.Meaning_Vi})").ToList();
        var cardContext = cardWords.Count > 0 ? string.Join("; ", cardWords) : $"từ vựng học thuật thuộc đề tài {topicNames}";

        var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? _configuration["GEMINI_API_KEY"];
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            var prompt = $@"Bạn là chuyên gia khảo thí tiếng Anh đại học. Hãy nhận diện đề tài và tạo đề thi khảo thí tiếng Anh chuẩn khung CEFR {req.Cefr_Level} cho sinh viên '{student.Name}'.
Đề tài / Chủ đề bài thi: '{topicNames}'.
Từ vựng ưu tiên sử dụng từ các thẻ học: {cardContext}.
Toàn bộ ngữ cảnh đoạn văn, câu trắc nghiệm và câu sắp xếp từ phải bám sát đề tài '{topicNames}'.

Yêu cầu cấu trúc đề thi đúng theo mẫu khảo thí:
- Khung phách tiêu đề: 'THÍ SINH KHÔNG ĐƯỢC VIẾT GÌ VÀO PHÁCH NÀY'
- Tiêu đề đề thi: 'BÀI KIỂM TRA ĐÁNH GIÁ NĂNG LỰC TIẾNG ANH (THEO CHỦ ĐỀ)'
- Đánh số câu hỏi liên tục từ câu 1 xuyên suốt toàn bộ đề thi.
{(req.Include_Signs ? $"- Phần Warnings - Caution signs: {req.Signs_Count} câu (tổng {req.Signs_Points} điểm). Mỗi câu có mô tả/ký hiệu biển báo, câu hỏi 'What does the sign say?' hoặc tương tự, 4 phương án A, B, C, D và 1 đáp án đúng." : "")}
{(req.Include_Vocabulary ? $"- Phần Vocabulary Multiple Choice: {req.Vocabulary_Count} câu (tổng {req.Vocabulary_Points} điểm). Các câu đơn chọn từ đúng lấy từ chủ đề, 4 phương án A, B, C, D." : "")}
{(req.Include_Cloze ? $"- Phần Cloze Test: đoạn văn có {req.Cloze_Count} chỗ trống đánh số liên tục (tổng {req.Cloze_Points} điểm). Đoạn văn theo chủ đề '{topicNames}'. Mỗi chỗ trống có 4 phương án A, B, C, D." : "")}
{(req.Include_Word_Ordering ? $"- Phần Word Ordering (Sắp xếp từ thành câu): {req.Word_Ordering_Count} câu (tổng {req.Word_Ordering_Points} điểm). Mỗi câu gồm các từ/cụm từ bị xáo trộn (scrambled_words) lấy từ chủ đề; correct_answer là câu hoàn chỉnh." : "")}

Trả về DUY NHẤT một JSON object (không dùng markdown backticks ```) tuân theo schema sau:
{{
  ""header_notice"": ""THÍ SINH KHÔNG ĐƯỢC VIẾT GÌ VÀO PHÁCH NÀY"",
  ""exam_title"": ""BÀI KIỂM TRA ĐÁNH GIÁ NĂNG LỰC TIẾNG ANH - ĐỀ TÀI: {topicNames.ToUpperInvariant()}"",
  ""student_name"": ""{student.Name}"",
  ""student_id"": ""{student.Student_Id ?? student.Id}"",
  ""duration_minutes"": {req.Duration_Minutes},
  ""total_score"": 10.0,
  ""sections"": [
    {{
      ""section_id"": ""sec-1"",
      ""section_number"": ""I"",
      ""section_type"": ""warnings"",
      ""title"": ""I. WARNINGS – CAUTION SIGNS ({req.Signs_Points}pt)"",
      ""instructions"": ""Choose the best option (A, B, C or D) that best fits each sign."",
      ""points"": {req.Signs_Points},
      ""passage"": null,
      ""questions"": [
        {{
          ""question_number"": 1,
          ""question_text"": ""What does the sign say?"",
          ""sign_type"": ""NO_PARKING_TRUCKS"",
          ""sign_icon"": ""🚫🚛 [NO TRUCKS]"",
          ""options"": [""A. The truck can stop."", ""B. The truck can drive."", ""C. The truck can't drive."", ""D. The truck can't park here.""],
          ""correct_answer"": ""D"",
          ""scrambled_words"": null,
          ""explanation"": ""Biển báo cấm xe tải đỗ."",
          ""points"": 0.25
        }}
      ]
    }}
  ]
}}";

            var generatedText = await TryCallGeminiJsonAsync(prompt, config, apiKey, cancellationToken);
            if (!string.IsNullOrWhiteSpace(generatedText))
            {
                try
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
                    var parsed = JsonSerializer.Deserialize<ExamStructure>(StripMarkdownJsonFences(generatedText), options);
                    if (parsed is not null && parsed.Sections.Count > 0)
                    {
                        parsed.Student_Name = student.Name;
                        parsed.Student_Id = student.Student_Id ?? student.Id;
                        parsed.Duration_Minutes = req.Duration_Minutes;
                        return NormalizeExamStructure(parsed, req);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to deserialize Gemini exam JSON for topic {Topic}; using fallback exam.", topicNames);
                }
            }
            else
            {
                _logger?.LogWarning("Gemini returned empty response for topic {Topic}; using fallback exam.", topicNames);
            }
        }

        return GenerateFallbackExam(student, topics, cards, req);
    }

    private static List<string> GetCandidateModels(AiConfig config, bool includeLitePreview = false)
    {
        var preferredModel = string.IsNullOrWhiteSpace(config.Model_Name) || RetiredModels.Contains(config.Model_Name.Trim())
            ? "gemini-2.5-flash"
            : config.Model_Name.Trim();
        var candidates = new List<string> { preferredModel, "gemini-2.5-flash", "gemini-2.5-flash-lite" };
        if (includeLitePreview && !candidates.Contains("gemini-2.5-flash-lite", StringComparer.OrdinalIgnoreCase))
            candidates.Add("gemini-2.5-flash-lite");

        return candidates
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildGeminiRequestBody(string prompt, AiConfig config, int minTokens, int maxTokens)
        => JsonSerializer.Serialize(new
        {
            contents = new[] { new { parts = new[] { new { text = prompt } } } },
            generationConfig = new
            {
                responseMimeType = "application/json",
                temperature = Math.Clamp(config.Temperature, 0.1, 1.0),
                maxOutputTokens = Math.Clamp(Math.Max(config.Max_Output_Tokens, minTokens), minTokens, maxTokens),
                thinkingConfig = new { thinkingBudget = 0 }
            }
        });

    private static string StripMarkdownJsonFences(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var text = raw.Trim();
        if (text.StartsWith("```"))
        {
            var firstNewline = text.IndexOf('\n');
            if (firstNewline >= 0) text = text[(firstNewline + 1)..];
            if (text.EndsWith("```")) text = text[..^3];
        }
        return text.Trim();
    }

    private async Task<string?> TryCallGeminiJsonAsync(
        string prompt,
        AiConfig config,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var models = GetCandidateModels(config, includeLitePreview: true);
        var requestBody = BuildGeminiRequestBody(prompt, config, minTokens: 8192, maxTokens: 8192);

        foreach (var model in models)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent");
                request.Headers.Add("x-goog-api-key", apiKey);
                request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
                using var response = await Http.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var errBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger?.LogWarning("Gemini model {Model} returned HTTP {Status}: {Error}", model, (int)response.StatusCode, TryReadApiError(errBody) ?? errBody);
                    if ((int)response.StatusCode is 400 or 401 or 403) break;
                    continue;
                }
                var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
                using var document = JsonDocument.Parse(responseText);
                var text = StripMarkdownJsonFences(document.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString());
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Gemini call failed for model {Model}", model);
            }
        }
        return null;
    }

    public async Task<SkillAssignmentDraft> GenerateSkillAssignmentAsync(
        string topic,
        string skill,
        string cefrLevel,
        int questionCount,
        AiConfig config,
        CancellationToken cancellationToken = default)
    {
        var normalizedSkill = skill.Equals("listening", StringComparison.OrdinalIgnoreCase) ? "listening" : "reading";
        var normalizedCefr = string.IsNullOrWhiteSpace(cefrLevel) ? "B2" : cefrLevel.Trim().ToUpperInvariant();
        var count = Math.Clamp(questionCount, 1, 10);

        var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? _configuration["GEMINI_API_KEY"];
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            var skillInstruction = normalizedSkill == "listening"
                ? "Kỹ năng LISTENING: hãy tạo một bài nghe (Audio Script / bài nói hoặc hội thoại tiếng Anh tự nhiên dài khoảng 140-200 từ) về đúng đề tài trên để hệ thống đọc TTS hoặc giảng viên lồng audio, kèm hướng dẫn làm bài nghe bằng tiếng Việt."
                : "Kỹ năng READING: hãy tạo một đoạn văn đọc hiểu tiếng Anh học thuật dài khoảng 150-220 từ về đúng đề tài trên, kèm hướng dẫn đọc hiểu và yêu cầu viết 01 câu chủ đề (Topic Sentence) bằng tiếng Việt.";

            var prompt = $@"Bạn là giảng viên khảo thí tiếng Anh đại học. Hãy nhận diện đề tài: '{topic}' và biên soạn đề bài tập/kiểm tra chuẩn khung CEFR {normalizedCefr}.
{skillInstruction}
Tạo đúng {count} câu hỏi trắc nghiệm tiếng Anh (4 phương án A, B, C, D; mỗi phương án bắt đầu bằng 'A. ', 'B. ', 'C. ', 'D. '; correct_answer chỉ là 1 ký tự 'A', 'B', 'C' hoặc 'D').

Trả về DUY NHẤT JSON object theo cấu trúc:
{{
  ""title"": ""Tiêu đề bài thi ngắn gọn bằng tiếng Anh theo đề tài"",
  ""instructions"": ""Hướng dẫn làm bài rõ ràng bằng tiếng Việt"",
  ""content"": ""Nội dung đoạn văn Reading hoặc Audio Script Listening bằng tiếng Anh"",
  ""questions"": [
    {{
      ""id"": 1,
      ""question"": ""Câu hỏi tiếng Anh?"",
      ""options"": [""A. ..."", ""B. ..."", ""C. ..."", ""D. ...""],
      ""correct_answer"": ""A""
    }}
  ]
}}";

            var generatedText = await TryCallGeminiJsonAsync(prompt, config, apiKey, cancellationToken);
            if (!string.IsNullOrWhiteSpace(generatedText))
            {
                try
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
                    var parsed = JsonSerializer.Deserialize<SkillAssignmentDraft>(generatedText, options);
                    if (parsed is not null && !string.IsNullOrWhiteSpace(parsed.Content) && parsed.Questions.Count > 0)
                    {
                        parsed.Topic = topic.Trim();
                        parsed.Skill = normalizedSkill;
                        parsed.Target_Level = normalizedCefr;
                        for (int i = 0; i < parsed.Questions.Count; i++)
                        {
                            parsed.Questions[i].Id = i + 1;
                            if (string.IsNullOrWhiteSpace(parsed.Questions[i].Correct_Answer))
                                parsed.Questions[i].Correct_Answer = "A";
                        }
                        if (string.IsNullOrWhiteSpace(parsed.Title))
                            parsed.Title = $"{(normalizedSkill == "listening" ? "Listening" : "Reading")}: {topic.Trim()}";
                        return parsed;
                    }
                }
                catch { }
            }
        }

        return GenerateFallbackSkillAssignment(topic, normalizedSkill, normalizedCefr, count);
    }

    public static SkillAssignmentDraft GenerateFallbackSkillAssignment(string topic, string skill, string cefrLevel, int count)
    {
        var cleanTopic = topic.Trim();
        bool isListening = skill.Equals("listening", StringComparison.OrdinalIgnoreCase);

        var content = isListening
            ? $"Welcome to today's audio lecture on {cleanTopic}. In recent years, specialists at level {cefrLevel} have emphasized how {cleanTopic} shapes modern society and daily innovation. First, researchers point out that understanding {cleanTopic} improves practical decision-making. Second, collaborative projects centered on {cleanTopic} help communities adapt faster to global changes. Finally, investing in education about {cleanTopic} builds long-term resilience and sustainable progress."
            : $"{cleanTopic} has become an essential area of study in contemporary academic and professional contexts. At the {cefrLevel} level, examining {cleanTopic} reveals three core dimensions: practical efficiency, long-term sustainability, and human-centered innovation. Recent studies demonstrate that organizations integrating {cleanTopic} into their core strategies achieve higher adaptability and stronger outcomes. As global challenges evolve, developing deeper literacy in {cleanTopic} equips learners and practitioners with the analytical skills needed for future success.";

        var bank = new List<ReadingQuizQuestion>
        {
            new()
            {
                Id = 1,
                Question = $"What is the main focus of the {(isListening ? "audio lecture" : "passage")}?",
                Options = new() { $"A. The importance and impact of {cleanTopic}", "B. Historical sports competitions", "C. Traditional cooking recipes", "D. Fictional space travel" },
                Correct_Answer = "A"
            },
            new()
            {
                Id = 2,
                Question = $"According to the {(isListening ? "speaker" : "text")}, what benefit is associated with {cleanTopic}?",
                Options = new() { "A. Higher transportation fees", "B. Stronger adaptability and better decision-making", "C. Reduced communication among teams", "D. Avoiding modern technology" },
                Correct_Answer = "B"
            },
            new()
            {
                Id = 3,
                Question = "Which factor is highlighted as supporting long-term progress?",
                Options = new() { "A. Ignoring community feedback", "B. Short-term cost cutting only", "C. Education and human-centered innovation", "D. Working without collaboration" },
                Correct_Answer = "C"
            },
            new()
            {
                Id = 4,
                Question = "What CEFR competence is this task designed to assess?",
                Options = new() { "A. Unrelated physical training", "B. Manual drawing speed", "C. Memorizing random numbers", $"D. {cefrLevel} comprehension on {cleanTopic}" },
                Correct_Answer = "D"
            }
        };

        return new SkillAssignmentDraft
        {
            Topic = cleanTopic,
            Skill = isListening ? "listening" : "reading",
            Target_Level = cefrLevel,
            Title = $"{(isListening ? "Listening" : "Reading")} Practice: {cleanTopic}",
            Instructions = isListening
                ? $"Nghe đoạn audio hoặc dùng nút đọc TTS về đề tài '{cleanTopic}', trả lời {count} câu hỏi trắc nghiệm và tóm tắt các ý chính."
                : $"Đọc kỹ đoạn văn về đề tài '{cleanTopic}', trả lời {count} câu hỏi trắc nghiệm (tự chấm tối đa 5.0đ) và viết 01 câu chủ đề (Topic Sentence).",
            Content = content,
            Questions = bank.Take(count).ToList()
        };
    }

    private static ExamStructure NormalizeExamStructure(ExamStructure exam, ExamGenerationRequestDto req)
    {
        exam.Header_Notice = "THÍ SINH KHÔNG ĐƯỢC VIẾT GÌ VÀO PHÁCH NÀY";
        if (!string.IsNullOrWhiteSpace(req.Custom_Topic))
        {
            var customTopic = req.Custom_Topic.Trim();
            if (string.IsNullOrWhiteSpace(exam.Exam_Title))
                exam.Exam_Title = $"BÀI KIỂM TRA ĐÁNH GIÁ NĂNG LỰC TIẾNG ANH - ĐỀ TÀI: {customTopic.ToUpperInvariant()}";
            else if (!exam.Exam_Title.Contains(customTopic, StringComparison.OrdinalIgnoreCase))
                exam.Exam_Title = $"{exam.Exam_Title.Trim()} - ĐỀ TÀI: {customTopic.ToUpperInvariant()}";
        }
        int qNum = 1;
        double calculatedTotal = 0;
        foreach (var sec in exam.Sections)
        {
            if (sec.Questions.Count > 0)
            {
                double qPts = Math.Round(sec.Points / sec.Questions.Count, 2);
                foreach (var q in sec.Questions)
                {
                    q.Question_Number = qNum++;
                    q.Points = qPts;
                }
            }
            calculatedTotal += sec.Points;
        }
        exam.Total_Score = Math.Round(calculatedTotal, 1);
        if (exam.Total_Score <= 0) exam.Total_Score = 10.0;
        return exam;
    }

    public ExamStructure GenerateFallbackExam(
        User student,
        List<Topic> topics,
        List<VocabCard> cards,
        ExamGenerationRequestDto req)
    {
        var topicTitle = !string.IsNullOrWhiteSpace(req.Custom_Topic)
            ? req.Custom_Topic.Trim()
            : (topics.Count > 0 ? topics[0].Name : "Kiến trúc & Môi trường");
        var exam = new ExamStructure
        {
            Header_Notice = "THÍ SINH KHÔNG ĐƯỢC VIẾT GÌ VÀO PHÁCH NÀY",
            Exam_Title = $"BÀI THI KHẢO THÍ TIẾNG ANH - CHỦ ĐỀ: {topicTitle.ToUpperInvariant()}",
            Student_Name = student.Name,
            Student_Id = student.Student_Id ?? student.Id,
            Duration_Minutes = req.Duration_Minutes > 0 ? req.Duration_Minutes : 45,
            Total_Score = 10.0,
            Sections = new List<ExamSection>()
        };

        int qCounter = 1;
        int sectionIndex = 1;
        string[] romanNumerals = { "I", "II", "III", "IV", "V", "VI" };

        // 1. WARNINGS - CAUTION SIGNS
        if (req.Include_Signs && req.Signs_Count > 0)
        {
            var signsSec = new ExamSection
            {
                Section_Id = $"sec-{sectionIndex}",
                Section_Number = romanNumerals[sectionIndex - 1],
                Section_Type = "warnings",
                Title = $"{romanNumerals[sectionIndex - 1]}. WARNINGS – CAUTION SIGNS ({req.Signs_Points:F1}pt)",
                Instructions = "Look at the sign. What does it say? Choose the best option (A, B, C or D).",
                Points = req.Signs_Points,
                Questions = new List<ExamQuestion>()
            };

            var signBank = new[]
            {
                (
                    "🚫🚛", "NO_PARKING_TRUCKS", "What does the sign say?",
                    new List<string> { "A. The truck can stop.", "B. The truck can drive.", "C. The truck can't drive.", "D. The truck can't park here." },
                    "D", "Biển báo cấm xe tải đỗ trong khu vực này."
                ),
                (
                    "🚯", "NO_LITTERING", "What does the sign warn us?",
                    new List<string> { "A. You aren't allowed to litter here.", "B. There is a waste basket here.", "C. You must litter here.", "D. Put the litter in the garbage bin here." },
                    "A", "Biển báo cấm vứt rác bừa bãi."
                ),
                (
                    "⚠️", "CAUTION_WET_FLOOR", "What does this caution sign mean?",
                    new List<string> { "A. You can run quickly across the room.", "B. Be careful because the floor is slippery.", "C. The floor is being washed with hot water.", "D. Remove shoes before entering." },
                    "B", "Cảnh báo sàn nhà đang trơn trượt, hãy đi cẩn thận."
                ),
                (
                    "🦺", "SAFETY_HELMET_REQUIRED", "What must workers do when seeing this sign?",
                    new List<string> { "A. Wear protective helmets and safety gear.", "B. Leave all tools outside.", "C. Visitors may enter without supervision.", "D. Work without protective shoes." },
                    "A", "Bắt buộc đội mũ và trang bị đồ bảo hộ lao động."
                )
            };

            int count = Math.Min(req.Signs_Count, signBank.Length);
            double ptsPerQ = Math.Round(req.Signs_Points / count, 2);
            for (int i = 0; i < count; i++)
            {
                var item = signBank[i];
                signsSec.Questions.Add(new ExamQuestion
                {
                    Question_Number = qCounter++,
                    Question_Text = item.Item3,
                    Sign_Type = item.Item2,
                    Sign_Icon = item.Item1,
                    Options = item.Item4,
                    Correct_Answer = item.Item5,
                    Explanation = item.Item6,
                    Points = ptsPerQ
                });
            }
            exam.Sections.Add(signsSec);
            sectionIndex++;
        }

        // 2. VOCABULARY MULTIPLE CHOICE
        if (req.Include_Vocabulary && req.Vocabulary_Count > 0)
        {
            var vocabSec = new ExamSection
            {
                Section_Id = $"sec-{sectionIndex}",
                Section_Number = romanNumerals[sectionIndex - 1],
                Section_Type = "vocabulary",
                Title = $"{romanNumerals[sectionIndex - 1]}. VOCABULARY MULTIPLE CHOICE ({req.Vocabulary_Points:F1}pt)",
                Instructions = "Choose the word or phrase (A, B, C or D) that best completes each sentence.",
                Points = req.Vocabulary_Points,
                Questions = new List<ExamQuestion>()
            };

            var vocabItems = cards.Where(c => !string.IsNullOrWhiteSpace(c.Word_En)).Take(req.Vocabulary_Count).ToList();
            if (vocabItems.Count == 0)
            {
                vocabItems = new List<VocabCard>
                {
                    new() { Word_En = "sustainable", Meaning_Vi = "bền vững", Example_En = "Modern builders use sustainable materials." },
                    new() { Word_En = "resilience", Meaning_Vi = "sự kiên cường / độ đàn hồi", Example_En = "Urban infrastructure requires structural resilience." },
                    new() { Word_En = "enhance", Meaning_Vi = "nâng cao", Example_En = "Good insulation will enhance energy efficiency." },
                    new() { Word_En = "innovative", Meaning_Vi = "đổi mới / sáng tạo", Example_En = "Architects design innovative public spaces." }
                };
            }

            int count = Math.Min(req.Vocabulary_Count, vocabItems.Count);
            double ptsPerQ = Math.Round(req.Vocabulary_Points / count, 2);

            var distractors = new[] { "traditional", "temporary", "expensive", "fragile", "complicated", "dangerous" };
            for (int i = 0; i < count; i++)
            {
                var card = vocabItems[i];
                var sentence = !string.IsNullOrWhiteSpace(card.Example_En) && card.Example_En.Contains(card.Word_En, StringComparison.OrdinalIgnoreCase)
                    ? Regex.Replace(card.Example_En, Regex.Escape(card.Word_En), "_______", RegexOptions.IgnoreCase)
                    : $"The engineer emphasized the importance of _______ design in {topicTitle}.";

                var opts = new List<string>
                {
                    $"A. {card.Word_En}",
                    $"B. {distractors[i % distractors.Length]}",
                    $"C. {distractors[(i + 1) % distractors.Length]}",
                    $"D. {distractors[(i + 2) % distractors.Length]}"
                };

                vocabSec.Questions.Add(new ExamQuestion
                {
                    Question_Number = qCounter++,
                    Question_Text = sentence,
                    Options = opts,
                    Correct_Answer = "A",
                    Explanation = $"Từ '{card.Word_En}' có nghĩa là: {card.Meaning_Vi}.",
                    Points = ptsPerQ
                });
            }
            exam.Sections.Add(vocabSec);
            sectionIndex++;
        }

        // 3. CLOZE TEST
        if (req.Include_Cloze && req.Cloze_Count > 0)
        {
            var clozeSec = new ExamSection
            {
                Section_Id = $"sec-{sectionIndex}",
                Section_Number = romanNumerals[sectionIndex - 1],
                Section_Type = "cloze",
                Title = $"{romanNumerals[sectionIndex - 1]}. CLOZE TEST ({req.Cloze_Points:F1}pts)",
                Instructions = "Choose the word or phrase (A, B, C or D) that best fits each space in the following passage.",
                Points = req.Cloze_Points,
                Questions = new List<ExamQuestion>()
            };

            int startBlank = qCounter;
            clozeSec.Passage = $"Hue Citadel is one of the most famous ({startBlank}) ________ in Vietnam. There are many historical citadels in Vietnam, but now only a few remain preserved. A citadel is similar ({startBlank + 1}) ________ a royal castle. Citadels were homes for Kings and Queens. Hue Citadel was the home of many Kings and ({startBlank + 2}) ________ families. It was built in the 19th century and was mainly constructed from wood and bricks. The walls were made from strong stone bricks to keep the royal family safe. It doesn't have ({startBlank + 3}) ________ dungeons, but there is an impressive moat surrounding it. Today, architects use ({startBlank + 4}) ________ techniques to protect historical heritage while maintaining structural integrity. Every year, thousands of international visitors ({startBlank + 5}) ________ the Citadel and admire its majestic royal palaces.";

            var clozeData = new[]
            {
                (new List<string> { "A. buildings", "B. homes", "C. citadels", "D. shopping malls" }, "C", "Hue Citadel là một trong những thành trì (citadels) nổi tiếng."),
                (new List<string> { "A. in", "B. on", "C. to", "D. with" }, "C", "Cấu trúc: 'similar to' (tương tự như)."),
                (new List<string> { "A. Kings'", "B. theirs", "C. their", "D. both A & C" }, "C", "Tính từ sở hữu: 'their families'."),
                (new List<string> { "A. some", "B. any", "C. few", "D. a little" }, "B", "Câu phủ định: 'doesn't have any dungeons'."),
                (new List<string> { "A. sustainable", "B. destructive", "C. fragile", "D. polluted" }, "A", "Kỹ thuật bền vững (sustainable techniques)."),
                (new List<string> { "A. visits", "B. visiting", "C. visited", "D. visit" }, "D", "Chủ ngữ số nhiều 'thousands of people' đi với động từ nguyên mẫu 'visit'.")
            };

            int count = Math.Min(req.Cloze_Count, clozeData.Length);
            double ptsPerQ = Math.Round(req.Cloze_Points / count, 2);

            for (int i = 0; i < count; i++)
            {
                var item = clozeData[i];
                clozeSec.Questions.Add(new ExamQuestion
                {
                    Question_Number = qCounter++,
                    Question_Text = $"Choose the best word for space ({startBlank + i}):",
                    Options = item.Item1,
                    Correct_Answer = item.Item2,
                    Explanation = item.Item3,
                    Points = ptsPerQ
                });
            }
            exam.Sections.Add(clozeSec);
            sectionIndex++;
        }

        // 4. WORD ORDERING
        if (req.Include_Word_Ordering && req.Word_Ordering_Count > 0)
        {
            var wordSec = new ExamSection
            {
                Section_Id = $"sec-{sectionIndex}",
                Section_Number = romanNumerals[sectionIndex - 1],
                Section_Type = "word_ordering",
                Title = $"{romanNumerals[sectionIndex - 1]}. WORD ORDERING ({req.Word_Ordering_Points:F1}pt)",
                Instructions = "Rearrange the given words or phrases into a complete, meaningful and grammatically correct sentence.",
                Points = req.Word_Ordering_Points,
                Questions = new List<ExamQuestion>()
            };

            var orderingBank = new[]
            {
                (
                    new List<string> { "was built", "The ancient citadel", "with", "sustainable bricks.", "strong and" },
                    "The ancient citadel was built with strong and sustainable bricks.",
                    "Sắp xếp thành câu hoàn chỉnh đúng ngữ pháp tiếng Anh."
                ),
                (
                    new List<string> { "to preserve", "Modern architects", "heritage structures.", "work meticulously", "valuable" },
                    "Modern architects work meticulously to preserve valuable heritage structures.",
                    "Sắp xếp thành câu: Chủ ngữ + trạng từ/động từ + mục đích."
                ),
                (
                    new List<string> { "help enhance", "Green construction materials", "energy efficiency", "in urban buildings." },
                    "Green construction materials help enhance energy efficiency in urban buildings.",
                    "Sắp xếp thành câu khẳng định hoàn chỉnh."
                )
            };

            int count = Math.Min(req.Word_Ordering_Count, orderingBank.Length);
            double ptsPerQ = Math.Round(req.Word_Ordering_Points / count, 2);

            for (int i = 0; i < count; i++)
            {
                var item = orderingBank[i];
                wordSec.Questions.Add(new ExamQuestion
                {
                    Question_Number = qCounter++,
                    Question_Text = "Rearrange the words to form a correct sentence:",
                    Scrambled_Words = item.Item1,
                    Correct_Answer = item.Item2,
                    Explanation = item.Item3,
                    Points = ptsPerQ
                });
            }
            exam.Sections.Add(wordSec);
            sectionIndex++;
        }

        double totalSum = exam.Sections.Sum(s => s.Points);
        exam.Total_Score = Math.Round(totalSum, 1);
        return exam;
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
