using System.Text.Json;
using BilingualVocabApi.Models;
using BilingualVocabApi.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
});

builder.Services.AddSingleton<JsonDatabase>();
builder.Services.AddSingleton<AiFallbackService>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();
app.UseCors();

app.MapGet("/", (JsonDatabase db) => Results.Json(new
{
    name = "Bilingual Vocab AI - C# Backend API",
    status = "online",
    version = "1.0.0",
    time = DateTime.UtcNow,
    endpoints = new { health = "/api/health", vocab = "/api/vocab/*", video = "/api/video/*", translate = "/api/translate/*" }
}));

app.MapGet("/api/health", (JsonDatabase db) => Results.Json(new { status = "ok", service = "bilingual-vocab-api-csharp", database = db.StorageMode, time = DateTime.UtcNow }));

app.MapPost("/api/auth/login", (JsonDatabase db, JsonElement body) =>
{
    var email = body.TryGetProperty("email", out var e) ? e.GetString() : null;
    var password = body.TryGetProperty("password", out var p) ? p.GetString() : null;
    if (string.IsNullOrWhiteSpace(email)) return Results.BadRequest(new { error = "Email không được để trống" });
    var storedUser = db.GetUserByEmail(email);
    if (storedUser is null) return Results.Unauthorized();
    if (storedUser.Status == "suspended") return Results.StatusCode(403);
    if (storedUser.Password != password) return Results.Unauthorized();
    db.UpdateUser(storedUser.Id, new Dictionary<string, object?> { ["last_active"] = "Vừa mới" });
    var user = db.GetUser(storedUser.Id);
    return Results.Json(new { success = true, user, token = $"token_{user!.Id}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}" });
});
app.MapPost("/api/auth/register", (JsonDatabase db, JsonElement body) =>
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
        Password = password,
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
    return Results.Json(new { success = true, user = saved, token = $"token_{saved.Id}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}" }, statusCode: 201);
});

app.MapGet("/api/auth/me", (HttpRequest request, JsonDatabase db) =>
{
    var userId = request.Headers["x-user-id"].ToString();
    var email = request.Headers["x-user-email"].ToString();
    if (!string.IsNullOrWhiteSpace(userId))
    {
        var byId = db.GetUser(userId);
        if (byId is not null) return Results.Json(new { success = true, user = byId });
    }
    if (!string.IsNullOrWhiteSpace(email))
    {
        var byEmail = db.GetUserByEmail(email);
        if (byEmail is not null)
        {
            byEmail.Password = null;
            return Results.Json(new { success = true, user = byEmail });
        }
    }
    return Results.Json(new { success = true, user = db.GetUsers().FirstOrDefault() });
});

app.MapGet("/api/auth/users", (JsonDatabase db) => Results.Json(new { success = true, users = db.GetUsers() }));
app.MapPut("/api/auth/users/{id}", (string id, JsonDatabase db, Dictionary<string, object?> updates) =>
{
    if (id == "user-admin-root" || db.GetUser(id)?.Email.Equals("admin@123", StringComparison.OrdinalIgnoreCase) == true)
        return Results.BadRequest(new { error = "Không thể thay đổi tài khoản quản trị cố định." });
    if (updates.TryGetValue("role", out var roleValue) && roleValue?.ToString() is not ("student" or "expert"))
        return Results.BadRequest(new { error = "Vai trò chỉ có thể là student hoặc expert." });
    var updated = db.UpdateUser(id, updates);
    return updated is null ? Results.NotFound(new { error = "Không tìm thấy người dùng" }) : Results.Json(new { success = true, user = updated });
});
app.MapDelete("/api/auth/users/{id}", (string id, JsonDatabase db) => db.DeleteUser(id) ? Results.Json(new { success = true }) : Results.NotFound(new { error = "Không tìm thấy người dùng" }));

// Helper trích xuất thông tin người dùng từ token / headers
User? GetAuthUser(HttpRequest request, JsonDatabase db)
{
    var authHeader = request.Headers["Authorization"].ToString();
    if (!string.IsNullOrWhiteSpace(authHeader))
    {
        var token = authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? authHeader[7..].Trim() : authHeader.Trim();
        if (token.StartsWith("token_"))
        {
            var parts = token.Split('_');
            if (parts.Length >= 2)
            {
                var byToken = db.GetUser(parts[1]);
                if (byToken is not null) return byToken;
            }
        }
        var directUser = db.GetUser(token) ?? db.GetUserByEmail(token);
        if (directUser is not null) return directUser;
    }

    var userId = request.Headers["x-user-id"].ToString();
    if (!string.IsNullOrWhiteSpace(userId))
    {
        var byId = db.GetUser(userId);
        if (byId is not null) return byId;
    }

    var email = request.Headers["x-user-email"].ToString();
    if (!string.IsNullOrWhiteSpace(email))
    {
        var byEmail = db.GetUserByEmail(email);
        if (byEmail is not null) return byEmail;
    }

    return null;
}

