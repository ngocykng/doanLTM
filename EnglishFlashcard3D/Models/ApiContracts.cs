using System.Text.Json.Serialization;

namespace EnglishFlashcard3D.Models;

public class LoginResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("user")]
    public AuthUser? User { get; set; }

    [JsonPropertyName("token")]
    public string? Token { get; set; }
}

public class AuthUser
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; set; } = "student";
    [JsonPropertyName("status")]
    public string Status { get; set; } = "active";
    [JsonPropertyName("department")]
    public string? Department { get; set; }
}

public class TopicsResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("topics")]
    public List<ApiTopic> Topics { get; set; } = new();
}

public class ApiTopic
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("level")]
    public string Level { get; set; } = "ALL";

    [JsonPropertyName("card_count")]
    public int CardCount { get; set; }
}

public class GenerateTopicResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("topic")]
    public ApiTopic? Topic { get; set; }
}

public class GenerateBatchResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("topic")]
    public ApiTopic? Topic { get; set; }

    [JsonPropertyName("new_cards")]
    public List<ApiVocabCard> NewCards { get; set; } = new();

    [JsonPropertyName("total_cards")]
    public int TotalCards { get; set; }
}

public class TopicCardsResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("topic")]
    public ApiTopic? Topic { get; set; }

    [JsonPropertyName("cards")]
    public List<ApiVocabCard> Cards { get; set; } = new();
}

public class ApiVocabCard
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("word_en")]
    public string Word_En { get; set; } = string.Empty;

    [JsonPropertyName("ipa_en")]
    public string? Ipa_En { get; set; }

    [JsonPropertyName("meaning_vi")]
    public string Meaning_Vi { get; set; } = string.Empty;

    [JsonPropertyName("example_en")]
    public string Example_En { get; set; } = string.Empty;

    [JsonPropertyName("example_meaning_vi")]
    public string Example_Meaning_Vi { get; set; } = string.Empty;

    [JsonPropertyName("difficulty")]
    public string? Difficulty { get; set; }

    [JsonPropertyName("learned")]
    public bool Learned { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("review_count")]
    public int Review_Count { get; set; }

    [JsonPropertyName("last_reviewed_at")]
    public string? Last_Reviewed_At { get; set; }

    [JsonPropertyName("next_review_at")]
    public string? Next_Review_At { get; set; }

    [JsonPropertyName("interval_days")]
    public int Interval_Days { get; set; }

    [JsonPropertyName("ease_factor")]
    public double Ease_Factor { get; set; } = 2.5;
}

public class AssignmentsResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("assignments")]
    public List<ApiAssignment> Assignments { get; set; } = new();
}

public class ApiReadingQuizQuestion
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("question")]
    public string Question { get; set; } = string.Empty;

    [JsonPropertyName("options")]
    public List<string> Options { get; set; } = new();

    [JsonPropertyName("correct_answer")]
    public string Correct_Answer { get; set; } = string.Empty;
}

public class ApiAssignment
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("expert_id")]
    public string Expert_Id { get; set; } = string.Empty;

    [JsonPropertyName("expert_name")]
    public string Expert_Name { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("skill")]
    public string Skill { get; set; } = "writing";

    [JsonPropertyName("target_level")]
    public string Target_Level { get; set; } = "B2";

    [JsonPropertyName("duration_minutes")]
    public int Duration_Minutes { get; set; } = 45;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("instructions")]
    public string Instructions { get; set; } = string.Empty;

    [JsonPropertyName("reference_media_url")]
    public string? Reference_Media_Url { get; set; }

    [JsonPropertyName("due_date")]
    public string? Due_Date { get; set; }

    [JsonPropertyName("max_score")]
    public double Max_Score { get; set; } = 10.0;

    [JsonPropertyName("reading_quiz")]
    public string? Reading_Quiz { get; set; }

    [JsonPropertyName("submission_count")]
    public int Submission_Count { get; set; }

    [JsonPropertyName("target_student_id")]
    public string? Target_Student_Id { get; set; }

    [JsonPropertyName("target_student_name")]
    public string? Target_Student_Name { get; set; }

    [JsonPropertyName("topic_ids")]
    public List<string> Topic_Ids { get; set; } = new();

    [JsonPropertyName("topic_names")]
    public List<string> Topic_Names { get; set; } = new();

    [JsonIgnore]
    public string TopicSummary => Topic_Names is { Count: > 0 } ? string.Join(", ", Topic_Names) : string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = "draft"; // draft, published

    [JsonPropertyName("show_answers")]
    public bool Show_Answers { get; set; } = false;

    [JsonPropertyName("exam_structure")]
    public ApiExamStructure? Exam_Structure { get; set; }
}

