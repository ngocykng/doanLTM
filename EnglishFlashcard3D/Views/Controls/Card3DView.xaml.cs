using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using EnglishFlashcard3D.ViewModels;

namespace EnglishFlashcard3D.Views.Controls;

public partial class Card3DView : UserControl
{
    private bool _currentlyShowingBack;

    public Card3DView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is Flashcard3DViewModel oldVm)
        {
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (e.NewValue is Flashcard3DViewModel newVm)
        {
            newVm.PropertyChanged += OnViewModelPropertyChanged;
            SyncFlipState(newVm.IsFlipped, animate: false);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not Flashcard3DViewModel vm) return;

        if (e.PropertyName == nameof(Flashcard3DViewModel.IsFlipped))
        {
            SyncFlipState(vm.IsFlipped, animate: true);
        }
    }

    private void SyncFlipState(bool isFlipped, bool animate)
    {
        if (isFlipped == _currentlyShowingBack && animate) return;

        _currentlyShowingBack = isFlipped;

        if (!animate)
        {
            if (isFlipped)
            {
                FrontFace.Visibility = Visibility.Collapsed;
                BackFace.Visibility = Visibility.Visible;
                BackScale.ScaleX = 1.0;
                FrontScale.ScaleX = 0.0;
            }
            else
            {
                FrontFace.Visibility = Visibility.Visible;
                BackFace.Visibility = Visibility.Collapsed;
                FrontScale.ScaleX = 1.0;
                BackScale.ScaleX = 0.0;
            }
            return;
        }

        var storyboardName = isFlipped ? "FlipToBackStoryboard" : "FlipToFrontStoryboard";
        if (FindResource(storyboardName) is Storyboard sb)
        {
            sb.Begin();
        }
    }

    private void OnCardFaceClicked(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is Flashcard3DViewModel vm)
        {
            vm.FlipCard();
        }
    }

    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        // Tránh sự kiện click nút bấm làm kích hoạt lật thẻ
        e.Handled = true;
    }

    private void NoteBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Cho phép nhập ghi chép mà không lật thẻ
        e.Handled = false;
    }
}