var validCefrs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "A1", "A2", "B1", "B2", "C1", "C2" };
var validSkills = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "writing", "speaking", "listening", "reading" };

// ==========================================
// F4.1 EXPERT: TẠO / XEM / SỬA / XÓA BÀI TẬP
// ==========================================

// GET /api/expert/assignments - Danh sách assignment
app.MapGet("/api/expert/assignments", (HttpRequest request, string? expert_id, JsonDatabase db) =>
{
    var user = GetAuthUser(request, db);
    if (user is not null && user.Role.Equals("admin", StringComparison.OrdinalIgnoreCase))
        return Results.Json(new { error = "Admin không có quyền truy cập phân hệ Khảo thí và chấm thi." }, statusCode: 403);
    if (user is not null && !user.Role.Equals("expert", StringComparison.OrdinalIgnoreCase))
        return Results.Json(new { error = "Quyền truy cập bị từ chối. Chỉ Expert mới được thực hiện thao tác này." }, statusCode: 403);

    var targetExpertId = expert_id ?? (user?.Role.Equals("expert", StringComparison.OrdinalIgnoreCase) == true ? user.Id : null);
    return Results.Json(new { success = true, assignments = db.GetAssignments(targetExpertId) });
});

// POST /api/expert/assignments - Tạo assignment mới
app.MapPost("/api/expert/assignments", (HttpRequest request, JsonDatabase db, JsonElement body) =>
{
    var user = GetAuthUser(request, db);
    if (user is not null && user.Role.Equals("admin", StringComparison.OrdinalIgnoreCase))
        return Results.Json(new { error = "Admin không có quyền truy cập phân hệ Khảo thí và chấm thi." }, statusCode: 403);
    if (user is null)
        user = db.GetUsers().FirstOrDefault(u => u.Role == "expert") ?? new User { Id = "user-expert-root", Name = "Chuyên gia", Role = "expert" };
    else if (!user.Role.Equals("expert", StringComparison.OrdinalIgnoreCase))
        return Results.Json(new { error = "Quyền truy cập bị từ chối. Chỉ Expert mới được tạo bài tập." }, statusCode: 403);

    var title = body.TryGetProperty("title", out var t) ? t.GetString() : null;
    var skill = body.TryGetProperty("skill", out var s) ? s.GetString() : "writing";
    var targetLevel = body.TryGetProperty("target_level", out var tl) ? tl.GetString() : "B2";
    var duration = body.TryGetProperty("duration_minutes", out var dm) && dm.TryGetInt32(out var dmVal) ? dmVal : 45;
    var content = body.TryGetProperty("content", out var c) ? c.GetString() : string.Empty;
    var instructions = body.TryGetProperty("instructions", out var i) ? i.GetString() : string.Empty;
    var dueDate = body.TryGetProperty("due_date", out var due) ? due.GetString() : DateTime.UtcNow.AddDays(7).ToString("yyyy-MM-dd");
    var maxScore = body.TryGetProperty("max_score", out var ms) && ms.TryGetDouble(out var msVal) ? msVal : 10.0;
    var readingQuiz = body.TryGetProperty("reading_quiz", out var rq) ? (rq.ValueKind == JsonValueKind.String ? rq.GetString() : rq.GetRawText()) : null;

    // Quy tắc kiểm tra dữ liệu
    if (string.IsNullOrWhiteSpace(title))
        return Results.BadRequest(new { error = "Đề bài / tiêu đề không được để trống." });
    if (duration <= 0)
        return Results.BadRequest(new { error = "Thời gian làm bài phải lớn hơn 0 phút." });
    if (!validCefrs.Contains(targetLevel ?? ""))
        return Results.BadRequest(new { error = "Cấp độ CEFR phải thuộc A1–C2." });
    if (!validSkills.Contains(skill ?? ""))
        return Results.BadRequest(new { error = "Kỹ năng phải là writing, speaking, listening hoặc reading." });

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
        Due_Date = dueDate,
        Created_At = DateTime.UtcNow.ToString("O"),
        Max_Score = maxScore > 0 ? maxScore : 10.0,
        Reading_Quiz = readingQuiz
    };

    var saved = db.AddAssignment(a);
    return Results.Json(new { success = true, assignment = saved }, statusCode: 201);
});