public class ApiExamStructure
{
    [JsonPropertyName("header_notice")]
    public string Header_Notice { get; set; } = "THÍ SINH KHÔNG ĐƯỢC VIẾT GÌ VÀO PHÁCH NÀY";

    [JsonPropertyName("exam_title")]
    public string Exam_Title { get; set; } = "BÀI KIỂM TRA ĐÁNH GIÁ NĂNG LỰC TIẾNG ANH";

    [JsonPropertyName("student_name")]
    public string? Student_Name { get; set; }

    [JsonPropertyName("student_id")]
    public string? Student_Id { get; set; }

    [JsonPropertyName("duration_minutes")]
    public int Duration_Minutes { get; set; } = 45;

    [JsonPropertyName("total_score")]
    public double Total_Score { get; set; } = 10.0;

    [JsonPropertyName("sections")]
    public List<ApiExamSection> Sections { get; set; } = new();
}

public class ApiExamSection
{
    [JsonPropertyName("section_id")]
    public string Section_Id { get; set; } = string.Empty;

    [JsonPropertyName("section_number")]
    public string Section_Number { get; set; } = "I";

    [JsonPropertyName("section_type")]
    public string Section_Type { get; set; } = "vocabulary"; // warnings, cloze, word_ordering, vocabulary

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("instructions")]
    public string Instructions { get; set; } = string.Empty;

    [JsonPropertyName("points")]
    public double Points { get; set; } = 1.0;

    [JsonPropertyName("passage")]
    public string? Passage { get; set; }

    [JsonPropertyName("questions")]
    public List<ApiExamQuestion> Questions { get; set; } = new();
}

public class ApiExamQuestion
{
    [JsonPropertyName("question_number")]
    public int Question_Number { get; set; }

    [JsonPropertyName("question_text")]
    public string Question_Text { get; set; } = string.Empty;

    [JsonPropertyName("sign_type")]
    public string? Sign_Type { get; set; }

    [JsonPropertyName("sign_icon")]
    public string? Sign_Icon { get; set; }

    [JsonPropertyName("options")]
    public List<string> Options { get; set; } = new();

    [JsonPropertyName("correct_answer")]
    public string Correct_Answer { get; set; } = string.Empty;

    [JsonPropertyName("scrambled_words")]
    public List<string>? Scrambled_Words { get; set; }

    [JsonPropertyName("explanation")]
    public string? Explanation { get; set; }

    [JsonPropertyName("points")]
    public double Points { get; set; } = 0.5;
}

public class ApiStudentExamAnswer
{
    [JsonPropertyName("question_number")]
    public int Question_Number { get; set; }

    [JsonPropertyName("section_id")]
    public string Section_Id { get; set; } = string.Empty;

    [JsonPropertyName("selected_answer")]
    public string Selected_Answer { get; set; } = string.Empty;

    [JsonPropertyName("correct_answer")]
    public string? Correct_Answer { get; set; }

    [JsonPropertyName("is_correct")]
    public bool Is_Correct { get; set; }

    [JsonPropertyName("earned_points")]
    public double Earned_Points { get; set; }

    [JsonPropertyName("max_points")]
    public double Max_Points { get; set; }
}

public class ExamGenerationRequestDto
{
    [JsonPropertyName("student_id")]
    public string Student_Id { get; set; } = string.Empty;

    [JsonPropertyName("topic_ids")]
    public List<string> Topic_Ids { get; set; } = new();

    [JsonPropertyName("custom_topic")]
    public string? Custom_Topic { get; set; }

    [JsonPropertyName("cefr_level")]
    public string Cefr_Level { get; set; } = "B2";

    [JsonPropertyName("include_signs")]
    public bool Include_Signs { get; set; } = true;

    [JsonPropertyName("signs_count")]
    public int Signs_Count { get; set; } = 2;

    [JsonPropertyName("signs_points")]
    public double Signs_Points { get; set; } = 0.5;

    [JsonPropertyName("include_cloze")]
    public bool Include_Cloze { get; set; } = true;

    [JsonPropertyName("cloze_count")]
    public int Cloze_Count { get; set; } = 6;

