using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using EnglishFlashcard3D.Models;
using EnglishFlashcard3D.Services;

namespace EnglishFlashcard3D.ViewModels;

public class MainViewModel : BaseViewModel
{
    private readonly ApiClientService _apiClient;
    private readonly AudioService _mediaAudioService = new();
    private string _email = string.Empty;
    private string _password = string.Empty;
    private string _registerName = string.Empty;
    private bool _isRegistering;
    private int _activeTabIndex;
    private string _statusMessage = string.Empty;
    private bool _isLoggedIn;
    private bool _isTopicBusy;
    private bool _isBusy;
    private string _busyMessage = string.Empty;
    private TopicDeck? _selectedTopic;
    private string _newTopicName = string.Empty;
    private string _selectedCefr = "B1";
    private AuthUser? _currentUser;
    private ApiAssignment? _selectedAssignment;
    private ApiSubmission? _selectedSubmission;
    private AuthUser? _selectedUser;
    private ApiTopic? _selectedCurriculum;
    private string _assignmentTitle = "";
    private string _assignmentTopic = "";
    private bool _isGeneratingSkillAssignment = false;
    private string _assignmentInstructions = "";
    private string _assignmentContent = "";
    private int _assignmentDuration = 45;
    private string _assignmentReadingQuiz = "";
    private string _assignmentMediaUrl = "";
    private bool _isEditingAssignment = false;
    private string _assignmentSkill = "reading";
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
    private string _examTopicInput = string.Empty;
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

    // Tư vấn & Tiến độ sinh viên (F4)
    private ApiStudentProgressItem? _selectedProgressStudent;
    private string _consultCurrentLevel = "B1";
    private string _consultRecommendedLevel = "B2";
    private string _consultStrengths = "Từ vựng nền tảng ổn định";
    private string _consultWeaknesses = "Cần tăng cường kỹ năng Nghe/Đọc";
    private string _consultAdvice = "";
    private string _consultStudyPlan = "Luyện tập 30 phút mỗi ngày với Flashcard 3D và bài tập Reading/Listening.";

    // Điều hướng Duolingo Shell & Gamification thật
    private string _currentNavPage = "LearningPath";
    private readonly Dictionary<string, int> _masteredByTopic = new();
    private int _totalXp;
    private int _streakDays;
    private int _dailyMasteredCount;
    private string _currentCefrDisplay = "B1";