// PUT /api/expert/assignments/{id} - Sửa assignment
app.MapPut("/api/expert/assignments/{id}", (HttpRequest request, string id, JsonDatabase db, JsonElement body) =>
{
    var user = GetAuthUser(request, db);
    if (user is not null && user.Role.Equals("admin", StringComparison.OrdinalIgnoreCase))
        return Results.Json(new { error = "Admin không có quyền truy cập phân hệ Khảo thí và chấm thi." }, statusCode: 403);
    if (user is null)
        user = db.GetUsers().FirstOrDefault(u => u.Role == "expert");
    else if (!user.Role.Equals("expert", StringComparison.OrdinalIgnoreCase))
        return Results.Json(new { error = "Quyền truy cập bị từ chối. Chỉ Expert mới được sửa bài tập." }, statusCode: 403);

    var existing = db.GetAssignment(id);
    if (existing is null)
        return Results.NotFound(new { error = "Không tìm thấy bài tập để chỉnh sửa." });

    // Quy tắc: Expert chỉ sửa assignment do chính mình tạo (trừ tài khoản root chuyên gia khi demo)
    if (user is not null && existing.Expert_Id != user.Id && user.Id != "user-expert-root" && !string.IsNullOrWhiteSpace(existing.Expert_Id))
        return Results.Json(new { error = "Bạn chỉ có thể chỉnh sửa bài tập do chính mình tạo." }, statusCode: 403);

    var updates = new AssignmentUpdateDto
    {
        Title = body.TryGetProperty("title", out var t) ? t.GetString() : null,
        Skill = body.TryGetProperty("skill", out var s) ? s.GetString() : null,
        Target_Level = body.TryGetProperty("target_level", out var tl) ? tl.GetString() : null,
        Duration_Minutes = body.TryGetProperty("duration_minutes", out var dm) && dm.TryGetInt32(out var dmVal) ? dmVal : null,
        Content = body.TryGetProperty("content", out var c) ? c.GetString() : null,
        Instructions = body.TryGetProperty("instructions", out var i) ? i.GetString() : null,
        Due_Date = body.TryGetProperty("due_date", out var due) ? due.GetString() : null,
        Max_Score = body.TryGetProperty("max_score", out var ms) && ms.TryGetDouble(out var msVal) ? msVal : null,
        Reading_Quiz = body.TryGetProperty("reading_quiz", out var rq) ? (rq.ValueKind == JsonValueKind.String ? rq.GetString() : rq.GetRawText()) : null
    };

    if (updates.Duration_Minutes.HasValue && updates.Duration_Minutes.Value <= 0)
        return Results.BadRequest(new { error = "Thời gian làm bài phải lớn hơn 0 phút." });
    if (!string.IsNullOrWhiteSpace(updates.Target_Level) && !validCefrs.Contains(updates.Target_Level))
        return Results.BadRequest(new { error = "Cấp độ CEFR phải thuộc A1–C2." });
    if (!string.IsNullOrWhiteSpace(updates.Skill) && !validSkills.Contains(updates.Skill))
        return Results.BadRequest(new { error = "Kỹ năng phải là writing, speaking, listening hoặc reading." });

    var updated = db.UpdateAssignment(id, updates);
    return Results.Json(new { success = true, assignment = updated });
});

// DELETE /api/expert/assignments/{id} - Xóa assignment
app.MapDelete("/api/expert/assignments/{id}", (HttpRequest request, string id, JsonDatabase db) =>
{
    var user = GetAuthUser(request, db);
    if (user is not null && user.Role.Equals("admin", StringComparison.OrdinalIgnoreCase))
        return Results.Json(new { error = "Admin không có quyền truy cập phân hệ Khảo thí và chấm thi." }, statusCode: 403);
    if (user is null)
        user = db.GetUsers().FirstOrDefault(u => u.Role == "expert");
    else if (!user.Role.Equals("expert", StringComparison.OrdinalIgnoreCase))
        return Results.Json(new { error = "Quyền truy cập bị từ chối. Chỉ Expert mới được xóa bài tập." }, statusCode: 403);

    var existing = db.GetAssignment(id);
    if (existing is null)
        return Results.NotFound(new { error = "Không tìm thấy bài tập cần xóa." });

    if (user is not null && existing.Expert_Id != user.Id && user.Id != "user-expert-root" && !string.IsNullOrWhiteSpace(existing.Expert_Id))
        return Results.Json(new { error = "Bạn chỉ có thể xóa bài tập do chính mình tạo." }, statusCode: 403);

    var deleted = db.DeleteAssignment(id);
    return deleted ? Results.Json(new { success = true, message = "Đã xóa bài tập thành công." }) : Results.NotFound(new { error = "Không tìm thấy bài tập." });
});

