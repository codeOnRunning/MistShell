using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using MistShell.Models;
using MistShell.Services;

namespace MistShell.Views;

public partial class ConnectionDialog : Window
{
    private readonly ConnectionProfile? _editing;
    private readonly AppSettings _settings;
    private string? _selectedKeyId;
    private static readonly string[] Colors = ["#3D7C73", "#8A6245", "#4C6382", "#9A5B5B", "#6B5B7A", "#5F7B4C"];

    public ConnectionDialog(ConnectionProfile? editing = null, AppSettings? settings = null)
    {
        InitializeComponent();
        _editing = editing;
        _settings = settings ?? new SettingsStore().Load();
        _settings.PrivateKeys ??= new List<SavedPrivateKey>();
        if (editing is null)
        {
            TitleText.Text = "新建连接";
            GroupBox.Text = "默认";
            PortBox.Text = "22";
            UsernameBox.Text = "root";
            NameBox.Focus();
        }
        else
        {
            TitleText.Text = "编辑连接";
            NameBox.Text = editing.Name;
            GroupBox.Text = editing.Group;
            HostBox.Text = editing.Host;
            PortBox.Text = editing.Port.ToString();
            UsernameBox.Text = editing.Username;
            KeyPathBox.Text = editing.PrivateKeyPath;
            NotesBox.Text = editing.Notes;
            if (editing.AuthMethod == SshAuthMethod.PrivateKey)
            {
                KeyAuthRadio.IsChecked = true;
                PasswordAuthRadio.IsChecked = false;
            }
            PasswordHint.Text = string.IsNullOrEmpty(editing.EncryptedPassword)
                ? "尚未保存密码。"
                : "留空则保留已保存的密码。";
            PassphraseHint.Text = string.IsNullOrEmpty(editing.EncryptedPassphrase)
                ? "尚未保存私钥口令。"
                : "留空则保留已保存的私钥口令。";
        }
        RebuildSavedKeyChoices(editing?.PrivateKeyId);
        UpdateAuthPanels();
    }

    public ConnectionProfile? Result { get; private set; }

    private void Auth_Changed(object sender, RoutedEventArgs e) => UpdateAuthPanels();

