using System.Text.Json;
using BilingualVocabApi.Models;

namespace BilingualVocabApi.Services;

public class JsonDatabase
{
    private readonly string _dbPath;
    private readonly MySqlPersistence _mysql;
    private readonly ILogger<JsonDatabase> _logger;
    private readonly object _sync = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    private DatabaseSchema _data;
    public string StorageMode { get; private set; } = "JSON";

    public JsonDatabase(IWebHostEnvironment env, IConfiguration configuration, ILogger<JsonDatabase> logger)
    {
        _dbPath = Path.Combine(env.ContentRootPath, "vocab_database.json");
        _mysql = new MySqlPersistence(configuration, env);
        _logger = logger;
        _data = Load();
        if (_mysql.TryLoad(out var mysqlData, out var error) && mysqlData is not null)
        {
            var hasData = mysqlData.Users.Count + mysqlData.Topics.Count + mysqlData.Vocab_Cards.Count + mysqlData.Assignments.Count > 0;
            if (hasData) _data = mysqlData;
            else _mysql.Save(_data);
            StorageMode = "MySQL";
            _logger.LogInformation("Database storage: MySQL.");
        }
        else _logger.LogWarning("MySQL unavailable; using local JSON storage. {Reason}", error);
        EnsureFixedAdmin();
        EnsureLegacyTopicOwners();
    }

    private void EnsureLegacyTopicOwners()
    {
        var defaultStudent = _data.Users.FirstOrDefault(u => u.Id == "user-student-root" && u.Role == "student");
        if (defaultStudent is null) return;
        var changed = false;
        foreach (var topic in _data.Topics.Where(t => string.IsNullOrWhiteSpace(t.Owner_User_Id)))
        {
            topic.Owner_User_Id = defaultStudent.Id;
            changed = true;
        }
        if (changed) Save();
    }

    private void EnsureFixedAdmin()
    {
        var admin = _data.Users.FirstOrDefault(u => u.Email.Equals("admin@123", StringComparison.OrdinalIgnoreCase))
            ?? _data.Users.FirstOrDefault(u => u.Id == "user-admin-root" && u.Role == "admin");
        if (admin is null)
        {
            _data.Users.Add(new User
            {
                Id = "user-admin-root", Email = "admin@123", Password = "123456",
                Name = "Quản trị viên", Role = "admin", Department = "DAU", Status = "active"
            });
        }
        else
        {
            admin.Password = "123456";
            admin.Email = "admin@123";
            admin.Role = "admin";
            admin.Status = "active";
            admin.Name = "Quản trị viên";
        }
        foreach (var user in _data.Users.Where(u => u.Id != admin!.Id && string.IsNullOrWhiteSpace(u.Password)))
        {
            user.Password = "123456";
            _logger.LogWarning("User {Email} had no stored password and was assigned the temporary recovery password.", user.Email);
        }
        Save();
    }

    private DatabaseSchema Load()
    {
        if (File.Exists(_dbPath))
        {
            try
            {
                var text = File.ReadAllText(_dbPath);
                var parsed = JsonSerializer.Deserialize<DatabaseSchema>(text, _jsonOptions);
                if (parsed is not null) return parsed;
            }
            catch { }
        }

        return new DatabaseSchema
        {
            Topics = new List<Topic>
            {
                new() { Id = "topic-my-vocab", Name = "Sổ tay từ vựng của tôi", Level = "ALL", Department = "Cá nhân", Created_At = DateTime.UtcNow.ToString("O") },
                new() { Id = "topic-dau-arch", Name = "DAU: Kiến trúc Công trình & Vật liệu", Level = "B2", Is_Dau_Curriculum = true, Department = "Khoa Kiến trúc", Created_At = DateTime.UtcNow.ToString("O") }
            },
            Users = new List<User>
            {
                new() { Id = "user-admin-root", Email = "admin@dau.edu.vn", Password = "1234", Name = "Quản Trị Viên", Role = "admin", Department = "DAU", Status = "active", Created_At = DateTime.UtcNow.ToString("O"), Last_Active = "Vừa mới" },
                new() { Id = "user-expert-root", Email = "expert@dau.edu.vn", Password = "1234", Name = "Chuyên Gia", Role = "expert", Department = "DAU", Status = "active", Created_At = DateTime.UtcNow.ToString("O"), Last_Active = "Vừa mới" },
                new() { Id = "user-student-root", Email = "student@dau.edu.vn", Password = "1234", Name = "Sinh Viên", Role = "student", Department = "Khoa Kiến trúc", Status = "active", Target_Cefr = "B2", Created_At = DateTime.UtcNow.ToString("O"), Last_Active = "Vừa mới" }
            }
        };
    }

