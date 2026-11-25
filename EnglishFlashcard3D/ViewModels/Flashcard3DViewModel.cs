using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using EnglishFlashcard3D.Models;
using EnglishFlashcard3D.Services;

namespace EnglishFlashcard3D.ViewModels;

public enum FlashcardFilterMode
{
    All,
    Unmastered,
    Mastered,
    DueForReview
}

public class Flashcard3DViewModel : BaseViewModel
{
    private readonly AudioService _audioService;
    private readonly SpacedRepetitionService _srsService = new();
    private readonly List<EnglishFlashcard> _allCards = new();
    private readonly ObservableCollection<EnglishFlashcard> _cards = new();
    private int _currentIndex;
    private bool _isFlipped;
    private FlashcardFilterMode _filterMode = FlashcardFilterMode.All;
    private string _toastNotification = string.Empty;
    private string _topicTitle = "Bộ thẻ từ vựng";
    private string _topicLevel = "B1";

    public Flashcard3DViewModel(List<EnglishFlashcard> cards)
    {
        _audioService = new AudioService();

        FlipCommand = new RelayCommand(_ => FlipCard(), _ => _cards.Count > 0);
        NextCommand = new RelayCommand(_ => NextCard(), _ => CanNext);
        PrevCommand = new RelayCommand(_ => PrevCard(), _ => CanPrev);
        PronounceCommand = new RelayCommand(_ => SpeakCurrentWord(), _ => CurrentCard is not null);
        MarkMasteredCommand = new RelayCommand(_ => ToggleMastered(), _ => CurrentCard is not null);
        SaveNoteCommand = new RelayCommand(_ => SaveCurrentNote(), _ => CurrentCard is not null);
        CopyCommand = new RelayCommand(_ => CopyCurrentCard(), _ => CurrentCard is not null);
        ShuffleCommand = new RelayCommand(_ => ShuffleCards(), _ => _cards.Count > 1);
        ResetCommand = new RelayCommand(_ => ResetDeck(), _ => _cards.Count > 0);

        FilterAllCommand = new RelayCommand(_ => FilterMode = FlashcardFilterMode.All);
        FilterUnmasteredCommand = new RelayCommand(_ => FilterMode = FlashcardFilterMode.Unmastered);
        FilterMasteredCommand = new RelayCommand(_ => FilterMode = FlashcardFilterMode.Mastered);
        FilterDueCommand = new RelayCommand(_ => FilterMode = FlashcardFilterMode.DueForReview);

        SetCards(cards);
    }

    public event Action<EnglishFlashcard>? CardMarkedMastered;
    public event Action<string>? ToastTriggered;

    public ObservableCollection<EnglishFlashcard> Cards => _cards;

    public EnglishFlashcard? CurrentCard => _cards.Count > 0 && _currentIndex >= 0 && _currentIndex < _cards.Count
        ? _cards[_currentIndex]
        : null;

    public int CurrentIndex
    {
        get => _currentIndex;
        set
        {
            if (!SetField(ref _currentIndex, value)) return;
            OnPropertyChanged(nameof(CurrentCard));
            OnPropertyChanged(nameof(ProgressText));
            OnPropertyChanged(nameof(CanNext));
            OnPropertyChanged(nameof(CanPrev));
            IsFlipped = false;
            RaiseCommandStates();
        }
    }

    public bool IsFlipped
    {
        get => _isFlipped;
        set => SetField(ref _isFlipped, value);
    }

    public string TopicTitle
    {
        get => _topicTitle;
        set => SetField(ref _topicTitle, value);
    }

    public string TopicLevel
    {
        get => _topicLevel;
        set => SetField(ref _topicLevel, value);
    }

    public FlashcardFilterMode FilterMode
    {
        get => _filterMode;
        set
        {
            if (!SetField(ref _filterMode, value)) return;
            ApplyFilter();
            OnPropertyChanged(nameof(IsFilterAllActive));
            OnPropertyChanged(nameof(IsFilterUnmasteredActive));
            OnPropertyChanged(nameof(IsFilterMasteredActive));
            OnPropertyChanged(nameof(IsFilterDueActive));
        }
    }

    public bool IsFilterAllActive => _filterMode == FlashcardFilterMode.All;
    public bool IsFilterUnmasteredActive => _filterMode == FlashcardFilterMode.Unmastered;
    public bool IsFilterMasteredActive => _filterMode == FlashcardFilterMode.Mastered;
    public bool IsFilterDueActive => _filterMode == FlashcardFilterMode.DueForReview;

    public static bool IsCardDue(EnglishFlashcard card)
        => !card.IsMastered || !card.NextReviewAt.HasValue || card.NextReviewAt.Value <= DateTime.UtcNow;

    public string ToastNotification
    {
        get => _toastNotification;
        set
        {
            if (SetField(ref _toastNotification, value) && !string.IsNullOrEmpty(value))
            {
                ToastTriggered?.Invoke(value);
            }
        }
    }

    public int TotalCount => _allCards.Count;
    public int MasteredCount => _allCards.Count(c => c.IsMastered);
    public int UnmasteredCount => _allCards.Count(c => !c.IsMastered);
    public int DueCount => _allCards.Count(IsCardDue);
    public double ProgressPercentage => TotalCount > 0 ? (MasteredCount * 100.0) / TotalCount : 0;
    public string ProgressSummary => $"Đã thuộc: {MasteredCount} / {TotalCount} từ ({ProgressPercentage:0}%)";
    public string ProgressText => _cards.Count == 0 ? "0 / 0" : $"{_currentIndex + 1} / {_cards.Count}";

    public bool CanNext => _currentIndex < _cards.Count - 1;
    public bool CanPrev => _currentIndex > 0;