    private void UpdateAuthPanels()
    {
        if (PasswordPanel is null || KeyPanel is null) return;
        var usePassword = PasswordAuthRadio.IsChecked == true;
        PasswordPanel.Visibility = usePassword ? Visibility.Visible : Visibility.Collapsed;
        KeyPanel.Visibility = usePassword ? Visibility.Collapsed : Visibility.Visible;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void RebuildSavedKeyChoices(string? selectedId)
    {
        SavedKeyPanel.Children.Clear();
        _selectedKeyId = null;

        var fileRadio = new RadioButton
        {
            Content = "使用文件路径",
            Style = (Style)FindResource("ChoiceRadio"),
            GroupName = "SavedPrivateKey",
            Tag = "",
            MinWidth = 112,
            Margin = new Thickness(0, 0, 7, 7),
            IsChecked = string.IsNullOrWhiteSpace(selectedId)
        };
        fileRadio.Checked += (_, _) => SelectSavedKey(null);
        SavedKeyPanel.Children.Add(fileRadio);

        foreach (var key in _settings.PrivateKeys)
        {
            var radio = new RadioButton
            {
                Content = key.Name,
                Style = (Style)FindResource("ChoiceRadio"),
                GroupName = "SavedPrivateKey",
                Tag = key.Id,
                MinWidth = 96,
                Margin = new Thickness(0, 0, 7, 7),
                IsChecked = string.Equals(key.Id, selectedId, StringComparison.Ordinal)
            };
            radio.Checked += (_, _) => SelectSavedKey(key.Id);
            SavedKeyPanel.Children.Add(radio);
        }

        if (!SavedKeyPanel.Children.OfType<RadioButton>().Any(r => r.IsChecked == true))
            fileRadio.IsChecked = true;
        if (_settings.PrivateKeys.Count == 0)
        {
            SavedKeyPanel.Children.Add(new TextBlock
            {
                Text = "还没有保存私钥，点“管理私钥…”添加。",
                Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush"),
                FontSize = 10.5,
                Margin = new Thickness(2, 5, 0, 0)
            });
        }
    }

    private void SelectSavedKey(string? keyId)
    {
        _selectedKeyId = keyId;
        if (!string.IsNullOrWhiteSpace(keyId))
        {
            KeyPathBox.Text = "";
            KeyPathBox.IsEnabled = false;
        }
        else
        {
            KeyPathBox.IsEnabled = true;
        }
    }

    private void ManageKeys_Click(object sender, RoutedEventArgs e)
    {
        var manager = new PrivateKeyManagerWindow(_settings) { Owner = this };
        manager.ShowDialog();
        RebuildSavedKeyChoices(_selectedKeyId);
    }

    private void BrowseKey_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 SSH 私钥",
            Filter = "私钥文件|id_*;*.pem;*.ppk;*.key|所有文件|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            KeyPathBox.Text = dialog.FileName;
            SelectSavedKey(null);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorPanel.Visibility = Visibility.Collapsed;
        var name = NameBox.Text.Trim();
        var host = HostBox.Text.Trim();
        var group = GroupBox.Text.Trim();
        var username = UsernameBox.Text.Trim();
        if (name.Length == 0) { ShowError("请填写连接名称。"); return; }
        if (host.Length == 0) { ShowError("请填写主机地址。"); return; }
        if (username.Length == 0) { ShowError("请填写用户名。"); return; }
        if (!int.TryParse(PortBox.Text.Trim(), out var port) || port is < 1 or > 65535)
        {
            ShowError("端口必须是 1–65535 之间的数字。");
            return;
        }

        var usePassword = PasswordAuthRadio.IsChecked == true;
        var useSavedKey = !usePassword && !string.IsNullOrWhiteSpace(_selectedKeyId) &&
                          _settings.PrivateKeys.Any(key => key.Id == _selectedKeyId);
        if (!usePassword && !useSavedKey && string.IsNullOrWhiteSpace(KeyPathBox.Text))
        {
            ShowError("请选择已保存私钥或私钥文件。");
            return;
        }
        if (!usePassword && !useSavedKey && !File.Exists(KeyPathBox.Text.Trim()))
        {
            ShowError("私钥文件不存在。");
            return;
        }
        if (usePassword && string.IsNullOrEmpty(PasswordBox.Password) && string.IsNullOrEmpty(_editing?.EncryptedPassword))
        {
            ShowError("请填写登录密码，或切换到私钥认证。");
            return;
        }

        var profile = _editing?.Clone() ?? new ConnectionProfile();
        profile.Name = name;
        profile.Group = group.Length == 0 ? "默认" : group;
        profile.Host = host;
        profile.Port = port;
        profile.Username = username;
        profile.AuthMethod = usePassword ? SshAuthMethod.Password : SshAuthMethod.PrivateKey;
        profile.Notes = NotesBox.Text.Trim();
        profile.UpdatedUtc = DateTimeOffset.UtcNow;

        if (profile.Name == "新连接" || string.IsNullOrWhiteSpace(profile.Color))
            profile.Color = Colors[Math.Abs(profile.Id.GetHashCode()) % Colors.Length];

        if (usePassword)
        {
            if (!string.IsNullOrEmpty(PasswordBox.Password))
                profile.EncryptedPassword = SecretProtector.Protect(PasswordBox.Password);
            profile.EncryptedPassphrase = "";
            profile.PrivateKeyPath = "";
            profile.PrivateKeyId = "";
        }
        else
        {
            var newPath = useSavedKey ? "" : KeyPathBox.Text.Trim();
            var newKeyId = useSavedKey ? _selectedKeyId! : "";
            var keyChanged = !string.Equals(profile.PrivateKeyPath, newPath, StringComparison.OrdinalIgnoreCase) ||
                             !string.Equals(profile.PrivateKeyId, newKeyId, StringComparison.Ordinal);
            profile.PrivateKeyPath = newPath;
            profile.PrivateKeyId = newKeyId;
            profile.EncryptedPassword = "";
            if (!string.IsNullOrEmpty(PassphraseBox.Password))
                profile.EncryptedPassphrase = SecretProtector.Protect(PassphraseBox.Password);
            else if (keyChanged)
                profile.EncryptedPassphrase = "";
        }

        Result = profile;
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