    private void Save()
    {
        var text = JsonSerializer.Serialize(_data, _jsonOptions);
        File.WriteAllText(_dbPath, text);
        if (StorageMode == "MySQL") _mysql.Save(_data);
    }

    public DatabaseSchema Snapshot() => _data;

    public List<Topic> GetTopics() => _data.Topics.Select(t =>
    {
        t.Card_Count = _data.Vocab_Cards.Count(c => c.Topic_Id == t.Id);
        return t;
    }).ToList();

    public List<Topic> GetTopicsForUser(string userId) => GetTopics()
        .Where(t => string.Equals(t.Owner_User_Id, userId, StringComparison.Ordinal)).ToList();

    public List<Topic> ReplaceDauCurriculum(List<Topic> topics)
    {
        lock (_sync)
        {
            var existingIds = _data.Topics.Where(t => t.Is_Dau_Curriculum || t.Name.StartsWith("DAU:", StringComparison.OrdinalIgnoreCase)).Select(t => t.Id).ToHashSet();
            var incomingIds = topics.Select(t => t.Id).ToHashSet();
            _data.Vocab_Cards = _data.Vocab_Cards.Where(c => !existingIds.Contains(c.Topic_Id) || incomingIds.Contains(c.Topic_Id)).ToList();
            _data.Topics = _data.Topics.Where(t => !existingIds.Contains(t.Id)).ToList();
            foreach (var topic in topics)
            {
                topic.Is_Dau_Curriculum = true;
                if (!topic.Name.StartsWith("DAU:", StringComparison.OrdinalIgnoreCase)) topic.Name = "DAU: " + topic.Name;
                if (string.IsNullOrWhiteSpace(topic.Level)) topic.Level = "B1";
                _data.Topics.Add(topic);
            }
            Save();
            return topics;
        }
    }

    public Topic? GetTopic(string id) => _data.Topics.FirstOrDefault(t => t.Id == id);
    public Topic? GetTopicForUser(string id, string userId) => _data.Topics.FirstOrDefault(t => t.Id == id && t.Owner_User_Id == userId);
    public bool IsCardOwnedByUser(string cardId, string userId)
    {
        var card = _data.Vocab_Cards.FirstOrDefault(c => c.Id == cardId);
        return card is not null && GetTopicForUser(card.Topic_Id, userId) is not null;
    }
    public void AddTopic(Topic topic) { lock (_sync) { _data.Topics.Insert(0, topic); Save(); } }
    public void DeleteTopic(string topicId) { lock (_sync) { _data.Topics = _data.Topics.Where(t => t.Id != topicId).ToList(); _data.Vocab_Cards = _data.Vocab_Cards.Where(c => c.Topic_Id != topicId).ToList(); Save(); } }
    public void UpdateTopicCardCount(string topicId)
    {
        lock (_sync)
        {
            var topic = _data.Topics.FirstOrDefault(item => item.Id == topicId);
            if (topic is null) return;
            topic.Card_Count = _data.Vocab_Cards.Count(card => card.Topic_Id == topicId);
            Save();
        }
    }