    public ICommand FlipCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand PrevCommand { get; }
    public ICommand PronounceCommand { get; }
    public ICommand MarkMasteredCommand { get; }
    public ICommand SaveNoteCommand { get; }
    public ICommand CopyCommand { get; }
    public ICommand ShuffleCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand FilterAllCommand { get; }
    public ICommand FilterUnmasteredCommand { get; }
    public ICommand FilterMasteredCommand { get; }
    public ICommand FilterDueCommand { get; }

    public void SetCards(List<EnglishFlashcard> cards, string? topicTitle = null, string? topicLevel = null)
    {
        if (!string.IsNullOrWhiteSpace(topicTitle)) TopicTitle = topicTitle;
        if (!string.IsNullOrWhiteSpace(topicLevel)) TopicLevel = topicLevel;

        _allCards.Clear();
        _allCards.AddRange(cards);

        ApplyFilter();
        UpdateProgressMetrics();
    }

    private void ApplyFilter()
    {
        var targetCards = _filterMode switch
        {
            FlashcardFilterMode.Unmastered => _allCards.Where(c => !c.IsMastered).ToList(),
            FlashcardFilterMode.Mastered => _allCards.Where(c => c.IsMastered).ToList(),
            FlashcardFilterMode.DueForReview => _allCards.Where(IsCardDue).ToList(),
            _ => _allCards.ToList()
        };

        _cards.Clear();
        foreach (var c in targetCards)
        {
            _cards.Add(c);
        }

        CurrentIndex = 0;
        IsFlipped = false;
        OnPropertyChanged(nameof(CurrentCard));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(CanNext));
        OnPropertyChanged(nameof(CanPrev));
        RaiseCommandStates();
    }

    public void FlipCard() => IsFlipped = !IsFlipped;

    public void NextCard()
    {
        if (CanNext) CurrentIndex++;
    }

    public void PrevCard()
    {
        if (CanPrev) CurrentIndex--;
    }

    public void SpeakCurrentWord()
    {
        if (CurrentCard is null) return;
        _audioService.SpeakAsync(CurrentCard.Word);
    }

    public void ToggleMastered()
    {
        if (CurrentCard is null) return;
        int quality = CurrentCard.IsMastered ? 2 : 5;
        _srsService.ApplyReview(CurrentCard, quality);

        UpdateProgressMetrics();
        OnPropertyChanged(nameof(CurrentCard));
        CardMarkedMastered?.Invoke(CurrentCard);

        ToastNotification = CurrentCard.IsMastered
            ? $"✓ Đã thuộc từ: {CurrentCard.Word} (Ôn lại sau {CurrentCard.IntervalDays} ngày)"
            : $"⏳ Đã chuyển sang cần ôn: {CurrentCard.Word}";
    }

    public void SaveCurrentNote()
    {
        if (CurrentCard is null) return;
        CardMarkedMastered?.Invoke(CurrentCard);
        ToastNotification = $"💾 Đã lưu ghi chú cho từ '{CurrentCard.Word}'.";
    }

    public void CopyCurrentCard()
    {
        if (CurrentCard is null) return;
        try
        {
            var text =
                $"Word: {CurrentCard.Word}\n" +
                $"Phonetic: {CurrentCard.Phonetic}\n" +
                $"Type: {CurrentCard.PartOfSpeech}\n" +
                $"Meaning: {CurrentCard.MeaningVi}\n" +
                $"Example: {CurrentCard.ExampleEn}\n" +
                $"Translation: {CurrentCard.ExampleVi}" +
                (string.IsNullOrWhiteSpace(CurrentCard.Note) ? "" : $"\nNote: {CurrentCard.Note}");

            Clipboard.SetText(text);
            ToastNotification = $"📋 Đã sao chép từ '{CurrentCard.Word}' vào Clipboard!";
        }
        catch
        {
            ToastNotification = "Không thể truy cập Clipboard.";
        }
    }

    public void ShuffleCards()
    {
        if (_cards.Count <= 1) return;
        var rng = new Random();
        int n = _cards.Count;
        while (n > 1)
        {
            n--;
            int k = rng.Next(n + 1);
            (_cards[k], _cards[n]) = (_cards[n], _cards[k]);
        }
        CurrentIndex = 0;
        IsFlipped = false;
        ToastNotification = "🔀 Đã xáo trộn danh sách thẻ!";
    }

    public void ResetDeck()
    {
        CurrentIndex = 0;
        IsFlipped = false;
        ToastNotification = "↺ Đã quay lại thẻ đầu tiên.";
    }

    private void UpdateProgressMetrics()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(MasteredCount));
        OnPropertyChanged(nameof(UnmasteredCount));
        OnPropertyChanged(nameof(DueCount));
        OnPropertyChanged(nameof(ProgressPercentage));
        OnPropertyChanged(nameof(ProgressSummary));
    }

    private void RaiseCommandStates()
    {
        if (FlipCommand is RelayCommand flip) flip.RaiseCanExecuteChanged();
        if (NextCommand is RelayCommand next) next.RaiseCanExecuteChanged();
        if (PrevCommand is RelayCommand prev) prev.RaiseCanExecuteChanged();
        if (PronounceCommand is RelayCommand speak) speak.RaiseCanExecuteChanged();
        if (MarkMasteredCommand is RelayCommand master) master.RaiseCanExecuteChanged();
        if (SaveNoteCommand is RelayCommand saveNote) saveNote.RaiseCanExecuteChanged();
        if (CopyCommand is RelayCommand copy) copy.RaiseCanExecuteChanged();
        if (ShuffleCommand is RelayCommand shuffle) shuffle.RaiseCanExecuteChanged();
        if (ResetCommand is RelayCommand reset) reset.RaiseCanExecuteChanged();
    }
}