    [JsonPropertyName("cloze_points")]
    public double Cloze_Points { get; set; } = 1.5;

    [JsonPropertyName("include_word_ordering")]
    public bool Include_Word_Ordering { get; set; } = true;

    [JsonPropertyName("word_ordering_count")]
    public int Word_Ordering_Count { get; set; } = 2;

    [JsonPropertyName("word_ordering_points")]
    public double Word_Ordering_Points { get; set; } = 1.0;

    [JsonPropertyName("include_vocabulary")]
    public bool Include_Vocabulary { get; set; } = true;

    [JsonPropertyName("vocabulary_count")]
    public int Vocabulary_Count { get; set; } = 4;

    [JsonPropertyName("vocabulary_points")]
    public double Vocabulary_Points { get; set; } = 1.0;

    [JsonPropertyName("duration_minutes")]
    public int Duration_Minutes { get; set; } = 45;

    [JsonPropertyName("due_date")]
    public string? Due_Date { get; set; }

    [JsonPropertyName("show_answers")]
    public bool Show_Answers { get; set; } = false;
}

public class StudentTopicsResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("student")]
    public AuthUser? Student { get; set; }

    [JsonPropertyName("topics")]
    public List<ApiTopic> Topics { get; set; } = new();
}

public class GenerateExamResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("exam_structure")]
    public ApiExamStructure? Exam_Structure { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public class AssignmentDetailResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("assignment")]
    public ApiAssignment? Assignment { get; set; }
}

public class SubmissionsResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("submissions")]
    public List<ApiSubmission> Submissions { get; set; } = new();
}

public class SubmissionDetailResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("submission")]
    public ApiSubmission? Submission { get; set; }
}

public class ApiSubmission
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("assignment_id")]
    public string Assignment_Id { get; set; } = string.Empty;

    [JsonPropertyName("assignment_title")]
    public string Assignment_Title { get; set; } = string.Empty;

    [JsonPropertyName("skill")]
    public string Skill { get; set; } = "writing";

    [JsonPropertyName("student_id")]
    public string Student_Id { get; set; } = string.Empty;

    [JsonPropertyName("student_name")]
    public string Student_Name { get; set; } = string.Empty;

    [JsonPropertyName("student_code")]
    public string? Student_Code { get; set; }

    [JsonPropertyName("student_department")]
    public string? Student_Department { get; set; }

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("audio_url")]
    public string? Audio_Url { get; set; }

    [JsonPropertyName("reading_answers")]
    public string? Reading_Answers { get; set; }

    [JsonPropertyName("reading_auto_score")]
    public double? Reading_Auto_Score { get; set; }

    [JsonPropertyName("topic_sentence")]
    public string? Topic_Sentence { get; set; }

    [JsonPropertyName("duration_spent")]
    public int? Duration_Spent { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "pending";

    [JsonPropertyName("score")]
    public double? Score { get; set; }

    [JsonPropertyName("feedback")]
    public string? Feedback { get; set; }

    [JsonPropertyName("assessed_level")]
    public string? Assessed_Level { get; set; }

    [JsonPropertyName("graded_by")]
    public string? Graded_By { get; set; }

    [JsonPropertyName("graded_at")]
    public string? Graded_At { get; set; }

    [JsonPropertyName("submitted_at")]
    public string Submitted_At { get; set; } = string.Empty;

    [JsonPropertyName("exam_answers")]
    public List<ApiStudentExamAnswer> Exam_Answers { get; set; } = new();

    [JsonPropertyName("section_scores")]
    public Dictionary<string, double> Section_Scores { get; set; } = new();

    [JsonPropertyName("auto_score")]
    public double? Auto_Score { get; set; }

    [JsonPropertyName("show_answers")]
    public bool Show_Answers { get; set; } = false;

    [JsonPropertyName("exam_review")]
    public ApiExamStructure? Exam_Review { get; set; }
}

