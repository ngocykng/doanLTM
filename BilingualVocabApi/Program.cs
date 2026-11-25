using System.Diagnostics;
using System.Text.Json;
using BilingualVocabApi.Models;
using BilingualVocabApi.Services;

static void LoadEnvFileIfExists(string startDirectory)
{
    var dir = new DirectoryInfo(startDirectory);
    for (int depth = 0; depth < 8 && dir is not null; depth++, dir = dir.Parent)
    {
        var envPath = Path.Combine(dir.FullName, ".env");
        if (!File.Exists(envPath)) continue;
        foreach (var raw in File.ReadLines(envPath))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || !line.Contains('=')) continue;
            var idx = line.IndexOf('=');
            var key = line[..idx].Trim();
            var value = line[(idx + 1)..].Trim().Trim('"', '\'');
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                Environment.SetEnvironmentVariable(key, value);
        }
        break;
    }
}

var builder = WebApplication.CreateBuilder(args);
LoadEnvFileIfExists(builder.Environment.ContentRootPath);
LoadEnvFileIfExists(AppContext.BaseDirectory);
LoadEnvFileIfExists(Directory.GetCurrentDirectory());
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
});

builder.Services.AddSingleton<JsonDatabase>();
builder.Services.AddSingleton<AiFallbackService>();
builder.Services.AddSingleton<TokenService>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();
app.UseCors();

// Middleware ghi API logs cho phân hệ F5 (Quản trị)
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? string.Empty;
    if (!path.StartsWith("/api", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/api/admin/api-logs", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/api/health", StringComparison.OrdinalIgnoreCase))
    {
        await next();
        return;
    }

    var sw = Stopwatch.StartNew();
    await next();
    sw.Stop();

    var db = context.RequestServices.GetRequiredService<JsonDatabase>();
    var authHeader = context.Request.Headers["x-user-email"].ToString();
    db.AddApiLog(new ApiLog
    {
        Id = $"log-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Guid.NewGuid():N}"[..24],
        Timestamp = DateTime.UtcNow.ToString("O"),
        Endpoint = path,
        Method = context.Request.Method,
        Status = context.Response.StatusCode,
        Duration_Ms = (int)sw.ElapsedMilliseconds,
        User_Email = string.IsNullOrWhiteSpace(authHeader) ? null : authHeader
    });
});

app.MapGet("/", (JsonDatabase db) => Results.Json(new
{
    name = "Bilingual Vocab AI - C# Backend API",
    status = "online",
    version = "1.0.0",
    time = DateTime.UtcNow,
    endpoints = new { health = "/api/health", vocab = "/api/vocab/*", video = "/api/video/*", translate = "/api/translate/*" }
}));

app.MapGet("/api/health", (JsonDatabase db) => Results.Json(new { status = "ok", service = "bilingual-vocab-api-csharp", database = db.StorageMode, time = DateTime.UtcNow }));

app.MapPost("/api/auth/login", (JsonDatabase db, TokenService tokens, JsonElement body) =>
{
    var email = body.TryGetProperty("email", out var e) ? e.GetString() : null;
    var password = body.TryGetProperty("password", out var p) ? p.GetString() : null;
    if (string.IsNullOrWhiteSpace(email)) return Results.BadRequest(new { error = "Email không được để trống" });
    var storedUser = db.GetUserByEmail(email);
    if (storedUser is null) return Results.Unauthorized();
    if (storedUser.Status == "suspended") return Results.StatusCode(403);
    if (!PasswordSecurity.VerifyPassword(password, storedUser.Password)) return Results.Unauthorized();
    if (!PasswordSecurity.IsHashed(storedUser.Password) && !string.IsNullOrEmpty(password))
        db.UpgradePasswordHash(storedUser.Id, password);
    db.UpdateUser(storedUser.Id, new Dictionary<string, object?> { ["last_active"] = "Vừa mới" });
    var user = db.GetUser(storedUser.Id);
    return Results.Json(new { success = true, user, token = tokens.IssueToken(user!.Id) });
});
app.MapPost("/api/auth/register", (JsonDatabase db, TokenService tokens, JsonElement body) =>
{
    var email = body.TryGetProperty("email", out var e) ? e.GetString() : null;
    var name = body.TryGetProperty("name", out var n) ? n.GetString() : null;
    var password = body.TryGetProperty("password", out var p) ? p.GetString() : null;
    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(password))
        return Results.BadRequest(new { error = "Vui lòng điền đầy đủ email, họ tên và mật khẩu" });
    if (db.GetUserByEmail(email) is not null) return Results.BadRequest(new { error = "Email này đã được đăng ký" });

    var user = new User
    {
        Id = $"user-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
        Email = email.Trim().ToLowerInvariant(),
        Password = PasswordSecurity.HashPassword(password),
        Name = name.Trim(),
        Role = "student",
        Student_Id = body.TryGetProperty("student_id", out var sid) ? sid.GetString() : null,
        Department = body.TryGetProperty("department", out var d) ? d.GetString() : "Khoa Kiến trúc",
        Expertise = body.TryGetProperty("expertise", out var ex) ? ex.GetString() : null,
        Target_Cefr = body.TryGetProperty("target_cefr", out var tc) ? tc.GetString() : "B2",
        Status = "active",
        Created_At = DateTime.UtcNow.ToString("O"),
        Last_Active = "Vừa mới"
    };

    var saved = db.AddUser(user);
    return Results.Json(new { success = true, user = saved, token = tokens.IssueToken(saved.Id) }, statusCode: 201);
});

app.MapGet("/api/auth/me", (HttpRequest request, JsonDatabase db) =>
{
    var user = GetAuthUser(request, db);
    return user is null ? Results.Unauthorized() : Results.Json(new { success = true, user });
});

app.MapGet("/api/auth/users", (HttpRequest request, JsonDatabase db) =>
{
    var user = GetAuthUser(request, db);
    if (user is null) return Results.Unauthorized();
    if (!user.Role.Equals("admin", StringComparison.OrdinalIgnoreCase) && !user.Role.Equals("expert", StringComparison.OrdinalIgnoreCase))
        return Results.Json(new { error = "Quyền truy cập bị từ chối." }, statusCode: 403);
    return Results.Json(new { success = true, users = db.GetUsers() });
});
app.MapPut("/api/auth/users/{id}", (HttpRequest request, string id, JsonDatabase db, Dictionary<string, object?> updates) =>
{
    var user = GetAuthUser(request, db);
    if (user is null) return Results.Unauthorized();
    if (!user.Role.Equals("admin", StringComparison.OrdinalIgnoreCase))
        return Results.Json(new { error = "Chỉ Admin mới được chỉnh sửa tài khoản." }, statusCode: 403);
    if (id == "user-admin-root" || db.GetUser(id)?.Email.Equals("admin@123", StringComparison.OrdinalIgnoreCase) == true)
        return Results.BadRequest(new { error = "Không thể thay đổi tài khoản quản trị cố định." });
    if (updates.TryGetValue("role", out var roleValue) && roleValue?.ToString() is not ("student" or "expert"))
        return Results.BadRequest(new { error = "Vai trò chỉ có thể là student hoặc expert." });
    var updated = db.UpdateUser(id, updates);
    return updated is null ? Results.NotFound(new { error = "Không tìm thấy người dùng" }) : Results.Json(new { success = true, user = updated });
});
app.MapDelete("/api/auth/users/{id}", (HttpRequest request, string id, JsonDatabase db) =>
{
    var user = GetAuthUser(request, db);
    if (user is null) return Results.Unauthorized();
    if (!user.Role.Equals("admin", StringComparison.OrdinalIgnoreCase))
        return Results.Json(new { error = "Chỉ Admin mới được xóa người dùng." }, statusCode: 403);
    return db.DeleteUser(id) ? Results.Json(new { success = true }) : Results.NotFound(new { error = "Không tìm thấy người dùng" });
});

