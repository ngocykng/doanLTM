using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using EnglishFlashcard3D.Models;
using EnglishFlashcard3D.Services;

namespace EnglishFlashcard3D.ViewModels;

public class MainViewModel : BaseViewModel
{
    private readonly ApiClientService _apiClient;
    private readonly DataStorageService _storageService;
    private string _email = string.Empty;
    private string _password = string.Empty;
    private string _registerName = string.Empty;
    private bool _isRegistering;
    private int _activeTabIndex;
    private string _statusMessage = "Sẵn sàng";
    private bool _isLoggedIn;
    private bool _isTopicBusy;
    private TopicDeck? _selectedTopic;
    private string _newTopicName = string.Empty;
    private string _selectedCefr = "B1";
    private AuthUser? _currentUser;
    private ApiAssignment? _selectedAssignment;
    private ApiSubmission? _selectedSubmission;
    private AuthUser? _selectedUser;
    private ApiTopic? _selectedCurriculum;
    private string _assignmentTitle = "";
    private string _assignmentInstructions = "";
    private string _assignmentContent = "";
    private int _assignmentDuration = 45;
    private string _assignmentReadingQuiz = "";
    private bool _isEditingAssignment = false;
    private string _assignmentSkill = "writing";
    private string _assignmentDueDate = DateTime.Today.AddDays(7).ToString("yyyy-MM-dd");
    private string _submissionContent = "";
    private string _studentAudioUrl = "";
    private string _studentSpeakingNotes = "";
    private string _studentTopicSentence = "";
    private string _studentReadingAns1 = "A";
    private string _studentReadingAns2 = "B";
    private string _studentReadingAns3 = "C";
    private string _wordCountText = "Số từ: 0 từ";
    private string _gradeScore = "";
    private string _gradeFeedback = "";
    private string _gradeLevel = "B2";
    private string _curriculumName = "";
    private string _curriculumLevel = "B1";
    private string _adminSummary = "Chưa tải dữ liệu quản trị";
    private ApiAiConfig _aiConfig = new();

    // ==========================================
    // CÁC TRƯỜNG DÀNH CHO KHẢO THÍ F4
    // ==========================================
    private AuthUser? _selectedExamStudent;
    private ApiTopic? _selectedExamTopic;
    private string _examCefrLevel = "B2";
    private bool _includeSigns = true;
    private int _signsCount = 2;
    private double _signsPoints = 0.5;
    private bool _includeCloze = true;
    private int _clozeCount = 6;
    private double _clozePoints = 1.5;
    private bool _includeWordOrdering = true;
    private int _wordOrderingCount = 2;
    private double _wordOrderingPoints = 1.0;
    private bool _includeVocabulary = true;
    private int _vocabularyCount = 4;
    private double _vocabularyPoints = 1.0;
    private int _examDuration = 45;
    private string _examDueDate = DateTime.Today.AddDays(7).ToString("yyyy-MM-dd");
    private bool _examShowAnswers = false;
    private ApiExamStructure? _generatedExam;
    private bool _isGeneratingExam = false;

    private bool _isTakingExam = false;
    private ApiAssignment? _activeTakingAssignment;
    private string _examRemainingTimeText = "45:00";
    private System.Windows.Threading.DispatcherTimer? _examTimer;
    private int _examRemainingSeconds = 2700;