public class ApiStatsResponse { [JsonPropertyName("stats")] public ApiStats Stats { get; set; } = new(); }
public class ApiStats
{
    [JsonPropertyName("total_students")] public int Total_Students { get; set; }
    [JsonPropertyName("total_experts")] public int Total_Experts { get; set; }
    [JsonPropertyName("total_topics")] public int Total_Topics { get; set; }
    [JsonPropertyName("total_cards")] public int Total_Cards { get; set; }
    [JsonPropertyName("total_assignments")] public int Total_Assignments { get; set; }
    [JsonPropertyName("total_submissions")] public int Total_Submissions { get; set; }
    [JsonPropertyName("total_api_calls")] public int Total_Api_Calls { get; set; }
}
public class UsersResponse { [JsonPropertyName("users")] public List<AuthUser> Users { get; set; } = new(); }
public class AiConfigResponse { [JsonPropertyName("config")] public ApiAiConfig Config { get; set; } = new(); }
public class ApiAiConfig
{
    [JsonPropertyName("model_name")] public string Model_Name { get; set; } = "gemini-3.8-flash";
    [JsonPropertyName("temperature")] public double Temperature { get; set; } = 0.4;
    [JsonPropertyName("max_output_tokens")] public int Max_Output_Tokens { get; set; } = 4096;
    [JsonPropertyName("anti_repetition")] public bool Anti_Repetition { get; set; } = true;
    [JsonPropertyName("system_instruction")] public string System_Instruction { get; set; } = "";
}
public class ApiLogItem
{
    [JsonPropertyName("timestamp")] public string Timestamp { get; set; } = "";
    [JsonPropertyName("method")] public string Method { get; set; } = "";
    [JsonPropertyName("endpoint")] public string Endpoint { get; set; } = "";
    [JsonPropertyName("status")] public int Status { get; set; }
    [JsonPropertyName("duration_ms")] public int Duration_Ms { get; set; }
}
public class LogsResponse { [JsonPropertyName("logs")] public List<ApiLogItem> Logs { get; set; } = new(); }

public class ApiConsultation
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("expert_id")] public string Expert_Id { get; set; } = "";
    [JsonPropertyName("expert_name")] public string Expert_Name { get; set; } = "";
    [JsonPropertyName("student_id")] public string Student_Id { get; set; } = "";
    [JsonPropertyName("student_name")] public string Student_Name { get; set; } = "";
    [JsonPropertyName("current_level")] public string Current_Level { get; set; } = "B1";
    [JsonPropertyName("recommended_level")] public string Recommended_Level { get; set; } = "B2";
    [JsonPropertyName("advice")] public string Advice { get; set; } = "";
    [JsonPropertyName("strengths")] public List<string> Strengths { get; set; } = new();
    [JsonPropertyName("weaknesses")] public List<string> Weaknesses { get; set; } = new();
    [JsonPropertyName("study_plan")] public string Study_Plan { get; set; } = "";
    [JsonPropertyName("created_at")] public string Created_At { get; set; } = "";
}

public class ConsultationsResponse { [JsonPropertyName("consultations")] public List<ApiConsultation> Consultations { get; set; } = new(); }

public class ApiStudentProgressItem
{
    [JsonPropertyName("user")] public AuthUser? User { get; set; }
    [JsonPropertyName("total_cards")] public int Total_Cards { get; set; }
    [JsonPropertyName("learned_cards")] public int Learned_Cards { get; set; }
    [JsonPropertyName("retention_rate")] public int Retention_Rate { get; set; }
    [JsonPropertyName("submissions_count")] public int Submissions_Count { get; set; }
    [JsonPropertyName("average_score")] public double Average_Score { get; set; }
    [JsonPropertyName("assessed_cefr")] public string Assessed_Cefr { get; set; } = "B2";
    [JsonPropertyName("recent_activity")] public string Recent_Activity { get; set; } = "";
}

public class StudentsProgressResponse { [JsonPropertyName("students")] public List<ApiStudentProgressItem> Students { get; set; } = new(); }

public class SkillAssignmentDraftDto
{
    [JsonPropertyName("topic")] public string Topic { get; set; } = string.Empty;
    [JsonPropertyName("skill")] public string Skill { get; set; } = "reading";
    [JsonPropertyName("target_level")] public string Target_Level { get; set; } = "B2";
    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
    [JsonPropertyName("instructions")] public string Instructions { get; set; } = string.Empty;
    [JsonPropertyName("content")] public string Content { get; set; } = string.Empty;
    [JsonPropertyName("questions")] public List<ApiReadingQuizQuestion> Questions { get; set; } = new();
}

public class GenerateSkillAssignmentResponse
{
    [JsonPropertyName("success")] public bool Success { get; set; }
    [JsonPropertyName("draft")] public SkillAssignmentDraftDto? Draft { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
}
