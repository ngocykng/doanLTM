using System.Collections.ObjectModel;
using System.Windows.Input;
using EnglishFlashcard3D.ViewModels;

namespace EnglishFlashcard3D.Models;

/// <summary>Câu hỏi trắc nghiệm hiển thị cho sinh viên ở bài tập Reading / Listening.</summary>
public class SkillQuizQuestionItem : BaseViewModel
{
    public int Id { get; set; }
    public string Question { get; set; } = string.Empty;
    public List<string> Options { get; set; } = new();

    private string _selectedAnswer = string.Empty;
    public string SelectedAnswer
    {
        get => _selectedAnswer;
        set => SetField(ref _selectedAnswer, value);
    }

    public ICommand SelectOptionCommand { get; }

    public SkillQuizQuestionItem(ApiReadingQuizQuestion q)
    {
        Id = q.Id;
        Question = q.Question;
        Options = q.Options ?? new();

        SelectOptionCommand = new RelayCommand(opt =>
        {
            if (opt is string s && !string.IsNullOrWhiteSpace(s))
            {
                var trimmed = s.Trim();
                SelectedAnswer = trimmed.Length > 0 ? trimmed[..1].ToUpperInvariant() : trimmed;
            }
        });
    }
}

/// <summary>Câu hỏi trắc nghiệm cho giảng viên soạn thảo ở bài tập Reading / Listening.</summary>
public class EditableQuizQuestionItem : BaseViewModel
{
    private int _id;
    private string _question = string.Empty;
    private string _optionA = string.Empty;
    private string _optionB = string.Empty;
    private string _optionC = string.Empty;
    private string _optionD = string.Empty;
    private string _correctAnswer = "A";

    public int Id { get => _id; set => SetField(ref _id, value); }
    public string Question { get => _question; set => SetField(ref _question, value); }
    public string OptionA { get => _optionA; set => SetField(ref _optionA, value); }
    public string OptionB { get => _optionB; set => SetField(ref _optionB, value); }
    public string OptionC { get => _optionC; set => SetField(ref _optionC, value); }
    public string OptionD { get => _optionD; set => SetField(ref _optionD, value); }
    public string CorrectAnswer { get => _correctAnswer; set => SetField(ref _correctAnswer, value); }
    public ObservableCollection<string> AnswerChoices { get; } = new() { "A", "B", "C", "D" };

    public ApiReadingQuizQuestion ToDto() => new()
    {
        Id = Id,
        Question = Question.Trim(),
        Options = new List<string>
        {
            FormatOption("A", OptionA),
            FormatOption("B", OptionB),
            FormatOption("C", OptionC),
            FormatOption("D", OptionD)
        },
        Correct_Answer = string.IsNullOrWhiteSpace(CorrectAnswer) ? "A" : CorrectAnswer.Trim().ToUpperInvariant()
    };

    public static EditableQuizQuestionItem FromDto(ApiReadingQuizQuestion dto)
    {
        var opts = dto.Options ?? new();
        return new EditableQuizQuestionItem
        {
            Id = dto.Id,
            Question = dto.Question,
            OptionA = StripPrefix(opts.ElementAtOrDefault(0), "A"),
            OptionB = StripPrefix(opts.ElementAtOrDefault(1), "B"),
            OptionC = StripPrefix(opts.ElementAtOrDefault(2), "C"),
            OptionD = StripPrefix(opts.ElementAtOrDefault(3), "D"),
            CorrectAnswer = string.IsNullOrWhiteSpace(dto.Correct_Answer) ? "A" : dto.Correct_Answer.Trim().ToUpperInvariant()
        };
    }

    private static string FormatOption(string letter, string text)
    {
        var clean = text.Trim();
        if (clean.StartsWith($"{letter}.", StringComparison.OrdinalIgnoreCase)) return clean;
        return $"{letter}. {clean}";
    }

    private static string StripPrefix(string? text, string letter)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var t = text.Trim();
        if (t.StartsWith($"{letter}.", StringComparison.OrdinalIgnoreCase) || t.StartsWith($"{letter})", StringComparison.OrdinalIgnoreCase))
            return t[2..].Trim();
        return t;
    }
}