// ==========================================
// F4.2 & F4.4 STUDENT: LÀM BÀI VÀ XEM KẾT QUẢ
// ==========================================

// GET /api/student/assignments - Danh sách assignment được giao
app.MapGet("/api/student/assignments", (HttpRequest request, JsonDatabase db) =>
{
    var user = GetAuthUser(request, db);
    if (user is not null && user.Role.Equals("admin", StringComparison.OrdinalIgnoreCase))
        return Results.Json(new { error = "Admin không có quyền truy cập phân hệ Khảo thí và chấm thi." }, statusCode: 403);

    return Results.Json(new { success = true, assignments = db.GetAssignments() });
});

// POST /api/student/submissions - Nộp bài (Hỗ trợ 4 kỹ năng)
app.MapPost("/api/student/submissions", (HttpRequest request, JsonDatabase db, JsonElement body) =>
{
    var user = GetAuthUser(request, db);
    if (user is not null && user.Role.Equals("admin", StringComparison.OrdinalIgnoreCase))
        return Results.Json(new { error = "Admin không có quyền truy cập phân hệ Khảo thí và chấm thi." }, statusCode: 403);
    if (user is null)
        user = db.GetUsers().FirstOrDefault(u => u.Role == "student") ?? new User { Id = "user-student-root", Name = "Sinh viên", Role = "student" };
    else if (!user.Role.Equals("student", StringComparison.OrdinalIgnoreCase))
        return Results.Json(new { error = "Quyền truy cập bị từ chối. Chỉ Sinh viên mới được nộp bài." }, statusCode: 403);

    var assignmentId = body.TryGetProperty("assignment_id", out var aid) ? aid.GetString() : null;
    if (string.IsNullOrWhiteSpace(assignmentId))
        return Results.BadRequest(new { error = "Vui lòng chọn bài tập cần nộp (assignment_id)." });

    var assignment = db.GetAssignment(assignmentId);
    if (assignment is null)
        return Results.NotFound(new { error = "Không tìm thấy bài tập." });

    var content = body.TryGetProperty("content", out var c) ? c.GetString() : null;
    var audioUrl = body.TryGetProperty("audio_url", out var au) ? au.GetString() : null;
    var readingAnswers = body.TryGetProperty("reading_answers", out var ra) ? (ra.ValueKind == JsonValueKind.String ? ra.GetString() : ra.GetRawText()) : null;
    var topicSentence = body.TryGetProperty("topic_sentence", out var ts) ? ts.GetString() : null;
    var durationSpent = body.TryGetProperty("duration_spent", out var ds) && ds.TryGetInt32(out var dsVal) ? dsVal : (int?)null;

    // Kiểm tra tính hợp lệ bài nộp theo kỹ năng
    if (string.IsNullOrWhiteSpace(content) && string.IsNullOrWhiteSpace(audioUrl) && string.IsNullOrWhiteSpace(readingAnswers) && string.IsNullOrWhiteSpace(topicSentence))
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
        Content = content ?? string.Empty,
        Audio_Url = audioUrl,
        Reading_Answers = readingAnswers,
        Topic_Sentence = topicSentence,
        Duration_Spent = durationSpent,
        Submitted_At = DateTime.UtcNow.ToString("O"),
        Status = "pending"
    };

    var saved = db.AddSubmission(s);
    return Results.Json(new { success = true, submission = saved }, statusCode: 201);
});