    public List<VocabCard> GetCardsByTopic(string topicId) => _data.Vocab_Cards.Where(c => c.Topic_Id == topicId).ToList();
    public List<VocabCard> GetAllCards() => _data.Vocab_Cards;
    public void AddCards(IEnumerable<VocabCard> cards) { lock (_sync) { _data.Vocab_Cards.AddRange(cards); Save(); } }
    public VocabCard AddCard(VocabCard card) { lock (_sync) { _data.Vocab_Cards.Insert(0, card); Save(); return card; } }
    public VocabCard? UpdateCard(string cardId, VocabCard patch)
    {
        lock (_sync)
        {
            var card = _data.Vocab_Cards.FirstOrDefault(c => c.Id == cardId);
            if (card is null) return null;
            card.Word_En = patch.Word_En ?? card.Word_En;
            card.Ipa_En = patch.Ipa_En ?? card.Ipa_En;
            card.Word_Zh = patch.Word_Zh ?? card.Word_Zh;
            card.Pinyin = patch.Pinyin ?? card.Pinyin;
            card.Word_Ja = patch.Word_Ja ?? card.Word_Ja;
            card.Reading_Ja = patch.Reading_Ja ?? card.Reading_Ja;
            card.Meaning_Vi = patch.Meaning_Vi ?? card.Meaning_Vi;
            card.Example_En = patch.Example_En ?? card.Example_En;
            card.Example_Zh = patch.Example_Zh ?? card.Example_Zh;
            card.Example_Ja = patch.Example_Ja ?? card.Example_Ja;
            card.Example_Meaning_Vi = patch.Example_Meaning_Vi ?? card.Example_Meaning_Vi;
            card.Difficulty = patch.Difficulty ?? card.Difficulty;
            Save();
            return card;
        }
    }

    public bool DeleteCard(string cardId) { lock (_sync) { var c = _data.Vocab_Cards.RemoveAll(c => c.Id == cardId); Save(); return c > 0; } }
    public VocabCard? ToggleCardLearned(string cardId)
    {
        lock (_sync)
        {
            var card = _data.Vocab_Cards.FirstOrDefault(c => c.Id == cardId);
            if (card is null) return null;
            card.Learned = !card.Learned;
            Save();
            return card;
        }
    }

    public List<User> GetUsers() => _data.Users.Select(u => new User
    {
        Id = u.Id,
        Email = u.Email,
        Name = u.Name,
        Role = u.Role,
        Student_Id = u.Student_Id,
        Department = u.Department,
        Expertise = u.Expertise,
        Target_Cefr = u.Target_Cefr,
        Created_At = u.Created_At,
        Status = u.Status,
        Last_Active = u.Last_Active
    }).ToList();

    public User? GetUser(string id) => GetUsers().FirstOrDefault(x => x.Id == id);
    public User? GetUserByEmail(string email) => _data.Users.FirstOrDefault(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
    public User AddUser(User user) { lock (_sync) { _data.Users.RemoveAll(u => u.Email.Equals(user.Email, StringComparison.OrdinalIgnoreCase)); _data.Users.Add(user); Save(); return GetUser(user.Id)!; } }
    public User? UpdateUser(string id, Dictionary<string, object?> updates)
    {
        lock (_sync)
        {
            var u = _data.Users.FirstOrDefault(x => x.Id == id);
            if (u is null) return null;
            if (updates.TryGetValue("name", out var name) && name is not null) u.Name = name.ToString()!;
            if (updates.TryGetValue("role", out var role) && role is not null) u.Role = role.ToString()!;
            if (updates.TryGetValue("department", out var dep)) u.Department = dep?.ToString();
            if (updates.TryGetValue("status", out var status) && status is not null) u.Status = status.ToString()!;
            if (updates.TryGetValue("last_active", out var lastActive)) u.Last_Active = lastActive?.ToString();
            Save();
            return GetUser(id);
        }
    }

    public bool DeleteUser(string id) { lock (_sync) { var c = _data.Users.RemoveAll(x => x.Id == id); Save(); return c > 0; } }

    public List<Assignment> GetAssignments(string? expertId = null)
    {
        var list = _data.Assignments;
        if (!string.IsNullOrWhiteSpace(expertId)) list = list.Where(a => a.Expert_Id == expertId).ToList();
        return list.Select(a =>
        {
            a.Submission_Count = _data.Submissions.Count(s => s.Assignment_Id == a.Id);
            return a;
        }).ToList();
    }


    public Assignment? GetAssignment(string id) => _data.Assignments.FirstOrDefault(a => a.Id == id);
    public Assignment AddAssignment(Assignment assignment) { lock (_sync) { _data.Assignments.Insert(0, assignment); Save(); return assignment; } }
    public Assignment? UpdateAssignment(string id, AssignmentUpdateDto updates)
    {
        lock (_sync)
        {
            var a = _data.Assignments.FirstOrDefault(x => x.Id == id);
            if (a is null) return null;
            if (!string.IsNullOrWhiteSpace(updates.Title)) a.Title = updates.Title.Trim();
            if (!string.IsNullOrWhiteSpace(updates.Skill)) a.Skill = updates.Skill.Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(updates.Target_Level)) a.Target_Level = updates.Target_Level.Trim();
            if (updates.Duration_Minutes.HasValue && updates.Duration_Minutes.Value > 0) a.Duration_Minutes = updates.Duration_Minutes.Value;
            if (updates.Content is not null) a.Content = updates.Content.Trim();
            if (updates.Instructions is not null) a.Instructions = updates.Instructions.Trim();
            if (updates.Due_Date is not null) a.Due_Date = updates.Due_Date.Trim();
            if (updates.Max_Score.HasValue && updates.Max_Score.Value > 0) a.Max_Score = updates.Max_Score.Value;
            if (updates.Reading_Quiz is not null) a.Reading_Quiz = updates.Reading_Quiz;
            Save();
            return a;
        }
    }
    public bool DeleteAssignment(string id) { lock (_sync) { var c = _data.Assignments.RemoveAll(a => a.Id == id); Save(); return c > 0; } }

