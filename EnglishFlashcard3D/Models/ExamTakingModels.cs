using System.Collections.ObjectModel;
using System.Windows.Input;
using EnglishFlashcard3D.ViewModels;

namespace EnglishFlashcard3D.Models;

public class ExamTakingQuestionItem : BaseViewModel
{
    public int QuestionNumber { get; set; }
    public string SectionId { get; set; } = string.Empty;
    public string SectionType { get; set; } = "vocabulary"; // warnings, cloze, word_ordering, vocabulary
    public string QuestionText { get; set; } = string.Empty;
    public string? SignType { get; set; }
    public string? SignIcon { get; set; }
    public double Points { get; set; }
    public List<string> Options { get; set; } = new();

    public bool IsMultipleChoice => SectionType != "word_ordering";
    public bool IsWordOrdering => SectionType == "word_ordering";

    public ObservableCollection<string> AvailableWords { get; } = new();
    public ObservableCollection<string> SelectedWords { get; } = new();

    private string _selectedAnswer = string.Empty;
    public string SelectedAnswer
    {
        get => _selectedAnswer;
        set
        {
            if (SetField(ref _selectedAnswer, value))
            {
                AnswerChanged?.Invoke(this, value);
            }
        }
    }

    public event Action<ExamTakingQuestionItem, string>? AnswerChanged;

    public ICommand SelectOptionCommand { get; }
    public ICommand AddWordCommand { get; }
    public ICommand UndoWordCommand { get; }
    public ICommand ResetWordsCommand { get; }

    public ExamTakingQuestionItem(ApiExamQuestion q, string sectionType, string sectionId)
    {
        QuestionNumber = q.Question_Number;
        SectionType = sectionType;
        SectionId = sectionId;
        QuestionText = q.Question_Text;
        SignType = q.Sign_Type;
        SignIcon = q.Sign_Icon;
        Points = q.Points;
        Options = q.Options ?? new();

        if (q.Scrambled_Words is not null)
        {
            foreach (var w in q.Scrambled_Words) AvailableWords.Add(w);
        }

        SelectOptionCommand = new RelayCommand(opt =>
        {
            if (opt is string s && !string.IsNullOrWhiteSpace(s))
            {
                var trimmed = s.Trim();
                SelectedAnswer = trimmed.Length > 0 ? trimmed.Substring(0, 1).ToUpperInvariant() : trimmed;
            }
        });

        AddWordCommand = new RelayCommand(wordObj =>
        {
            if (wordObj is string w && AvailableWords.Contains(w))
            {
                AvailableWords.Remove(w);
                SelectedWords.Add(w);
                SelectedAnswer = string.Join(" ", SelectedWords);
            }
        });

        UndoWordCommand = new RelayCommand(_ =>
        {
            if (SelectedWords.Count > 0)
            {
                var last = SelectedWords[^1];
                SelectedWords.RemoveAt(SelectedWords.Count - 1);
                AvailableWords.Add(last);
                SelectedAnswer = string.Join(" ", SelectedWords);
            }
        });

        ResetWordsCommand = new RelayCommand(_ =>
        {
            SelectedWords.Clear();
            AvailableWords.Clear();
            if (q.Scrambled_Words is not null)
            {
                foreach (var w in q.Scrambled_Words) AvailableWords.Add(w);
            }
            SelectedAnswer = string.Empty;
        });
    }
}

public class ExamTakingSectionItem
{
    public string SectionId { get; set; } = string.Empty;
    public string SectionNumber { get; set; } = "I";
    public string SectionType { get; set; } = "vocabulary";
    public string Title { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public double Points { get; set; }
    public string? Passage { get; set; }
    public ObservableCollection<ExamTakingQuestionItem> Questions { get; } = new();
}

public class ExamAnswerGridItem : BaseViewModel
{
    public int QuestionNumber { get; set; }
    private string _answer = "...";
    public string Answer
    {
        get => _answer;
        set => SetField(ref _answer, value);
    }
}