// Alias tương thích: POST /api/student/assignments/{id}/submit
app.MapPost("/api/student/assignments/{id}/submit", (HttpRequest request, string id, JsonDatabase db, JsonElement body) =>
{
    var user = GetAuthUser(request, db);
    if (user is not null && user.Role.Equals("admin", StringComparison.OrdinalIgnoreCase))
        return Results.Json(new { error = "Admin không có quyền truy cập phân hệ Khảo thí và chấm thi." }, statusCode: 403);
    if (user is null)
        user = db.GetUsers().FirstOrDefault(u => u.Role == "student") ?? new User { Id = "user-student-root", Name = "Sinh viên", Role = "student" };

    var assignment = db.GetAssignment(id);
    if (assignment is null)
        return Results.NotFound(new { error = "Không tìm thấy bài tập." });

    var content = body.TryGetProperty("content", out var c) ? c.GetString() : null;
    var audio = body.TryGetProperty("audio_url", out var au) ? au.GetString() : null;
    var readingAnswers = body.TryGetProperty("reading_answers", out var ra) ? (ra.ValueKind == JsonValueKind.String ? ra.GetString() : ra.GetRawText()) : null;
    var topicSentence = body.TryGetProperty("topic_sentence", out var ts) ? ts.GetString() : null;

    if (string.IsNullOrWhiteSpace(content) && string.IsNullOrWhiteSpace(audio) && string.IsNullOrWhiteSpace(readingAnswers))
        return Results.BadRequest(new { error = "Nội dung bài nộp không được để trống." });

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
        Status = "pending"
    };

    return Results.Json(new { success = true, submission = db.AddSubmission(s) }, statusCode: 201);
});

// GET /api/student/submissions - F4.4 Xem kết quả chấm & lịch sử
app.MapGet("/api/student/submissions", (HttpRequest request, string? student_id, JsonDatabase db) =>
{
    var user = GetAuthUser(request, db);
    if (user is not null && user.Role.Equals("admin", StringComparison.OrdinalIgnoreCase))
        return Results.Json(new { error = "Admin không có quyền truy cập phân hệ Khảo thí và chấm thi." }, statusCode: 403);

    // Sinh viên chỉ xem được kết quả bài nộp của chính mình
    var targetStudentId = (user is not null && user.Role.Equals("student", StringComparison.OrdinalIgnoreCase)) ? user.Id : (student_id ?? "user-student-root");
    return Results.Json(new { success = true, submissions = db.GetSubmissions(targetStudentId) });
});

// ==========================================
// F4.3 EXPERT: CHẤM ĐIỂM VÀ ĐÁNH GIÁ CEFR
// ==========================================

// GET /api/expert/submissions - Danh sách bài nộp của sinh viên
app.MapGet("/api/expert/submissions", (HttpRequest request, string? status, string? assignment_id, JsonDatabase db) =>
{
    var user = GetAuthUser(request, db);
    if (user is not null && user.Role.Equals("admin", StringComparison.OrdinalIgnoreCase))
        return Results.Json(new { error = "Admin không có quyền truy cập phân hệ Khảo thí và chấm thi." }, statusCode: 403);
    if (user is not null && !user.Role.Equals("expert", StringComparison.OrdinalIgnoreCase))
        return Results.Json(new { error = "Quyền truy cập bị từ chối. Chỉ Expert mới được xem danh sách bài nộp." }, statusCode: 403);

    return Results.Json(new { success = true, submissions = db.GetSubmissions(null, assignment_id, status) });
});

// GET /api/expert/submissions/{id} - Chi tiết một bài nộp
app.MapGet("/api/expert/submissions/{id}", (HttpRequest request, string id, JsonDatabase db) =>
{
    var user = GetAuthUser(request, db);
    if (user is not null && user.Role.Equals("admin", StringComparison.OrdinalIgnoreCase))
        return Results.Json(new { error = "Admin không có quyền truy cập phân hệ Khảo thí và chấm thi." }, statusCode: 403);
    if (user is not null && !user.Role.Equals("expert", StringComparison.OrdinalIgnoreCase))
        return Results.Json(new { error = "Quyền truy cập bị từ chối. Chỉ Expert mới được xem chi tiết bài nộp." }, statusCode: 403);

    var sub = db.GetSubmission(id);
    return sub is null ? Results.NotFound(new { error = "Không tìm thấy bài nộp." }) : Results.Json(new { success = true, submission = sub });
});