// Helper xác thực HMAC-SHA256 Bearer token từ Authorization header
User? GetAuthUser(HttpRequest request, JsonDatabase db)
{
    var authHeader = request.Headers["Authorization"].ToString();
    if (string.IsNullOrWhiteSpace(authHeader)) return null;

    var token = authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
        ? authHeader[7..].Trim()
        : authHeader.Trim();
    var tokens = request.HttpContext.RequestServices.GetRequiredService<TokenService>();
    if (!tokens.TryValidateToken(token, out var userId)) return null;

    var user = db.GetUser(userId);
    if (user is null || string.Equals(user.Status, "suspended", StringComparison.OrdinalIgnoreCase))
        return null;
    return user;
}

IResult? RequireStudentVocabAccess(HttpRequest request, JsonDatabase db, string? requestedUserId, out User? student)
{
    student = GetAuthUser(request, db);
    if (student is null) return Results.Unauthorized();
    if (!student.Role.Equals("student", StringComparison.OrdinalIgnoreCase))
        return Results.Json(new { error = "Student account is required." }, statusCode: 403);
    if (!string.IsNullOrWhiteSpace(requestedUserId) && !string.Equals(requestedUserId, student.Id, StringComparison.Ordinal))
        return Results.Json(new { error = "Không thể truy cập dữ liệu từ vựng của tài khoản khác." }, statusCode: 403);
    return null;
}

var validCefrs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "A1", "A2", "B1", "B2", "C1", "C2" };
var validSkills = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "reading", "listening" };

IResult? RequireAssessmentRole(HttpRequest request, JsonDatabase db, string expectedRole, out User? user)
{
    user = GetAuthUser(request, db);
    if (user is null)
        return Results.Unauthorized();
    if (user.Role.Equals("admin", StringComparison.OrdinalIgnoreCase))
        return Results.Json(new { error = "Admin không có quyền truy cập phân hệ Khảo thí và chấm thi." }, statusCode: 403);
    if (!user.Role.Equals(expectedRole, StringComparison.OrdinalIgnoreCase))
        return Results.Json(new { error = $"Quyền truy cập bị từ chối. Chỉ {expectedRole} mới được thực hiện thao tác này." }, statusCode: 403);
    return null;
}

// ==========================================
// F4.1 EXPERT: TẠO / XEM / SỬA / XÓA BÀI TẬP & SINH ĐỀ AI
// ==========================================

// GET /api/expert/students/{id}/topics - Lấy chủ đề của sinh viên từ F2
app.MapGet("/api/expert/students/{id}/topics", (HttpRequest request, string id, JsonDatabase db) =>
{
    if (RequireAssessmentRole(request, db, "expert", out _) is { } denied) return denied;

    var student = db.GetUser(id);
    if (student is null || !student.Role.Equals("student", StringComparison.OrdinalIgnoreCase))
        return Results.NotFound(new { error = "Không tìm thấy sinh viên." });

    var topics = db.GetTopicsForUser(student.Id);
    return Results.Json(new { success = true, student, topics });
});

// POST /api/expert/assignments/generate-skill - AI nhận diện đề tài tự nhập và ra đề Reading/Listening
app.MapPost("/api/expert/assignments/generate-skill", async (HttpRequest request, JsonDatabase db, AiFallbackService ai, JsonElement body) =>
{
    if (RequireAssessmentRole(request, db, "expert", out _) is { } denied) return denied;

    var topic = body.TryGetProperty("topic", out var tp) ? tp.GetString() : null;
    var skill = body.TryGetProperty("skill", out var sk) ? sk.GetString() : "reading";
    var cefrLevel = body.TryGetProperty("cefr_level", out var cl) ? cl.GetString() : "B2";
    var qCount = body.TryGetProperty("question_count", out var qc) && qc.TryGetInt32(out var qcVal) ? qcVal : 4;

    if (string.IsNullOrWhiteSpace(topic))
        return Results.BadRequest(new { error = "Vui lòng nhập đề tài để AI nhận diện và ra đề." });
    if (!validSkills.Contains(skill ?? ""))
        return Results.BadRequest(new { error = "Kỹ năng phải là reading hoặc listening." });
    if (!validCefrs.Contains(cefrLevel ?? ""))
        return Results.BadRequest(new { error = "Cấp độ CEFR phải thuộc A1–C2." });

    var draft = await ai.GenerateSkillAssignmentAsync(topic.Trim(), skill!.Trim(), cefrLevel!.Trim(), qCount, db.GetAiConfig());
    return Results.Json(new { success = true, draft });
});

// POST /api/expert/assignments/generate - AI sinh đề thi theo chủ đề sinh viên (Bản xem trước)
app.MapPost("/api/expert/assignments/generate", async (HttpRequest request, JsonDatabase db, AiFallbackService ai, JsonElement body) =>
{
    if (RequireAssessmentRole(request, db, "expert", out _) is { } denied) return denied;

    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    var req = JsonSerializer.Deserialize<ExamGenerationRequestDto>(body.GetRawText(), options);
    if (req is null || string.IsNullOrWhiteSpace(req.Student_Id))
        return Results.BadRequest(new { error = "Vui lòng chọn sinh viên cần ra đề." });

    var student = db.GetUser(req.Student_Id);
    if (student is null)
        return Results.NotFound(new { error = "Không tìm thấy sinh viên được chỉ định." });

    var topicIds = req.Topic_Ids ?? new List<string>();
    var topics = topicIds.Select(tid => db.GetTopic(tid)).Where(t => t is not null).Cast<Topic>().ToList();
    bool hasCustomTopic = !string.IsNullOrWhiteSpace(req.Custom_Topic);
    if (!hasCustomTopic && topics.Count == 0)
        return Results.BadRequest(new { error = "Vui lòng nhập đề tài hoặc chọn ít nhất một chủ đề của sinh viên để sinh đề thi." });

    var cards = topicIds.SelectMany(tid => db.GetCardsByTopic(tid)).ToList();

    try
    {
        var examStructure = await ai.GenerateExamAsync(student, topics, cards, req, db.GetAiConfig());
        return Results.Json(new { success = true, exam_structure = examStructure });
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = $"Không thể sinh đề thi: {ex.Message}" }, statusCode: 502);
    }
});

// GET /api/expert/assignments - Danh sách assignment
app.MapGet("/api/expert/assignments", (HttpRequest request, string? expert_id, JsonDatabase db) =>
{
    if (RequireAssessmentRole(request, db, "expert", out var user) is { } denied) return denied;
    var targetExpertId = expert_id ?? user!.Id;
    return Results.Json(new { success = true, assignments = db.GetAssignments(targetExpertId) });
});

// GET /api/expert/assignments/{id} - Chi tiết assignment
app.MapGet("/api/expert/assignments/{id}", (HttpRequest request, string id, JsonDatabase db) =>
{
    if (RequireAssessmentRole(request, db, "expert", out _) is { } denied) return denied;
    var a = db.GetAssignment(id);
    return a is null ? Results.NotFound(new { error = "Không tìm thấy bài tập." }) : Results.Json(new { success = true, assignment = a });
});

