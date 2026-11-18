using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;
using MistShell.Infrastructure;
using MistShell.Models;
using MistShell.Services;

namespace MistShell.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly ProfileStore _profileStore = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly DispatcherTimer _memoryTimer;
    private ConnectionProfile? _selectedConnection;
    private SessionViewModel? _selectedSession;
    private string _searchText = "";
    private string _statusText = "就绪";
    private string _memoryText = "内存 —";
    private string _quickCommand = "";
    private bool _isDockOpen;
    private int _dockIndex;
    private string _themeDisplayName = "雾纸";
    private bool _isBusy;
    private int _historyIndex = -1;
    private int _disposed;

    public MainViewModel()
    {
        Settings = _settingsStore.Load();
        if (Settings.Snippets.Count == 0) Settings.Snippets = AppSettings.CreateDefaultSnippets();
        Connections = new ObservableCollection<ConnectionProfile>(_profileStore.Load());
        ConnectionsView = CollectionViewSource.GetDefaultView(Connections);
        ConnectionsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ConnectionProfile.Group)));
        ConnectionsView.SortDescriptions.Add(new SortDescription(nameof(ConnectionProfile.IsFavorite), ListSortDirection.Descending));
        ConnectionsView.SortDescriptions.Add(new SortDescription(nameof(ConnectionProfile.Name), ListSortDirection.Ascending));
        ConnectionsView.Filter = FilterConnection;
        Connections.CollectionChanged += (_, _) => OnPropertiesChanged(nameof(HasConnections), nameof(ConnectionCountText));

        foreach (var profile in Connections) Watch(profile);
        Sessions.CollectionChanged += (_, _) =>
        {
            OnPropertiesChanged(nameof(HasSessions), nameof(SessionCountText));
            if (Sessions.Count == 0) SelectedSession = null;
        };

        _isDockOpen = Settings.DockOpen;
        _dockIndex = Math.Clamp(Settings.DockTab, 0, 2);
        _themeDisplayName = DisplayTheme(Settings.Theme);
        ThemeManager.Apply(ThemeManager.Resolve(Settings.Theme));

        Snippets = new ObservableCollection<CommandSnippet>(Settings.Snippets);
        _memoryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _memoryTimer.Tick += (_, _) => UpdateMemory();
        _memoryTimer.Start();
        UpdateMemory();
    }

    public AppSettings Settings { get; }
    public ObservableCollection<ConnectionProfile> Connections { get; }
    public ICollectionView ConnectionsView { get; }
    public ObservableCollection<SessionViewModel> Sessions { get; } = new();
    public ObservableCollection<CommandSnippet> Snippets { get; }

    public Func<HostKeyInfo, HostKeyDecision>? HostKeyVerifier { get; set; }

    public ConnectionProfile? SelectedConnection
    {
        get => _selectedConnection;
        set => SetProperty(ref _selectedConnection, value);
    }

    public SessionViewModel? SelectedSession
    {
        get => _selectedSession;
        set
        {
            if (!SetProperty(ref _selectedSession, value)) return;
            OnPropertiesChanged(nameof(HasSelectedSession), nameof(SelectedSessionTitle), nameof(SelectedSessionHost));
            ActiveSessionChanged?.Invoke(value);
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value)) return;
            ConnectionsView.Refresh();
        }
    }

    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }
    public string MemoryText { get => _memoryText; set => SetProperty(ref _memoryText, value); }
    public string QuickCommand { get => _quickCommand; set => SetProperty(ref _quickCommand, value); }
    public string ThemeDisplayName { get => _themeDisplayName; set => SetProperty(ref _themeDisplayName, value); }
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public bool IsDockOpen
    {
        get => _isDockOpen;
        set
        {
            if (!SetProperty(ref _isDockOpen, value)) return;
            Settings.DockOpen = value;
            SaveSettings();
        }
    }

    public int DockIndex
    {
        get => _dockIndex;
        set
        {
            if (!SetProperty(ref _dockIndex, value)) return;
            Settings.DockTab = value;
            SaveSettings();
            DockTabChanged?.Invoke(value);
        }
    }

    public bool HasConnections => Connections.Count > 0;
    public bool HasSessions => Sessions.Count > 0;
    public bool HasSelectedSession => SelectedSession is not null;
    public string ConnectionCountText => $"{Connections.Count} 台服务器";
    public string SessionCountText => Sessions.Count == 0 ? "无活动会话" : $"{Sessions.Count} 个会话";
    public string SelectedSessionTitle => SelectedSession?.Title ?? "未选择会话";
    public string SelectedSessionHost => SelectedSession?.HostDisplay ?? "从左侧选择服务器开始";
    public string AppVersion => "v0.1.26";

    public event Action<SessionViewModel?>? ActiveSessionChanged;
    public event Action<int>? DockTabChanged;
    public event Action? ThemeChanged;

    public SessionViewModel CreateSession(ConnectionProfile profile)
    {
        var (password, passphrase) = SettingsStore.ResolveCredentials(profile);
        var baseTitle = profile.Name;
        var title = baseTitle;
        var suffix = 2;
        var usedTitles = Sessions.Select(s => s.Title).ToHashSet(StringComparer.OrdinalIgnoreCase);
        while (usedTitles.Contains(title)) title = $"{baseTitle} ({suffix++})";

        var session = new SessionViewModel(
            profile,
            password,
            passphrase,
            Settings,
            info => HostKeyVerifier?.Invoke(info) ?? HostKeyDecision.Reject,
            keyType => GetKnownHostKey(profile, keyType),
            title,
            SettingsStore.ResolvePrivateKeyContent(Settings, profile));
        session.DataReceived += (s, data) => DataReceived?.Invoke(s, data);
        session.Disconnected += (s, reason) => SessionDisconnected?.Invoke(s, reason);
        Sessions.Add(session);
        SelectedSession = session;
        SelectedConnection = profile;
        StatusText = $"正在连接 {profile.Name}…";
        return session;
    }

    public event Action<SessionViewModel, byte[]>? DataReceived;
    public event Action<SessionViewModel, string>? SessionDisconnected;

    public void RemoveSession(SessionViewModel session)
    {
        session.Dispose();
        var index = Sessions.IndexOf(session);
        if (index < 0) return;
        Sessions.Remove(session);
        if (SelectedSession == session)
            SelectedSession = Sessions.Count == 0 ? null : Sessions[Math.Clamp(index, 0, Sessions.Count - 1)];
        if (Sessions.Count == 0) StatusText = "就绪";
    }

    public void AddConnection(ConnectionProfile profile)
    {
        var existing = Connections.FirstOrDefault(c => c.Id == profile.Id);
        if (existing is null)
        {
            Connections.Add(profile);
        }
        else
        {
            existing.Name = profile.Name;
            existing.Group = profile.Group;
            existing.Host = profile.Host;
            existing.Port = profile.Port;
            existing.Username = profile.Username;
            existing.AuthMethod = profile.AuthMethod;
            existing.EncryptedPassword = profile.EncryptedPassword;
            existing.PrivateKeyPath = profile.PrivateKeyPath;
            existing.PrivateKeyId = profile.PrivateKeyId;
            existing.EncryptedPassphrase = profile.EncryptedPassphrase;
            existing.Color = profile.Color;
            existing.Notes = profile.Notes;
            existing.UpdatedUtc = DateTimeOffset.UtcNow;
        }
        Watch(profile);
        ConnectionsView.Refresh();
        _ = SaveConnectionsAsync();
        OnPropertiesChanged(nameof(HasConnections), nameof(ConnectionCountText));
    }

    public void RemoveConnection(ConnectionProfile profile)
    {
        Connections.Remove(profile);
        ConnectionsView.Refresh();
        _ = SaveConnectionsAsync();
        OnPropertiesChanged(nameof(HasConnections), nameof(ConnectionCountText));
    }

    public async Task SaveConnectionsAsync()
    {
        try { await _profileStore.SaveAsync(Connections); }
        catch (Exception ex) { AppLog.Write("Saving connections failed.", ex); }
    }

    public string? GetKnownHostKey(ConnectionProfile profile, string? keyType = null)
    {
        if (!string.IsNullOrWhiteSpace(keyType) &&
            Settings.KnownHostKeyTypes.TryGetValue($"{profile.Host}:{profile.Port}|{keyType}", out var typed))
            return typed;
        return Settings.KnownHostKeys.TryGetValue($"{profile.Host}:{profile.Port}", out var value) ? value : null;
    }

    public void SaveKnownHostKey(HostKeyInfo info)
    {
        Settings.KnownHostKeys[$"{info.Host}:{info.Port}"] = info.Fingerprint;
        Settings.KnownHostKeyTypes[$"{info.Host}:{info.Port}|{info.KeyType}"] = info.Fingerprint;
        SaveSettings();
    }

    public void SetTheme(string key)
    {
        Settings.Theme = key;
        var palette = ThemeManager.Resolve(key);
        ThemeManager.Apply(palette);
        ThemeDisplayName = DisplayTheme(key);
        SaveSettings();
        ThemeChanged?.Invoke();
    }

    public void ToggleLightDark()
    {
        var current = ThemeManager.Current.Key;
        var index = ThemePalettes.All.ToList().FindIndex(theme => theme.Key == current);
        var next = ThemePalettes.All[(Math.Max(index, -1) + 1) % ThemePalettes.All.Count];
        SetTheme(next.Key);
    }

    public static string DisplayTheme(string key) => key switch
    {
        "Sand" => "暖砂",
        "Sakura" => "樱雾",
        "Glacier" => "冰川",
        "Sage" => "青苔",
        "Slate" => "深岩",
        "Midnight" => "午夜蓝",
        "Ember" => "炭火",
        "Amethyst" => "紫夜",
        "Carbon" => "石墨",
        "System" => "跟随系统",
        _ => "雾纸"
    };

    public void SetStatus(string text) => StatusText = text;

    public void RecordCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return;
        Settings.CommandHistory.RemoveAll(c => string.Equals(c, command, StringComparison.Ordinal));
        Settings.CommandHistory.Insert(0, command);
        if (Settings.CommandHistory.Count > 200) Settings.CommandHistory.RemoveRange(200, Settings.CommandHistory.Count - 200);
        _historyIndex = -1;
        SaveSettings();
    }

    public string? GetPreviousCommand()
    {
        if (Settings.CommandHistory.Count == 0) return null;
        _historyIndex = Math.Min(Settings.CommandHistory.Count - 1, _historyIndex + 1);
        return Settings.CommandHistory[_historyIndex];
    }

    public string? GetNextCommand()
    {
        if (Settings.CommandHistory.Count == 0) return null;
        _historyIndex = Math.Max(-1, _historyIndex - 1);
        return _historyIndex < 0 ? "" : Settings.CommandHistory[_historyIndex];
    }

    public void AddSnippet(string name, string command)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(command)) return;
        var snippet = new CommandSnippet { Name = name.Trim(), Command = command.Trim(), Group = "自定义" };
        Settings.Snippets.Add(snippet);
        Snippets.Add(snippet);
        SaveSettings();
    }

    public void RemoveSnippet(CommandSnippet snippet)
    {
        Settings.Snippets.Remove(snippet);
        Snippets.Remove(snippet);
        SaveSettings();
    }

    public void SaveSettings()
    {
        try { _settingsStore.Save(Settings); }
        catch (Exception ex) { AppLog.Write("Saving settings failed.", ex); }
    }

    private bool FilterConnection(object obj)
    {
        if (obj is not ConnectionProfile profile) return false;
        var query = SearchText.Trim();
        if (query.Length == 0) return true;
        return profile.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
               || profile.Host.Contains(query, StringComparison.OrdinalIgnoreCase)
               || profile.Username.Contains(query, StringComparison.OrdinalIgnoreCase)
               || profile.Group.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void Watch(ConnectionProfile profile)
    {
        profile.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(ConnectionProfile.Name) or nameof(ConnectionProfile.Group) or nameof(ConnectionProfile.Host))
                ConnectionsView.Refresh();
        };
    }

    private void UpdateMemory()
    {
        var bytes = MemoryMeter.GetProcessTreeWorkingSet();
        MemoryText = $"占用 {bytes / 1024d / 1024d:0} MB";
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        _memoryTimer.Stop();
        foreach (var session in Sessions.ToList()) session.Dispose();
        SaveSettings();
    }
}
