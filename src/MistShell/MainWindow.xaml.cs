using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using MistShell.Models;
using MistShell.Services;
using MistShell.ViewModels;
using MistShell.Views;

namespace MistShell;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly HashSet<string> _closingSessions = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _fontSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private ConnectionProfile? _contextProfile;
    private SessionViewModel? _lastActiveSession;
    private bool _disposed;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        _viewModel.HostKeyVerifier = VerifyHostKey;
        _viewModel.ActiveSessionChanged += OnActiveSessionChanged;
        _viewModel.DockTabChanged += ignored => { _ = UpdateMonitorVisibilityAsync(); };
        _viewModel.ThemeChanged += OnThemeChanged;
        _viewModel.DataReceived += OnSessionDataReceived;
        _viewModel.SessionDisconnected += OnSessionDisconnected;

        TerminalView.InputReceived += OnTerminalInput;
        TerminalView.ResizeRequested += OnTerminalResize;
        TerminalView.TitleChanged += OnTerminalTitle;
        TerminalView.CwdChanged += OnTerminalCwdChanged;
        TerminalView.CommandLineSubmitted += OnTerminalCommandLine;
        TerminalView.FontSizeChanged += OnTerminalFontSizeChanged;
        _fontSaveTimer.Tick += (_, _) =>
        {
            _fontSaveTimer.Stop();
            _viewModel.SaveSettings();
        };
        ConnectionList.ContextMenuOpening += ConnectionList_ContextMenuOpening;

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;
        SourceInitialized += MainWindow_SourceInitialized;
        StateChanged += (_, _) => UpdateMaximizeButton();

        TerminalView.SetScrollback(_viewModel.Settings.EffectiveScrollback);
        TerminalView.SetFontFamily(_viewModel.Settings.TerminalFontFamily);
        TerminalView.SetLineHeight(_viewModel.Settings.TerminalLineHeight);
        TerminalView.SetFontSize(_viewModel.Settings.TerminalFontSize);
        TerminalView.SetTheme(ThemeManager.Resolve(_viewModel.Settings.Theme),
            _viewModel.Settings.TerminalForegroundColor);
        UpdateThemeButton();
        RestoreWindowBounds();
        SidebarColumn.Width = new GridLength(Math.Clamp(_viewModel.Settings.SidebarWidth, 225, 460));
        ApplyDockVisibility();
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateMaximizeButton();
        if (_viewModel.Settings.WindowMaximized) WindowState = WindowState.Maximized;
    }

    private void RestoreWindowBounds()
    {
        var settings = _viewModel.Settings;
        Width = Math.Max(MinWidth, settings.WindowWidth);
        Height = Math.Max(MinHeight, settings.WindowHeight);
        if (settings.WindowLeft is double savedLeft && settings.WindowTop is double savedTop &&
            savedLeft >= SystemParameters.VirtualScreenLeft - 50 &&
            savedTop >= SystemParameters.VirtualScreenTop - 50 &&
            savedLeft < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100 &&
            savedTop < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = savedLeft;
            Top = savedTop;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private void OnTerminalInput(string sessionId, byte[] data)
    {
        var session = _viewModel.Sessions.FirstOrDefault(s => s.Id == sessionId);
        if (session is null) return;
        _ = session.WriteAsync(data);
    }

    private void OnTerminalCommandLine(string sessionId, string line)
    {
        var session = _viewModel.Sessions.FirstOrDefault(s => s.Id == sessionId);
        if (session is not null) ProcessTerminalCommand(session, line);
    }

    private void ProcessTerminalCommand(SessionViewModel session, string command)
    {
        var currentDirectory = session.TerminalDirectory is { Length: > 0 } tracked
            ? tracked
            : session.Sftp.CurrentPath;
        if (!_viewModel.Settings.FollowTerminalDirectory ||
            !TryResolveTerminalDirectory(command, currentDirectory,
                session.Profile.Username, out var target))
            return;
        session.TerminalDirectory = target;
        _ = FollowSftpDirectoryAsync(session, target);
    }

    private async Task FollowSftpDirectoryAsync(SessionViewModel session, string path)
    {
        if (!path.StartsWith('/')) return;
        session.TerminalDirectory = path;
        if (string.Equals(session.Sftp.CurrentPath, path, StringComparison.Ordinal))
            return;
        // Keep the path for when the file panel is opened later; only open a
        // new SFTP connection now if the Files panel is actually visible.
        if (!session.Sftp.IsConnected &&
            !(_viewModel.IsDockOpen && _viewModel.DockIndex == 0))
            return;
        try
        {
            await session.Sftp.LoadAsync(path);
        }
        catch (Exception ex)
        {
            AppLog.Write("Following terminal directory failed.", ex);
        }
    }

    private void OnTerminalCwdChanged(string sessionId, string path)
    {
        if (!_viewModel.Settings.FollowTerminalDirectory) return;
        var session = _viewModel.Sessions.FirstOrDefault(s => s.Id == sessionId);
        if (session is not null) _ = FollowSftpDirectoryAsync(session, path);
    }

    private static bool TryResolveTerminalDirectory(string command, string currentPath,
        string username, out string target)
    {
        target = "";
        var text = command.Trim();
        if (text.Length == 0) return false;
        var space = text.IndexOf(' ');
        var verb = space < 0 ? text : text[..space];
        if (!string.Equals(verb, "cd", StringComparison.Ordinal) &&
            !string.Equals(verb, "pushd", StringComparison.Ordinal))
            return false;

        var argument = space < 0 ? "" : text[(space + 1)..].Trim();
        var home = string.Equals(username, "root", StringComparison.Ordinal) ? "/root" : "/home/" + username;
        if (argument.Length == 0)
        {
            if (!string.Equals(verb, "cd", StringComparison.Ordinal)) return false;
            target = home;
            return true;
        }
        if (argument == "-") return false;
        if (argument.StartsWith("-- ", StringComparison.Ordinal))
            argument = argument[3..].Trim();
        else if (argument == "--")
            argument = home;
        if (argument[0] is '"' or '\'')
        {
            var quote = argument[0];
            var end = argument.IndexOf(quote, 1);
            argument = end > 0 ? argument[1..end] : argument[1..];
        }
        else
        {
            var separator = argument.IndexOfAny([' ', ';', '&', '|']);
            if (separator > 0) argument = argument[..separator];
        }
        if (argument.Length == 0) return false;

        if (argument == "~") argument = home;
        else if (argument.StartsWith("~/", StringComparison.Ordinal)) argument = home + argument[1..];
        else if (argument is "$HOME" or "${HOME}") argument = home;

        if (!argument.StartsWith('/'))
        {
            if (string.IsNullOrWhiteSpace(currentPath)) return false;
            argument = SftpFileItem.CombineUnix(currentPath, argument);
        }
        target = NormalizeUnixPath(argument);
        return target.Length > 0;
    }

    private static string NormalizeUnixPath(string path)
    {
        var parts = new List<string>();
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") continue;
            if (part == "..")
            {
                if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);
                continue;
            }
            parts.Add(part);
        }
        return "/" + string.Join('/', parts);
    }

    private void OnTerminalResize(string sessionId, int cols, int rows)
    {
        var session = _viewModel.Sessions.FirstOrDefault(s => s.Id == sessionId);
        session?.Resize(cols, rows);
    }

    private void OnTerminalTitle(string sessionId, string title)
    {
        // OSC title changes are intentionally not applied to the tab, so the
        // user's connection name stays stable. Kept for future status display.
    }

    private void OnTerminalFontSizeChanged(int size)
    {
        _viewModel.Settings.TerminalFontSize = Math.Clamp(size, 10, 26);
        _fontSaveTimer.Stop();
        _fontSaveTimer.Start();
    }

    private void OnSessionDataReceived(SessionViewModel session, byte[] data)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
        {
            if (!_closingSessions.Contains(session.Id)) TerminalView.Write(session.Id, data);
        });
    }

    private void OnSessionDisconnected(SessionViewModel session, string reason)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_closingSessions.Contains(session.Id)) return;
            TerminalView.Write(session.Id,
                Encoding.UTF8.GetBytes($"\r\n\x1b[38;5;179m[MistShell] {reason}\x1b[0m\r\n"));
            if (_viewModel.SelectedSession == session) _viewModel.SetStatus(reason);
        });
    }

    private void OnActiveSessionChanged(SessionViewModel? session)
    {
        if (_lastActiveSession is not null && _lastActiveSession != session)
            _lastActiveSession.Monitor.Stop();
        _lastActiveSession = session;

        if (session is null)
        {
            _viewModel.SetStatus("就绪");
            return;
        }

        TerminalView.ActivateSession(session.Id);
        _viewModel.SetStatus(session.IsConnected ? $"已连接 {session.Title}" : session.StatusDetail);
        _ = UpdateMonitorVisibilityAsync();
        if (_viewModel.IsDockOpen && _viewModel.DockIndex == 0 && session.IsConnected && !session.Sftp.IsConnected)
            _ = LoadSftpAsync(session);
    }

    private async Task UpdateMonitorVisibilityAsync()
    {
        var session = _viewModel.SelectedSession;
        if (session is null) return;
        var shouldRun = _viewModel.IsDockOpen && _viewModel.DockIndex == 1 && session.IsConnected;
        await session.SetMonitorVisibleAsync(shouldRun);
    }

    private void ApplyDockVisibility()
    {
        var open = _viewModel.IsDockOpen;
        DockColumn.Width = open ? new GridLength(Math.Max(300, _viewModel.Settings.DockWidth)) : new GridLength(0);
        DockColumn.MinWidth = open ? 300 : 0;
        DockSplitterColumn.Width = open ? new GridLength(5) : new GridLength(0);
        DockSplitter.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        DockPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnThemeChanged()
    {
        var palette = ThemeManager.Current;
        TerminalView.SetTheme(palette, _viewModel.Settings.TerminalForegroundColor);
        UpdateThemeButton();
    }

    private void UpdateThemeButton()
    {
        ThemeButton.Content = ThemeManager.Current.IsDark ? "\uE708" : "\uE706";
        ThemeButton.ToolTip = $"当前主题：{ThemeManager.Current.DisplayName}，点击切换下一套主题";
    }

    private HostKeyDecision VerifyHostKey(HostKeyInfo info)
    {
        if (!Dispatcher.CheckAccess())
            return Dispatcher.Invoke(() => VerifyHostKey(info));
        if (_disposed) return HostKeyDecision.Reject;

        var dialog = new HostKeyDialog(info) { Owner = this };
        var accepted = dialog.ShowDialog() == true;
        var decision = accepted ? dialog.Decision : HostKeyDecision.Reject;
        if (decision == HostKeyDecision.TrustAndSave)
        {
            _viewModel.SaveKnownHostKey(info);
            _viewModel.SetStatus("已保存服务器主机指纹");
        }
        return decision;
    }

    private async Task OpenConnectionAsync(ConnectionProfile profile)
    {
        if (_viewModel.IsBusy)
        {
            _viewModel.SetStatus("正在处理另一个连接，请稍候…");
            return;
        }

        if ((profile.AuthMethod == SshAuthMethod.Password && string.IsNullOrEmpty(profile.EncryptedPassword)) ||
            (profile.AuthMethod == SshAuthMethod.PrivateKey &&
             string.IsNullOrWhiteSpace(profile.PrivateKeyPath) &&
             string.IsNullOrWhiteSpace(profile.PrivateKeyId)))
        {
            var credentialDialog = new ConnectionDialog(profile, _viewModel.Settings) { Owner = this };
            if (credentialDialog.ShowDialog() != true || credentialDialog.Result is null) return;
            _viewModel.AddConnection(credentialDialog.Result);
            profile = credentialDialog.Result;
        }

        _viewModel.IsBusy = true;
        var session = _viewModel.CreateSession(profile);
        try
        {
            await TerminalView.CreateSessionAsync(session.Id);
            await session.ConnectAsync(120, 32);
            await _viewModel.SaveConnectionsAsync();
            _viewModel.SetStatus($"已连接 {profile.Name}");
            if (_viewModel.IsDockOpen && _viewModel.DockIndex == 1)
                await session.SetMonitorVisibleAsync(true);
            TerminalView.FocusTerminal();
        }
        catch (Exception ex)
        {
            AppLog.Write("Open connection failed.", ex);
            TerminalView.Write(session.Id,
                Encoding.UTF8.GetBytes($"\r\n\x1b[38;5;203m[MistShell] 连接失败：{ex.Message}\x1b[0m\r\n"));
            _viewModel.SetStatus("连接失败");
            MessageBox.Show(this, ex.Message, "连接失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _viewModel.IsBusy = false;
        }
    }

    private async void ConnectionList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.SelectedConnection is { } profile)
            await OpenConnectionAsync(profile);
    }

    private async void ConnectConnection_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.Tag is ConnectionProfile profile)
            await OpenConnectionAsync(profile);
    }

    private async void ConnectionList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _viewModel.SelectedConnection is not { } profile) return;
        e.Handled = true;
        await OpenConnectionAsync(profile);
    }

    private void ConnectionList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (_contextProfile is null)
        {
            e.Handled = true;
            return;
        }
        BulkKeyMenuItem.IsEnabled = ConnectionList.SelectedItems.Count > 1;
    }

    private void ConnectionList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item?.DataContext is ConnectionProfile profile)
        {
            if (!ConnectionList.SelectedItems.Contains(profile))
                ConnectionList.SelectedItem = profile;
            _contextProfile = profile;
        }
        else
        {
            _contextProfile = null;
        }
    }

    private async void ContextConnect_Click(object sender, RoutedEventArgs e)
    {
        if (_contextProfile is not null) await OpenConnectionAsync(_contextProfile);
    }

    private void ContextEdit_Click(object sender, RoutedEventArgs e)
    {
        if (_contextProfile is not null) EditConnection(_contextProfile);
    }

    private async void ContextBulkPrivateKey_Click(object sender, RoutedEventArgs e)
    {
        var profiles = ConnectionList.SelectedItems.OfType<ConnectionProfile>().ToList();
        if (profiles.Count == 0) return;

        var dialog = new BulkPrivateKeyDialog(_viewModel.Settings, profiles.Count) { Owner = this };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.SelectedKeyId)) return;

        var passphrase = string.IsNullOrEmpty(dialog.Passphrase)
            ? ""
            : SecretProtector.Protect(dialog.Passphrase);
        foreach (var profile in profiles)
        {
            profile.AuthMethod = SshAuthMethod.PrivateKey;
            profile.PrivateKeyId = dialog.SelectedKeyId;
            profile.PrivateKeyPath = "";
            profile.EncryptedPassword = "";
            profile.EncryptedPassphrase = passphrase;
            profile.UpdatedUtc = DateTimeOffset.UtcNow;
        }
        await _viewModel.SaveConnectionsAsync();
        _viewModel.ConnectionsView.Refresh();
        _viewModel.SetStatus($"已为 {profiles.Count} 条连接设置私钥");
    }

    private void ContextDuplicate_Click(object sender, RoutedEventArgs e)
    {
        if (_contextProfile is null) return;
        var clone = _contextProfile.Clone();
        clone.Id = Guid.NewGuid().ToString("N");
        clone.Name = _contextProfile.Name + " 副本";
        clone.CreatedUtc = DateTimeOffset.UtcNow;
        clone.UpdatedUtc = DateTimeOffset.UtcNow;
        _viewModel.AddConnection(clone);
    }

    private async void ContextDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_contextProfile is not null) await DeleteConnectionAsync(_contextProfile);
    }

    private async void NewConnection_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ConnectionDialog(settings: _viewModel.Settings) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null) return;
        var profile = dialog.Result;
        _viewModel.AddConnection(profile);
        await OpenConnectionAsync(profile);
    }

    private void EditConnection(ConnectionProfile profile)
    {
        var dialog = new ConnectionDialog(profile, _viewModel.Settings) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is not null)
            _viewModel.AddConnection(dialog.Result);
    }

    private async Task DeleteConnectionAsync(ConnectionProfile profile)
    {
        var active = _viewModel.Sessions.FirstOrDefault(s => s.Profile.Id == profile.Id);
        var message = active is null
            ? $"确定删除连接“{profile.Name}”吗？"
            : $"“{profile.Name}”当前有打开的会话，删除连接会同时关闭会话。继续吗？";
        if (MessageBox.Show(this, message, "删除连接", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        if (active is not null) CloseSession(active);
        _viewModel.RemoveConnection(profile);
        await _viewModel.SaveConnectionsAsync();
    }

    private async void SharePackage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "保存 MistShell 安装包",
            FileName = $"MistShell-{_viewModel.AppVersion.TrimStart('v')}-Setup.exe",
            DefaultExt = ".exe",
            Filter = "安装包 (*.exe)|*.exe|便携压缩包 (*.zip)|*.zip"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            _viewModel.SetStatus("正在生成安装包…");
            var result = await SharePackageService.CreateAsync(
                dialog.FileName,
                new Progress<string>(message => _viewModel.SetStatus(message)));
            _viewModel.SetStatus("安装包已生成");
            MessageBox.Show(this,
                $"生成完成：\n\n{result.Path}\n\n{result.Detail}",
                result.IsInstaller ? "安装包已生成" : "便携包已生成",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppLog.Write("Share package failed.", ex);
            _viewModel.SetStatus("安装包生成失败");
            MessageBox.Show(this, "生成失败：\n\n" + ex.Message, "分享",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ImportFinalShell_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择 FinalShell 的 servers / conn 目录",
            Multiselect = false
        };
        var desktopServers = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "servers");
        if (Directory.Exists(desktopServers)) dialog.InitialDirectory = desktopServers;
        else if (Directory.Exists(@"G:\softwares\finalshell\conn")) dialog.InitialDirectory = @"G:\softwares\finalshell\conn";
        if (dialog.ShowDialog(this) != true) return;

        var result = FinalShellImporter.Import(dialog.FolderName, _viewModel.Connections);
        foreach (var profile in result.Profiles) _viewModel.AddConnection(profile);
        await _viewModel.SaveConnectionsAsync();
        var groups = result.Profiles.Select(p => p.Group).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        _viewModel.SetStatus($"已导入 {result.Profiles.Count} 个连接，{groups} 个分组");
        var detail = $"读取 {result.TotalFiles} 个连接文件，导入 {result.Profiles.Count} 个，{groups} 个分组。";
        if (result.SkippedDuplicates > 0) detail += $"\n跳过重复：{result.SkippedDuplicates} 个。";
        if (result.FailedFiles > 0) detail += $"\n无法解析：{result.FailedFiles} 个。";
        detail += "\n\n目录层级会保留为分组。FinalShell 的密码无法迁移，首次连接时会提示补全密码或私钥。";
        MessageBox.Show(this, detail, "导入完成", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not SessionViewModel session) return;
        if (session.IsConnected &&
            MessageBox.Show(this, $"关闭“{session.Title}”的会话？", "关闭会话", MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        CloseSession(session);
        await Task.CompletedTask;
    }

    private void CloseSession(SessionViewModel session)
    {
        _closingSessions.Add(session.Id);
        TerminalView.CloseSession(session.Id);
        _viewModel.RemoveSession(session);
        if (_viewModel.Sessions.Count == 0) _viewModel.SetStatus("就绪");
    }

    private async void Reconnect_Click(object sender, RoutedEventArgs e)
    {
        var session = _viewModel.SelectedSession;
        if (session is null || session.IsBusy) return;
        var (password, passphrase) = SettingsStore.ResolveCredentials(session.Profile);
        if (session.Profile.AuthMethod == SshAuthMethod.Password && string.IsNullOrEmpty(password))
        {
            var dialog = new ConnectionDialog(session.Profile, _viewModel.Settings) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Result is null) return;
            _viewModel.AddConnection(dialog.Result);
            (password, passphrase) = SettingsStore.ResolveCredentials(dialog.Result);
        }

        try
        {
            TerminalView.ClearSession(session.Id);
            await session.ReconnectAsync(120, 32, password, passphrase);
            _viewModel.SetStatus($"已连接 {session.Title}");
            if (_viewModel.IsDockOpen && _viewModel.DockIndex == 1)
                await session.SetMonitorVisibleAsync(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "重连失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ClearTerminal_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedSession is { } session) TerminalView.ClearSession(session.Id);
        TerminalView.FocusTerminal();
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e) => _viewModel.ToggleLightDark();

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var originalTheme = _viewModel.Settings.Theme;
        var dialog = new SettingsWindow(_viewModel.Settings, _viewModel.SetTheme) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            // Cancel/X should restore the theme that was active before live preview.
            _viewModel.SetTheme(originalTheme);
            return;
        }
        _viewModel.Settings.TerminalFontFamily = dialog.SelectedFontFamily;
        _viewModel.Settings.TerminalFontSize = dialog.SelectedFontSize;
        _viewModel.Settings.TerminalLineHeight = dialog.SelectedLineHeight;
        _viewModel.Settings.ScrollbackLines = dialog.SelectedScrollback;
        _viewModel.Settings.LowMemoryMode = dialog.SelectedLowMemory;
        _viewModel.Settings.FollowTerminalDirectory = dialog.SelectedFollowDirectory;
        _viewModel.Settings.TerminalForegroundColor = dialog.SelectedForeground;
        _viewModel.SetTheme(dialog.SelectedTheme);
        TerminalView.SetTheme(ThemeManager.Current, dialog.SelectedForeground);
        TerminalView.SetFontFamily(dialog.SelectedFontFamily);
        TerminalView.SetFontSize(dialog.SelectedFontSize);
        TerminalView.SetLineHeight(dialog.SelectedLineHeight);
        TerminalView.SetScrollback(_viewModel.Settings.EffectiveScrollback);
        _viewModel.SaveSettings();
    }

    private void OpenFiles_Click(object sender, RoutedEventArgs e)
    {
        OpenDockTab(0);
        if (_viewModel.SelectedSession is { } session && !session.Sftp.IsConnected)
            _ = LoadSftpAsync(session);
    }

    private void OpenMonitor_Click(object sender, RoutedEventArgs e) => OpenDockTab(1);

    private void OpenDockTab(int index)
    {
        _viewModel.IsDockOpen = true;
        _viewModel.DockIndex = index;
        ApplyDockVisibility();
        _ = UpdateMonitorVisibilityAsync();
    }

    private void ToggleDock_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.IsDockOpen = !_viewModel.IsDockOpen;
        ApplyDockVisibility();
        _ = UpdateMonitorVisibilityAsync();
    }

    private void DockTab_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string tag || !int.TryParse(tag, out var index)) return;
        _viewModel.DockIndex = index;
        _ = UpdateMonitorVisibilityAsync();
        if (index == 0 && _viewModel.SelectedSession is { } session && !session.Sftp.IsConnected)
            _ = LoadSftpAsync(session);
    }

    private async Task LoadSftpAsync(SessionViewModel session)
    {
        try
        {
            await session.Sftp.EnsureConnectedAsync();
            if (session.TerminalDirectory is { Length: > 0 } directory &&
                !string.Equals(session.Sftp.CurrentPath, directory, StringComparison.Ordinal))
                await session.Sftp.LoadAsync(directory);
        }
        catch (Exception ex)
        {
            _viewModel.SetStatus("SFTP 连接失败：" + ex.Message);
        }
    }

    private async void SftpRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedSession is not { } session) return;
        await session.Sftp.RefreshAsync();
    }

    private async void SftpUp_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedSession is not { } session) return;
        await session.Sftp.NavigateUpAsync();
    }

    private async void SftpGo_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedSession is not { } session) return;
        await session.Sftp.LoadAsync(SftpPathBox.Text);
    }

    private async void SftpPath_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        if (_viewModel.SelectedSession is { } session) await session.Sftp.LoadAsync(SftpPathBox.Text);
    }

    private async void SftpList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.SelectedSession is not { } session || session.Sftp.SelectedItem is not { } item) return;
        if (item.IsDirectory)
        {
            await session.Sftp.OpenAsync(item);
            return;
        }

        // Double-click a text file in FinalShell style: open the remote editor
        // instead of immediately downloading it. The download button remains
        // available in the toolbar for files that should be saved locally.
        var editor = new RemoteFileEditorWindow(session.Sftp, item) { Owner = this };
        editor.ShowDialog();
    }

    private async Task DownloadSftpItemAsync(SessionViewModel session, SftpFileItem item)
    {
        var dialog = new SaveFileDialog
        {
            Title = "保存文件",
            FileName = item.Name,
            InitialDirectory = Directory.Exists(AppPaths.DownloadsFolder)
                ? AppPaths.DownloadsFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            await session.Sftp.DownloadAsync(item, dialog.FileName);
            _viewModel.SetStatus("已加入下载队列：" + item.Name);
        }
        catch (Exception ex)
        {
            ShowOperationError(ex);
        }
    }

    private async void SftpDownload_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedSession is not { } session) return;

        var selected = SftpList.SelectedItems.OfType<SftpFileItem>().ToList();
        if (selected.Count == 0 && session.Sftp.SelectedItem is { } fallback)
            selected.Add(fallback);
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "请先在文件列表中选择要下载的文件或文件夹。", "MistShell",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (selected.Count == 1 && !selected[0].IsDirectory)
        {
            await DownloadSftpItemAsync(session, selected[0]);
            return;
        }

        var folderDialog = new OpenFolderDialog
        {
            Title = "选择下载目录",
            InitialDirectory = Directory.Exists(AppPaths.DownloadsFolder)
                ? AppPaths.DownloadsFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
        };
        if (folderDialog.ShowDialog(this) != true) return;

        var folders = 0;
        var files = 0;
        foreach (var item in selected)
        {
            if (item.IsDirectory)
            {
                await session.Sftp.DownloadFolderAsync(item, folderDialog.FolderName);
                folders++;
            }
            else
            {
                var target = UniqueLocalPath(Path.Combine(folderDialog.FolderName, item.Name));
                await session.Sftp.DownloadAsync(item, target);
                files++;
            }
        }
        _viewModel.SetStatus($"已加入下载队列：{files} 个文件、{folders} 个文件夹");
    }

    private static string UniqueLocalPath(string path)
    {
        if (!File.Exists(path)) return path;
        var directory = Path.GetDirectoryName(path) ?? "";
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var index = 2; ; index++)
        {
            var candidate = Path.Combine(directory, $"{name} ({index}){extension}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    private async void SftpUpload_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedSession is not { } session) return;
        var dialog = new OpenFileDialog { Title = "选择要上传的文件", Multiselect = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            await session.Sftp.UploadFilesAsync(dialog.FileNames);
            _viewModel.SetStatus($"已加入上传队列：{dialog.FileNames.Length} 个文件");
        }
        catch (Exception ex)
        {
            ShowOperationError(ex);
        }
    }

    private async void SftpUploadFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedSession is not { } session) return;
        var dialog = new OpenFolderDialog
        {
            Title = "选择要上传的文件夹",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            await session.Sftp.UploadFolderAsync(dialog.FolderName);
            _viewModel.SetStatus("已加入上传队列：文件夹 " + Path.GetFileName(dialog.FolderName));
        }
        catch (Exception ex)
        {
            ShowOperationError(ex);
        }
    }

    private async void SftpNewFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedSession is not { } session) return;
        var dialog = new PromptDialog("新建文件夹", "文件夹名称", "", "将在当前远程目录中创建。") { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try { await session.Sftp.CreateFolderAsync(dialog.Value); }
        catch (Exception ex) { ShowOperationError(ex); }
    }

    private async void SftpRename_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedSession is not { } session || session.Sftp.SelectedItem is not { } item) return;
        var dialog = new PromptDialog("重命名", "新名称", item.Name) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try { await session.Sftp.RenameAsync(item, dialog.Value); }
        catch (Exception ex) { ShowOperationError(ex); }
    }

    private async void SftpDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedSession is not { } session) return;

        var selected = SftpList.SelectedItems.OfType<SftpFileItem>().ToList();
        if (selected.Count == 0 && session.Sftp.SelectedItem is { } fallback)
            selected.Add(fallback);
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "请先在文件列表中选择要删除的文件或文件夹。", "MistShell",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var message = selected.Count == 1
            ? (selected[0].IsDirectory
                ? $"确定递归删除目录“{selected[0].Name}”及其全部内容吗？此操作不可撤销。"
                : $"确定删除文件“{selected[0].Name}”吗？")
            : $"确定删除选中的 {selected.Count} 个项目吗？\n\n其中文件夹会被递归删除，此操作不可撤销。";
        if (MessageBox.Show(this, message, "删除", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        try { await session.Sftp.DeleteManyAsync(selected); }
        catch (Exception ex) { ShowOperationError(ex); }
    }

    private async void SftpPermissions_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedSession is not { } session || session.Sftp.SelectedItem is not { } item) return;
        var dialog = new PromptDialog("修改权限", "八进制权限", ModeToOctal(item.Mode),
            "例如 644、755、600。当前权限：" + item.Mode) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try { await session.Sftp.ChangePermissionsAsync(item, dialog.Value); }
        catch (Exception ex) { ShowOperationError(ex); }
    }

    private static string ModeToOctal(string mode)
    {
        if (mode.Length < 10) return "644";
        var value = 0;
        for (var i = 1; i < 10; i += 3)
        {
            value <<= 3;
            if (mode[i] == 'r') value += 4;
            if (mode[i + 1] == 'w') value += 2;
            if (mode[i + 2] == 'x') value += 1;
        }
        return Convert.ToString(value, 8);
    }

    private void SftpList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void SftpList_Drop(object sender, DragEventArgs e)
    {
        if (_viewModel.SelectedSession is not { } session) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        var files = paths.Where(File.Exists).ToArray();
        if (files.Length == 0) return;
        try
        {
            await session.Sftp.UploadFilesAsync(files);
            _viewModel.SetStatus($"已加入上传队列：{files.Length} 个文件");
        }
        catch (Exception ex)
        {
            ShowOperationError(ex);
        }
    }

    private void CancelTransfer_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is TransferItem item && _viewModel.SelectedSession is { } session)
            session.Sftp.CancelTransfer(item);
    }

    private async void RetryTransfer_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is TransferItem item && _viewModel.SelectedSession is { } session)
        {
            _viewModel.SetStatus($"正在重试：{item.Name}");
            await session.Sftp.RetryAsync(item);
        }
    }

    private void ClearTransfers_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SelectedSession?.Sftp.ClearFinishedTransfers();
    }

    private async void MonitorRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedSession is { } session) await session.Monitor.RefreshAsync();
    }

    private async void BeautifyShell_Click(object sender, RoutedEventArgs e)
    {
        var snippet = _viewModel.Snippets.FirstOrDefault(s => s.Name.StartsWith("彩色 ls", StringComparison.Ordinal));
        if (snippet is null)
        {
            _viewModel.SetStatus("没有找到彩色 Shell 命令片段");
            return;
        }
        await SendCommandToActiveSessionAsync(snippet.Command);
        _viewModel.SetStatus("已发送彩色 Shell 配置");
    }

    private async void SendSnippet_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is CommandSnippet snippet)
            await SendCommandToActiveSessionAsync(snippet.Command);
    }

    private async void SnippetList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (CommandsListSelectedSnippet() is { } snippet)
            await SendCommandToActiveSessionAsync(snippet.Command);
    }

    private CommandSnippet? CommandsListSelectedSnippet() => SnippetList.SelectedItem as CommandSnippet;

    private void AddSnippet_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SnippetDialog { Owner = this };
        if (dialog.ShowDialog() == true) _viewModel.AddSnippet(dialog.SnippetName, dialog.SnippetCommand);
    }

    private void DeleteSnippet_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is CommandSnippet snippet)
            _viewModel.RemoveSnippet(snippet);
    }

    private async void QuickCommand_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await SendQuickCommandAsync();
        }
        else if (e.Key == Key.Up)
        {
            e.Handled = true;
            var previous = _viewModel.GetPreviousCommand();
            if (previous is not null)
            {
                QuickCommandBox.Text = previous;
                QuickCommandBox.CaretIndex = previous.Length;
            }
        }
        else if (e.Key == Key.Down)
        {
            e.Handled = true;
            var next = _viewModel.GetNextCommand() ?? "";
            QuickCommandBox.Text = next;
            QuickCommandBox.CaretIndex = next.Length;
        }
        else if (e.Key == Key.Escape)
        {
            QuickCommandBox.Text = "";
        }
    }

    private async void SendCommand_Click(object sender, RoutedEventArgs e) => await SendQuickCommandAsync();

    private async Task SendQuickCommandAsync()
    {
        var command = _viewModel.QuickCommand;
        if (string.IsNullOrWhiteSpace(command)) return;
        await SendCommandToActiveSessionAsync(command);
        _viewModel.QuickCommand = "";
        QuickCommandBox.Text = "";
        TerminalView.FocusTerminal();
    }

    private async Task SendCommandToActiveSessionAsync(string command)
    {
        var session = _viewModel.SelectedSession;
        if (session is null || !session.IsConnected)
        {
            _viewModel.SetStatus("没有已连接的终端");
            return;
        }
        _viewModel.RecordCommand(command);
        await session.SendCommandAsync(command);
        ProcessTerminalCommand(session, command);
        TerminalView.FocusTerminal();
    }

    private void ShowOperationError(Exception ex)
    {
        _viewModel.SetStatus(ex.Message);
        MessageBox.Show(this, ex.Message, "操作失败", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void UpdateMaximizeButton()
    {
        if (MaximizeButton is null) return;
        var maximized = WindowState == WindowState.Maximized;
        MaximizeButton.Content = maximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = maximized ? "还原" : "最大化";
        AutomationProperties.SetName(MaximizeButton, maximized ? "还原" : "最大化");
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(WindowProc);
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmGetMinMaxInfo)
        {
            ApplyMaximizeBounds(hwnd, lParam);
            handled = true;
        }
        return IntPtr.Zero;
    }

    private static void ApplyMaximizeBounds(IntPtr hwnd, IntPtr lParam)
    {
        var mmi = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        var monitorInfo = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        if (GetMonitorInfo(monitor, ref monitorInfo))
        {
            var work = monitorInfo.rcWork;
            var full = monitorInfo.rcMonitor;
            mmi.ptMaxPosition.X = work.Left - full.Left;
            mmi.ptMaxPosition.Y = work.Top - full.Top;
            mmi.ptMaxSize.X = work.Right - work.Left;
            mmi.ptMaxSize.Y = work.Bottom - work.Top;
            mmi.ptMaxTrackSize.X = mmi.ptMaxSize.X;
            mmi.ptMaxTrackSize.Y = mmi.ptMaxSize.Y;
            Marshal.StructureToPtr(mmi, lParam, true);
        }
    }

    private const int WmGetMinMaxInfo = 0x0024;
    private const int MonitorDefaultToNearest = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint ptReserved;
        public NativePoint ptMaxSize;
        public NativePoint ptMaxPosition;
        public NativePoint ptMinTrackSize;
        public NativePoint ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int cbSize;
        public NativeRect rcMonitor;
        public NativeRect rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        var settings = _viewModel.Settings;
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (bounds.Width > 0 && bounds.Height > 0)
        {
            settings.WindowLeft = bounds.Left;
            settings.WindowTop = bounds.Top;
            settings.WindowWidth = bounds.Width;
            settings.WindowHeight = bounds.Height;
        }
        settings.WindowMaximized = WindowState == WindowState.Maximized;
        if (SidebarColumn.Width.Value > 0) settings.SidebarWidth = SidebarColumn.Width.Value;
        if (DockColumn.Width.Value > 0) settings.DockWidth = DockColumn.Width.Value;
        settings.DockOpen = _viewModel.IsDockOpen;
        settings.DockTab = _viewModel.DockIndex;
        _viewModel.SaveSettings();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        if (_disposed) return;
        _disposed = true;
        TerminalView.Dispose();
        _viewModel.Dispose();
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T result) return result;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
