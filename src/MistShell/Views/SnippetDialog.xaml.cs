using System.Windows;
using System.Windows.Input;

namespace MistShell.Views;

public partial class SnippetDialog : Window
{
    public SnippetDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => NameBox.Focus();
    }

    public string SnippetName => NameBox.Text.Trim();
    public string SnippetCommand => CommandBox.Text.Trim();

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (SnippetName.Length == 0 || SnippetCommand.Length == 0) return;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
