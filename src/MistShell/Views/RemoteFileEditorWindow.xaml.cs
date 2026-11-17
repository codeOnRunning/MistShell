using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using MistShell.Models;
using MistShell.ViewModels;

namespace MistShell.Views;

public partial class RemoteFileEditorWindow : Window
{
    private readonly SftpViewModel _sftp;
    private readonly SftpFileItem _file;
    private bool _loading = true;
    private bool _dirty;
    private bool _saving;
    private string _newline = "\n";

    public RemoteFileEditorWindow(SftpViewModel sftp, SftpFileItem file)
    {
        InitializeComponent();
        _sftp = sftp;
        _file = file;
        TitleText.Text = "编辑：" + file.Name;
        PathText.Text = file.FullPath;
        StatusText.Text = "正在读取远程文件…";
        Loaded += Window_Loaded;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = await _sftp.ReadTextFileAsync(_file);
            _loading = true;
            _newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            EditorBox.Text = text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
            _loading = false;
            _dirty = false;
            StatusText.Text = $"{_file.Name} · 已读取，Ctrl+S 保存";
            EditorBox.Focus();
        }
        catch (Exception ex)
        {
            _loading = false;
            StatusText.Text = "无法打开文件";
            MessageBox.Show(this, ex.Message, "无法打开文件", MessageBoxButton.OK, MessageBoxImage.Warning);
            Close();
        }
    }

    private void EditorBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_loading) return;
        _dirty = true;
        StatusText.Text = "未保存 · Ctrl+S 保存";
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_saving) return;
        _saving = true;
        StatusText.Text = "正在保存…";
        try
        {
            var normalized = EditorBox.Text.Replace("\r\n", "\n").Replace("\n", _newline);
            await _sftp.WriteTextFileAsync(_file, normalized);
            _dirty = false;
            StatusText.Text = $"{_file.Name} · 已保存";
        }
        catch (Exception ex)
        {
            StatusText.Text = "保存失败";
            MessageBox.Show(this, ex.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _saving = false;
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            Save_Click(sender, e);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_dirty)
        {
            var result = MessageBox.Show(this, "文件还没有保存，确定关闭吗？", "MistShell",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) e.Cancel = true;
        }
        base.OnClosing(e);
    }
}