// POST /api/expert/assignments - Tạo assignment mới (Hỗ trợ cấu trúc đề F4 và legacy)
app.MapPost("/api/expert/assignments", (HttpRequest request, JsonDatabase db, JsonElement body) =>
{
    if (RequireAssessmentRole(request, db, "expert", out var user) is { } denied) return denied;
    if (user is null) return Results.Unauthorized();

    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    // Hỗ trợ lưu đề thi sinh bằng AI
    if (body.TryGetProperty("exam_structure", out var esElement) && esElement.ValueKind == JsonValueKind.Object)
    {
        var examStruct = JsonSerializer.Deserialize<ExamStructure>(esElement.GetRawText(), options);
        var titleFromExam = body.TryGetProperty("title", out var et) ? et.GetString() : examStruct?.Exam_Title;
        var targetLevelExam = body.TryGetProperty("target_level", out var etl) ? etl.GetString() : "B2";
        var durationExam = body.TryGetProperty("duration_minutes", out var edm) && edm.TryGetInt32(out var edmVal) ? edmVal : (examStruct?.Duration_Minutes ?? 45);
        var dueDateExam = body.TryGetProperty("due_date", out var edue) ? edue.GetString() : DateTime.UtcNow.AddDays(7).ToString("yyyy-MM-dd");
        var statusExam = body.TryGetProperty("status", out var est) ? est.GetString() : "draft";
        var showAnswersExam = body.TryGetProperty("show_answers", out var esa) && esa.GetBoolean();
        var targetStudentId = body.TryGetProperty("target_student_id", out var etsid) ? etsid.GetString() : examStruct?.Student_Id;
        var targetStudentName = body.TryGetProperty("target_student_name", out var etsn) ? etsn.GetString() : examStruct?.Student_Name;

        var topicIds = body.TryGetProperty("topic_ids", out var etids) && etids.ValueKind == JsonValueKind.Array
            ? JsonSerializer.Deserialize<List<string>>(etids.GetRawText(), options) ?? new()
            : new List<string>();
        var topicNames = body.TryGetProperty("topic_names", out var etnames) && etnames.ValueKind == JsonValueKind.Array
            ? JsonSerializer.Deserialize<List<string>>(etnames.GetRawText(), options) ?? new()
            : new List<string>();

        if (string.IsNullOrWhiteSpace(titleFromExam))
            return Results.BadRequest(new { error = "Tiêu đề đề thi không được để trống." });
        if (durationExam <= 0)
            return Results.BadRequest(new { error = "Thời gian làm bài phải lớn hơn 0 phút." });

        var examAssign = new Assignment
        {
            Id = $"assign-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            Expert_Id = user.Id,
            Expert_Name = user.Name,
            Title = titleFromExam.Trim(),
            Skill = "exam",
            Target_Level = !string.IsNullOrWhiteSpace(targetLevelExam) ? targetLevelExam.Trim().ToUpperInvariant() : "B2",
            Duration_Minutes = durationExam,
            Content = examStruct?.Sections.FirstOrDefault(s => s.Section_Type == "cloze")?.Passage ?? string.Empty,
            Instructions = "Đề thi gồm các câu hỏi trắc nghiệm và câu sắp xếp từ theo mẫu khảo thí.",
            Due_Date = dueDateExam,
            Created_At = DateTime.UtcNow.ToString("O"),
            Max_Score = examStruct?.Total_Score > 0 ? examStruct.Total_Score : 10.0,
            Target_Student_Id = targetStudentId,
            Target_Student_Name = targetStudentName,
            Topic_Ids = topicIds,
            Topic_Names = topicNames,
            Status = string.Equals(statusExam, "published", StringComparison.OrdinalIgnoreCase) ? "published" : "draft",
            Show_Answers = showAnswersExam,
            Exam_Structure = examStruct
        };

        var savedExam = db.AddAssignment(examAssign);
        return Results.Json(new { success = true, assignment = savedExam }, statusCode: 201);
    }

    var title = body.TryGetProperty("title", out var t) ? t.GetString() : null;
    var skill = body.TryGetProperty("skill", out var s) ? s.GetString() : "reading";
    var targetLevel = body.TryGetProperty("target_level", out var tl) ? tl.GetString() : "B2";
    var duration = body.TryGetProperty("duration_minutes", out var dm) && dm.TryGetInt32(out var dmVal) ? dmVal : 45;
    var content = body.TryGetProperty("content", out var c) ? c.GetString() : string.Empty;
    var instructions = body.TryGetProperty("instructions", out var i) ? i.GetString() : string.Empty;
    var referenceMediaUrl = body.TryGetProperty("reference_media_url", out var rmu) ? rmu.GetString() : null;
    var dueDate = body.TryGetProperty("due_date", out var due) ? due.GetString() : DateTime.UtcNow.AddDays(7).ToString("yyyy-MM-dd");
    var maxScore = body.TryGetProperty("max_score", out var ms) && ms.TryGetDouble(out var msVal) ? msVal : 10.0;
    var readingQuiz = body.TryGetProperty("reading_quiz", out var rq) ? (rq.ValueKind == JsonValueKind.String ? rq.GetString() : rq.GetRawText()) : null;
    var manualTopicNames = body.TryGetProperty("topic_names", out var tnEl) && tnEl.ValueKind == JsonValueKind.Array
        ? JsonSerializer.Deserialize<List<string>>(tnEl.GetRawText(), options) ?? new()
        : new List<string>();
    if (manualTopicNames.Count == 0 && body.TryGetProperty("topic", out var singleTopic) && !string.IsNullOrWhiteSpace(singleTopic.GetString()))
        manualTopicNames.Add(singleTopic.GetString()!.Trim());

    if (string.IsNullOrWhiteSpace(title))
        return Results.BadRequest(new { error = "Đề bài / tiêu đề không được để trống." });
    if (duration <= 0)
        return Results.BadRequest(new { error = "Thời gian làm bài phải lớn hơn 0 phút." });
    if (!validCefrs.Contains(targetLevel ?? ""))
        return Results.BadRequest(new { error = "Cấp độ CEFR phải thuộc A1–C2." });
    if (!validSkills.Contains(skill ?? ""))
        return Results.BadRequest(new { error = "Kỹ năng phải là reading hoặc listening." });

    var a = new Assignment
    {
        Id = $"assign-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
        Expert_Id = user.Id,
        Expert_Name = user.Name,
        Title = title.Trim(),
        Skill = skill!.Trim().ToLowerInvariant(),
        Target_Level = targetLevel!.Trim().ToUpperInvariant(),
        Duration_Minutes = duration,
        Content = content ?? string.Empty,
        Instructions = instructions ?? string.Empty,
        Reference_Media_Url = string.IsNullOrWhiteSpace(referenceMediaUrl) ? null : referenceMediaUrl.Trim(),
        Due_Date = dueDate,
        Created_At = DateTime.UtcNow.ToString("O"),
        Max_Score = maxScore > 0 ? maxScore : 10.0,
        Reading_Quiz = readingQuiz,
        Topic_Names = manualTopicNames,
        Status = "published"
    };

    var saved = db.AddAssignment(a);
    return Results.Json(new { success = true, assignment = saved }, statusCode: 201);
});

// PUT /api/expert/assignments/{id} - Sửa assignment
app.MapPut("/api/expert/assignments/{id}", (HttpRequest request, string id, JsonDatabase db, JsonElement body) =>
{
    if (RequireAssessmentRole(request, db, "expert", out var user) is { } denied) return denied;

    var existing = db.GetAssignment(id);
    if (existing is null)
        return Results.NotFound(new { error = "Không tìm thấy bài tập để chỉnh sửa." });

    if (user is not null && existing.Expert_Id != user.Id && user.Id != "user-expert-root" && !string.IsNullOrWhiteSpace(existing.Expert_Id))
        return Results.Json(new { error = "Bạn chỉ có thể chỉnh sửa bài tập do chính mình tạo." }, statusCode: 403);

    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    ExamStructure? examStruct = null;
    if (body.TryGetProperty("exam_structure", out var esElement) && esElement.ValueKind == JsonValueKind.Object)
    {
        examStruct = JsonSerializer.Deserialize<ExamStructure>(esElement.GetRawText(), options);
    }

    var updates = new AssignmentUpdateDto
    {
        Title = body.TryGetProperty("title", out var t) ? t.GetString() : null,
        Skill = body.TryGetProperty("skill", out var s) ? s.GetString() : null,
        Target_Level = body.TryGetProperty("target_level", out var tl) ? tl.GetString() : null,
        Duration_Minutes = body.TryGetProperty("duration_minutes", out var dm) && dm.TryGetInt32(out var dmVal) ? dmVal : null,
        Content = body.TryGetProperty("content", out var c) ? c.GetString() : null,
        Instructions = body.TryGetProperty("instructions", out var i) ? i.GetString() : null,
        Reference_Media_Url = body.TryGetProperty("reference_media_url", out var rmu) ? rmu.GetString() : null,
        Due_Date = body.TryGetProperty("due_date", out var due) ? due.GetString() : null,
        Max_Score = body.TryGetProperty("max_score", out var ms) && ms.TryGetDouble(out var msVal) ? msVal : null,
        Reading_Quiz = body.TryGetProperty("reading_quiz", out var rq) ? (rq.ValueKind == JsonValueKind.String ? rq.GetString() : rq.GetRawText()) : null,
        Target_Student_Id = body.TryGetProperty("target_student_id", out var tsid) ? tsid.GetString() : null,
        Target_Student_Name = body.TryGetProperty("target_student_name", out var tsn) ? tsn.GetString() : null,
        Topic_Names = body.TryGetProperty("topic_names", out var tnEl) && tnEl.ValueKind == JsonValueKind.Array
            ? JsonSerializer.Deserialize<List<string>>(tnEl.GetRawText(), options)
            : (body.TryGetProperty("topic", out var singleTopic) && !string.IsNullOrWhiteSpace(singleTopic.GetString())
                ? new List<string> { singleTopic.GetString()!.Trim() }
                : null),
        Status = body.TryGetProperty("status", out var st) ? st.GetString() : null,
        Show_Answers = body.TryGetProperty("show_answers", out var sa) ? sa.GetBoolean() : null,
        Exam_Structure = examStruct
    };

    if (updates.Duration_Minutes.HasValue && updates.Duration_Minutes.Value <= 0)
        return Results.BadRequest(new { error = "Thời gian làm bài phải lớn hơn 0 phút." });
    if (!string.IsNullOrWhiteSpace(updates.Target_Level) && !validCefrs.Contains(updates.Target_Level))
        return Results.BadRequest(new { error = "Cấp độ CEFR phải thuộc A1–C2." });
    if (!string.IsNullOrWhiteSpace(updates.Skill)
        && !updates.Skill.Equals("exam", StringComparison.OrdinalIgnoreCase)
        && !validSkills.Contains(updates.Skill))
        return Results.BadRequest(new { error = "Kỹ năng phải là reading hoặc listening." });

    var updated = db.UpdateAssignment(id, updates);
    return Results.Json(new { success = true, assignment = updated });
});