    public MainViewModel()
    {
        _apiClient = new ApiClientService();
        _storageService = new DataStorageService();

        var localCards = _storageService.LoadDefaultCards();
        FlashcardSession = new Flashcard3DViewModel(localCards);
        FlashcardSession.CardMarkedMastered += OnCardMarkedMastered;
        FlashcardSession.ToastTriggered += msg => StatusMessage = msg;

        Topics = new ObservableCollection<TopicDeck>();
        Assignments = new ObservableCollection<ApiAssignment>();
        Submissions = new ObservableCollection<ApiSubmission>();
        StudentSubmissions = new ObservableCollection<ApiSubmission>();
        Users = new ObservableCollection<AuthUser>();
        ApiLogs = new ObservableCollection<ApiLogItem>();
        Curriculum = new ObservableCollection<ApiTopic>();
        CefrLevels = new ObservableCollection<string> { "A1", "A2", "B1", "B2", "C1", "C2" };
        Skills = new ObservableCollection<string> { "writing", "speaking", "listening", "reading" };
        Roles = new ObservableCollection<string> { "student", "expert" };
        UserStatuses = new ObservableCollection<string> { "active", "suspended" };

        StudentsList = new ObservableCollection<AuthUser>();
        StudentTopics = new ObservableCollection<ApiTopic>();
        ExamTakingSections = new ObservableCollection<ExamTakingSectionItem>();
        ExamAnswerGrid = new ObservableCollection<ExamAnswerGridItem>();

        GenerateExamCommand = new RelayCommand(_ => _ = GenerateExamAsync(), _ => IsRole("expert") && SelectedExamStudent is not null && !IsGeneratingExam);
        SaveExamDraftCommand = new RelayCommand(_ => _ = SaveExamAsync("draft"), _ => IsRole("expert") && GeneratedExam is not null);
        PublishExamCommand = new RelayCommand(_ => _ = SaveExamAsync("published"), _ => IsRole("expert") && GeneratedExam is not null);
        PublishSelectedAssignmentCommand = new RelayCommand(_ => _ = PublishSelectedAssignmentAsync(), _ => IsRole("expert") && SelectedAssignment is not null);
        ToggleShowAnswersCommand = new RelayCommand(_ => _ = ToggleShowAnswersAsync(), _ => IsRole("expert") && SelectedAssignment is not null);
        StartExamCommand = new RelayCommand(_ => _ = StartExamAsync(), _ => IsRole("student") && SelectedAssignment is not null && !IsTakingExam);
        SubmitExamCommand = new RelayCommand(_ => _ = SubmitExamAsync(false), _ => IsRole("student") && IsTakingExam);
        CancelExamCommand = new RelayCommand(_ => CancelExam(), _ => IsTakingExam);

        LoginCommand = new RelayCommand(_ => _ = LoginAsync());
        LogoutCommand = new RelayCommand(_ =>
        {
            IsLoggedIn = false;
            CurrentUser = null;
            SelectedTopic = null;
            Topics.Clear();
            FlashcardSession.SetCards(new List<EnglishFlashcard>());
            _apiClient.ClearAuthSession();
            IsLoggedIn = false;
            CurrentUser = null;
            Email = string.Empty;
            Password = string.Empty;
            StatusMessage = "Bạn đã đăng xuất.";
        });
        ToggleRegisterCommand = new RelayCommand(_ => { IsRegistering = !IsRegistering; StatusMessage = IsRegistering ? "Tạo tài khoản mới (role mặc định: student)." : "Đăng nhập bằng tài khoản của bạn."; });
        LoadTopicsCommand = new RelayCommand(_ => _ = LoadTopicsAsync(), _ => IsStudent && !IsTopicBusy);
        GenerateTopicCommand = new RelayCommand(_ => _ = GenerateTopicAsync(), _ => IsStudent && !IsTopicBusy && !string.IsNullOrWhiteSpace(NewTopicName) && NewTopicName.Trim().Length <= 80);
        OpenTopicCommand = new RelayCommand(_ => _ = OpenSelectedTopicAsync(), _ => IsStudent && !IsTopicBusy && SelectedTopic is not null);
        GenerateBatchCommand = new RelayCommand(_ => _ = GenerateBatchAsync(), _ => IsStudent && !IsTopicBusy && SelectedTopic is not null);
        DeleteTopicCommand = new RelayCommand(_ => _ = DeleteTopicAsync(), _ => IsStudent && !IsTopicBusy && SelectedTopic is not null);
        RefreshF4Command = new RelayCommand(_ => _ = LoadF4Async(), _ => IsLoggedIn);
        CreateAssignmentCommand = new RelayCommand(_ => _ = CreateAssignmentAsync(), _ => IsRole("expert"));
        UpdateAssignmentCommand = new RelayCommand(_ => _ = UpdateAssignmentAsync(), _ => IsRole("expert") && SelectedAssignment is not null);
        DeleteAssignmentCommand = new RelayCommand(_ => _ = DeleteAssignmentAsync(), _ => IsRole("expert") && SelectedAssignment is not null);
        EditAssignmentCommand = new RelayCommand(_ => PrepareEditAssignment(), _ => IsRole("expert") && SelectedAssignment is not null);
        ClearAssignmentFormCommand = new RelayCommand(_ => ClearAssignmentForm());
        SubmitWorkCommand = new RelayCommand(_ => _ = SubmitWorkAsync(), _ => IsRole("student") && SelectedAssignment is not null);
        GradeWorkCommand = new RelayCommand(_ => _ = GradeWorkAsync(), _ => IsRole("expert") && SelectedSubmission is not null);
        RefreshAdminCommand = new RelayCommand(_ => _ = LoadAdminAsync(), _ => IsRole("admin"));
        SyncMysqlCommand = new RelayCommand(_ => _ = SyncMysqlAsync(), _ => IsRole("admin"));
        SaveUserCommand = new RelayCommand(_ => _ = SaveUserAsync(), _ => IsRole("admin") && SelectedUser is not null);
        SaveAiConfigCommand = new RelayCommand(_ => _ = SaveAiConfigAsync(), _ => IsRole("admin"));
        AddCurriculumCommand = new RelayCommand(_ => _ = AddCurriculumAsync(), _ => IsRole("admin") && !string.IsNullOrWhiteSpace(CurriculumName));
        DeleteCurriculumCommand = new RelayCommand(_ => _ = DeleteCurriculumAsync(), _ => IsRole("admin") && SelectedCurriculum is not null);
    }

    public Flashcard3DViewModel FlashcardSession { get; }

    public ObservableCollection<TopicDeck> Topics { get; }
    public ObservableCollection<ApiAssignment> Assignments { get; }
    public ObservableCollection<ApiSubmission> Submissions { get; }
    public ObservableCollection<ApiSubmission> StudentSubmissions { get; }
    public ObservableCollection<AuthUser> Users { get; }
    public ObservableCollection<ApiLogItem> ApiLogs { get; }
    public ObservableCollection<ApiTopic> Curriculum { get; }
    public ObservableCollection<string> Skills { get; }
    public ObservableCollection<string> Roles { get; }
    public ObservableCollection<string> UserStatuses { get; }

    public ObservableCollection<AuthUser> StudentsList { get; }
    public ObservableCollection<ApiTopic> StudentTopics { get; }
    public ObservableCollection<ExamTakingSectionItem> ExamTakingSections { get; }
    public ObservableCollection<ExamAnswerGridItem> ExamAnswerGrid { get; }

    public ApiStats AdminStats { get; private set; } = new();
    public ApiAiConfig AiConfig { get => _aiConfig; set => SetField(ref _aiConfig, value); }
    public AuthUser? CurrentUser { get => _currentUser; set { if (SetField(ref _currentUser, value)) { RaiseCommandStates(); OnPropertyChanged(nameof(IsStudent)); OnPropertyChanged(nameof(IsExpert)); OnPropertyChanged(nameof(IsAdmin)); OnPropertyChanged(nameof(IsAssessmentUser)); } } }
    public string RegisterName { get => _registerName; set => SetField(ref _registerName, value); }
    public bool IsRegistering { get => _isRegistering; set => SetField(ref _isRegistering, value); }
    public int ActiveTabIndex { get => _activeTabIndex; set => SetField(ref _activeTabIndex, value); }
    public bool IsLoggedOut => !IsLoggedIn;
    public bool IsStudent => IsRole("student");
    public bool IsTopicBusy
    {
        get => _isTopicBusy;
        private set
        {
            if (SetField(ref _isTopicBusy, value)) RaiseCommandStates();
        }
    }
    public bool IsExpert => IsRole("expert");
    public bool IsAdmin => IsRole("admin");
    public bool IsAssessmentUser => IsStudent || IsExpert;
    public ApiAssignment? SelectedAssignment
    {
        get => _selectedAssignment;
        set
        {
            if (SetField(ref _selectedAssignment, value))
            {
                RaiseCommandStates();
                OnPropertyChanged(nameof(HasSelectedAssignment));
                OnPropertyChanged(nameof(IsReadingSkillSelected));
                OnPropertyChanged(nameof(IsSpeakingSkillSelected));
                OnPropertyChanged(nameof(IsWritingSkillSelected));
                OnPropertyChanged(nameof(IsListeningSkillSelected));
                OnPropertyChanged(nameof(IsExamSkillSelected));
                OnPropertyChanged(nameof(IsNotExamSkillSelected));
            }
        }
    }
    public bool HasSelectedAssignment => SelectedAssignment is not null;
    public bool IsReadingSkillSelected => SelectedAssignment?.Skill.Equals("reading", StringComparison.OrdinalIgnoreCase) == true;
    public bool IsSpeakingSkillSelected => SelectedAssignment?.Skill.Equals("speaking", StringComparison.OrdinalIgnoreCase) == true;
    public bool IsWritingSkillSelected => SelectedAssignment?.Skill.Equals("writing", StringComparison.OrdinalIgnoreCase) == true;
    public bool IsListeningSkillSelected => SelectedAssignment?.Skill.Equals("listening", StringComparison.OrdinalIgnoreCase) == true;
    public bool IsExamSkillSelected => SelectedAssignment?.Skill.Equals("exam", StringComparison.OrdinalIgnoreCase) == true || SelectedAssignment?.Exam_Structure != null;
    public bool IsNotExamSkillSelected => !IsExamSkillSelected;

