using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace EnglishFlashcard3D.Models;

public class EnglishFlashcard : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString();
    private string _word = string.Empty;
    private string _phonetic = string.Empty;
    private string _partOfSpeech = string.Empty;
    private string _meaningVi = string.Empty;
    private string _exampleEn = string.Empty;
    private string _exampleVi = string.Empty;
    private string _note = string.Empty;
    private int _reviewCount;
    private bool _isMastered;
    private DateTime? _lastReviewedAt;

    public string Id
    {
        get => _id;
        set => SetField(ref _id, value);
    }

    public string Word
    {
        get => _word;
        set => SetField(ref _word, value);
    }

    public string Phonetic
    {
        get => _phonetic;
        set => SetField(ref _phonetic, value);
    }

    public string PartOfSpeech
    {
        get => _partOfSpeech;
        set => SetField(ref _partOfSpeech, value);
    }

    public string MeaningVi
    {
        get => _meaningVi;
        set => SetField(ref _meaningVi, value);
    }

    public string ExampleEn
    {
        get => _exampleEn;
        set => SetField(ref _exampleEn, value);
    }

    public string ExampleVi
    {
        get => _exampleVi;
        set => SetField(ref _exampleVi, value);
    }

    public string Note
    {
        get => _note;
        set => SetField(ref _note, value);
    }

    public int ReviewCount
    {
        get => _reviewCount;
        set => SetField(ref _reviewCount, value);
    }

    public bool IsMastered
    {
        get => _isMastered;
        set
        {
            if (SetField(ref _isMastered, value))
            {
                OnPropertyChanged(nameof(StatusBadgeText));
                OnPropertyChanged(nameof(StatusBadgeBackground));
                OnPropertyChanged(nameof(StatusBadgeForeground));
            }
        }
    }

    public DateTime? LastReviewedAt
    {
        get => _lastReviewedAt;
        set => SetField(ref _lastReviewedAt, value);
    }

    public string StatusBadgeText => IsMastered ? "✓ Đã thuộc" : "⏳ Cần ôn tập";
    public string StatusBadgeBackground => IsMastered ? "#15803D" : "#334155";
    public string StatusBadgeForeground => IsMastered ? "#86EFAC" : "#94A3B8";

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