// POST /api/expert/assignments/{id}/publish - Giao đề cho sinh viên (Chuyển Nháp -> Đã giao)
app.MapPost("/api/expert/assignments/{id}/publish", (HttpRequest request, string id, JsonDatabase db) =>
{
    if (RequireAssessmentRole(request, db, "expert", out var user) is { } denied) return denied;

    var existing = db.GetAssignment(id);
    if (existing is null) return Results.NotFound(new { error = "Không tìm thấy đề thi." });

    if (existing.Expert_Id != user!.Id && user.Id != "user-expert-root" && !string.IsNullOrWhiteSpace(existing.Expert_Id))
        return Results.Json(new { error = "Bạn chỉ có thể giao đề do chính mình tạo." }, statusCode: 403);

    var published = db.PublishAssignment(id);
    return Results.Json(new { success = true, assignment = published, message = "Đã giao đề thi cho sinh viên thành công." });
});

// PUT /api/expert/assignments/{id}/show-answers - Bật / tắt tính năng cho xem đáp án đúng
app.MapPut("/api/expert/assignments/{id}/show-answers", (HttpRequest request, string id, JsonDatabase db, JsonElement body) =>
{
    if (RequireAssessmentRole(request, db, "expert", out var user) is { } denied) return denied;

    var existing = db.GetAssignment(id);
    if (existing is null) return Results.NotFound(new { error = "Không tìm thấy đề thi." });

    if (existing.Expert_Id != user!.Id && user.Id != "user-expert-root" && !string.IsNullOrWhiteSpace(existing.Expert_Id))
        return Results.Json(new { error = "Bạn chỉ có thể đổi cài đặt đề do chính mình tạo." }, statusCode: 403);

    bool show = body.TryGetProperty("show_answers", out var sa) && sa.GetBoolean();
    var updated = db.SetShowAnswers(id, show);
    return Results.Json(new { success = true, assignment = updated, message = $"Đã {(show ? "bật" : "tắt")} tính năng cho sinh viên xem đáp án đúng." });
});

// DELETE /api/expert/assignments/{id} - Xóa assignment
app.MapDelete("/api/expert/assignments/{id}", (HttpRequest request, string id, JsonDatabase db) =>
{
    if (RequireAssessmentRole(request, db, "expert", out var user) is { } denied) return denied;

    var existing = db.GetAssignment(id);
    if (existing is null)
        return Results.NotFound(new { error = "Không tìm thấy bài tập cần xóa." });

    if (existing.Expert_Id != user!.Id && user.Id != "user-expert-root" && !string.IsNullOrWhiteSpace(existing.Expert_Id))
        return Results.Json(new { error = "Bạn chỉ có thể xóa bài tập do chính mình tạo." }, statusCode: 403);

    var deleted = db.DeleteAssignment(id);
    return deleted ? Results.Json(new { success = true, message = "Đã xóa bài tập thành công." }) : Results.NotFound(new { error = "Không tìm thấy bài tập." });
});

// ==========================================
// F4.2 & F4.4 STUDENT: LÀM BÀI VÀ XEM KẾT QUẢ
// ==========================================

// GET /api/student/assignments - Danh sách assignment được giao (Chỉ lấy đề published, bảo mật không lộ đáp án đúng)
app.MapGet("/api/student/assignments", (HttpRequest request, JsonDatabase db) =>
{
    if (RequireAssessmentRole(request, db, "student", out var user) is { } denied) return denied;
    return Results.Json(new { success = true, assignments = db.GetStudentAssignments(user!.Id) });
});

// GET /api/student/assignments/{id} - Lấy chi tiết đề thi làm bài (Đã làm sạch đáp án đúng)
app.MapGet("/api/student/assignments/{id}", (HttpRequest request, string id, JsonDatabase db) =>
{
    if (RequireAssessmentRole(request, db, "student", out var user) is { } denied) return denied;
    var a = db.GetSanitizedAssignmentForStudent(id, user!.Id);
    return a is null ? Results.NotFound(new { error = "Không tìm thấy đề thi hoặc đề chưa được giao." }) : Results.Json(new { success = true, assignment = a });
});

// POST /api/student/submissions - Nộp bài (Hỗ trợ cấu trúc đề F4 tự chấm và legacy)
app.MapPost("/api/student/submissions", (HttpRequest request, JsonDatabase db, JsonElement body) =>
{
    if (RequireAssessmentRole(request, db, "student", out var user) is { } denied) return denied;
    if (user is null) return Results.Unauthorized();

    var assignmentId = body.TryGetProperty("assignment_id", out var aid) ? aid.GetString() : null;
    if (string.IsNullOrWhiteSpace(assignmentId))
        return Results.BadRequest(new { error = "Vui lòng chọn bài thi cần nộp (assignment_id)." });

    var assignment = db.GetAssignment(assignmentId);
    if (assignment is null)
        return Results.NotFound(new { error = "Không tìm thấy bài thi." });

    var content = body.TryGetProperty("content", out var c) ? c.GetString() : null;
    var audioUrl = body.TryGetProperty("audio_url", out var au) ? au.GetString() : null;
    var readingAnswers = body.TryGetProperty("reading_answers", out var ra) ? (ra.ValueKind == JsonValueKind.String ? ra.GetString() : ra.GetRawText()) : null;
    var topicSentence = body.TryGetProperty("topic_sentence", out var ts) ? ts.GetString() : null;
    var durationSpent = body.TryGetProperty("duration_spent", out var ds) && ds.TryGetInt32(out var dsVal) ? dsVal : (int?)null;

    var examAnswers = new List<StudentExamAnswer>();
    if (body.TryGetProperty("answers", out var ansDictElement) && ansDictElement.ValueKind == JsonValueKind.Object)
    {
        foreach (var prop in ansDictElement.EnumerateObject())
        {
            if (int.TryParse(prop.Name, out int qNum))
            {
                examAnswers.Add(new StudentExamAnswer
                {
                    Question_Number = qNum,
                    Selected_Answer = prop.Value.GetString() ?? ""
                });
            }
        }
    }
    else if (body.TryGetProperty("exam_answers", out var eaListElement) && eaListElement.ValueKind == JsonValueKind.Array)
    {
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        examAnswers = JsonSerializer.Deserialize<List<StudentExamAnswer>>(eaListElement.GetRawText(), opts) ?? new();
    }

    if (examAnswers.Count == 0 && string.IsNullOrWhiteSpace(content) && string.IsNullOrWhiteSpace(audioUrl) && string.IsNullOrWhiteSpace(readingAnswers) && string.IsNullOrWhiteSpace(topicSentence))
        return Results.BadRequest(new { error = "Nội dung bài làm không được để trống." });

    var s = new Submission
    {
        Id = $"sub-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
        Assignment_Id = assignment.Id,
        Assignment_Title = assignment.Title,
        Skill = assignment.Skill,
        Student_Id = user.Id,
        Student_Name = user.Name,
        Student_Code = user.Student_Id,
        Student_Department = user.Department,
        Content = content ?? (examAnswers.Count > 0 ? $"Sinh viên đã hoàn thành {examAnswers.Count} câu hỏi của đề thi." : string.Empty),
        Audio_Url = audioUrl,
        Reading_Answers = readingAnswers,
        Topic_Sentence = topicSentence,
        Duration_Spent = durationSpent,
        Exam_Answers = examAnswers,
        Submitted_At = DateTime.UtcNow.ToString("O"),
        Status = "submitted"
    };

    var saved = db.AddSubmission(s);
    return Results.Json(new { success = true, submission = saved }, statusCode: 201);
});