    public ApiSubmission? SelectedSubmission
    {
        get => _selectedSubmission;
        set
        {
            if (SetField(ref _selectedSubmission, value))
            {
                RaiseCommandStates();
                if (value is not null)
                {
                    GradeScore = value.Score?.ToString("F1") ?? "";
                    GradeFeedback = value.Feedback ?? "";
                    GradeLevel = !string.IsNullOrWhiteSpace(value.Assessed_Level) ? value.Assessed_Level : "B2";
                }
            }
        }
    }
    public AuthUser? SelectedUser { get => _selectedUser; set { if (SetField(ref _selectedUser, value)) RaiseCommandStates(); } }
    public ApiTopic? SelectedCurriculum { get => _selectedCurriculum; set { if (SetField(ref _selectedCurriculum, value) && DeleteCurriculumCommand is RelayCommand cmd) cmd.RaiseCanExecuteChanged(); } }
    public string AdminSummary { get => _adminSummary; set => SetField(ref _adminSummary, value); }
    public string AssignmentTitle { get => _assignmentTitle; set { if (SetField(ref _assignmentTitle, value)) RaiseCommandStates(); } }
    public string AssignmentInstructions { get => _assignmentInstructions; set { if (SetField(ref _assignmentInstructions, value)) RaiseCommandStates(); } }
    public string AssignmentContent { get => _assignmentContent; set => SetField(ref _assignmentContent, value); }
    public int AssignmentDuration { get => _assignmentDuration; set => SetField(ref _assignmentDuration, value); }
    public string AssignmentReadingQuiz { get => _assignmentReadingQuiz; set => SetField(ref _assignmentReadingQuiz, value); }
    public bool IsEditingAssignment { get => _isEditingAssignment; set => SetField(ref _isEditingAssignment, value); }
    public string AssignmentSkill { get => _assignmentSkill; set => SetField(ref _assignmentSkill, value); }
    public string AssignmentDueDate { get => _assignmentDueDate; set => SetField(ref _assignmentDueDate, value); }
    public string SubmissionContent
    {
        get => _submissionContent;
        set
        {
            if (SetField(ref _submissionContent, value))
            {
                RaiseCommandStates();
                var words = string.IsNullOrWhiteSpace(value) ? 0 : value.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
                WordCountText = $"Số từ: {words} từ";
            }
        }
    }
    public string WordCountText { get => _wordCountText; set => SetField(ref _wordCountText, value); }
    public string StudentAudioUrl { get => _studentAudioUrl; set => SetField(ref _studentAudioUrl, value); }
    public string StudentSpeakingNotes { get => _studentSpeakingNotes; set => SetField(ref _studentSpeakingNotes, value); }
    public string StudentTopicSentence { get => _studentTopicSentence; set => SetField(ref _studentTopicSentence, value); }
    public string StudentReadingAns1 { get => _studentReadingAns1; set => SetField(ref _studentReadingAns1, value); }
    public string StudentReadingAns2 { get => _studentReadingAns2; set => SetField(ref _studentReadingAns2, value); }
    public string StudentReadingAns3 { get => _studentReadingAns3; set => SetField(ref _studentReadingAns3, value); }
    public string GradeScore { get => _gradeScore; set => SetField(ref _gradeScore, value); }
    public string GradeFeedback { get => _gradeFeedback; set => SetField(ref _gradeFeedback, value); }
    public string GradeLevel { get => _gradeLevel; set => SetField(ref _gradeLevel, value); }
    public string CurriculumName { get => _curriculumName; set { if (SetField(ref _curriculumName, value)) RaiseCommandStates(); } }
    public string CurriculumLevel { get => _curriculumLevel; set => SetField(ref _curriculumLevel, value); }

    public ObservableCollection<string> CefrLevels { get; }

    public string Email
    {
        get => _email;
        set => SetField(ref _email, value);
    }