// PUT & POST /api/expert/submissions/{id}/grade - Lưu kết quả chấm
IResult HandleGradeSubmission(HttpRequest request, string id, JsonDatabase db, JsonElement body)
{
    var user = GetAuthUser(request, db);
    if (user is not null && user.Role.Equals("admin", StringComparison.OrdinalIgnoreCase))
        return Results.Json(new { error = "Admin không có quyền truy cập phân hệ Khảo thí và chấm thi." }, statusCode: 403);
    if (user is null)
        user = db.GetUsers().FirstOrDefault(u => u.Role == "expert") ?? new User { Id = "user-expert-root", Name = "Chuyên gia", Role = "expert" };
    else if (!user.Role.Equals("expert", StringComparison.OrdinalIgnoreCase))
        return Results.Json(new { error = "Quyền truy cập bị từ chối. Chỉ Expert mới được chấm bài." }, statusCode: 403);

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

app.MapGet("/api/expert/consultations", (string? student_id, JsonDatabase db) => Results.Json(new { success = true, consultations = db.GetConsultations(student_id) }));
app.MapPost("/api/expert/consultations", (JsonDatabase db, JsonElement body) =>
{
    var studentId = body.TryGetProperty("student_id", out var sid) ? sid.GetString() : null;
    var advice = body.TryGetProperty("advice", out var adv) ? adv.GetString() : null;
    var level = body.TryGetProperty("recommended_level", out var rl) ? rl.GetString() : null;
    if (string.IsNullOrWhiteSpace(studentId) || string.IsNullOrWhiteSpace(advice) || string.IsNullOrWhiteSpace(level))
        return Results.BadRequest(new { error = "Thiếu thông tin tư vấn" });

    var c = new Consultation
    {
        Id = $"cons-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
        Expert_Id = body.TryGetProperty("expert_id", out var e) ? e.GetString() ?? "user-expert-1" : "user-expert-1",
        Expert_Name = body.TryGetProperty("expert_name", out var en) ? en.GetString() ?? "Chuyên gia" : "Chuyên gia",
        Student_Id = studentId,
        Student_Name = body.TryGetProperty("student_name", out var sn) ? sn.GetString() ?? "Sinh viên" : "Sinh viên",
        Current_Level = body.TryGetProperty("current_level", out var cl) ? cl.GetString() ?? "B1" : "B1",
        Recommended_Level = level,
        Advice = advice,
        Strengths = new List<string> { "Nền tảng tốt" },
        Weaknesses = new List<string> { "Cần luyện thêm" },
        Study_Plan = body.TryGetProperty("study_plan", out var sp) ? sp.GetString() ?? "30 phút mỗi ngày" : "30 phút mỗi ngày"
    };

    return Results.Json(new { success = true, consultation = db.AddConsultation(c) }, statusCode: 201);
});
app.MapGet("/api/expert/students-progress", (JsonDatabase db) => Results.Json(new { success = true, students = db.GetStudentProgress() }));
app.MapGet("/api/student/consultations", (string? student_id, JsonDatabase db) => Results.Json(new { success = true, consultations = db.GetConsultations(student_id) }));

app.MapGet("/api/admin/stats", (JsonDatabase db) => Results.Json(new { success = true, stats = db.GetAdminStats() }));
app.MapGet("/api/admin/ai-config", (JsonDatabase db) => Results.Json(new { success = true, config = db.GetAiConfig() }));
app.MapPut("/api/admin/ai-config", (JsonDatabase db, AiConfig cfg) => Results.Json(new { success = true, config = db.UpdateAiConfig(cfg) }));
app.MapGet("/api/admin/api-logs", (int? limit, JsonDatabase db) => Results.Json(new { success = true, logs = db.GetApiLogs(limit ?? 50) }));
app.MapGet("/api/admin/dau-curriculum", (JsonDatabase db) =>
{
    var topics = db.GetTopics().Where(t => t.Is_Dau_Curriculum || t.Name.StartsWith("DAU:", StringComparison.OrdinalIgnoreCase)).ToList();
    return Results.Json(new { success = true, topics });
});
app.MapPut("/api/admin/dau-curriculum", (JsonDatabase db, JsonElement body) =>
{
    if (!body.TryGetProperty("topics", out var topicsElement) || topicsElement.ValueKind != JsonValueKind.Array)
        return Results.BadRequest(new { error = "Danh sách chương trình không hợp lệ." });
    var topics = JsonSerializer.Deserialize<List<Topic>>(topicsElement.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }) ?? new();
    if (topics.Any(t => string.IsNullOrWhiteSpace(t.Name))) return Results.BadRequest(new { error = "Tên chủ đề không được để trống." });
    return Results.Json(new { success = true, topics = db.ReplaceDauCurriculum(topics) });
});
app.MapPost("/api/admin/sync-mysql", () => Results.Json(new { success = true, message = "Bản C# đang dùng JSON local, không yêu cầu MySQL sync." }));