// Alias tương thích: POST /api/student/assignments/{id}/submit
app.MapPost("/api/student/assignments/{id}/submit", (HttpRequest request, string id, JsonDatabase db, JsonElement body) =>
{
    if (RequireAssessmentRole(request, db, "student", out var user) is { } denied) return denied;
    if (user is null) return Results.Unauthorized();

    var assignment = db.GetAssignment(id);
    if (assignment is null)
        return Results.NotFound(new { error = "Không tìm thấy bài tập." });

    var content = body.TryGetProperty("content", out var c) ? c.GetString() : null;
    var audio = body.TryGetProperty("audio_url", out var au) ? au.GetString() : null;
    var readingAnswers = body.TryGetProperty("reading_answers", out var ra) ? (ra.ValueKind == JsonValueKind.String ? ra.GetString() : ra.GetRawText()) : null;
    var topicSentence = body.TryGetProperty("topic_sentence", out var ts) ? ts.GetString() : null;

    var s = new Submission
    {
        Id = $"sub-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
        Assignment_Id = id,
        Assignment_Title = assignment.Title,
        Skill = assignment.Skill,
        Student_Id = user.Id,
        Student_Name = user.Name,
        Student_Code = user.Student_Id,
        Student_Department = user.Department,
        Content = content ?? string.Empty,
        Audio_Url = audio,
        Reading_Answers = readingAnswers,
        Topic_Sentence = topicSentence,
        Submitted_At = DateTime.UtcNow.ToString("O"),
        Status = "submitted"
    };

    return Results.Json(new { success = true, submission = db.AddSubmission(s) }, statusCode: 201);
});

// GET /api/student/submissions - F4.4 Xem kết quả chấm & lịch sử
app.MapGet("/api/student/submissions", (HttpRequest request, string? student_id, JsonDatabase db) =>
{
    if (RequireAssessmentRole(request, db, "student", out var user) is { } denied) return denied;
    return Results.Json(new { success = true, submissions = db.GetSubmissionsForStudent(user!.Id) });
});

// GET /api/student/submissions/{id} - Xem chi tiết bài nộp của sinh viên
app.MapGet("/api/student/submissions/{id}", (HttpRequest request, string id, JsonDatabase db) =>
{
    if (RequireAssessmentRole(request, db, "student", out var user) is { } denied) return denied;
    var sub = db.GetSanitizedSubmissionForStudent(id, user!.Id);
    return sub is null ? Results.NotFound(new { error = "Không tìm thấy bài nộp." }) : Results.Json(new { success = true, submission = sub });
});

// ==========================================
// F4.3 EXPERT: CHẤM ĐIỂM VÀ ĐÁNH GIÁ CEFR
// ==========================================

// GET /api/expert/submissions - Danh sách bài nộp của sinh viên
app.MapGet("/api/expert/submissions", (HttpRequest request, string? status, string? assignment_id, JsonDatabase db) =>
{
    if (RequireAssessmentRole(request, db, "expert", out _) is { } denied) return denied;
    return Results.Json(new { success = true, submissions = db.GetSubmissions(null, assignment_id, status) });
});

// GET /api/expert/submissions/{id} - Chi tiết một bài nộp
app.MapGet("/api/expert/submissions/{id}", (HttpRequest request, string id, JsonDatabase db) =>
{
    if (RequireAssessmentRole(request, db, "expert", out _) is { } denied) return denied;
    var sub = db.GetSubmission(id);
    return sub is null ? Results.NotFound(new { error = "Không tìm thấy bài nộp." }) : Results.Json(new { success = true, submission = sub });
});

// PUT & POST /api/expert/submissions/{id}/grade - Lưu kết quả chấm
IResult HandleGradeSubmission(HttpRequest request, string id, JsonDatabase db, JsonElement body)
{
    if (RequireAssessmentRole(request, db, "expert", out var user) is { } denied) return denied;
    if (user is null) return Results.Unauthorized();

    var sub = db.GetSubmission(id);
    if (sub is null)
        return Results.NotFound(new { error = "Không tìm thấy bài nộp." });

    var feedback = body.TryGetProperty("feedback", out var f) ? f.GetString() : null;
    var score = body.TryGetProperty("score", out var s) && s.TryGetDouble(out var sv) ? sv : (double?)null;
    var assessedLevel = body.TryGetProperty("assessed_level", out var al) ? al.GetString() : "B2";

    // Quy tắc kiểm tra kết quả chấm
    if (!score.HasValue)
        return Results.BadRequest(new { error = "Vui lòng nhập điểm số của bài làm." });
    if (score.Value < 0.0 || score.Value > 10.0)
        return Results.BadRequest(new { error = "Điểm số phải nằm trong thang điểm quy định từ 0 đến 10." });
    if (string.IsNullOrWhiteSpace(feedback))
        return Results.BadRequest(new { error = "Nhận xét (feedback) không được để trống." });
    if (!validCefrs.Contains(assessedLevel ?? ""))
        return Results.BadRequest(new { error = "Cấp độ CEFR phải thuộc A1–C2." });

    var graded = db.GradeSubmission(id, score.Value, feedback.Trim(), user.Name, assessedLevel!.Trim().ToUpperInvariant());
    return Results.Json(new { success = true, submission = graded });
}

app.MapPut("/api/expert/submissions/{id}/grade", HandleGradeSubmission);
app.MapPost("/api/expert/submissions/{id}/grade", HandleGradeSubmission);

app.MapGet("/api/expert/consultations", (HttpRequest request, string? student_id, JsonDatabase db) =>
{
    if (RequireAssessmentRole(request, db, "expert", out _) is { } denied) return denied;
    return Results.Json(new { success = true, consultations = db.GetConsultations(student_id) });
});
app.MapPost("/api/expert/consultations", (HttpRequest request, JsonDatabase db, JsonElement body) =>
{
    if (RequireAssessmentRole(request, db, "expert", out var user) is { } denied) return denied;

    var studentId = body.TryGetProperty("student_id", out var sid) ? sid.GetString() : null;
    var advice = body.TryGetProperty("advice", out var adv) ? adv.GetString() : null;
    var level = body.TryGetProperty("recommended_level", out var rl) ? rl.GetString() : null;
    if (string.IsNullOrWhiteSpace(studentId) || string.IsNullOrWhiteSpace(advice) || string.IsNullOrWhiteSpace(level))
        return Results.BadRequest(new { error = "Thiếu thông tin tư vấn" });

    var student = db.GetUser(studentId);
    var strengths = body.TryGetProperty("strengths", out var stEl) && stEl.ValueKind == JsonValueKind.Array
        ? stEl.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => !string.IsNullOrWhiteSpace(x)).ToList()
        : new List<string> { "Nền tảng tốt" };
    var weaknesses = body.TryGetProperty("weaknesses", out var wkEl) && wkEl.ValueKind == JsonValueKind.Array
        ? wkEl.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => !string.IsNullOrWhiteSpace(x)).ToList()
        : new List<string> { "Cần luyện thêm" };

    var c = new Consultation
    {
        Id = $"cons-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
        Expert_Id = user?.Id ?? (body.TryGetProperty("expert_id", out var e) ? e.GetString() ?? "user-expert-root" : "user-expert-root"),
        Expert_Name = user?.Name ?? (body.TryGetProperty("expert_name", out var en) ? en.GetString() ?? "Chuyên gia" : "Chuyên gia"),
        Student_Id = studentId,
        Student_Name = student?.Name ?? (body.TryGetProperty("student_name", out var sn) ? sn.GetString() ?? "Sinh viên" : "Sinh viên"),
        Current_Level = body.TryGetProperty("current_level", out var cl) ? cl.GetString() ?? "B1" : "B1",
        Recommended_Level = level.Trim().ToUpperInvariant(),
        Advice = advice.Trim(),
        Strengths = strengths,
        Weaknesses = weaknesses,
        Study_Plan = body.TryGetProperty("study_plan", out var sp) ? sp.GetString() ?? "30 phút mỗi ngày" : "30 phút mỗi ngày"
    };

    return Results.Json(new { success = true, consultation = db.AddConsultation(c) }, statusCode: 201);
});
app.MapGet("/api/expert/students-progress", (HttpRequest request, JsonDatabase db) =>
{
    if (RequireAssessmentRole(request, db, "expert", out _) is { } denied) return denied;
    return Results.Json(new { success = true, students = db.GetStudentProgress() });
});
app.MapGet("/api/student/consultations", (HttpRequest request, string? student_id, JsonDatabase db) =>
{
    if (RequireAssessmentRole(request, db, "student", out var user) is { } denied) return denied;
    return Results.Json(new { success = true, consultations = db.GetConsultations(user!.Id) });
});

