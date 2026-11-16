using System.Collections.ObjectModel;
using System.Windows.Input;
using EnglishFlashcard3D.Models;
using EnglishFlashcard3D.Services;

namespace EnglishFlashcard3D.ViewModels;

public class Flashcard3DViewModel : BaseViewModel
{
    private readonly AudioService _audioService;
    private readonly ObservableCollection<EnglishFlashcard> _cards;
    private int _currentIndex;
    private bool _isFlipped;

    public Flashcard3DViewModel(List<EnglishFlashcard> cards)
    {
        _audioService = new AudioService();
        _cards = new ObservableCollection<EnglishFlashcard>(cards);

        FlipCommand = new RelayCommand(_ => FlipCard(), _ => _cards.Count > 0);
        NextCommand = new RelayCommand(_ => NextCard(), _ => CanNext);
        PrevCommand = new RelayCommand(_ => PrevCard(), _ => CanPrev);
        PronounceCommand = new RelayCommand(_ => SpeakCurrentWord(), _ => CurrentCard is not null);
        MarkMasteredCommand = new RelayCommand(_ => MarkMastered(), _ => CurrentCard is not null);
    }

    public event Action<EnglishFlashcard>? CardMarkedMastered;

    public EnglishFlashcard? CurrentCard => _cards.Count > 0 && _currentIndex >= 0 && _currentIndex < _cards.Count ? _cards[_currentIndex] : null;

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

    public string ProgressText => _cards.Count == 0 ? "0 / 0" : $"{_currentIndex + 1} / {_cards.Count}";

    public bool CanNext => _currentIndex < _cards.Count - 1;
    public bool CanPrev => _currentIndex > 0;

    public ICommand FlipCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand PrevCommand { get; }
    public ICommand PronounceCommand { get; }
    public ICommand MarkMasteredCommand { get; }

    public void SetCards(List<EnglishFlashcard> cards)
    {
        _cards.Clear();
        foreach (var card in cards)
        {
            _cards.Add(card);
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

    public void MarkMastered()
    {
        if (CurrentCard is null) return;
        CurrentCard.IsMastered = true;
        CurrentCard.ReviewCount += 1;
        CurrentCard.LastReviewedAt = DateTime.Now;
        OnPropertyChanged(nameof(CurrentCard));
        CardMarkedMastered?.Invoke(CurrentCard);
    }

    private void RaiseCommandStates()
    {
        if (FlipCommand is RelayCommand flip) flip.RaiseCanExecuteChanged();
        if (NextCommand is RelayCommand next) next.RaiseCanExecuteChanged();
        if (PrevCommand is RelayCommand prev) prev.RaiseCanExecuteChanged();
        if (PronounceCommand is RelayCommand speak) speak.RaiseCanExecuteChanged();
        if (MarkMasteredCommand is RelayCommand master) master.RaiseCanExecuteChanged();
    }
}
