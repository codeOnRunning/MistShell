using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using MistShell.Models;
using MistShell.Services;

namespace MistShell.Views;

public partial class PrivateKeyManagerWindow : Window
{
    private readonly AppSettings _settings;
    private readonly ObservableCollection<SavedPrivateKey> _keys;
    private SavedPrivateKey? _selected;

    public PrivateKeyManagerWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        _settings.PrivateKeys ??= new List<SavedPrivateKey>();
        _keys = new ObservableCollection<SavedPrivateKey>(_settings.PrivateKeys);
        KeyList.ItemsSource = _keys;
        if (_keys.Count > 0) KeyList.SelectedIndex = 0;
        else StatusText.Text = "还没有保存私钥。填写名称和内容，然后点“保存私钥”。";
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void KeyList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selected = KeyList.SelectedItem as SavedPrivateKey;
        if (_selected is null) return;
        NameBox.Text = _selected.Name;
        try { KeyBox.Text = SecretProtector.Unprotect(_selected.EncryptedContent); }
        catch { KeyBox.Text = ""; StatusText.Text = "这条私钥无法解密（可能来自其他 Windows 用户）。"; }
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        KeyList.SelectedItem = null;
        _selected = null;
        NameBox.Text = "";
        KeyBox.Text = "";
        StatusText.Text = "";
        NameBox.Focus();
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 SSH 私钥",
            Filter = "私钥文件|id_*;*.pem;*.ppk;*.key|所有文件|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            KeyBox.Text = File.ReadAllText(dialog.FileName);
            if (string.IsNullOrWhiteSpace(NameBox.Text))
                NameBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
            StatusText.Text = "已读取文件，点“保存私钥”后才会保存。";
        }
        catch (Exception ex)
        {
            StatusText.Text = "读取失败：" + ex.Message;
        }
    }

    private void Paste_Click(object sender, RoutedEventArgs e)
    {
        if (!Clipboard.ContainsText())
        {
            StatusText.Text = "剪贴板里没有文本。";
            return;
        }
        KeyBox.Text = Clipboard.GetText();
        StatusText.Text = "已粘贴，点“保存私钥”后才会保存。";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var content = KeyBox.Text.Trim();
        if (name.Length == 0) { StatusText.Text = "请填写私钥名称。"; return; }
        if (content.Length == 0) { StatusText.Text = "请粘贴或导入私钥内容。"; return; }

        _selected ??= _keys.FirstOrDefault(key =>
            string.Equals(key.Name, name, StringComparison.OrdinalIgnoreCase));
        if (_selected is null)
        {
            _selected = new SavedPrivateKey { Name = name };
            _keys.Add(_selected);
        }
        _selected.Name = name;
        _selected.EncryptedContent = SecretProtector.Protect(content);
        _selected.UpdatedUtc = DateTimeOffset.UtcNow;
        _settings.PrivateKeys = _keys.ToList();
        new SettingsStore().Save(_settings);
        KeyList.Items.Refresh();
        KeyList.SelectedItem = _selected;
        StatusText.Text = "已保存。连接编辑窗口里可以选择这条私钥。";
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) { StatusText.Text = "请先选择一条私钥。"; return; }
        if (MessageBox.Show(this, $"删除私钥“{_selected.Name}”？", "MistShell",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _keys.Remove(_selected);
        _settings.PrivateKeys = _keys.ToList();
        new SettingsStore().Save(_settings);
        New_Click(sender, e);
        StatusText.Text = "已删除。";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