IResult? RequireAdmin(HttpRequest request, JsonDatabase db)
{
    var user = GetAuthUser(request, db);
    if (user is null) return Results.Unauthorized();
    if (!user.Role.Equals("admin", StringComparison.OrdinalIgnoreCase))
        return Results.Json(new { error = "Chỉ Admin mới có quyền thực hiện thao tác này." }, statusCode: 403);
    return null;
}

app.MapGet("/api/admin/stats", (HttpRequest request, JsonDatabase db) => RequireAdmin(request, db) ?? Results.Json(new { success = true, stats = db.GetAdminStats() }));
app.MapGet("/api/admin/ai-config", (HttpRequest request, JsonDatabase db) => RequireAdmin(request, db) ?? Results.Json(new { success = true, config = db.GetAiConfig() }));
app.MapPut("/api/admin/ai-config", (HttpRequest request, JsonDatabase db, AiConfig cfg) => RequireAdmin(request, db) ?? Results.Json(new { success = true, config = db.UpdateAiConfig(cfg) }));
app.MapGet("/api/admin/api-logs", (HttpRequest request, int? limit, JsonDatabase db) => RequireAdmin(request, db) ?? Results.Json(new { success = true, logs = db.GetApiLogs(limit ?? 50) }));
app.MapGet("/api/admin/dau-curriculum", (HttpRequest request, JsonDatabase db) =>
{
    if (RequireAdmin(request, db) is { } denied) return denied;
    var topics = db.GetTopics().Where(t => t.Is_Dau_Curriculum || t.Name.StartsWith("DAU:", StringComparison.OrdinalIgnoreCase)).ToList();
    return Results.Json(new { success = true, topics });
});
app.MapPut("/api/admin/dau-curriculum", (HttpRequest request, JsonDatabase db, JsonElement body) =>
{
    if (RequireAdmin(request, db) is { } denied) return denied;
    if (!body.TryGetProperty("topics", out var topicsElement) || topicsElement.ValueKind != JsonValueKind.Array)
        return Results.BadRequest(new { error = "Danh sách chương trình không hợp lệ." });
    var topics = JsonSerializer.Deserialize<List<Topic>>(topicsElement.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }) ?? new();
    if (topics.Any(t => string.IsNullOrWhiteSpace(t.Name))) return Results.BadRequest(new { error = "Tên chủ đề không được để trống." });
    return Results.Json(new { success = true, topics = db.ReplaceDauCurriculum(topics) });
});
app.MapPost("/api/admin/sync-mysql", (HttpRequest request, JsonDatabase db) =>
{
    if (RequireAdmin(request, db) is { } denied) return denied;
    return db.TrySyncToMySql(out var message)
        ? Results.Json(new { success = true, message, database = db.StorageMode })
        : Results.Json(new { success = false, error = message, database = db.StorageMode }, statusCode: 503);
});

