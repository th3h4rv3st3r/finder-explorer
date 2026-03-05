using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace FinderExplorer.Views.Dialogs;

public partial class TextInputDialog : Window
{
    public TextInputDialog()
        : this("Input", "Enter a value")
    {
    }

    public TextInputDialog(
        string title,
        string message,
        string confirmButtonText = "OK",
        string initialValue = "")
    {
        InitializeComponent();

        Title = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmButtonText;
        CancelButton.Content = "Cancel";
        InputBox.Text = initialValue;

        Opened += (_, _) =>
        {
            InputBox.Focus();
            InputBox.SelectAll();
            UpdateState();
        };

        InputBox.TextChanged += (_, _) => UpdateState();
        InputBox.KeyDown += InputBox_KeyDown;
    }

    private void UpdateState()
    {
        ConfirmButton.IsEnabled = !string.IsNullOrWhiteSpace(InputBox.Text);
    }

    private void InputBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ConfirmButton.IsEnabled)
        {
            e.Handled = true;
            ConfirmButton_Click(sender, new RoutedEventArgs());
        }
    }

    private void ConfirmButton_Click(object? sender, RoutedEventArgs e)
    {
        var value = InputBox.Text?.Trim();
        Close(string.IsNullOrWhiteSpace(value) ? null : value);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close(null);
}
