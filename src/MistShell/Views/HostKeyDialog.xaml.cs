using System.Windows;
using System.Windows.Input;
using MistShell.Models;

namespace MistShell.Views;

public partial class HostKeyDialog : Window
{
    public HostKeyDialog(HostKeyInfo info)
    {
        InitializeComponent();
        HostText.Text = $"{info.Host}:{info.Port}";
        KeyTypeText.Text = info.KeyType;
        FingerprintText.Text = info.Fingerprint;
        if (info.IsChanged)
        {
            WarningPanel.Visibility = Visibility.Visible;
            IntroText.Text = "服务器返回的指纹与上次保存的不一致。请确认这是预期变化后再继续。";
        }
    }

    public HostKeyDecision Decision { get; private set; } = HostKeyDecision.Reject;

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void TrustOnce_Click(object sender, RoutedEventArgs e)
    {
        Decision = HostKeyDecision.TrustOnce;
        DialogResult = true;
    }

    private void TrustSave_Click(object sender, RoutedEventArgs e)
    {
        Decision = HostKeyDecision.TrustAndSave;
        DialogResult = true;
    }

    private void Reject_Click(object sender, RoutedEventArgs e)
    {
        Decision = HostKeyDecision.Reject;
        DialogResult = false;
        Close();
    }
}
