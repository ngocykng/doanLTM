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
        if (changed) Save("topics");
    }

    private void EnsureFixedAdmin()
    {
        var admin = _data.Users.FirstOrDefault(u => u.Email.Equals("admin@123", StringComparison.OrdinalIgnoreCase))
            ?? _data.Users.FirstOrDefault(u => u.Id == "user-admin-root" && u.Role == "admin");
        if (admin is null)
        {
            admin = new User
            {
                Id = "user-admin-root", Email = "admin@123", Password = PasswordSecurity.HashPassword("123456"),
                Name = "Quản trị viên", Role = "admin", Department = "DAU", Status = "active"
            };
            _data.Users.Add(admin);
        }
        else
        {
            if (!PasswordSecurity.VerifyPassword("123456", admin.Password) || !PasswordSecurity.IsHashed(admin.Password))
                admin.Password = PasswordSecurity.HashPassword("123456");
            admin.Email = "admin@123";
            admin.Role = "admin";
            admin.Status = "active";
            admin.Name = "Quản trị viên";
        }
        foreach (var user in _data.Users.Where(u => u.Id != admin!.Id && string.IsNullOrWhiteSpace(u.Password)))
        {
            user.Password = PasswordSecurity.HashPassword("123456");
            _logger.LogWarning("User {Email} had no stored password and was assigned the temporary recovery password.", user.Email);
        }
        Save("users");
    }

    public bool TrySyncToMySql(out string message)
    {
        lock (_sync)
        {
            if (_mysql.TrySync(_data, out message))
            {
                StorageMode = "MySQL";
                return true;
            }
            return false;
        }
    }

    public void UpgradePasswordHash(string userId, string plainPassword)
    {
        lock (_sync)
        {
            var u = _data.Users.FirstOrDefault(x => x.Id == userId);
            if (u is null || PasswordSecurity.IsHashed(u.Password)) return;
            u.Password = PasswordSecurity.HashPassword(plainPassword);
            Save("users");
        }
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

        return new DatabaseSchema();
    }

    private void Save(params string[] changedTables)
    {
        var text = JsonSerializer.Serialize(_data, _jsonOptions);
        File.WriteAllText(_dbPath, text);
        if (StorageMode == "MySQL")
        {
            if (changedTables.Length == 0) _mysql.Save(_data);
            else _mysql.SaveTables(_data, changedTables);
        }
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
            Save("topics", "vocab_cards");
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
    public void AddTopic(Topic topic) { lock (_sync) { _data.Topics.Insert(0, topic); Save("topics"); } }
    public void DeleteTopic(string topicId) { lock (_sync) { _data.Topics = _data.Topics.Where(t => t.Id != topicId).ToList(); _data.Vocab_Cards = _data.Vocab_Cards.Where(c => c.Topic_Id != topicId).ToList(); Save("topics", "vocab_cards"); } }
    public void UpdateTopicCardCount(string topicId)
    {
        lock (_sync)
        {
            var topic = _data.Topics.FirstOrDefault(item => item.Id == topicId);
            if (topic is null) return;
            topic.Card_Count = _data.Vocab_Cards.Count(card => card.Topic_Id == topicId);
            Save("topics");
        }
    }

    public List<VocabCard> GetCardsByTopic(string topicId) => _data.Vocab_Cards.Where(c => c.Topic_Id == topicId).ToList();
    public List<VocabCard> GetAllCards() => _data.Vocab_Cards;
    public void AddCards(IEnumerable<VocabCard> cards) { lock (_sync) { _data.Vocab_Cards.AddRange(cards); Save("vocab_cards"); } }
    public VocabCard AddCard(VocabCard card) { lock (_sync) { _data.Vocab_Cards.Insert(0, card); Save("vocab_cards"); return card; } }
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
            if (patch.Note is not null) card.Note = patch.Note;
            Save("vocab_cards");
            return card;
        }
    }

    public VocabCard? UpdateCardReview(
        string cardId,
        bool? learned,
        string? note,
        int? reviewCount,
        string? lastReviewedAt,
        string? nextReviewAt,
        int? intervalDays,
        double? easeFactor)
    {
        lock (_sync)
        {
            var card = _data.Vocab_Cards.FirstOrDefault(c => c.Id == cardId);
            if (card is null) return null;
            if (learned.HasValue) card.Learned = learned.Value;
            if (note is not null) card.Note = note;
            if (reviewCount.HasValue) card.Review_Count = reviewCount.Value;
            if (lastReviewedAt is not null) card.Last_Reviewed_At = lastReviewedAt;
            if (nextReviewAt is not null) card.Next_Review_At = nextReviewAt;
            if (intervalDays.HasValue) card.Interval_Days = intervalDays.Value;
            if (easeFactor.HasValue && easeFactor.Value >= 1.3) card.Ease_Factor = easeFactor.Value;
            Save("vocab_cards");
            return card;
        }
    }

    public bool DeleteCard(string cardId) { lock (_sync) { var c = _data.Vocab_Cards.RemoveAll(c => c.Id == cardId); Save("vocab_cards"); return c > 0; } }
    public VocabCard? ToggleCardLearned(string cardId)
    {
        lock (_sync)
        {
            var card = _data.Vocab_Cards.FirstOrDefault(c => c.Id == cardId);
            if (card is null) return null;
            card.Learned = !card.Learned;
            Save("vocab_cards");
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
    public User AddUser(User user) { lock (_sync) { _data.Users.RemoveAll(u => u.Email.Equals(user.Email, StringComparison.OrdinalIgnoreCase)); _data.Users.Add(user); Save("users"); return GetUser(user.Id)!; } }
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
            Save("users");
            return GetUser(id);
        }
    }

    public bool DeleteUser(string id) { lock (_sync) { var c = _data.Users.RemoveAll(x => x.Id == id); Save("users"); return c > 0; } }

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

    public List<Assignment> GetStudentAssignments(string studentId)
    {
        var list = _data.Assignments
            .Where(a => (a.Status == "published" || string.IsNullOrWhiteSpace(a.Status)) &&
                        (string.IsNullOrWhiteSpace(a.Target_Student_Id) || a.Target_Student_Id == studentId))
            .ToList();

        return list.Select(a => SanitizeAssignmentForStudent(a)).ToList();
    }

    public Assignment? GetSanitizedAssignmentForStudent(string id, string studentId)
    {
        var a = _data.Assignments.FirstOrDefault(x => x.Id == id);
        if (a is null) return null;
        if (a.Status != "published" && !string.IsNullOrWhiteSpace(a.Status)) return null;
        if (!string.IsNullOrWhiteSpace(a.Target_Student_Id) && a.Target_Student_Id != studentId) return null;
        return SanitizeAssignmentForStudent(a);
    }

    private static Assignment SanitizeAssignmentForStudent(Assignment source)
    {
        // Tạo bản copy không chứa đáp án đúng để gửi cho sinh viên làm bài
        var copy = JsonSerializer.Deserialize<Assignment>(JsonSerializer.Serialize(source))!;
        if (copy.Exam_Structure is not null)
        {
            foreach (var section in copy.Exam_Structure.Sections)
            {
                foreach (var q in section.Questions)
                {
                    q.Correct_Answer = string.Empty;
                    q.Explanation = null;
                }
            }
        }
        if (!string.IsNullOrWhiteSpace(copy.Reading_Quiz))
        {
            try
            {
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
                var quiz = JsonSerializer.Deserialize<List<ReadingQuizQuestion>>(copy.Reading_Quiz, opts);
                if (quiz is not null)
                {
                    foreach (var q in quiz) q.Correct_Answer = string.Empty;
                    copy.Reading_Quiz = JsonSerializer.Serialize(quiz, opts);
                }
            }
            catch { }
        }
        return copy;
    }

    public Assignment? GetAssignment(string id) => _data.Assignments.FirstOrDefault(a => a.Id == id);
    public Assignment AddAssignment(Assignment assignment) { lock (_sync) { _data.Assignments.Insert(0, assignment); Save("assignments"); return assignment; } }
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
            if (updates.Reference_Media_Url is not null)
                a.Reference_Media_Url = string.IsNullOrWhiteSpace(updates.Reference_Media_Url) ? null : updates.Reference_Media_Url.Trim();
            if (updates.Due_Date is not null) a.Due_Date = updates.Due_Date.Trim();
            if (updates.Max_Score.HasValue && updates.Max_Score.Value > 0) a.Max_Score = updates.Max_Score.Value;
            if (updates.Reading_Quiz is not null) a.Reading_Quiz = updates.Reading_Quiz;
            if (updates.Target_Student_Id is not null) a.Target_Student_Id = updates.Target_Student_Id;
            if (updates.Target_Student_Name is not null) a.Target_Student_Name = updates.Target_Student_Name;
            if (updates.Topic_Ids is not null) a.Topic_Ids = updates.Topic_Ids;
            if (updates.Topic_Names is not null) a.Topic_Names = updates.Topic_Names;
            if (!string.IsNullOrWhiteSpace(updates.Status)) a.Status = updates.Status;
            if (updates.Show_Answers.HasValue) a.Show_Answers = updates.Show_Answers.Value;
            if (updates.Exam_Structure is not null) a.Exam_Structure = updates.Exam_Structure;
            Save("assignments");
            return a;
        }
    }

    public Assignment? PublishAssignment(string id)
    {
        lock (_sync)
        {
            var a = _data.Assignments.FirstOrDefault(x => x.Id == id);
            if (a is null) return null;
            a.Status = "published";
            Save("assignments");
            return a;
        }
    }

    public Assignment? SetShowAnswers(string id, bool showAnswers)
    {
        lock (_sync)
        {
            var a = _data.Assignments.FirstOrDefault(x => x.Id == id);
            if (a is null) return null;
            a.Show_Answers = showAnswers;
            Save("assignments");
            return a;
        }
    }
    public bool DeleteAssignment(string id) { lock (_sync) { var c = _data.Assignments.RemoveAll(a => a.Id == id); Save("assignments"); return c > 0; } }

    public List<Submission> GetSubmissions(string? studentId = null, string? assignmentId = null, string? status = null)
    {
        var list = _data.Submissions.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(studentId)) list = list.Where(s => s.Student_Id == studentId);
        if (!string.IsNullOrWhiteSpace(assignmentId)) list = list.Where(s => s.Assignment_Id == assignmentId);
        if (!string.IsNullOrWhiteSpace(status)) list = list.Where(s => s.Status == status);
        return list.Select(s =>
        {
            var assign = GetAssignment(s.Assignment_Id);
            s.Show_Answers = assign?.Show_Answers ?? false;
            if (s.Exam_Review is null && assign?.Exam_Structure is not null)
                s.Exam_Review = assign.Exam_Structure;
            return s;
        }).ToList();
    }

    public List<Submission> GetSubmissionsForStudent(string studentId)
    {
        var list = _data.Submissions.Where(s => s.Student_Id == studentId).ToList();
        return list.Select(s => SanitizeSubmissionForStudent(s)).ToList();
    }

    public Submission? GetSanitizedSubmissionForStudent(string id, string studentId)
    {
        var s = _data.Submissions.FirstOrDefault(x => x.Id == id && x.Student_Id == studentId);
        if (s is null) return null;
        return SanitizeSubmissionForStudent(s);
    }

    private Submission SanitizeSubmissionForStudent(Submission source)
    {
        var copy = JsonSerializer.Deserialize<Submission>(JsonSerializer.Serialize(source))!;
        var assign = GetAssignment(copy.Assignment_Id);
        bool showAnswers = assign?.Show_Answers == true;
        copy.Show_Answers = showAnswers;

        if (showAnswers)
        {
            copy.Exam_Review = assign?.Exam_Structure;
        }
        else
        {
            copy.Exam_Review = null;
            if (copy.Exam_Answers is not null)
            {
                foreach (var a in copy.Exam_Answers)
                {
                    a.Correct_Answer = null;
                }
            }
        }
        return copy;
    }

    public Submission? GetSubmission(string id)
    {
        var s = _data.Submissions.FirstOrDefault(x => x.Id == id);
        if (s is not null)
        {
            var assign = GetAssignment(s.Assignment_Id);
            s.Show_Answers = assign?.Show_Answers ?? false;
            if (s.Exam_Review is null && assign?.Exam_Structure is not null)
                s.Exam_Review = assign.Exam_Structure;
        }
        return s;
    }

    public Submission AddSubmission(Submission submission)
    {
        lock (_sync)
        {
            var assign = GetAssignment(submission.Assignment_Id);
            if (assign?.Exam_Structure is not null)
            {
                submission.Skill = "exam";
                submission.Assignment_Title = assign.Title;
                double totalPoints = 0;
                var gradedAnswers = new List<StudentExamAnswer>();
                var sectionPoints = new Dictionary<string, double>();

                var submittedDict = submission.Exam_Answers?
                    .ToDictionary(a => a.Question_Number, a => a.Selected_Answer?.Trim() ?? "")
                    ?? new Dictionary<int, string>();

                foreach (var section in assign.Exam_Structure.Sections)
                {
                    double secEarned = 0;
                    foreach (var q in section.Questions)
                    {
                        submittedDict.TryGetValue(q.Question_Number, out var studentAns);
                        studentAns ??= "";

                        bool isCorrect = false;
                        if (section.Section_Type.Equals("word_ordering", StringComparison.OrdinalIgnoreCase))
                        {
                            var normStudent = NormalizeSentence(studentAns);
                            var normCorrect = NormalizeSentence(q.Correct_Answer);
                            isCorrect = !string.IsNullOrWhiteSpace(normStudent) && normStudent.Equals(normCorrect, StringComparison.OrdinalIgnoreCase);
                        }
                        else
                        {
                            var cleanStudent = CleanOptionLetter(studentAns);
                            var cleanCorrect = CleanOptionLetter(q.Correct_Answer);
                            isCorrect = !string.IsNullOrWhiteSpace(cleanStudent) && cleanStudent.Equals(cleanCorrect, StringComparison.OrdinalIgnoreCase);
                        }

                        double earned = isCorrect ? q.Points : 0.0;
                        secEarned += earned;
                        totalPoints += earned;

                        gradedAnswers.Add(new StudentExamAnswer
                        {
                            Question_Number = q.Question_Number,
                            Section_Id = section.Section_Id,
                            Selected_Answer = studentAns,
                            Correct_Answer = q.Correct_Answer,
                            Is_Correct = isCorrect,
                            Earned_Points = Math.Round(earned, 2),
                            Max_Points = q.Points
                        });
                    }
                    sectionPoints[section.Section_Id] = Math.Round(secEarned, 2);
                }

                submission.Exam_Answers = gradedAnswers;
                submission.Section_Scores = sectionPoints;
                submission.Auto_Score = Math.Round(totalPoints, 2);
                submission.Score = submission.Auto_Score;
                submission.Status = "submitted";
                submission.Exam_Review = assign.Exam_Structure;
            }
            else if (submission.Skill.Equals("reading", StringComparison.OrdinalIgnoreCase)
                     || submission.Skill.Equals("listening", StringComparison.OrdinalIgnoreCase))
            {
                if (assign is not null && !string.IsNullOrWhiteSpace(assign.Reading_Quiz) && !string.IsNullOrWhiteSpace(submission.Reading_Answers))
                {
                    try
                    {
                        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
                        var questions = JsonSerializer.Deserialize<List<ReadingQuizQuestion>>(assign.Reading_Quiz, opts);
                        var answers = JsonSerializer.Deserialize<Dictionary<string, string>>(submission.Reading_Answers, opts);
                        if (questions is not null && questions.Count > 0 && answers is not null)
                        {
                            int correct = 0;
                            foreach (var q in questions)
                            {
                                if (answers.TryGetValue(q.Id.ToString(), out var ans)
                                    && CleanOptionLetter(ans).Equals(CleanOptionLetter(q.Correct_Answer), StringComparison.OrdinalIgnoreCase))
                                {
                                    correct++;
                                }
                            }
                            submission.Reading_Auto_Score = Math.Round((double)correct / questions.Count * 5.0, 1);
                        }
                    }
                    catch { }
                }
            }

            _data.Submissions.Insert(0, submission);
            Save("submissions");
            return submission;
        }
    }

    private static string CleanOptionLetter(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        var t = input.Trim();
        if (t.Length >= 2 && (t[1] == '.' || t[1] == ')' || t[1] == ' ' || t[1] == ':'))
            return t[..1].ToUpperInvariant();
        if (t.Length == 1) return t.ToUpperInvariant();
        return t;
    }

    private static string NormalizeSentence(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var trimmed = s.Trim().TrimEnd('.', '!', '?', ';', ',');
        return System.Text.RegularExpressions.Regex.Replace(trimmed, @"\s+", " ");
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
            Save("submissions");
            return s;
        }
    }

    public List<Consultation> GetConsultations(string? studentId = null)
    {
        if (string.IsNullOrWhiteSpace(studentId)) return _data.Consultations;
        return _data.Consultations.Where(c => c.Student_Id == studentId).ToList();
    }

    public Consultation AddConsultation(Consultation consultation) { lock (_sync) { _data.Consultations.Insert(0, consultation); Save("consultations"); return consultation; } }

    public List<object> GetStudentProgress()
    {
        var students = _data.Users.Where(u => u.Role == "student").ToList();
        return students.Select(s =>
        {
            var topicIds = _data.Topics
                .Where(t => string.Equals(t.Owner_User_Id, s.Id, StringComparison.Ordinal))
                .Select(t => t.Id)
                .ToHashSet();
            var studentCards = _data.Vocab_Cards.Where(c => topicIds.Contains(c.Topic_Id)).ToList();
            var allCards = studentCards.Count;
            var learned = studentCards.Count(c => c.Learned);
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
                average_score = Math.Round(avg, 1),
                assessed_cefr = graded.FirstOrDefault()?.Assessed_Level ?? s.Target_Cefr ?? "B2",
                recent_activity = s.Last_Active ?? "Gần đây"
            };
        }).Cast<object>().ToList();
    }

    public AiConfig GetAiConfig()
    {
        if (string.IsNullOrWhiteSpace(_data.Ai_Config.Model_Name))
            _data.Ai_Config.Model_Name = "gemini-2.5-flash";
        return _data.Ai_Config;
    }
    public AiConfig UpdateAiConfig(AiConfig cfg) { lock (_sync) { _data.Ai_Config = cfg; Save("ai_config"); return _data.Ai_Config; } }

    public List<ApiLog> GetApiLogs(int limit = 50) => _data.Api_Logs.Take(limit).ToList();
    public void AddApiLog(ApiLog log)
    {
        bool trimNeeded;
        lock (_sync)
        {
            _data.Api_Logs.Insert(0, log);
            trimNeeded = _data.Api_Logs.Count > 500;
            if (trimNeeded) _data.Api_Logs = _data.Api_Logs.Take(500).ToList();
            File.WriteAllText(_dbPath, JsonSerializer.Serialize(_data, _jsonOptions));
        }

        if (StorageMode == "MySQL")
        {
            if (trimNeeded) lock (_sync) _mysql.SaveTables(_data, "api_logs");
            else _mysql.InsertApiLog(log);
        }
    }

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
    public VideoSession AddVideoSession(VideoSession session) { lock (_sync) { _data.Video_Sessions.RemoveAll(x => x.Id == session.Id); _data.Video_Sessions.Insert(0, session); Save("video_sessions"); return session; } }
    public void AddVideoWords(IEnumerable<VideoWord> words) { lock (_sync) { var ids = words.Select(w => w.Id).ToHashSet(); _data.Video_Words = _data.Video_Words.Where(w => !ids.Contains(w.Id)).ToList(); _data.Video_Words.AddRange(words); Save("video_words"); } }
    public VideoSession? UpdateVideoSubtitles(string sessionId, List<SubtitleItem> subtitles)
    {
        lock (_sync)
        {
            var s = GetVideoSession(sessionId);
            if (s is null) return null;
            s.Subtitles = subtitles;
            Save("video_sessions");
            return s;
        }
    }
}