    public string Password
    {
        get => _password;
        set => SetField(ref _password, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public bool IsLoggedIn
    {
        get => _isLoggedIn;
        set
        {
            if (!SetField(ref _isLoggedIn, value)) return;
            OnPropertyChanged(nameof(IsLoggedOut));
            OnPropertyChanged(nameof(IsStudent)); OnPropertyChanged(nameof(IsExpert)); OnPropertyChanged(nameof(IsAdmin)); OnPropertyChanged(nameof(IsAssessmentUser));
            RaiseCommandStates();
        }
    }

    public TopicDeck? SelectedTopic
    {
        get => _selectedTopic;
        set
        {
            if (!SetField(ref _selectedTopic, value)) return;
            RaiseCommandStates();
        }
    }

    public string NewTopicName
    {
        get => _newTopicName;
        set
        {
            if (!SetField(ref _newTopicName, value)) return;
            RaiseCommandStates();
        }
    }

    public string SelectedCefr
    {
        get => _selectedCefr;
        set => SetField(ref _selectedCefr, value);
    }

    public ICommand LoginCommand { get; }
    public ICommand LogoutCommand { get; }
    public ICommand ToggleRegisterCommand { get; }
    public ICommand LoadTopicsCommand { get; }
    public ICommand GenerateTopicCommand { get; }
    public ICommand OpenTopicCommand { get; }
    public ICommand GenerateBatchCommand { get; }
    public ICommand DeleteTopicCommand { get; }
    public ICommand RefreshF4Command { get; }
    public ICommand CreateAssignmentCommand { get; }
    public ICommand UpdateAssignmentCommand { get; }
    public ICommand DeleteAssignmentCommand { get; }
    public ICommand EditAssignmentCommand { get; }
    public ICommand ClearAssignmentFormCommand { get; }
    public ICommand SubmitWorkCommand { get; }
    public ICommand GradeWorkCommand { get; }
    public ICommand RefreshAdminCommand { get; }
    public ICommand SyncMysqlCommand { get; }
    public ICommand SaveUserCommand { get; }
    public ICommand SaveAiConfigCommand { get; }
    public ICommand AddCurriculumCommand { get; }
    public ICommand DeleteCurriculumCommand { get; }

    // ==========================================
    // PROPERTIES DÀNH CHO KHẢO THÍ F4
    // ==========================================
    public AuthUser? SelectedExamStudent
    {
        get => _selectedExamStudent;
        set
        {
            if (SetField(ref _selectedExamStudent, value))
            {
                RaiseCommandStates();
                _ = LoadStudentTopicsAsync();
            }
        }
    }
    public ApiTopic? SelectedExamTopic { get => _selectedExamTopic; set => SetField(ref _selectedExamTopic, value); }
    public string ExamCefrLevel { get => _examCefrLevel; set => SetField(ref _examCefrLevel, value); }
    public bool IncludeSigns { get => _includeSigns; set => SetField(ref _includeSigns, value); }
    public int SignsCount { get => _signsCount; set => SetField(ref _signsCount, value); }
    public double SignsPoints { get => _signsPoints; set => SetField(ref _signsPoints, value); }
    public bool IncludeCloze { get => _includeCloze; set => SetField(ref _includeCloze, value); }
    public int ClozeCount { get => _clozeCount; set => SetField(ref _clozeCount, value); }
    public double ClozePoints { get => _clozePoints; set => SetField(ref _clozePoints, value); }
    public bool IncludeWordOrdering { get => _includeWordOrdering; set => SetField(ref _includeWordOrdering, value); }
    public int WordOrderingCount { get => _wordOrderingCount; set => SetField(ref _wordOrderingCount, value); }
    public double WordOrderingPoints { get => _wordOrderingPoints; set => SetField(ref _wordOrderingPoints, value); }
    public bool IncludeVocabulary { get => _includeVocabulary; set => SetField(ref _includeVocabulary, value); }
    public int VocabularyCount { get => _vocabularyCount; set => SetField(ref _vocabularyCount, value); }
    public double VocabularyPoints { get => _vocabularyPoints; set => SetField(ref _vocabularyPoints, value); }
    public int ExamDuration { get => _examDuration; set => SetField(ref _examDuration, value); }
    public string ExamDueDate { get => _examDueDate; set => SetField(ref _examDueDate, value); }
    public bool ExamShowAnswers { get => _examShowAnswers; set => SetField(ref _examShowAnswers, value); }
    public ApiExamStructure? GeneratedExam
    {
        get => _generatedExam;
        set
        {
            if (SetField(ref _generatedExam, value))
            {
                RaiseCommandStates();
                OnPropertyChanged(nameof(HasGeneratedExam));
            }
        }
    }
    public bool HasGeneratedExam => GeneratedExam is not null;
    public bool IsGeneratingExam
    {
        get => _isGeneratingExam;
        set
        {
            if (SetField(ref _isGeneratingExam, value)) RaiseCommandStates();
        }
    }

    // Exam taking (Student)
    public bool IsTakingExam
    {
        get => _isTakingExam;
        set
        {
            if (SetField(ref _isTakingExam, value))
            {
                RaiseCommandStates();
                OnPropertyChanged(nameof(IsNotTakingExam));
            }
        }
    }
    public bool IsNotTakingExam => !IsTakingExam;
    public ApiAssignment? ActiveTakingAssignment { get => _activeTakingAssignment; set => SetField(ref _activeTakingAssignment, value); }
    public string ExamRemainingTimeText { get => _examRemainingTimeText; set => SetField(ref _examRemainingTimeText, value); }

    // Commands for Exam
    public ICommand GenerateExamCommand { get; }
    public ICommand SaveExamDraftCommand { get; }
    public ICommand PublishExamCommand { get; }
    public ICommand PublishSelectedAssignmentCommand { get; }
    public ICommand ToggleShowAnswersCommand { get; }
    public ICommand StartExamCommand { get; }
    public ICommand SubmitExamCommand { get; }
    public ICommand CancelExamCommand { get; }

    private async Task LoginAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
            {
                StatusMessage = "Vui lòng nhập email và mật khẩu.";
                return;
            }
            if (IsRegistering && string.IsNullOrWhiteSpace(RegisterName))
            {
                StatusMessage = "Vui lòng nhập họ tên để đăng ký.";
                return;
            }

            StatusMessage = IsRegistering ? "Đang tạo tài khoản..." : "Đang đăng nhập...";
            var result = IsRegistering
                ? await _apiClient.RegisterAsync(RegisterName.Trim(), Email.Trim(), Password)
                : await _apiClient.LoginAsync(Email.Trim(), Password);
            if (result?.Success != true || result.User is null)
            {
                IsLoggedIn = false;
                StatusMessage = IsRegistering ? "Đăng ký thất bại. Email có thể đã được sử dụng." : "Email hoặc mật khẩu không đúng, hoặc tài khoản đã bị khóa.";
                return;
            }

            CurrentUser = result.User;
            IsLoggedIn = true;
            ActiveTabIndex = IsAdmin ? 2 : IsExpert ? 1 : 0;
            IsRegistering = false;
            RegisterName = string.Empty;
            StatusMessage = $"Xin chào {result.User.Name}";
            if (IsStudent) await LoadTopicsAsync();
            if (IsAssessmentUser) await LoadF4Async();
            if (IsAdmin) await LoadAdminAsync();
        }
        catch
        {
            IsLoggedIn = false;
            StatusMessage = "Không kết nối được API. Hãy chạy BilingualVocabApi trước.";
        }
    }
    private async Task LoadTopicsAsync()
    {
        if (!IsStudent || CurrentUser is null || IsTopicBusy) return;
        IsTopicBusy = true;
        try
        {
            var selectedId = SelectedTopic?.Id;
            var items = await _apiClient.GetTopicsAsync(CurrentUser.Id);
            Topics.Clear();
            foreach (var item in items) Topics.Add(item);

            SelectedTopic = Topics.FirstOrDefault(topic => topic.Id == selectedId);
            if (SelectedTopic is null) FlashcardSession.SetCards(new List<EnglishFlashcard>());
            StatusMessage = $"Đã tải {Topics.Count} chủ đề";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Tải danh sách chủ đề thất bại: {ex.Message}";
        }
        finally
        {
            IsTopicBusy = false;
        }
    }

