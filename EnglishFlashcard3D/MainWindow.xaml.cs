using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using EnglishFlashcard3D.ViewModels;

namespace EnglishFlashcard3D;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var vm = new MainViewModel();
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.Password)
                && string.IsNullOrEmpty(vm.Password)
                && !string.IsNullOrEmpty(AuthPasswordBox.Password))
            {
                AuthPasswordBox.Clear();
            }
        };
        DataContext = vm;
    }

    private void AuthPasswordBox_OnPasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && sender is System.Windows.Controls.PasswordBox box)
            vm.Password = box.Password;
    }
    private void Window_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBoxBase or PasswordBox ||
            Keyboard.FocusedElement is ComboBox { IsEditable: true })
            return;

        if (DataContext is not MainViewModel vm || !vm.IsStudent || vm.CurrentNavPage != "Flashcards") return;
        var command = e.Key switch
        {
            Key.Space => vm.FlashcardSession.FlipCommand,
            Key.Right => vm.FlashcardSession.NextCommand,
            Key.Left => vm.FlashcardSession.PrevCommand,
            Key.P => vm.FlashcardSession.PronounceCommand,
            Key.M => vm.FlashcardSession.MarkMasteredCommand,
            Key.C => vm.FlashcardSession.CopyCommand,
            Key.S => vm.FlashcardSession.ShuffleCommand,
            Key.R => vm.FlashcardSession.ResetCommand,
            _ => null
        };
        if (command?.CanExecute(null) == true)
        {
            command.Execute(null);
            e.Handled = true;
        }
    }
}