app.MapPost("/api/vocab/generate", async (HttpRequest request, JsonDatabase db, AiFallbackService ai, JsonElement body) =>
{
    var topicName = body.TryGetProperty("topic", out var t) ? t.GetString() : null;
    var requestedOwnerId = body.TryGetProperty("user_id", out var uid) ? uid.GetString() : null;
    if (RequireStudentVocabAccess(request, db, requestedOwnerId, out var student) is { } denied) return denied;
    var ownerId = student!.Id;
    if (string.IsNullOrWhiteSpace(topicName) || topicName.Trim().Length > 80) return Results.BadRequest(new { error = "Tên chủ đề phải có từ 1 đến 80 ký tự." });
    var level = body.TryGetProperty("level", out var l) ? l.GetString()?.Trim().ToUpperInvariant() : null;
    if (level is null || !new[] { "A1", "A2", "B1", "B2", "C1", "C2" }.Contains(level))
        return Results.BadRequest(new { error = "Vui lòng chọn cấp CEFR từ A1 đến C2." });
    if (db.GetTopicsForUser(ownerId).Any(topic => topic.Name.Equals(topicName.Trim(), StringComparison.OrdinalIgnoreCase)))
        return Results.Conflict(new { error = "Tên chủ đề đã tồn tại. Hãy chọn tên khác." });
    var topicId = $"topic-{Guid.NewGuid():N}";
    var topic = new Topic { Id = topicId, Owner_User_Id = ownerId, Name = topicName.Trim(), Created_At = DateTime.UtcNow.ToString("O"), Level = level };
    try
    {
        var cards = await ai.GenerateVocabAsync(topic.Name, level, topicId, db.GetAiConfig());
        cards = cards
            .Where(card => !string.IsNullOrWhiteSpace(card.Word_En) && !string.IsNullOrWhiteSpace(card.Meaning_Vi))
            .GroupBy(card => card.Word_En.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        if (cards.Count == 0) throw new JsonException("Gemini không trả về thẻ từ vựng hợp lệ.");
        topic.Card_Count = cards.Count;
        db.AddTopic(topic);
        db.AddCards(cards);
        return Results.Json(new { success = true, topic, cards });
    }
    catch (InvalidOperationException ex) { return Results.Json(new { error = ex.Message }, statusCode: 503); }
    catch (HttpRequestException ex) { return Results.Json(new { error = $"Gemini API: {ex.Message}" }, statusCode: 502); }
    catch (TaskCanceledException ex) { return Results.Json(new { error = $"Gemini API timeout: {ex.Message}" }, statusCode: 504); }
    catch (JsonException ex) { return Results.Json(new { error = $"Không đọc được phản hồi Gemini: {ex.Message}" }, statusCode: 502); }
});

app.MapPost("/api/vocab/generate-batch", async (HttpRequest request, JsonDatabase db, AiFallbackService ai, JsonElement body) =>
{
    var topicId = body.TryGetProperty("topic_id", out var tid) ? tid.GetString() : null;
    var requestedOwnerId = body.TryGetProperty("user_id", out var uid) ? uid.GetString() : null;
    if (RequireStudentVocabAccess(request, db, requestedOwnerId, out var student) is { } denied) return denied;
    var ownerId = student!.Id;
    if (string.IsNullOrWhiteSpace(topicId))
        return Results.BadRequest(new { error = "Chủ đề không hợp lệ." });

    var topic = db.GetTopicForUser(topicId, ownerId);
    if (topic is null) return Results.NotFound(new { error = "Không tìm thấy chủ đề hoặc bạn không có quyền." });

    var existingCards = db.GetCardsByTopic(topicId);
    var existingWords = existingCards.Select(card => card.Word_En).Where(word => !string.IsNullOrWhiteSpace(word)).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var batchIndex = existingCards.Count / 15 + 1;
    try
    {
        var newCards = await ai.GenerateVocabAsync(
            topic.Name, topic.Level ?? "B1", topicId, db.GetAiConfig(),
            existingWords, batchIndex, existingCards.Count);
        newCards = newCards
            .Where(card => !string.IsNullOrWhiteSpace(card.Word_En)
                && !string.IsNullOrWhiteSpace(card.Meaning_Vi)
                && !existingWords.Contains(card.Word_En.Trim()))
            .GroupBy(card => card.Word_En.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        if (newCards.Count == 0)
            return Results.Json(new { error = "Không còn từ mới phù hợp cho chủ đề này." }, statusCode: 422);

        db.AddCards(newCards);
        db.UpdateTopicCardCount(topicId);
        return Results.Json(new
        {
            success = true,
            topic = db.GetTopic(topicId),
            new_cards = newCards,
            total_cards = existingCards.Count + newCards.Count
        });
    }
    catch (InvalidOperationException ex) { return Results.Json(new { error = ex.Message }, statusCode: 503); }
    catch (HttpRequestException ex) { return Results.Json(new { error = $"Gemini API: {ex.Message}" }, statusCode: 502); }
    catch (TaskCanceledException ex) { return Results.Json(new { error = $"Gemini API timeout: {ex.Message}" }, statusCode: 504); }
    catch (JsonException ex) { return Results.Json(new { error = $"Không đọc được phản hồi Gemini: {ex.Message}" }, statusCode: 502); }
});

app.MapPost("/api/vocab/autofill", (HttpRequest request, JsonDatabase db, AiFallbackService ai, JsonElement body) =>
{
    if (GetAuthUser(request, db) is null) return Results.Unauthorized();
    var input = body.TryGetProperty("input_word", out var iw) ? iw.GetString() : body.TryGetProperty("inputWord", out var iw2) ? iw2.GetString() : null;
    if (string.IsNullOrWhiteSpace(input)) return Results.BadRequest(new { error = "Vui lòng nhập từ hoặc cụm từ." });
    return Results.Json(new { success = true, data = ai.AutoFill(input) });
});

app.MapGet("/api/vocab/topics", (HttpRequest request, string? user_id, JsonDatabase db) =>
{
    if (RequireStudentVocabAccess(request, db, user_id, out var student) is { } denied) return denied;
    return Results.Json(new { success = true, topics = db.GetTopicsForUser(student!.Id) });
});
app.MapPost("/api/vocab/topics", (HttpRequest request, JsonDatabase db, JsonElement body) =>
{
    var name = body.TryGetProperty("name", out var n) ? n.GetString() : null;
    var requestedOwnerId = body.TryGetProperty("user_id", out var uid) ? uid.GetString() : null;
    if (RequireStudentVocabAccess(request, db, requestedOwnerId, out var student) is { } denied) return denied;
    var ownerId = student!.Id;
    if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 80) return Results.BadRequest(new { error = "Tên chủ đề phải có từ 1 đến 80 ký tự." });
    if (db.GetTopicsForUser(ownerId).Any(topic => topic.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)))
        return Results.Conflict(new { error = "Tên chủ đề đã tồn tại. Hãy chọn tên khác." });
    var level = body.TryGetProperty("level", out var l) ? l.GetString()?.Trim().ToUpperInvariant() : null;
    if (level is null || !new[] { "A1", "A2", "B1", "B2", "C1", "C2" }.Contains(level))
        return Results.BadRequest(new { error = "Vui lòng chọn cấp CEFR từ A1 đến C2." });
    var topic = new Topic { Id = $"topic-{Guid.NewGuid():N}", Owner_User_Id = ownerId, Name = name.Trim(), Level = level, Created_At = DateTime.UtcNow.ToString("O") };
    db.AddTopic(topic);
    return Results.Json(new { success = true, topic }, statusCode: 201);
});
app.MapGet("/api/vocab/notebook/all", (HttpRequest request, string? user_id, JsonDatabase db) =>
{
    if (RequireStudentVocabAccess(request, db, user_id, out var student) is { } denied) return denied;
    var topics = db.GetTopicsForUser(student!.Id);
    var topicIds = topics.Select(topic => topic.Id).ToHashSet();
    return Results.Json(new { success = true, cards = db.GetAllCards().Where(card => topicIds.Contains(card.Topic_Id)).ToList(), topics });
});
app.MapPost("/api/vocab/cards", (HttpRequest request, JsonDatabase db, JsonElement body) =>
{
    var requestedOwnerId = body.TryGetProperty("user_id", out var uid) ? uid.GetString() : null;
    if (RequireStudentVocabAccess(request, db, requestedOwnerId, out var student) is { } denied) return denied;
    var ownerId = student!.Id;
    var topicId = body.TryGetProperty("topic_id", out var tid) ? tid.GetString() : null;
    var topicName = body.TryGetProperty("topicName", out var tn) ? tn.GetString() : "Sổ tay từ vựng của tôi";
    if (string.IsNullOrWhiteSpace(topicId))
    {
        var existing = db.GetTopicsForUser(ownerId).FirstOrDefault(t => t.Name.Equals(topicName, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            existing = new Topic { Id = $"topic-{Guid.NewGuid():N}", Owner_User_Id = ownerId, Name = topicName ?? "Sổ tay từ vựng của tôi", Level = "ALL", Created_At = DateTime.UtcNow.ToString("O") };
            db.AddTopic(existing);
        }
        topicId = existing.Id;
    }
    else if (db.GetTopicForUser(topicId, ownerId) is null) return Results.NotFound(new { error = "Topic not found for this account." });

    var card = new VocabCard
    {
        Id = $"card-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
        Topic_Id = topicId!,
        Word_En = body.TryGetProperty("word_en", out var we) ? we.GetString() ?? string.Empty : string.Empty,
        Ipa_En = body.TryGetProperty("ipa_en", out var ipa) ? ipa.GetString() : null,
        Word_Zh = body.TryGetProperty("word_zh", out var wz) ? wz.GetString() ?? string.Empty : string.Empty,
        Pinyin = body.TryGetProperty("pinyin", out var py) ? py.GetString() ?? string.Empty : string.Empty,
        Word_Ja = body.TryGetProperty("word_ja", out var wj) ? wj.GetString() : null,
        Reading_Ja = body.TryGetProperty("reading_ja", out var rj) ? rj.GetString() : null,
        Meaning_Vi = body.TryGetProperty("meaning_vi", out var mv) ? mv.GetString() ?? string.Empty : string.Empty,
        Example_En = body.TryGetProperty("example_en", out var ee) ? ee.GetString() ?? string.Empty : string.Empty,
        Example_Zh = body.TryGetProperty("example_zh", out var ez) ? ez.GetString() ?? string.Empty : string.Empty,
        Example_Ja = body.TryGetProperty("example_ja", out var eja) ? eja.GetString() : null,
        Example_Meaning_Vi = body.TryGetProperty("example_meaning_vi", out var emv) ? emv.GetString() ?? string.Empty : string.Empty,
        Difficulty = body.TryGetProperty("difficulty", out var d) ? d.GetString() : "B1",
        Learned = false
    };

    return Results.Json(new { success = true, card = db.AddCard(card), topic = db.GetTopic(topicId!) }, statusCode: 201);
});

app.MapPut("/api/vocab/cards/{cardId}", (HttpRequest request, string cardId, string? user_id, JsonDatabase db, VocabCard patch) =>
{
    if (RequireStudentVocabAccess(request, db, user_id, out var student) is { } denied) return denied;
    if (!db.IsCardOwnedByUser(cardId, student!.Id)) return Results.NotFound(new { error = "Card not found for this account." });
    var updated = db.UpdateCard(cardId, patch);
    return updated is null ? Results.NotFound(new { error = "Card not found." }) : Results.Json(new { success = true, card = updated });
});
app.MapDelete("/api/vocab/cards/{cardId}", (HttpRequest request, string cardId, string? user_id, JsonDatabase db) =>
{
    if (RequireStudentVocabAccess(request, db, user_id, out var student) is { } denied) return denied;
    if (!db.IsCardOwnedByUser(cardId, student!.Id)) return Results.NotFound(new { error = "Card not found for this account." });
    return db.DeleteCard(cardId) ? Results.Json(new { success = true }) : Results.NotFound(new { error = "Card not found." });
});
app.MapPost("/api/vocab/cards/{cardId}/toggle-learned", (HttpRequest request, string cardId, string? user_id, JsonDatabase db) =>
{
    if (RequireStudentVocabAccess(request, db, user_id, out var student) is { } denied) return denied;
    if (!db.IsCardOwnedByUser(cardId, student!.Id)) return Results.NotFound(new { error = "Card not found for this account." });
    var card = db.ToggleCardLearned(cardId);
    return card is null ? Results.NotFound(new { error = "Card not found." }) : Results.Json(new { success = true, card });
});
app.MapPatch("/api/vocab/cards/{cardId}/learned", (HttpRequest request, string cardId, string? user_id, JsonDatabase db) =>
{
    if (RequireStudentVocabAccess(request, db, user_id, out var student) is { } denied) return denied;
    if (!db.IsCardOwnedByUser(cardId, student!.Id)) return Results.NotFound(new { error = "Card not found for this account." });
    var card = db.ToggleCardLearned(cardId);
    return card is null ? Results.NotFound(new { error = "Card not found." }) : Results.Json(new { success = true, card });
});
app.MapPut("/api/vocab/cards/{cardId}/review", (HttpRequest request, string cardId, string? user_id, JsonDatabase db, JsonElement body) =>
{
    if (RequireStudentVocabAccess(request, db, user_id, out var student) is { } denied) return denied;
    if (!db.IsCardOwnedByUser(cardId, student!.Id)) return Results.NotFound(new { error = "Card not found for this account." });
    bool? learned = body.TryGetProperty("learned", out var l) && (l.ValueKind == JsonValueKind.True || l.ValueKind == JsonValueKind.False) ? l.GetBoolean() : null;
    string? note = body.TryGetProperty("note", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
    int? reviewCount = body.TryGetProperty("review_count", out var rc) && rc.TryGetInt32(out var rcVal) ? rcVal : null;
    string? lastReviewedAt = body.TryGetProperty("last_reviewed_at", out var lra) && lra.ValueKind == JsonValueKind.String ? lra.GetString() : null;
    string? nextReviewAt = body.TryGetProperty("next_review_at", out var nra) && nra.ValueKind == JsonValueKind.String ? nra.GetString() : null;
    int? intervalDays = body.TryGetProperty("interval_days", out var idays) && idays.TryGetInt32(out var idVal) ? idVal : null;
    double? easeFactor = body.TryGetProperty("ease_factor", out var ef) && ef.TryGetDouble(out var efVal) ? efVal : null;
    var card = db.UpdateCardReview(cardId, learned, note, reviewCount, lastReviewedAt, nextReviewAt, intervalDays, easeFactor);
    return card is null ? Results.NotFound(new { error = "Card not found." }) : Results.Json(new { success = true, card });
});
app.MapGet("/api/vocab/{topicId}", (HttpRequest request, string topicId, string? user_id, JsonDatabase db) =>
{
    if (RequireStudentVocabAccess(request, db, user_id, out var student) is { } denied) return denied;
    var topic = db.GetTopicForUser(topicId, student!.Id);
    return topic is null ? Results.NotFound(new { error = "Topic not found for this account." }) : Results.Json(new { success = true, topic, cards = db.GetCardsByTopic(topicId) });
});
app.MapDelete("/api/vocab/{topicId}", (HttpRequest request, string topicId, string? user_id, JsonDatabase db) =>
{
    if (RequireStudentVocabAccess(request, db, user_id, out var student) is { } denied) return denied;
    if (db.GetTopicForUser(topicId, student!.Id) is null) return Results.NotFound(new { error = "Topic not found for this account." });
    db.DeleteTopic(topicId);
    return Results.Json(new { success = true });
});
app.MapPost("/api/video/extract", (HttpRequest request, JsonDatabase db, AiFallbackService ai, JsonElement body) =>
{
    if (GetAuthUser(request, db) is null) return Results.Unauthorized();
    var url = body.TryGetProperty("youtube_url", out var y) ? y.GetString() : null;
    if (string.IsNullOrWhiteSpace(url)) return Results.BadRequest(new { error = "Vui lòng cung cấp link video YouTube." });
    var videoId = ai.ExtractYouTubeId(url);
    if (string.IsNullOrWhiteSpace(videoId)) return Results.BadRequest(new { error = "Link YouTube không hợp lệ." });

    var sessionId = $"session-{videoId}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
    var session = new VideoSession
    {
        Id = sessionId,
        Youtube_Url = url,
        Video_Id = videoId,
        Title = body.TryGetProperty("title", out var t) ? t.GetString() ?? $"YouTube Video ({videoId})" : $"YouTube Video ({videoId})",
        Duration_Sec = 300,
        Language = "en",
        Created_At = DateTime.UtcNow.ToString("O")
    };

    var words = new List<VideoWord>
    {
        new() { Id = $"vw-{sessionId}-1", Session_Id = sessionId, Word = "sustainable", Language = "en", Ipa_En = "/səˈsteɪ.nə.bəl/", Timestamp_Sec = 12, Meaning_Vi = "bền vững", Context_Sentence = "This is a sustainable model.", Difficulty = "B1" },
        new() { Id = $"vw-{sessionId}-2", Session_Id = sessionId, Word = "innovation", Language = "en", Ipa_En = "/ˌɪn.əˈveɪ.ʃən/", Timestamp_Sec = 45, Meaning_Vi = "đổi mới", Context_Sentence = "Innovation drives growth.", Difficulty = "B2" }
    };

    session.Words_Count = words.Count;
    session.Subtitles = new List<SubtitleItem>
    {
        new() { Id = "sub-1", Start = 10, Duration = 5, Text_Original = "This is a sustainable model.", Text_Vi = "Đây là một mô hình bền vững." },
        new() { Id = "sub-2", Start = 42, Duration = 5, Text_Original = "Innovation drives growth.", Text_Vi = "Đổi mới thúc đẩy tăng trưởng." }
    };

    db.AddVideoSession(session);
    db.AddVideoWords(words);

    return Results.Json(new { success = true, session, words, transcript_count = session.Subtitles.Count });
});

app.MapGet("/api/video/sessions", (HttpRequest request, JsonDatabase db) => GetAuthUser(request, db) is null ? Results.Unauthorized() : Results.Json(new { success = true, sessions = db.GetVideoSessions() }));
app.MapGet("/api/video/{sessionId}", (HttpRequest request, string sessionId, JsonDatabase db) =>
{
    if (GetAuthUser(request, db) is null) return Results.Unauthorized();
    var session = db.GetVideoSession(sessionId);
    if (session is null) return Results.NotFound(new { error = "Không tìm thấy phiên học video." });
    return Results.Json(new { success = true, session, words = db.GetVideoWords(session.Id) });
});
app.MapPut("/api/video/sessions/{sessionId}/subtitles", (HttpRequest request, string sessionId, JsonDatabase db, JsonElement body) =>
{
    if (GetAuthUser(request, db) is null) return Results.Unauthorized();
    if (!body.TryGetProperty("subtitles", out var subsEl) || subsEl.ValueKind != JsonValueKind.Array) return Results.BadRequest(new { error = "Danh sách phụ đề không hợp lệ." });
    var subtitles = JsonSerializer.Deserialize<List<SubtitleItem>>(subsEl.GetRawText(), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }) ?? new List<SubtitleItem>();
    var updated = db.UpdateVideoSubtitles(sessionId, subtitles);
    return updated is null ? Results.NotFound(new { error = "Không tìm thấy video session." }) : Results.Json(new { success = true, session = updated });
});

app.MapPost("/api/translate/lookup", (HttpRequest request, JsonDatabase db, AiFallbackService ai, JsonElement body) =>
{
    if (GetAuthUser(request, db) is null) return Results.Unauthorized();
    var text = body.TryGetProperty("text", out var t) ? t.GetString() : null;
    var sourceLang = body.TryGetProperty("sourceLang", out var sl) ? sl.GetString() ?? "auto" : "auto";
    if (string.IsNullOrWhiteSpace(text)) return Results.BadRequest(new { error = "Text required" });
    return Results.Json(new { success = true, data = ai.Lookup(text, sourceLang) });
});

app.Run();

public partial class Program { }