    private async Task GenerateTopicAsync()
    {
        if (!IsStudent || CurrentUser is null || string.IsNullOrWhiteSpace(NewTopicName) || IsTopicBusy) return;
        IsTopicBusy = true;
        try
        {
            StatusMessage = $"Đang tạo bộ từ vựng cho «{NewTopicName.Trim()}»...";
            var topic = await _apiClient.GenerateTopicAsync(NewTopicName.Trim(), SelectedCefr, CurrentUser.Id);
            if (topic is null)
            {
                throw new InvalidOperationException("API không trả về chủ đề.");
            }

            Topics.Insert(0, topic);
            SelectedTopic = topic;
            NewTopicName = string.Empty;
            StatusMessage = "Đã tạo bộ từ vựng bằng Gemini.";
            await OpenSelectedTopicAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Không thể tạo từ vựng: {ex.Message}";
        }
        finally
        {
            IsTopicBusy = false;
        }
    }
    private async Task OpenSelectedTopicAsync()
    {
        if (!IsStudent || SelectedTopic is null || CurrentUser is null) return;

        try
        {
            var cards = await _apiClient.GetTopicCardsAsync(SelectedTopic.Id, CurrentUser.Id);
            FlashcardSession.SetCards(cards, SelectedTopic.Name, SelectedTopic.Level);
            StatusMessage = $"Đã mở chủ đề: {SelectedTopic.Name} ({cards.Count} thẻ)";
        }
        catch (Exception ex)
        {
            FlashcardSession.SetCards(new List<EnglishFlashcard>());
            StatusMessage = $"Không tải được thẻ của chủ đề: {ex.Message}";
        }
    }

    private async Task GenerateBatchAsync()
    {
        if (!IsStudent || SelectedTopic is null || CurrentUser is null || IsTopicBusy) return;
        IsTopicBusy = true;
        var topic = SelectedTopic;
        try
        {
            StatusMessage = $"Đang sinh thêm từ mới cho «{topic.Name}»...";
            var result = await _apiClient.GenerateBatchAsync(topic.Id, CurrentUser.Id);
            var updatedTopic = new TopicDeck
            {
                Id = topic.Id,
                Name = topic.Name,
                Level = topic.Level,
                CardCount = result.TotalCards
            };
            var topicIndex = Topics.IndexOf(topic);
            if (topicIndex < 0)
            {
                IsTopicBusy = false;
                await LoadTopicsAsync();
                return;
            }
            Topics[topicIndex] = updatedTopic;
            SelectedTopic = updatedTopic;
            await OpenSelectedTopicAsync();
            StatusMessage = $"Đã thêm {result.NewCards.Count} từ mới vào «{topic.Name}». Tổng cộng {result.TotalCards} thẻ.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Không thể sinh thêm từ: {ex.Message}";
        }
        finally
        {
            IsTopicBusy = false;
        }
    }