app.MapPost("/api/vocab/generate", async (JsonDatabase db, AiFallbackService ai, JsonElement body) =>
{
    var topicName = body.TryGetProperty("topic", out var t) ? t.GetString() : null;
    var ownerId = body.TryGetProperty("user_id", out var uid) ? uid.GetString() : null;
    if (string.IsNullOrWhiteSpace(ownerId) || db.GetUser(ownerId)?.Role != "student") return Results.BadRequest(new { error = "Student account is required." });
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

app.MapPost("/api/vocab/generate-batch", async (JsonDatabase db, AiFallbackService ai, JsonElement body) =>
{
    var topicId = body.TryGetProperty("topic_id", out var tid) ? tid.GetString() : null;
    var ownerId = body.TryGetProperty("user_id", out var uid) ? uid.GetString() : null;
    if (string.IsNullOrWhiteSpace(ownerId) || db.GetUser(ownerId)?.Role != "student")
        return Results.BadRequest(new { error = "Student account is required." });
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

app.MapPost("/api/vocab/autofill", (AiFallbackService ai, JsonElement body) =>
{
    var input = body.TryGetProperty("input_word", out var iw) ? iw.GetString() : body.TryGetProperty("inputWord", out var iw2) ? iw2.GetString() : null;
    if (string.IsNullOrWhiteSpace(input)) return Results.BadRequest(new { error = "Vui lòng nhập từ hoặc cụm từ." });
    return Results.Json(new { success = true, data = ai.AutoFill(input) });
});

app.MapGet("/api/vocab/topics", (string? user_id, JsonDatabase db) =>
{
    if (string.IsNullOrWhiteSpace(user_id) || db.GetUser(user_id)?.Role != "student") return Results.Json(new { success = true, topics = new List<Topic>() });
    return Results.Json(new { success = true, topics = db.GetTopicsForUser(user_id) });
});
app.MapPost("/api/vocab/topics", (JsonDatabase db, JsonElement body) =>
{
    var name = body.TryGetProperty("name", out var n) ? n.GetString() : null;
    var ownerId = body.TryGetProperty("user_id", out var uid) ? uid.GetString() : null;
    if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 80) return Results.BadRequest(new { error = "Tên chủ đề phải có từ 1 đến 80 ký tự." });
    if (string.IsNullOrWhiteSpace(ownerId) || db.GetUser(ownerId)?.Role != "student") return Results.BadRequest(new { error = "Student account is required." });
    if (db.GetTopicsForUser(ownerId).Any(topic => topic.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)))
        return Results.Conflict(new { error = "Tên chủ đề đã tồn tại. Hãy chọn tên khác." });
    var level = body.TryGetProperty("level", out var l) ? l.GetString()?.Trim().ToUpperInvariant() : null;
    if (level is null || !new[] { "A1", "A2", "B1", "B2", "C1", "C2" }.Contains(level))
        return Results.BadRequest(new { error = "Vui lòng chọn cấp CEFR từ A1 đến C2." });
    var topic = new Topic { Id = $"topic-{Guid.NewGuid():N}", Owner_User_Id = ownerId, Name = name.Trim(), Level = level, Created_At = DateTime.UtcNow.ToString("O") };
    db.AddTopic(topic);
    return Results.Json(new { success = true, topic }, statusCode: 201);
});
app.MapGet("/api/vocab/notebook/all", (string? user_id, JsonDatabase db) =>
{
    var topics = string.IsNullOrWhiteSpace(user_id) || db.GetUser(user_id)?.Role != "student" ? new List<Topic>() : db.GetTopicsForUser(user_id);
    var topicIds = topics.Select(topic => topic.Id).ToHashSet();
    return Results.Json(new { success = true, cards = db.GetAllCards().Where(card => topicIds.Contains(card.Topic_Id)).ToList(), topics });
});
app.MapPost("/api/vocab/cards", (JsonDatabase db, JsonElement body) =>
{
    var ownerId = body.TryGetProperty("user_id", out var uid) ? uid.GetString() : null;
    if (string.IsNullOrWhiteSpace(ownerId) || db.GetUser(ownerId)?.Role != "student") return Results.BadRequest(new { error = "Student account is required." });
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

app.MapPut("/api/vocab/cards/{cardId}", (string cardId, string? user_id, JsonDatabase db, VocabCard patch) =>
{
    if (string.IsNullOrWhiteSpace(user_id) || !db.IsCardOwnedByUser(cardId, user_id)) return Results.NotFound(new { error = "Card not found for this account." });
    var updated = db.UpdateCard(cardId, patch);
    return updated is null ? Results.NotFound(new { error = "Card not found." }) : Results.Json(new { success = true, card = updated });
});
app.MapDelete("/api/vocab/cards/{cardId}", (string cardId, string? user_id, JsonDatabase db) =>
{
    if (string.IsNullOrWhiteSpace(user_id) || !db.IsCardOwnedByUser(cardId, user_id)) return Results.NotFound(new { error = "Card not found for this account." });
    return db.DeleteCard(cardId) ? Results.Json(new { success = true }) : Results.NotFound(new { error = "Card not found." });
});
app.MapPost("/api/vocab/cards/{cardId}/toggle-learned", (string cardId, string? user_id, JsonDatabase db) =>
{
    if (string.IsNullOrWhiteSpace(user_id) || !db.IsCardOwnedByUser(cardId, user_id)) return Results.NotFound(new { error = "Card not found for this account." });
    var card = db.ToggleCardLearned(cardId);
    return card is null ? Results.NotFound(new { error = "Card not found." }) : Results.Json(new { success = true, card });
});
app.MapPatch("/api/vocab/cards/{cardId}/learned", (string cardId, string? user_id, JsonDatabase db) =>
{
    if (string.IsNullOrWhiteSpace(user_id) || !db.IsCardOwnedByUser(cardId, user_id)) return Results.NotFound(new { error = "Card not found for this account." });
    var card = db.ToggleCardLearned(cardId);
    return card is null ? Results.NotFound(new { error = "Card not found." }) : Results.Json(new { success = true, card });
});
app.MapGet("/api/vocab/{topicId}", (string topicId, string? user_id, JsonDatabase db) =>
{
    var topic = string.IsNullOrWhiteSpace(user_id) ? null : db.GetTopicForUser(topicId, user_id);
    return topic is null ? Results.NotFound(new { error = "Topic not found for this account." }) : Results.Json(new { success = true, topic, cards = db.GetCardsByTopic(topicId) });
});
app.MapDelete("/api/vocab/{topicId}", (string topicId, string? user_id, JsonDatabase db) =>
{
    if (string.IsNullOrWhiteSpace(user_id) || db.GetTopicForUser(topicId, user_id) is null) return Results.NotFound(new { error = "Topic not found for this account." });
    db.DeleteTopic(topicId);
    return Results.Json(new { success = true });
});
app.MapPost("/api/video/extract", (JsonDatabase db, AiFallbackService ai, JsonElement body) =>
{
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

app.MapGet("/api/video/sessions", (JsonDatabase db) => Results.Json(new { success = true, sessions = db.GetVideoSessions() }));
app.MapGet("/api/video/{sessionId}", (string sessionId, JsonDatabase db) =>
{
    var session = db.GetVideoSession(sessionId);
    if (session is null) return Results.NotFound(new { error = "Không tìm thấy phiên học video." });
    return Results.Json(new { success = true, session, words = db.GetVideoWords(session.Id) });
});
app.MapPut("/api/video/sessions/{sessionId}/subtitles", (string sessionId, JsonDatabase db, JsonElement body) =>
{
    if (!body.TryGetProperty("subtitles", out var subsEl) || subsEl.ValueKind != JsonValueKind.Array) return Results.BadRequest(new { error = "Danh sách phụ đề không hợp lệ." });
    var subtitles = JsonSerializer.Deserialize<List<SubtitleItem>>(subsEl.GetRawText(), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }) ?? new List<SubtitleItem>();
    var updated = db.UpdateVideoSubtitles(sessionId, subtitles);
    return updated is null ? Results.NotFound(new { error = "Không tìm thấy video session." }) : Results.Json(new { success = true, session = updated });
});

app.MapPost("/api/translate/lookup", (AiFallbackService ai, JsonElement body) =>
{
    var text = body.TryGetProperty("text", out var t) ? t.GetString() : null;
    var sourceLang = body.TryGetProperty("sourceLang", out var sl) ? sl.GetString() ?? "auto" : "auto";
    if (string.IsNullOrWhiteSpace(text)) return Results.BadRequest(new { error = "Text required" });
    return Results.Json(new { success = true, data = ai.Lookup(text, sourceLang) });
});

app.Run();
