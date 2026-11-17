using System.Windows;
using System.Windows.Input;

namespace MistShell.Views;

public partial class PromptDialog : Window
{
    public PromptDialog(string title, string label, string initialValue = "", string hint = "")
    {
        InitializeComponent();
        TitleText.Text = title;
        LabelText.Text = label;
        ValueBox.Text = initialValue;
        HintText.Text = hint;
        Loaded += (_, _) => { ValueBox.Focus(); ValueBox.SelectAll(); };
    }

    public string Value => ValueBox.Text.Trim();

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ValueBox.Text)) return;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
