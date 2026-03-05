using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace FinderExplorer.Views.Dialogs;

public partial class ConfirmationDialog : Window
{
    public ConfirmationDialog()
        : this("Confirm action", "Are you sure?")
    {
    }

    public ConfirmationDialog(
        string title,
        string message,
        string primaryButtonText = "OK",
        string closeButtonText = "Cancel",
        bool isPrimaryDestructive = false)
    {
        InitializeComponent();

        Title = title;
        MessageText.Text = message;
        PrimaryButton.Content = primaryButtonText;
        CloseButton.Content = closeButtonText;

        if (isPrimaryDestructive)
        {
            PrimaryButton.Background = new SolidColorBrush(Color.FromRgb(196, 43, 28));
            PrimaryButton.Foreground = Brushes.White;
        }
    }

    private void PrimaryButton_Click(object? sender, RoutedEventArgs e) => Close(true);

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close(false);
}
