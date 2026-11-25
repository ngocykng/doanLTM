using System.Net.Http;
using System.Text;
using System.Text.Json;
using EnglishFlashcard3D.Models;

namespace EnglishFlashcard3D.Services;

public class ApiClientService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    public string? CurrentToken { get; private set; }
    public string? CurrentUserId { get; private set; }

    public ApiClientService()
    {
        _httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:5046/") };
    }

    public void SetAuthSession(string? token, string? userId)
    {
        CurrentToken = token;
        CurrentUserId = userId;
        _httpClient.DefaultRequestHeaders.Remove("Authorization");
        _httpClient.DefaultRequestHeaders.Remove("x-user-id");
        if (!string.IsNullOrWhiteSpace(token))
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        if (!string.IsNullOrWhiteSpace(userId))
            _httpClient.DefaultRequestHeaders.Add("x-user-id", userId);
    }

    public void ClearAuthSession()
    {
        CurrentToken = null;
        CurrentUserId = null;
        _httpClient.DefaultRequestHeaders.Remove("Authorization");
        _httpClient.DefaultRequestHeaders.Remove("x-user-id");
    }

    public async Task<LoginResponse?> LoginAsync(string email, string password)
    {
        var payload = JsonSerializer.Serialize(new { email, password });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync("api/auth/login", content);
        if (!response.IsSuccessStatusCode) return null;
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<LoginResponse>(json, JsonOptions);
        if (result?.Success == true && result.User is not null)
        {
            SetAuthSession(result.Token, result.User.Id);
        }
        return result;
    }

    public async Task<LoginResponse?> RegisterAsync(string name, string email, string password)
    {
        var payload = JsonSerializer.Serialize(new { name, email, password });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync("api/auth/register", content);
        if (!response.IsSuccessStatusCode) return null;
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<LoginResponse>(json, JsonOptions);
        if (result?.Success == true && result.User is not null)
        {
            SetAuthSession(result.Token, result.User.Id);
        }
        return result;
    }

    public async Task<List<TopicDeck>> GetTopicsAsync(string userId)
    {
        var json = await _httpClient.GetStringAsync($"api/vocab/topics?user_id={Uri.EscapeDataString(userId)}");
        var response = JsonSerializer.Deserialize<TopicsResponse>(json, JsonOptions);
        if (response is null) throw new JsonException("API không trả về danh sách chủ đề hợp lệ.");

        return response.Topics.Select(t => new TopicDeck
        {
            Id = t.Id,
            Name = t.Name,
            Level = t.Level,
            CardCount = t.CardCount
        }).ToList();
    }

    public async Task<TopicDeck?> GenerateTopicAsync(string topicName, string level, string userId)
    {
        var payload = JsonSerializer.Serialize(new { topic = topicName, level, user_id = userId });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync("api/vocab/generate", content);
        var json = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            var message = $"API từ vựng trả về HTTP {(int)response.StatusCode}.";
            try
            {
                using var error = JsonDocument.Parse(json);
                if (error.RootElement.TryGetProperty("error", out var detail)) message = detail.GetString() ?? message;
            }
            catch (JsonException) { }
            throw new HttpRequestException(message);
        }
        var model = JsonSerializer.Deserialize<GenerateTopicResponse>(json, JsonOptions);
        if (model?.Topic is null) return null;

        return new TopicDeck
        {
            Id = model.Topic.Id,
            Name = model.Topic.Name,
            Level = model.Topic.Level,
            CardCount = model.Topic.CardCount
        };
    }

    public async Task<GenerateBatchResponse> GenerateBatchAsync(string topicId, string userId)
    {
        var payload = JsonSerializer.Serialize(new { topic_id = topicId, user_id = userId });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync("api/vocab/generate-batch", content);
        var json = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(ReadErrorMessage(json, $"API trả về HTTP {(int)response.StatusCode}."));

        return JsonSerializer.Deserialize<GenerateBatchResponse>(json, JsonOptions)
            ?? throw new JsonException("API không trả về kết quả sinh từ hợp lệ.");
    }

    public async Task<bool> DeleteTopicAsync(string topicId, string userId)
    {
        var response = await _httpClient.DeleteAsync(
            $"api/vocab/{Uri.EscapeDataString(topicId)}?user_id={Uri.EscapeDataString(userId)}");
        return response.IsSuccessStatusCode;
    }

    public async Task<List<EnglishFlashcard>> GetTopicCardsAsync(string topicId, string userId)
    {
        var json = await _httpClient.GetStringAsync($"api/vocab/{Uri.EscapeDataString(topicId)}?user_id={Uri.EscapeDataString(userId)}");
        var response = JsonSerializer.Deserialize<TopicCardsResponse>(json, JsonOptions);
        if (response is null) return new List<EnglishFlashcard>();

        return response.Cards.Select(c => new EnglishFlashcard
        {
            Id = c.Id,
            Word = c.Word_En,
            Phonetic = c.Ipa_En ?? string.Empty,
            MeaningVi = c.Meaning_Vi,
            ExampleEn = c.Example_En,
            ExampleVi = c.Example_Meaning_Vi,
            PartOfSpeech = c.Difficulty ?? string.Empty,
            IsMastered = c.Learned
        }).ToList();
    }

    public async Task<bool> MarkCardLearnedAsync(string cardId, string userId)
    {
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await _httpClient.PatchAsync($"api/vocab/cards/{cardId}/learned?user_id={Uri.EscapeDataString(userId)}", content);
        if (!response.IsSuccessStatusCode)
        {
            response = await _httpClient.PostAsync($"api/vocab/cards/{cardId}/toggle-learned?user_id={Uri.EscapeDataString(userId)}", content);
        }
        return response.IsSuccessStatusCode;
    }

    private static string ReadErrorMessage(string json, string fallback)
    {
        try
        {
            using var error = JsonDocument.Parse(json);
            return error.RootElement.TryGetProperty("error", out var detail)
                ? detail.GetString() ?? fallback
                : fallback;
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    public async Task<List<ApiAssignment>> GetStudentAssignmentsAsync()
    {
        var json = await _httpClient.GetStringAsync("api/student/assignments");
        var response = JsonSerializer.Deserialize<AssignmentsResponse>(json, JsonOptions);
        return response?.Assignments ?? new List<ApiAssignment>();
    }

    public async Task<List<ApiAssignment>> GetExpertAssignmentsAsync(string? expertId = null)
    {
        var url = string.IsNullOrWhiteSpace(expertId) ? "api/expert/assignments" : $"api/expert/assignments?expert_id={Uri.EscapeDataString(expertId)}";
        var json = await _httpClient.GetStringAsync(url);
        return JsonSerializer.Deserialize<AssignmentsResponse>(json, JsonOptions)?.Assignments ?? new();
    }

    public async Task<bool> CreateAssignmentAsync(string title, string skill, string level, int durationMinutes, string contentText, string instructions, string dueDate, double maxScore = 10.0, string? readingQuiz = null)
    {
        var payload = JsonSerializer.Serialize(new
        {
            title,
            skill,
            target_level = level,
            duration_minutes = durationMinutes,
            content = contentText,
            instructions,
            due_date = dueDate,
            max_score = maxScore,
            reading_quiz = readingQuiz
        });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync("api/expert/assignments", content);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> UpdateAssignmentAsync(string id, string title, string skill, string level, int durationMinutes, string contentText, string instructions, string dueDate, double maxScore = 10.0, string? readingQuiz = null)
    {
        var payload = JsonSerializer.Serialize(new
        {
            title,
            skill,
            target_level = level,
            duration_minutes = durationMinutes,
            content = contentText,
            instructions,
            due_date = dueDate,
            max_score = maxScore,
            reading_quiz = readingQuiz
        });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await _httpClient.PutAsync($"api/expert/assignments/{Uri.EscapeDataString(id)}", content);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteAssignmentAsync(string id) => (await _httpClient.DeleteAsync($"api/expert/assignments/{Uri.EscapeDataString(id)}")).IsSuccessStatusCode;

    public async Task<bool> SubmitAssignmentAsync(string assignmentId, string contentText, string? audioUrl = null, string? readingAnswers = null, string? topicSentence = null, int? durationSpent = null)
    {
        var payload = JsonSerializer.Serialize(new
        {
            assignment_id = assignmentId,
            content = contentText,
            audio_url = audioUrl,
            reading_answers = readingAnswers,
            topic_sentence = topicSentence,
            duration_spent = durationSpent
        });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync("api/student/submissions", content);
        return response.IsSuccessStatusCode;
    }

    public async Task<List<ApiSubmission>> GetStudentSubmissionsAsync(string? studentId = null)
    {
        var url = string.IsNullOrWhiteSpace(studentId) ? "api/student/submissions" : $"api/student/submissions?student_id={Uri.EscapeDataString(studentId)}";
        var json = await _httpClient.GetStringAsync(url);
        var response = JsonSerializer.Deserialize<SubmissionsResponse>(json, JsonOptions);
        return response?.Submissions ?? new List<ApiSubmission>();
    }

    public async Task<List<ApiSubmission>> GetExpertSubmissionsAsync(string? status = null, string? assignmentId = null)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(status)) query.Add($"status={Uri.EscapeDataString(status)}");
        if (!string.IsNullOrWhiteSpace(assignmentId)) query.Add($"assignment_id={Uri.EscapeDataString(assignmentId)}");
        var url = "api/expert/submissions" + (query.Count > 0 ? "?" + string.Join("&", query) : "");

        var json = await _httpClient.GetStringAsync(url);
        var response = JsonSerializer.Deserialize<SubmissionsResponse>(json, JsonOptions);
        return response?.Submissions ?? new List<ApiSubmission>();
    }

    public async Task<ApiSubmission?> GetSubmissionDetailAsync(string id)
    {
        var json = await _httpClient.GetStringAsync($"api/expert/submissions/{Uri.EscapeDataString(id)}");
        var response = JsonSerializer.Deserialize<SubmissionDetailResponse>(json, JsonOptions);
        return response?.Submission;
    }

    public async Task<bool> GradeSubmissionAsync(string submissionId, double score, string feedback, string assessedLevel)
    {
        var payload = JsonSerializer.Serialize(new { score, feedback, assessed_level = assessedLevel });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        // Gọi PUT theo chuẩn tài liệu đặc tả F4.3
        var response = await _httpClient.PutAsync($"api/expert/submissions/{submissionId}/grade", content);
        return response.IsSuccessStatusCode;
    }

    // ==========================================
    // CÁC PHƯƠNG THỨC MỞ RỘNG THEO TÀI LIỆU F4
    // ==========================================

    public async Task<List<ApiTopic>> GetStudentTopicsForExpertAsync(string studentId)
    {
        try
        {
            var json = await _httpClient.GetStringAsync($"api/expert/students/{Uri.EscapeDataString(studentId)}/topics");
            var response = JsonSerializer.Deserialize<StudentTopicsResponse>(json, JsonOptions);
            return response?.Topics ?? new List<ApiTopic>();
        }
        catch
        {
            return new List<ApiTopic>();
        }
    }

    public async Task<ApiExamStructure?> GenerateExamAsync(ExamGenerationRequestDto req)
    {
        var payload = JsonSerializer.Serialize(req, JsonOptions);
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync("api/expert/assignments/generate", content);
        if (!response.IsSuccessStatusCode) return null;
        var json = await response.Content.ReadAsStringAsync();
        var res = JsonSerializer.Deserialize<GenerateExamResponse>(json, JsonOptions);
        return res?.Exam_Structure;
    }

    public async Task<ApiAssignment?> SaveExamAssignmentAsync(
        string title,
        string level,
        int durationMinutes,
        string dueDate,
        string? studentId,
        string? studentName,
        List<string> topicIds,
        List<string> topicNames,
        string status,
        bool showAnswers,
        ApiExamStructure examStructure)
    {
        var payload = JsonSerializer.Serialize(new
        {
            title,
            target_level = level,
            duration_minutes = durationMinutes,
            due_date = dueDate,
            target_student_id = studentId,
            target_student_name = studentName,
            topic_ids = topicIds,
            topic_names = topicNames,
            status,
            show_answers = showAnswers,
            exam_structure = examStructure
        }, JsonOptions);
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync("api/expert/assignments", content);
        if (!response.IsSuccessStatusCode) return null;
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("assignment", out var aElement))
        {
            return JsonSerializer.Deserialize<ApiAssignment>(aElement.GetRawText(), JsonOptions);
        }
        return null;
    }

    public async Task<bool> PublishAssignmentAsync(string assignmentId)
    {
        var response = await _httpClient.PostAsync($"api/expert/assignments/{Uri.EscapeDataString(assignmentId)}/publish", null);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> ToggleShowAnswersAsync(string assignmentId, bool showAnswers)
    {
        var payload = JsonSerializer.Serialize(new { show_answers = showAnswers }, JsonOptions);
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await _httpClient.PutAsync($"api/expert/assignments/{Uri.EscapeDataString(assignmentId)}/show-answers", content);
        return response.IsSuccessStatusCode;
    }

    public async Task<ApiSubmission?> SubmitExamAsync(string assignmentId, Dictionary<int, string> answers, int? durationSpent = null)
    {
        var payload = JsonSerializer.Serialize(new
        {
            assignment_id = assignmentId,
            answers,
            duration_spent = durationSpent
        }, JsonOptions);
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync("api/student/submissions", content);
        if (!response.IsSuccessStatusCode) return null;
        var json = await response.Content.ReadAsStringAsync();
        var subRes = JsonSerializer.Deserialize<SubmissionDetailResponse>(json, JsonOptions);
        return subRes?.Submission;
    }

    public async Task<ApiAssignment?> GetStudentAssignmentDetailAsync(string assignmentId)
    {
        try
        {
            var json = await _httpClient.GetStringAsync($"api/student/assignments/{Uri.EscapeDataString(assignmentId)}");
            var response = JsonSerializer.Deserialize<AssignmentDetailResponse>(json, JsonOptions);
            return response?.Assignment;
        }
        catch
        {
            return null;
        }
    }

    public async Task<ApiStats> GetAdminStatsAsync() => JsonSerializer.Deserialize<ApiStatsResponse>(await _httpClient.GetStringAsync("api/admin/stats"), JsonOptions)?.Stats ?? new();
    public async Task<List<AuthUser>> GetUsersAsync() => JsonSerializer.Deserialize<UsersResponse>(await _httpClient.GetStringAsync("api/auth/users"), JsonOptions)?.Users ?? new();
    public async Task<bool> UpdateUserAsync(string id, string role, string status)
    {
        var payload = JsonSerializer.Serialize(new { role, status });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        return (await _httpClient.PutAsync($"api/auth/users/{Uri.EscapeDataString(id)}", content)).IsSuccessStatusCode;
    }
    public async Task<ApiAiConfig> GetAiConfigAsync() => JsonSerializer.Deserialize<AiConfigResponse>(await _httpClient.GetStringAsync("api/admin/ai-config"), JsonOptions)?.Config ?? new();
    public async Task<bool> UpdateAiConfigAsync(ApiAiConfig config)
    {
        using var content = new StringContent(JsonSerializer.Serialize(config), Encoding.UTF8, "application/json");
        return (await _httpClient.PutAsync("api/admin/ai-config", content)).IsSuccessStatusCode;
    }
    public async Task<List<ApiLogItem>> GetApiLogsAsync() => JsonSerializer.Deserialize<LogsResponse>(await _httpClient.GetStringAsync("api/admin/api-logs?limit=100"), JsonOptions)?.Logs ?? new();
    public async Task<List<ApiTopic>> GetCurriculumAsync() => JsonSerializer.Deserialize<TopicsResponse>(await _httpClient.GetStringAsync("api/admin/dau-curriculum"), JsonOptions)?.Topics ?? new();
    public async Task<bool> UpdateCurriculumAsync(List<ApiTopic> topics)
    {
        using var content = new StringContent(JsonSerializer.Serialize(new { topics }), Encoding.UTF8, "application/json");
        return (await _httpClient.PutAsync("api/admin/dau-curriculum", content)).IsSuccessStatusCode;
    }
    public async Task<bool> SyncMysqlAsync() => (await _httpClient.PostAsync("api/admin/sync-mysql", new StringContent("{}", Encoding.UTF8, "application/json"))).IsSuccessStatusCode;
}
