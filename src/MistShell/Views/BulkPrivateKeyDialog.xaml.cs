using System.Windows;
using System.Windows.Input;
using MistShell.Models;
using MistShell.Services;

namespace MistShell.Views;

public partial class BulkPrivateKeyDialog : Window
{
    private readonly AppSettings _settings;

    public BulkPrivateKeyDialog(AppSettings settings, int connectionCount)
    {
        InitializeComponent();
        _settings = settings;
        _settings.PrivateKeys ??= new List<SavedPrivateKey>();
        IntroText.Text = $"将选中的 {connectionCount} 条连接切换为私钥认证。";
        RefreshKeys();
    }

    public string? SelectedKeyId { get; private set; }
    public string Passphrase => PassphraseBox.Password;

    private void RefreshKeys()
    {
        KeyList.ItemsSource = null;
        KeyList.ItemsSource = _settings.PrivateKeys;
        if (_settings.PrivateKeys.Count > 0) KeyList.SelectedIndex = 0;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void ManageKeys_Click(object sender, RoutedEventArgs e)
    {
        var manager = new PrivateKeyManagerWindow(_settings) { Owner = this };
        manager.ShowDialog();
        RefreshKeys();
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (KeyList.SelectedItem is not SavedPrivateKey key)
        {
            ErrorText.Text = "请先选择一条已保存私钥。没有私钥时，点“管理私钥…”添加。";
            ErrorPanel.Visibility = Visibility.Visible;
            return;
        }
        SelectedKeyId = key.Id;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