    public List<Submission> GetSubmissions(string? studentId = null, string? assignmentId = null, string? status = null)
    {
        var list = _data.Submissions.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(studentId)) list = list.Where(s => s.Student_Id == studentId);
        if (!string.IsNullOrWhiteSpace(assignmentId)) list = list.Where(s => s.Assignment_Id == assignmentId);
        if (!string.IsNullOrWhiteSpace(status)) list = list.Where(s => s.Status == status);
        return list.ToList();
    }

    public Submission? GetSubmission(string id) => _data.Submissions.FirstOrDefault(s => s.Id == id);

    public Submission AddSubmission(Submission submission)
    {
        lock (_sync)
        {
            // Tự động chấm phần trắc nghiệm nếu là bài Reading và có câu hỏi trắc nghiệm
            if (submission.Skill.Equals("reading", StringComparison.OrdinalIgnoreCase))
            {
                var assign = GetAssignment(submission.Assignment_Id);
                if (assign is not null && !string.IsNullOrWhiteSpace(assign.Reading_Quiz) && !string.IsNullOrWhiteSpace(submission.Reading_Answers))
                {
                    try
                    {
                        var questions = JsonSerializer.Deserialize<List<ReadingQuizQuestion>>(assign.Reading_Quiz, _jsonOptions);
                        var answers = JsonSerializer.Deserialize<Dictionary<string, string>>(submission.Reading_Answers, _jsonOptions);
                        if (questions is not null && questions.Count > 0 && answers is not null)
                        {
                            int correct = 0;
                            foreach (var q in questions)
                            {
                                if (answers.TryGetValue(q.Id.ToString(), out var ans) && ans.Trim().Equals(q.Correct_Answer.Trim(), StringComparison.OrdinalIgnoreCase))
                                {
                                    correct++;
                                }
                            }
                            // Thang điểm trắc nghiệm tối đa là 5.0 (50%), 5.0 còn lại dành cho câu chủ đề do Expert chấm
                            submission.Reading_Auto_Score = Math.Round((double)correct / questions.Count * 5.0, 1);
                        }
                    }
                    catch { }
                }
            }

            _data.Submissions.Insert(0, submission);
            Save();
            return submission;
        }
    }

    public Submission? GradeSubmission(string id, double score, string feedback, string gradedBy, string? assessedLevel)
    {
        lock (_sync)
        {
            var s = _data.Submissions.FirstOrDefault(x => x.Id == id);
            if (s is null) return null;
            s.Score = Math.Clamp(score, 0.0, 10.0);
            s.Feedback = feedback;
            s.Graded_By = gradedBy;
            s.Graded_At = DateTime.UtcNow.ToString("O");
            s.Status = "graded";
            s.Assessed_Level = assessedLevel ?? "B2";
            Save();
            return s;
        }
    }

    public List<Consultation> GetConsultations(string? studentId = null)
    {
        if (string.IsNullOrWhiteSpace(studentId)) return _data.Consultations;
        return _data.Consultations.Where(c => c.Student_Id == studentId).ToList();
    }

    public Consultation AddConsultation(Consultation consultation) { lock (_sync) { _data.Consultations.Insert(0, consultation); Save(); return consultation; } }

    public List<object> GetStudentProgress()
    {
        var students = _data.Users.Where(u => u.Role == "student").ToList();
        var allCards = _data.Vocab_Cards.Count;
        var learned = _data.Vocab_Cards.Count(c => c.Learned);
        return students.Select(s =>
        {
            var subs = _data.Submissions.Where(x => x.Student_Id == s.Id).ToList();
            var graded = subs.Where(x => x.Score.HasValue).ToList();
            var avg = graded.Count > 0 ? graded.Average(x => x.Score!.Value) : 0;
            return new
            {
                user = GetUser(s.Id),
                total_cards = allCards,
                learned_cards = learned,
                retention_rate = allCards == 0 ? 0 : (int)Math.Round((double)learned / allCards * 100),
                submissions_count = subs.Count,
                average_score = (int)Math.Round(avg),
                assessed_cefr = s.Target_Cefr ?? "B2",
                recent_activity = s.Last_Active ?? "Gần đây"
            };
        }).Cast<object>().ToList();
    }

    public AiConfig GetAiConfig()
    {
        if (string.IsNullOrWhiteSpace(_data.Ai_Config.Model_Name)
            || _data.Ai_Config.Model_Name.Equals("gemini-2.5-flash", StringComparison.OrdinalIgnoreCase))
            _data.Ai_Config.Model_Name = "gemini-3.8-flash";
        return _data.Ai_Config;
    }
    public AiConfig UpdateAiConfig(AiConfig cfg) { lock (_sync) { _data.Ai_Config = cfg; Save(); return _data.Ai_Config; } }

    public List<ApiLog> GetApiLogs(int limit = 50) => _data.Api_Logs.Take(limit).ToList();
    public void AddApiLog(ApiLog log) { lock (_sync) { _data.Api_Logs.Insert(0, log); if (_data.Api_Logs.Count > 500) _data.Api_Logs = _data.Api_Logs.Take(500).ToList(); Save(); } }

    public object GetAdminStats()
    {
        var students = _data.Users.Count(u => u.Role == "student");
        var experts = _data.Users.Count(u => u.Role == "expert");
        return new
        {
            total_students = students,
            total_experts = experts,
            total_topics = _data.Topics.Count,
            total_cards = _data.Vocab_Cards.Count,
            total_assignments = _data.Assignments.Count,
            total_submissions = _data.Submissions.Count,
            total_api_calls = _data.Api_Logs.Count,
            active_users_today = students + experts + 1
        };
    }

    public List<VideoSession> GetVideoSessions() => _data.Video_Sessions.Select(s =>
    {
        s.Words_Count = _data.Video_Words.Count(w => w.Session_Id == s.Id);
        return s;
    }).ToList();

    public VideoSession? GetVideoSession(string sessionOrVideoId) => _data.Video_Sessions.FirstOrDefault(s => s.Id == sessionOrVideoId || s.Video_Id == sessionOrVideoId);
    public List<VideoWord> GetVideoWords(string sessionId) => _data.Video_Words.Where(w => w.Session_Id == sessionId).OrderBy(w => w.Timestamp_Sec).ToList();
    public VideoSession AddVideoSession(VideoSession session) { lock (_sync) { _data.Video_Sessions.RemoveAll(x => x.Id == session.Id); _data.Video_Sessions.Insert(0, session); Save(); return session; } }
    public void AddVideoWords(IEnumerable<VideoWord> words) { lock (_sync) { var ids = words.Select(w => w.Id).ToHashSet(); _data.Video_Words = _data.Video_Words.Where(w => !ids.Contains(w.Id)).ToList(); _data.Video_Words.AddRange(words); Save(); } }
    public VideoSession? UpdateVideoSubtitles(string sessionId, List<SubtitleItem> subtitles)
    {
        lock (_sync)
        {
            var s = GetVideoSession(sessionId);
            if (s is null) return null;
            s.Subtitles = subtitles;
            Save();
            return s;
        }
    }
}