    public MainViewModel()
    {
        _apiClient = new ApiClientService();

        FlashcardSession = new Flashcard3DViewModel(new List<EnglishFlashcard>());
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
        Skills = new ObservableCollection<string> { "reading", "listening" };
        Roles = new ObservableCollection<string> { "student", "expert" };
        UserStatuses = new ObservableCollection<string> { "active", "suspended" };

        StudentsList = new ObservableCollection<AuthUser>();
        StudentTopics = new ObservableCollection<ApiTopic>();
        ExamTakingSections = new ObservableCollection<ExamTakingSectionItem>();
        ExamAnswerGrid = new ObservableCollection<ExamAnswerGridItem>();
        EditableQuizQuestions = new ObservableCollection<EditableQuizQuestionItem>();
        StudentQuizQuestions = new ObservableCollection<SkillQuizQuestionItem>();
        Consultations = new ObservableCollection<ApiConsultation>();
        StudentProgressList = new ObservableCollection<ApiStudentProgressItem>();

        SendConsultationCommand = new RelayCommand(_ => _ = SendConsultationAsync(), _ => IsRole("expert") && SelectedProgressStudent?.User is not null);
        OpenConsultationForStudentCommand = new RelayCommand(p =>
        {
            if (p is ApiStudentProgressItem item) SelectedProgressStudent = item;
            CurrentNavPage = "ExpertConsult";
        }, _ => IsRole("expert"));

        NavigateCommand = new RelayCommand(p =>
        {
            if (p is string page && !string.IsNullOrWhiteSpace(page))
                CurrentNavPage = page;
        });
        OpenPathTopicCommand = new RelayCommand(p => _ = OpenPathTopicAsync(p as TopicDeck));

        AddQuizQuestionCommand = new RelayCommand(_ => AddQuizQuestion());
        RemoveQuizQuestionCommand = new RelayCommand(q => { if (q is EditableQuizQuestionItem item) RemoveQuizQuestion(item); });
        GenerateSkillAssignmentCommand = new RelayCommand(_ => _ = GenerateSkillAssignmentAsync(), _ => IsRole("expert") && !IsGeneratingSkillAssignment);
        PlayListeningAudioCommand = new RelayCommand(_ => PlayAudioUrl(SelectedAssignment?.Reference_Media_Url));
        PlayExpertMediaCommand = new RelayCommand(_ => PlayAudioUrl(AssignmentMediaUrl));
        StopAudioCommand = new RelayCommand(_ => { _mediaAudioService.StopMedia(); StatusMessage = "Đã dừng phát âm thanh."; });
        SpeakListeningScriptCommand = new RelayCommand(_ => SpeakListeningScript());

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
            StatusMessage = string.Empty;
        });
        ToggleRegisterCommand = new RelayCommand(_ =>
        {
            IsRegistering = !IsRegistering;
            Password = string.Empty;
            RegisterName = string.Empty;
            StatusMessage = string.Empty;
        });
        LoadTopicsCommand = new RelayCommand(_ => _ = LoadTopicsAsync(), _ => IsStudent && !IsTopicBusy);
        GenerateTopicCommand = new RelayCommand(_ => _ = GenerateTopicAsync(), _ => IsStudent && !IsTopicBusy && !string.IsNullOrWhiteSpace(NewTopicName) && NewTopicName.Trim().Length <= 80);
        OpenTopicCommand = new RelayCommand(_ => _ = OpenPathTopicAsync(SelectedTopic), _ => IsStudent && !IsTopicBusy && SelectedTopic is not null);
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
    public ObservableCollection<EditableQuizQuestionItem> EditableQuizQuestions { get; }
    public ObservableCollection<SkillQuizQuestionItem> StudentQuizQuestions { get; }
    public ObservableCollection<ApiConsultation> Consultations { get; }
    public ObservableCollection<ApiStudentProgressItem> StudentProgressList { get; }

    public ApiStudentProgressItem? SelectedProgressStudent
    {
        get => _selectedProgressStudent;
        set
        {
            if (SetField(ref _selectedProgressStudent, value))
            {
                if (value is not null && !string.IsNullOrWhiteSpace(value.Assessed_Cefr))
                    ConsultCurrentLevel = value.Assessed_Cefr;
                RaiseCommandStates();
            }
        }
    }
    public string ConsultCurrentLevel { get => _consultCurrentLevel; set => SetField(ref _consultCurrentLevel, value); }
    public string ConsultRecommendedLevel { get => _consultRecommendedLevel; set => SetField(ref _consultRecommendedLevel, value); }
    public string ConsultStrengths { get => _consultStrengths; set => SetField(ref _consultStrengths, value); }
    public string ConsultWeaknesses { get => _consultWeaknesses; set => SetField(ref _consultWeaknesses, value); }
    public string ConsultAdvice { get => _consultAdvice; set => SetField(ref _consultAdvice, value); }
    public string ConsultStudyPlan { get => _consultStudyPlan; set => SetField(ref _consultStudyPlan, value); }
    public ICommand SendConsultationCommand { get; }
    public ICommand OpenConsultationForStudentCommand { get; }

    public string CurrentNavPage { get => _currentNavPage; set => SetField(ref _currentNavPage, value); }
    public int TotalXp { get => _totalXp; private set => SetField(ref _totalXp, value); }
    public int StreakDays { get => _streakDays; private set => SetField(ref _streakDays, value); }
    public int DailyMasteredCount
    {
        get => _dailyMasteredCount;
        private set
        {
            if (SetField(ref _dailyMasteredCount, value))
            {
                OnPropertyChanged(nameof(DailyGoalPercentage));
                OnPropertyChanged(nameof(DailyGoalText));
            }
        }
    }
    public int DailyGoalTarget => 15;
    public double DailyGoalPercentage => Math.Min(100.0, DailyMasteredCount * 100.0 / DailyGoalTarget);
    public string DailyGoalText => $"{DailyMasteredCount} / {DailyGoalTarget} từ";
    public string CurrentCefrDisplay { get => _currentCefrDisplay; private set => SetField(ref _currentCefrDisplay, value); }
    public int TotalTopicsCount => Topics.Count;
    public int TotalCardsCount => Topics.Sum(t => t.CardCount);
    public ICommand NavigateCommand { get; }
    public ICommand OpenPathTopicCommand { get; }

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
                LoadStudentQuizForAssignment(value);
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
    public string AssignmentTopic { get => _assignmentTopic; set { if (SetField(ref _assignmentTopic, value)) RaiseCommandStates(); } }
    public bool IsGeneratingSkillAssignment { get => _isGeneratingSkillAssignment; private set { if (SetField(ref _isGeneratingSkillAssignment, value)) RaiseCommandStates(); } }
    public string AssignmentInstructions { get => _assignmentInstructions; set { if (SetField(ref _assignmentInstructions, value)) RaiseCommandStates(); } }
    public string AssignmentContent { get => _assignmentContent; set => SetField(ref _assignmentContent, value); }
    public int AssignmentDuration { get => _assignmentDuration; set => SetField(ref _assignmentDuration, value); }
    public string AssignmentReadingQuiz { get => _assignmentReadingQuiz; set => SetField(ref _assignmentReadingQuiz, value); }
    public string AssignmentMediaUrl { get => _assignmentMediaUrl; set => SetField(ref _assignmentMediaUrl, value); }
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

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    public string BusyMessage
    {
        get => _busyMessage;
        private set => SetField(ref _busyMessage, value);
    }

    private async Task RunBusyAsync(string message, Func<Task> action)
    {
        BusyMessage = message;
        IsBusy = true;
        try { await action(); }
        finally
        {
            IsBusy = false;
            BusyMessage = string.Empty;
        }
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
    public ICommand AddQuizQuestionCommand { get; }
    public ICommand RemoveQuizQuestionCommand { get; }
    public ICommand GenerateSkillAssignmentCommand { get; }
    public ICommand PlayListeningAudioCommand { get; }
    public ICommand PlayExpertMediaCommand { get; }
    public ICommand StopAudioCommand { get; }
    public ICommand SpeakListeningScriptCommand { get; }

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
    public string ExamTopicInput { get => _examTopicInput; set => SetField(ref _examTopicInput, value); }
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
            LoginResponse? result = null;
            await RunBusyAsync(IsRegistering ? "Đang tạo tài khoản & đồng bộ..." : "Đang xác thực & tải dữ liệu...", async () =>
            {
                result = IsRegistering
                    ? await _apiClient.RegisterAsync(RegisterName.Trim(), Email.Trim(), Password)
                    : await _apiClient.LoginAsync(Email.Trim(), Password);
            });
            if (result?.Success != true || result.User is null)
            {
                IsLoggedIn = false;
                StatusMessage = IsRegistering ? "Đăng ký thất bại. Email có thể đã được sử dụng." : "Email hoặc mật khẩu không đúng, hoặc tài khoản đã bị khóa.";
                return;
            }

            CurrentUser = result.User;
            IsLoggedIn = true;
            ActiveTabIndex = IsAdmin ? 2 : IsExpert ? 1 : 0;
            CurrentNavPage = IsAdmin ? "AdminDashboard" : IsExpert ? "ExpertGenerate" : "LearningPath";
            IsRegistering = false;
            RegisterName = string.Empty;
            Password = string.Empty;
            StatusMessage = $"Xin chào {result.User.Name}";
            await RunBusyAsync("Đang tải dữ liệu học tập...", async () =>
            {
                if (IsStudent) await LoadTopicsAsync();
                if (IsAssessmentUser) await LoadF4Async();
                if (IsAdmin) await LoadAdminAsync();
            });
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
            for (int i = 0; i < items.Count; i++)
            {
                items[i].Index = i;
                Topics.Add(items[i]);
            }

            SelectedTopic = Topics.FirstOrDefault(topic => topic.Id == selectedId) ?? Topics.FirstOrDefault();
            if (SelectedTopic is null)
                FlashcardSession.SetCards(new List<EnglishFlashcard>());
            else
                await OpenSelectedTopicAsync();
            RefreshGamificationStats();
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
            TopicDeck? topic = null;
            await RunBusyAsync($"Gemini AI đang tạo 15 thẻ từ vựng cho «{NewTopicName.Trim()}»...", async () =>
            {
                topic = await _apiClient.GenerateTopicAsync(NewTopicName.Trim(), SelectedCefr, CurrentUser.Id);
            });
            if (topic is null)
            {
                throw new InvalidOperationException("API không trả về chủ đề.");
            }

            Topics.Insert(0, topic);
            ReindexTopics();
            SelectedTopic = topic;
            NewTopicName = string.Empty;
            StatusMessage = "Đã tạo bộ từ vựng bằng Gemini.";
            await OpenSelectedTopicAsync();
            CurrentNavPage = "Flashcards";
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
            _masteredByTopic[SelectedTopic.Id] = FlashcardSession.MasteredCount;
            RefreshGamificationStats();
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
            GenerateBatchResponse result = null!;
            await RunBusyAsync($"Gemini AI đang sinh thêm 15 thẻ mới cho «{topic.Name}»...", async () =>
            {
                result = await _apiClient.GenerateBatchAsync(topic.Id, CurrentUser.Id);
            });
            var updatedTopic = new TopicDeck
            {
                Id = topic.Id,
                Name = topic.Name,
                Level = topic.Level,
                CardCount = result.TotalCards,
                Index = topic.Index
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
            _masteredByTopic.Remove(topic.Id);
            ReindexTopics();
            SelectedTopic = Topics.FirstOrDefault();
            if (SelectedTopic is null)
                FlashcardSession.SetCards(new List<EnglishFlashcard>());
            else
                await OpenSelectedTopicAsync();
            RefreshGamificationStats();
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
        if (SelectedTopic is not null)
            _masteredByTopic[SelectedTopic.Id] = FlashcardSession.MasteredCount;
        RefreshGamificationStats();
        if (string.IsNullOrWhiteSpace(card.Id) || !IsLoggedIn || CurrentUser is null) return;
        await _apiClient.UpdateCardReviewAsync(card, CurrentUser.Id);
    }

    private async Task OpenPathTopicAsync(TopicDeck? deck)
    {
        if (deck is null) return;
        SelectedTopic = deck;
        await OpenSelectedTopicAsync();
        CurrentNavPage = "Flashcards";
    }

    private void ReindexTopics()
    {
        for (int i = 0; i < Topics.Count; i++) Topics[i].Index = i;
        OnPropertyChanged(nameof(TotalTopicsCount));
        OnPropertyChanged(nameof(TotalCardsCount));
    }

    public void RefreshGamificationStats()
    {
        if (SelectedTopic is not null)
            _masteredByTopic[SelectedTopic.Id] = FlashcardSession.MasteredCount;

        int masteredTotal = _masteredByTopic.Values.Sum();
        int submissionCount = StudentSubmissions.Count;
        int scoreXp = (int)Math.Round(StudentSubmissions.Sum(s => s.Score ?? s.Reading_Auto_Score ?? 0) * 5);
        TotalXp = masteredTotal * 10 + submissionCount * 25 + scoreXp;

        var activeDates = new HashSet<DateTime>();
        foreach (var sub in StudentSubmissions)
        {
            if (DateTime.TryParse(sub.Submitted_At, out var dt))
                activeDates.Add(dt.ToLocalTime().Date);
        }
        foreach (var c in FlashcardSession.Cards)
        {
            if (c.LastReviewedAt.HasValue)
                activeDates.Add(c.LastReviewedAt.Value.ToLocalTime().Date);
        }
        StreakDays = activeDates.Count > 0 ? activeDates.Count : (masteredTotal > 0 ? 1 : 0);

        DailyMasteredCount = FlashcardSession.MasteredCount;
        CurrentCefrDisplay = StudentSubmissions
            .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Assessed_Level))?.Assessed_Level
            ?? SelectedTopic?.Level
            ?? SelectedCefr;

        OnPropertyChanged(nameof(TotalTopicsCount));
        OnPropertyChanged(nameof(TotalCardsCount));
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
            ToggleShowAnswersCommand, StartExamCommand, SubmitExamCommand, CancelExamCommand,
            SendConsultationCommand, GenerateSkillAssignmentCommand
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
            Consultations.Clear();
            StudentProgressList.Clear();

            if (IsRole("expert"))
            {
                var assignmentsTask = _apiClient.GetExpertAssignmentsAsync();
                var submissionsTask = _apiClient.GetExpertSubmissionsAsync();
                var usersTask = _apiClient.GetUsersAsync();
                var progressTask = _apiClient.GetStudentsProgressAsync();
                var consultsTask = _apiClient.GetExpertConsultationsAsync();
                await Task.WhenAll(assignmentsTask, submissionsTask, usersTask, progressTask, consultsTask);

                foreach (var item in assignmentsTask.Result) Assignments.Add(item);
                foreach (var item in submissionsTask.Result) Submissions.Add(item);

                StudentsList.Clear();
                foreach (var u in usersTask.Result.Where(u => u.Role.Equals("student", StringComparison.OrdinalIgnoreCase)))
                {
                    StudentsList.Add(u);
                }

                var previousStudentId = SelectedProgressStudent?.User?.Id;
                foreach (var p in progressTask.Result) StudentProgressList.Add(p);
                SelectedProgressStudent = StudentProgressList.FirstOrDefault(p => p.User?.Id == previousStudentId)
                    ?? StudentProgressList.FirstOrDefault();

                foreach (var c in consultsTask.Result) Consultations.Add(c);
                RaiseCommandStates();
            }
            else if (IsRole("student"))
            {
                var assignmentsTask = _apiClient.GetStudentAssignmentsAsync();
                var submissionsTask = _apiClient.GetStudentSubmissionsAsync();
                var consultsTask = _apiClient.GetStudentConsultationsAsync(CurrentUser?.Id);
                await Task.WhenAll(assignmentsTask, submissionsTask, consultsTask);

                foreach (var item in assignmentsTask.Result) Assignments.Add(item);
                foreach (var item in submissionsTask.Result)
                {
                    Submissions.Add(item);
                    StudentSubmissions.Add(item);
                }
                foreach (var c in consultsTask.Result) Consultations.Add(c);
                RefreshGamificationStats();
            }
        }
        catch { StatusMessage = "Không tải được dữ liệu khảo thí."; }
    }

    private string? BuildReadingQuizJson()
    {
        var validQuestions = EditableQuizQuestions
            .Where(q => !string.IsNullOrWhiteSpace(q.Question))
            .Select((q, idx) =>
            {
                q.Id = idx + 1;
                return q.ToDto();
            })
            .ToList();
        if (validQuestions.Count > 0)
            return System.Text.Json.JsonSerializer.Serialize(validQuestions);
        return !string.IsNullOrWhiteSpace(AssignmentReadingQuiz) ? AssignmentReadingQuiz.Trim() : null;
    }

    private void LoadStudentQuizForAssignment(ApiAssignment? assignment)
    {
        StudentQuizQuestions.Clear();
        if (assignment is null || string.IsNullOrWhiteSpace(assignment.Reading_Quiz)) return;
        try
        {
            var opts = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var items = System.Text.Json.JsonSerializer.Deserialize<List<ApiReadingQuizQuestion>>(assignment.Reading_Quiz, opts);
            if (items is null) return;
            foreach (var q in items) StudentQuizQuestions.Add(new SkillQuizQuestionItem(q));
        }
        catch { }
    }

    private void AddQuizQuestion()
    {
        EditableQuizQuestions.Add(new EditableQuizQuestionItem
        {
            Id = EditableQuizQuestions.Count + 1,
            Question = $"Câu hỏi {EditableQuizQuestions.Count + 1}?",
            OptionA = "Phương án A",
            OptionB = "Phương án B",
            OptionC = "Phương án C",
            OptionD = "Phương án D",
            CorrectAnswer = "A"
        });
    }

    private void RemoveQuizQuestion(EditableQuizQuestionItem item)
    {
        EditableQuizQuestions.Remove(item);
        for (int i = 0; i < EditableQuizQuestions.Count; i++) EditableQuizQuestions[i].Id = i + 1;
    }

    private void PlayAudioUrl(string? url)
    {
        if (_mediaAudioService.PlayMedia(url, out var error))
            StatusMessage = "Đang phát audio bài nghe...";
        else
            StatusMessage = string.IsNullOrWhiteSpace(error) ? "Không phát được audio." : $"Không phát được audio: {error}";
    }

    private void SpeakListeningScript()
    {
        var text = SelectedAssignment?.Content;
        if (string.IsNullOrWhiteSpace(text)) text = SelectedAssignment?.Instructions;
        if (string.IsNullOrWhiteSpace(text))
        {
            StatusMessage = "Bài nghe chưa có nội dung để đọc bằng TTS.";
            return;
        }
        _mediaAudioService.SpeakAsync(text);
        StatusMessage = "Đang đọc nội dung bài nghe bằng giọng đọc TTS...";
    }

    private async Task GenerateSkillAssignmentAsync()
    {
        if (string.IsNullOrWhiteSpace(AssignmentTopic))
        {
            StatusMessage = "Vui lòng nhập đề tài để AI nhận diện và ra đề.";
            MessageBox.Show("Hãy nhập đề tài (ví dụ: Renewable Energy, AI in Education) trước khi bấm AI Ra đề.", "Thiếu đề tài", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsGeneratingSkillAssignment = true;
        StatusMessage = $"AI đang nhận diện đề tài «{AssignmentTopic.Trim()}» và tạo đề {AssignmentSkill} ({SelectedCefr})...";
        try
        {
            var draft = await _apiClient.GenerateSkillAssignmentAsync(AssignmentTopic.Trim(), AssignmentSkill, SelectedCefr, 4);
            if (draft is null)
            {
                StatusMessage = "AI không tạo được đề cho đề tài này.";
                return;
            }

            AssignmentTitle = draft.Title;
            AssignmentInstructions = draft.Instructions;
            AssignmentContent = draft.Content;
            EditableQuizQuestions.Clear();
            foreach (var q in draft.Questions)
                EditableQuizQuestions.Add(EditableQuizQuestionItem.FromDto(q));

            StatusMessage = $"AI đã tạo đề {draft.Skill} theo đề tài «{draft.Topic}» ({EditableQuizQuestions.Count} câu trắc nghiệm). Kiểm tra rồi bấm Tạo bài mới.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi khi gọi AI ra đề: {ex.Message}";
        }
        finally
        {
            IsGeneratingSkillAssignment = false;
        }
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
            var topicNames = string.IsNullOrWhiteSpace(AssignmentTopic)
                ? new List<string>()
                : new List<string> { AssignmentTopic.Trim() };
            var ok = await _apiClient.CreateAssignmentAsync(
                AssignmentTitle.Trim(),
                AssignmentSkill,
                SelectedCefr,
                AssignmentDuration > 0 ? AssignmentDuration : 45,
                AssignmentContent.Trim(),
                AssignmentInstructions.Trim(),
                AssignmentDueDate,
                10.0,
                BuildReadingQuizJson(),
                !string.IsNullOrWhiteSpace(AssignmentMediaUrl) ? AssignmentMediaUrl.Trim() : null,
                topicNames);

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
            var topicNames = string.IsNullOrWhiteSpace(AssignmentTopic)
                ? new List<string>()
                : new List<string> { AssignmentTopic.Trim() };
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
                BuildReadingQuizJson(),
                !string.IsNullOrWhiteSpace(AssignmentMediaUrl) ? AssignmentMediaUrl.Trim() : null,
                topicNames);

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
        AssignmentTopic = SelectedAssignment.Topic_Names.FirstOrDefault() ?? "";
        AssignmentSkill = Skills.FirstOrDefault(s => s.Equals(SelectedAssignment.Skill, StringComparison.OrdinalIgnoreCase)) ?? "reading";
        SelectedCefr = SelectedAssignment.Target_Level;
        AssignmentDuration = SelectedAssignment.Duration_Minutes > 0 ? SelectedAssignment.Duration_Minutes : 45;
        AssignmentDueDate = SelectedAssignment.Due_Date ?? DateTime.Today.AddDays(7).ToString("yyyy-MM-dd");
        AssignmentContent = SelectedAssignment.Content;
        AssignmentInstructions = SelectedAssignment.Instructions;
        AssignmentMediaUrl = SelectedAssignment.Reference_Media_Url ?? "";
        AssignmentReadingQuiz = SelectedAssignment.Reading_Quiz ?? "";
        EditableQuizQuestions.Clear();
        if (!string.IsNullOrWhiteSpace(SelectedAssignment.Reading_Quiz))
        {
            try
            {
                var opts = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var parsed = System.Text.Json.JsonSerializer.Deserialize<List<ApiReadingQuizQuestion>>(SelectedAssignment.Reading_Quiz, opts);
                if (parsed is not null)
                {
                    foreach (var q in parsed) EditableQuizQuestions.Add(EditableQuizQuestionItem.FromDto(q));
                }
            }
            catch { }
        }
        IsEditingAssignment = true;
        StatusMessage = $"Đang chỉnh sửa bài tập: {SelectedAssignment.Title}";
    }

    private void ClearAssignmentForm()
    {
        AssignmentTitle = "";
        AssignmentTopic = "";
        AssignmentSkill = "reading";
        AssignmentContent = "";
        AssignmentInstructions = "";
        AssignmentMediaUrl = "";
        AssignmentReadingQuiz = "";
        EditableQuizQuestions.Clear();
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
            else if (SelectedAssignment.Skill.Equals("reading", StringComparison.OrdinalIgnoreCase)
                     || SelectedAssignment.Skill.Equals("listening", StringComparison.OrdinalIgnoreCase))
            {
                if (StudentQuizQuestions.Count > 0)
                {
                    var answersDict = StudentQuizQuestions
                        .Where(q => !string.IsNullOrWhiteSpace(q.SelectedAnswer))
                        .ToDictionary(q => q.Id.ToString(), q => q.SelectedAnswer);
                    if (answersDict.Count > 0)
                        readingAnswers = System.Text.Json.JsonSerializer.Serialize(answersDict);
                }
                topicSentence = !string.IsNullOrWhiteSpace(StudentTopicSentence) ? StudentTopicSentence.Trim() : null;
                if (string.IsNullOrWhiteSpace(finalContent) && (!string.IsNullOrWhiteSpace(readingAnswers) || !string.IsNullOrWhiteSpace(topicSentence)))
                {
                    finalContent = $"Đã hoàn thành bài {SelectedAssignment.Skill} ({StudentQuizQuestions.Count(q => !string.IsNullOrWhiteSpace(q.SelectedAnswer))}/{StudentQuizQuestions.Count} câu trắc nghiệm).";
                }
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

    private async Task SendConsultationAsync()
    {
        if (SelectedProgressStudent?.User is null)
        {
            StatusMessage = "Vui lòng chọn sinh viên cần tư vấn.";
            return;
        }
        if (string.IsNullOrWhiteSpace(ConsultAdvice))
        {
            StatusMessage = "Vui lòng nhập nhận xét / lời khuyên học tập.";
            MessageBox.Show("Hãy nhập nội dung nhận xét & lời khuyên trước khi gửi tư vấn.", "Thiếu nội dung tư vấn", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var targetStudent = SelectedProgressStudent.User;
        var strengths = SplitTags(ConsultStrengths, "Nền tảng tốt");
        var weaknesses = SplitTags(ConsultWeaknesses, "Cần luyện thêm");
        try
        {
            bool ok = false;
            await RunBusyAsync($"Đang gửi tư vấn cho {targetStudent.Name}...", async () =>
            {
                ok = await _apiClient.CreateConsultationAsync(
                    targetStudent.Id,
                    targetStudent.Name,
                    ConsultCurrentLevel,
                    ConsultRecommendedLevel,
                    ConsultAdvice.Trim(),
                    ConsultStudyPlan.Trim(),
                    strengths,
                    weaknesses);
            });
            if (ok)
            {
                ConsultAdvice = "";
                await LoadF4Async();
                StatusMessage = $"Đã gửi tư vấn lộ trình học cho sinh viên {targetStudent.Name}.";
            }
            else
            {
                StatusMessage = "Gửi tư vấn thất bại.";
            }
        }
        catch { StatusMessage = "Không kết nối được API để gửi tư vấn."; }
    }

    private static List<string> SplitTags(string raw, string fallback)
    {
        var items = (raw ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
        return items.Count > 0 ? items : new List<string> { fallback };
    }

    private async Task LoadAdminAsync()
    {
        try
        {
            var statsTask = _apiClient.GetAdminStatsAsync();
            var usersTask = _apiClient.GetUsersAsync();
            var logsTask = _apiClient.GetApiLogsAsync();
            var curriculumTask = _apiClient.GetCurriculumAsync();
            var aiTask = _apiClient.GetAiConfigAsync();
            await Task.WhenAll(statsTask, usersTask, logsTask, curriculumTask, aiTask);

            AdminStats = statsTask.Result; OnPropertyChanged(nameof(AdminStats));
            AdminSummary = $"Sinh viên: {AdminStats.Total_Students} | Chuyên gia: {AdminStats.Total_Experts} | Chủ đề: {AdminStats.Total_Topics} | Thẻ: {AdminStats.Total_Cards} | Bài tập: {AdminStats.Total_Assignments} | Bài nộp: {AdminStats.Total_Submissions} | API calls: {AdminStats.Total_Api_Calls}";
            Users.Clear(); foreach (var u in usersTask.Result) Users.Add(u);
            ApiLogs.Clear(); foreach (var log in logsTask.Result) ApiLogs.Add(log);
            Curriculum.Clear(); foreach (var topic in curriculumTask.Result) Curriculum.Add(topic);
            AiConfig = aiTask.Result;
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
        try
        {
            var ok = await _apiClient.SyncMysqlAsync();
            StatusMessage = ok
                ? "Đã đồng bộ dữ liệu sang MySQL thành công."
                : "Không kết nối được MySQL (đang dùng JSON cục bộ). Hãy kiểm tra dịch vụ MySQL và cấu hình .env.";
        }
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
                Custom_Topic = string.IsNullOrWhiteSpace(ExamTopicInput) ? null : ExamTopicInput.Trim(),
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
            await RunBusyAsync("Gemini AI đang phân tích đề tài & sinh đề thi...", async () =>
            {
                GeneratedExam = await _apiClient.GenerateExamAsync(req);
            });
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
        var topicNames = new List<string>();
        if (!string.IsNullOrWhiteSpace(ExamTopicInput))
            topicNames.Add(ExamTopicInput.Trim());
        var selectedNames = SelectedExamTopic is not null ? new List<string> { SelectedExamTopic.Name } : StudentTopics.Select(t => t.Name).ToList();
        foreach (var name in selectedNames)
        {
            if (!string.IsNullOrWhiteSpace(name) && !topicNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                topicNames.Add(name);
        }
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