    private async Task DeleteTopicAsync()
    {
        if (!IsStudent || SelectedTopic is null || CurrentUser is null || IsTopicBusy) return;
        var topic = SelectedTopic;
        var confirm = MessageBox.Show(
            $"Xóa chủ đề «{topic.Name}» và toàn bộ {topic.CardCount} thẻ từ vựng liên quan?",
            "Xác nhận xóa chủ đề",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        IsTopicBusy = true;
        try
        {
            StatusMessage = $"Đang xóa «{topic.Name}»...";
            if (!await _apiClient.DeleteTopicAsync(topic.Id, CurrentUser.Id))
            {
                StatusMessage = "Xóa chủ đề thất bại. Hãy tải lại danh sách và thử lại.";
                return;
            }

            Topics.Remove(topic);
            SelectedTopic = Topics.FirstOrDefault();
            if (SelectedTopic is null)
                FlashcardSession.SetCards(new List<EnglishFlashcard>());
            else
                await OpenSelectedTopicAsync();
            StatusMessage = $"Đã xóa chủ đề «{topic.Name}» và các thẻ liên quan.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Không thể xóa chủ đề: {ex.Message}";
        }
        finally
        {
            IsTopicBusy = false;
        }
    }

    private async void OnCardMarkedMastered(EnglishFlashcard card)
    {
        if (string.IsNullOrWhiteSpace(card.Id) || !IsLoggedIn) return;
        await _apiClient.MarkCardLearnedAsync(card.Id, CurrentUser!.Id);
    }

    private void RaiseCommandStates()
    {
        if (LoadTopicsCommand is RelayCommand load) load.RaiseCanExecuteChanged();
        if (GenerateTopicCommand is RelayCommand gen) gen.RaiseCanExecuteChanged();
        if (OpenTopicCommand is RelayCommand open) open.RaiseCanExecuteChanged();
        if (GenerateBatchCommand is RelayCommand batch) batch.RaiseCanExecuteChanged();
        if (DeleteTopicCommand is RelayCommand deleteTopic) deleteTopic.RaiseCanExecuteChanged();
        foreach (var command in new[]
        {
            RefreshF4Command, CreateAssignmentCommand, UpdateAssignmentCommand, DeleteAssignmentCommand,
            EditAssignmentCommand, ClearAssignmentFormCommand, SubmitWorkCommand, GradeWorkCommand,
            RefreshAdminCommand, SyncMysqlCommand, SaveUserCommand, SaveAiConfigCommand, AddCurriculumCommand, DeleteCurriculumCommand,
            GenerateExamCommand, SaveExamDraftCommand, PublishExamCommand, PublishSelectedAssignmentCommand,
            ToggleShowAnswersCommand, StartExamCommand, SubmitExamCommand, CancelExamCommand
        })
        {
            if (command is RelayCommand relay) relay.RaiseCanExecuteChanged();
        }
    }

    private bool IsRole(string role) => IsLoggedIn && CurrentUser?.Role.Equals(role, StringComparison.OrdinalIgnoreCase) == true;

    private async Task LoadF4Async()
    {
        try
        {
            Assignments.Clear();
            Submissions.Clear();
            StudentSubmissions.Clear();

            if (IsRole("expert"))
            {
                var assignments = await _apiClient.GetExpertAssignmentsAsync();
                foreach (var item in assignments) Assignments.Add(item);

                var submissions = await _apiClient.GetExpertSubmissionsAsync();
                foreach (var item in submissions) Submissions.Add(item);

                StudentsList.Clear();
                var allUsers = await _apiClient.GetUsersAsync();
                foreach (var u in allUsers.Where(u => u.Role.Equals("student", StringComparison.OrdinalIgnoreCase)))
                {
                    StudentsList.Add(u);
                }
            }
            else if (IsRole("student"))
            {
                var assignments = await _apiClient.GetStudentAssignmentsAsync();
                foreach (var item in assignments) Assignments.Add(item);

                var submissions = await _apiClient.GetStudentSubmissionsAsync();
                foreach (var item in submissions)
                {
                    Submissions.Add(item);
                    StudentSubmissions.Add(item);
                }
            }
        }
        catch { StatusMessage = "Không tải được dữ liệu khảo thí."; }
    }

    private async Task CreateAssignmentAsync()
    {
        if (CurrentUser is null) return;
        if (string.IsNullOrWhiteSpace(AssignmentTitle))
        {
            StatusMessage = "Vui lòng nhập tiêu đề bài thi.";
            MessageBox.Show("Vui lòng nhập tiêu đề bài thi trước khi bấm Tạo bài.", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            var ok = await _apiClient.CreateAssignmentAsync(
                AssignmentTitle.Trim(),
                AssignmentSkill,
                SelectedCefr,
                AssignmentDuration > 0 ? AssignmentDuration : 45,
                AssignmentContent.Trim(),
                AssignmentInstructions.Trim(),
                AssignmentDueDate,
                10.0,
                !string.IsNullOrWhiteSpace(AssignmentReadingQuiz) ? AssignmentReadingQuiz.Trim() : null);

            if (!ok) { StatusMessage = "Tạo bài tập thất bại."; return; }
            ClearAssignmentForm();
            await LoadF4Async();
            StatusMessage = "Đã tạo bài tập mới thành công.";
        }
        catch { StatusMessage = "Không kết nối được API để tạo bài."; }
    }

    private async Task UpdateAssignmentAsync()
    {
        if (CurrentUser is null || SelectedAssignment is null) return;
        if (string.IsNullOrWhiteSpace(AssignmentTitle))
        {
            StatusMessage = "Vui lòng nhập tiêu đề bài thi.";
            MessageBox.Show("Vui lòng nhập tiêu đề bài thi trước khi lưu cập nhật.", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            var ok = await _apiClient.UpdateAssignmentAsync(
                SelectedAssignment.Id,
                AssignmentTitle.Trim(),
                AssignmentSkill,
                SelectedCefr,
                AssignmentDuration > 0 ? AssignmentDuration : 45,
                AssignmentContent.Trim(),
                AssignmentInstructions.Trim(),
                AssignmentDueDate,
                10.0,
                !string.IsNullOrWhiteSpace(AssignmentReadingQuiz) ? AssignmentReadingQuiz.Trim() : null);

            if (!ok) { StatusMessage = "Cập nhật bài tập thất bại."; return; }
            ClearAssignmentForm();
            await LoadF4Async();
            StatusMessage = "Đã cập nhật bài tập thành công.";
        }
        catch { StatusMessage = "Không kết nối được API để cập nhật bài."; }
    }

    private void PrepareEditAssignment()
    {
        if (SelectedAssignment is null) return;
        AssignmentTitle = SelectedAssignment.Title;
        AssignmentSkill = SelectedAssignment.Skill;
        SelectedCefr = SelectedAssignment.Target_Level;
        AssignmentDuration = SelectedAssignment.Duration_Minutes > 0 ? SelectedAssignment.Duration_Minutes : 45;
        AssignmentDueDate = SelectedAssignment.Due_Date ?? DateTime.Today.AddDays(7).ToString("yyyy-MM-dd");
        AssignmentContent = SelectedAssignment.Content;
        AssignmentInstructions = SelectedAssignment.Instructions;
        AssignmentReadingQuiz = SelectedAssignment.Reading_Quiz ?? "";
        IsEditingAssignment = true;
        StatusMessage = $"Đang chỉnh sửa bài tập: {SelectedAssignment.Title}";
    }

    private void ClearAssignmentForm()
    {
        AssignmentTitle = "";
        AssignmentContent = "";
        AssignmentInstructions = "";
        AssignmentReadingQuiz = "";
        AssignmentDuration = 45;
        AssignmentDueDate = DateTime.Today.AddDays(7).ToString("yyyy-MM-dd");
        IsEditingAssignment = false;
        StatusMessage = "Đã làm mới form bài tập.";
    }

    private async Task DeleteAssignmentAsync()
    {
        if (SelectedAssignment is null) return;
        try
        {
            if (await _apiClient.DeleteAssignmentAsync(SelectedAssignment.Id))
            {
                SelectedAssignment = null;
                ClearAssignmentForm();
                await LoadF4Async();
                StatusMessage = "Đã xóa bài tập.";
            }
            else
            {
                StatusMessage = "Xóa bài tập thất bại.";
            }
        }
        catch { StatusMessage = "Lỗi khi gọi API xóa bài tập."; }
    }

    private async Task SubmitWorkAsync()
    {
        if (CurrentUser is null || SelectedAssignment is null) return;
        try
        {
            string finalContent = SubmissionContent.Trim();
            string? audioUrl = null;
            string? readingAnswers = null;
            string? topicSentence = null;

            if (SelectedAssignment.Skill.Equals("speaking", StringComparison.OrdinalIgnoreCase))
            {
                audioUrl = !string.IsNullOrWhiteSpace(StudentAudioUrl) ? StudentAudioUrl.Trim() : null;
                if (!string.IsNullOrWhiteSpace(StudentSpeakingNotes))
                {
                    finalContent = $"[Ghi chú vấn đáp 1-1]: {StudentSpeakingNotes.Trim()}\n{finalContent}";
                }
            }
            else if (SelectedAssignment.Skill.Equals("reading", StringComparison.OrdinalIgnoreCase))
            {
                var answersDict = new Dictionary<string, string>
                {
                    ["1"] = StudentReadingAns1,
                    ["2"] = StudentReadingAns2,
                    ["3"] = StudentReadingAns3
                };
                readingAnswers = System.Text.Json.JsonSerializer.Serialize(answersDict);
                topicSentence = !string.IsNullOrWhiteSpace(StudentTopicSentence) ? StudentTopicSentence.Trim() : null;
            }

            var ok = await _apiClient.SubmitAssignmentAsync(
                SelectedAssignment.Id,
                finalContent,
                audioUrl,
                readingAnswers,
                topicSentence,
                durationSpent: 30);

            if (ok)
            {
                SubmissionContent = "";
                StudentAudioUrl = "";
                StudentSpeakingNotes = "";
                StudentTopicSentence = "";
                await LoadF4Async();
                StatusMessage = "Nộp bài thành công! Bài làm đang ở trạng thái Chờ chấm.";
            }
            else
            {
                StatusMessage = "Nộp bài thất bại. Vui lòng kiểm tra lại nội dung.";
            }
        }
        catch { StatusMessage = "Không kết nối được API để nộp bài."; }
    }

    private async Task GradeWorkAsync()
    {
        if (SelectedSubmission is null) return;
        if (!double.TryParse(GradeScore, out var score) || score < 0.0 || score > 10.0)
        {
            StatusMessage = "Điểm số phải thuộc thang điểm 0.0 đến 10.0 theo quy định.";
            return;
        }
        if (string.IsNullOrWhiteSpace(GradeFeedback))
        {
            StatusMessage = "Vui lòng nhập nhận xét (feedback) trước khi lưu điểm.";
            return;
        }

        try
        {
            if (await _apiClient.GradeSubmissionAsync(SelectedSubmission.Id, score, GradeFeedback.Trim(), GradeLevel))
            {
                GradeScore = GradeFeedback = "";
                await LoadF4Async();
                StatusMessage = "Đã lưu kết quả chấm điểm và gán CEFR thành công.";
            }
            else
            {
                StatusMessage = "Chấm bài thất bại.";
            }
        }
        catch { StatusMessage = "Không kết nối được API để chấm bài."; }
    }

    private async Task LoadAdminAsync()
    {
        try
        {
            AdminStats = await _apiClient.GetAdminStatsAsync(); OnPropertyChanged(nameof(AdminStats));
            AdminSummary = $"Sinh viên: {AdminStats.Total_Students} | Chuyên gia: {AdminStats.Total_Experts} | Chủ đề: {AdminStats.Total_Topics} | Thẻ: {AdminStats.Total_Cards} | Bài tập: {AdminStats.Total_Assignments} | Bài nộp: {AdminStats.Total_Submissions} | API calls: {AdminStats.Total_Api_Calls}";
            Users.Clear(); foreach (var u in await _apiClient.GetUsersAsync()) Users.Add(u);
            ApiLogs.Clear(); foreach (var log in await _apiClient.GetApiLogsAsync()) ApiLogs.Add(log);
            Curriculum.Clear(); foreach (var topic in await _apiClient.GetCurriculumAsync()) Curriculum.Add(topic);
            AiConfig = await _apiClient.GetAiConfigAsync();
        }
        catch { StatusMessage = "Không tải được dữ liệu quản trị."; }
    }

    private async Task SaveUserAsync()
    {
        if (SelectedUser is null) return;
        try { if (await _apiClient.UpdateUserAsync(SelectedUser.Id, SelectedUser.Role, SelectedUser.Status)) { await LoadAdminAsync(); StatusMessage = "Đã cập nhật người dùng."; } }
        catch { StatusMessage = "Cập nhật người dùng thất bại."; }
    }

    private async Task SaveAiConfigAsync()
    {
        try { StatusMessage = await _apiClient.UpdateAiConfigAsync(AiConfig) ? "Đã lưu cấu hình AI." : "Lưu cấu hình AI thất bại."; }
        catch { StatusMessage = "Không kết nối được API để lưu cấu hình."; }
    }

    private async Task SyncMysqlAsync()
    {
        try { await _apiClient.SyncMysqlAsync(); StatusMessage = "Backend C# hiện lưu JSON cục bộ; thao tác sync MySQL chưa được cấu hình."; }
        catch { StatusMessage = "Không thể gọi chức năng đồng bộ."; }
    }

    private async Task AddCurriculumAsync()
    {
        Curriculum.Add(new ApiTopic { Id = $"topic-dau-{Guid.NewGuid():N}", Name = CurriculumName.Trim(), Level = CurriculumLevel });
        await SaveCurriculumAsync(); CurriculumName = "";
    }
    private async Task DeleteCurriculumAsync()
    {
        if (SelectedCurriculum is not null) Curriculum.Remove(SelectedCurriculum);
        await SaveCurriculumAsync();
    }
    private async Task SaveCurriculumAsync()
    {
        try { StatusMessage = await _apiClient.UpdateCurriculumAsync(Curriculum.ToList()) ? "Đã lưu chương trình DAU." : "Lưu chương trình thất bại."; }
        catch { StatusMessage = "Không thể cập nhật chương trình DAU."; }
    }

    // ==========================================
    // CÁC HÀM XỬ LÝ KHẢO THÍ F4
    // ==========================================
    private async Task LoadStudentTopicsAsync()
    {
        StudentTopics.Clear();
        if (SelectedExamStudent is null) return;
        try
        {
            var topics = await _apiClient.GetStudentTopicsForExpertAsync(SelectedExamStudent.Id);
            foreach (var t in topics) StudentTopics.Add(t);
            SelectedExamTopic = StudentTopics.FirstOrDefault();
        }
        catch { StatusMessage = "Không tải được chủ đề của sinh viên."; }
    }

    private async Task GenerateExamAsync()
    {
        if (SelectedExamStudent is null) return;
        IsGeneratingExam = true;
        StatusMessage = "Đang sinh đề thi bằng AI...";
        try
        {
            var topicIds = SelectedExamTopic is not null ? new List<string> { SelectedExamTopic.Id } : StudentTopics.Select(t => t.Id).ToList();
            var req = new ExamGenerationRequestDto
            {
                Student_Id = SelectedExamStudent.Id,
                Topic_Ids = topicIds,
                Cefr_Level = ExamCefrLevel,
                Include_Signs = IncludeSigns,
                Signs_Count = SignsCount,
                Signs_Points = SignsPoints,
                Include_Cloze = IncludeCloze,
                Cloze_Count = ClozeCount,
                Cloze_Points = ClozePoints,
                Include_Word_Ordering = IncludeWordOrdering,
                Word_Ordering_Count = WordOrderingCount,
                Word_Ordering_Points = WordOrderingPoints,
                Include_Vocabulary = IncludeVocabulary,
                Vocabulary_Count = VocabularyCount,
                Vocabulary_Points = VocabularyPoints,
                Duration_Minutes = ExamDuration,
                Due_Date = ExamDueDate,
                Show_Answers = ExamShowAnswers
            };
            GeneratedExam = await _apiClient.GenerateExamAsync(req);
            StatusMessage = GeneratedExam is not null ? "Đã sinh đề thi thành công. Xem trước và lưu hoặc giao ngay." : "Không sinh được đề thi.";
        }
        catch (Exception ex) { StatusMessage = $"Lỗi sinh đề: {ex.Message}"; }
        finally { IsGeneratingExam = false; }
    }

    private async Task SaveExamAsync(string status)
    {
        if (GeneratedExam is null) return;
        var title = GeneratedExam.Exam_Title;
        var topicIds = SelectedExamTopic is not null ? new List<string> { SelectedExamTopic.Id } : StudentTopics.Select(t => t.Id).ToList();
        var topicNames = SelectedExamTopic is not null ? new List<string> { SelectedExamTopic.Name } : StudentTopics.Select(t => t.Name).ToList();
        try
        {
            var saved = await _apiClient.SaveExamAssignmentAsync(
                title, ExamCefrLevel, ExamDuration, ExamDueDate,
                SelectedExamStudent?.Id, SelectedExamStudent?.Name,
                topicIds, topicNames, status, ExamShowAnswers, GeneratedExam);
            if (saved is not null)
            {
                await LoadF4Async();
                GeneratedExam = null;
                StatusMessage = status == "published" ? "Đã giao đề thi cho sinh viên." : "Đã lưu bản nháp đề thi.";
            }
            else StatusMessage = "Lưu đề thi thất bại.";
        }
        catch { StatusMessage = "Không kết nối được API để lưu đề thi."; }
    }

    private async Task PublishSelectedAssignmentAsync()
    {
        if (SelectedAssignment is null) return;
        try
        {
            if (await _apiClient.PublishAssignmentAsync(SelectedAssignment.Id))
            {
                await LoadF4Async();
                StatusMessage = "Đã giao đề thi cho sinh viên thành công.";
            }
            else StatusMessage = "Giao đề thất bại.";
        }
        catch { StatusMessage = "Không kết nối được API."; }
    }

    private async Task ToggleShowAnswersAsync()
    {
        if (SelectedAssignment is null) return;
        bool newValue = !SelectedAssignment.Show_Answers;
        try
        {
            if (await _apiClient.ToggleShowAnswersAsync(SelectedAssignment.Id, newValue))
            {
                await LoadF4Async();
                StatusMessage = newValue ? "Sinh viên sẽ thấy đáp án đúng khi xem kết quả." : "Đã ẩn đáp án đúng khỏi sinh viên.";
            }
        }
        catch { StatusMessage = "Không cập nhật được cài đặt xem đáp án."; }
    }

    private async Task StartExamAsync()
    {
        if (SelectedAssignment is null) return;
        var fullAssignment = await _apiClient.GetStudentAssignmentDetailAsync(SelectedAssignment.Id);
        if (fullAssignment?.Exam_Structure is null) { StatusMessage = "Đề thi này chưa có cấu trúc câu hỏi."; return; }

        ActiveTakingAssignment = fullAssignment;
        ExamTakingSections.Clear();
        ExamAnswerGrid.Clear();

        foreach (var sec in fullAssignment.Exam_Structure.Sections)
        {
            var secItem = new ExamTakingSectionItem
            {
                SectionId = sec.Section_Id,
                SectionNumber = sec.Section_Number,
                SectionType = sec.Section_Type,
                Title = sec.Title,
                Instructions = sec.Instructions,
                Points = sec.Points,
                Passage = sec.Passage
            };
            foreach (var q in sec.Questions)
            {
                var qItem = new ExamTakingQuestionItem(q, sec.Section_Type, sec.Section_Id);
                qItem.AnswerChanged += (qi, ans) =>
                {
                    var gridItem = ExamAnswerGrid.FirstOrDefault(g => g.QuestionNumber == qi.QuestionNumber);
                    if (gridItem is not null) gridItem.Answer = string.IsNullOrWhiteSpace(ans) ? "..." : ans;
                };
                secItem.Questions.Add(qItem);
                ExamAnswerGrid.Add(new ExamAnswerGridItem { QuestionNumber = q.Question_Number, Answer = "..." });
            }
            ExamTakingSections.Add(secItem);
        }

        _examRemainingSeconds = fullAssignment.Duration_Minutes * 60;
        ExamRemainingTimeText = FormatTime(_examRemainingSeconds);
        _examTimer?.Stop();
        _examTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _examTimer.Tick += (_, _) =>
        {
            _examRemainingSeconds--;
            ExamRemainingTimeText = FormatTime(_examRemainingSeconds);
            if (_examRemainingSeconds <= 0)
            {
                _examTimer.Stop();
                _ = SubmitExamAsync(true);
            }
        };
        _examTimer.Start();
        IsTakingExam = true;
        StatusMessage = $"Bắt đầu làm bài: {fullAssignment.Title} ({fullAssignment.Duration_Minutes} phút)";
    }

    private static string FormatTime(int totalSeconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, totalSeconds));
        return $"{(int)t.TotalMinutes:D2}:{t.Seconds:D2}";
    }

    private async Task SubmitExamAsync(bool forceSubmit)
    {
        if (ActiveTakingAssignment is null) return;
        var answers = new Dictionary<int, string>();
        foreach (var sec in ExamTakingSections)
            foreach (var q in sec.Questions)
                answers[q.QuestionNumber] = q.SelectedAnswer;

        int unanswered = answers.Values.Count(v => string.IsNullOrWhiteSpace(v) || v == "...");
        if (!forceSubmit && unanswered > 0)
        {
            var msg = MessageBox.Show(
                $"Còn {unanswered} câu chưa trả lời. Bạn có muốn nộp bài không?",
                "Xác nhận nộp bài", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (msg != MessageBoxResult.Yes) return;
        }

        _examTimer?.Stop();
        int durationSpent = (ActiveTakingAssignment.Duration_Minutes * 60) - _examRemainingSeconds;

        try
        {
            var result = await _apiClient.SubmitExamAsync(ActiveTakingAssignment.Id, answers, Math.Max(1, durationSpent / 60));
            IsTakingExam = false;
            ExamTakingSections.Clear();
            ExamAnswerGrid.Clear();
            ActiveTakingAssignment = null;
            await LoadF4Async();
            if (result is not null)
                StatusMessage = $"Nộp bài thành công! Điểm tự động: {result.Auto_Score:F2}/{result.Score:F2}";
            else
                StatusMessage = "Nộp bài thành công!";
        }
        catch { StatusMessage = "Lỗi khi nộp bài. Vui lòng thử lại."; }
    }

    private void CancelExam()
    {
        _examTimer?.Stop();
        IsTakingExam = false;
        ExamTakingSections.Clear();
        ExamAnswerGrid.Clear();
        ActiveTakingAssignment = null;
        StatusMessage = "Đã hủy làm bài thi.";
    }
}
