using System.Text;
using MistShell.Infrastructure;
using MistShell.Models;
using MistShell.Services;

namespace MistShell.ViewModels;

public sealed class SessionViewModel : ObservableObject, IDisposable
{
    private readonly AppSettings _settings;
    private readonly string? _privateKeyContent;
    private readonly Func<HostKeyInfo, HostKeyDecision> _hostKeyVerifier;
    private readonly Func<string, string?> _expectedFingerprintProvider;
    private SshSession? _session;
    private string _status = "连接中";
    private string _statusDetail = "正在建立 SSH 连接…";
    private bool _isBusy;
    private int _disposed;

    public SessionViewModel(
        ConnectionProfile profile,
        string password,
        string passphrase,
        AppSettings settings,
        Func<HostKeyInfo, HostKeyDecision> hostKeyVerifier,
        Func<string, string?> expectedFingerprintProvider,
        string? tabTitle = null,
        string? privateKeyContent = null)
    {
        Profile = profile;
        Title = string.IsNullOrWhiteSpace(tabTitle) ? profile.Name : tabTitle;
        _settings = settings;
        _privateKeyContent = privateKeyContent;
        _hostKeyVerifier = hostKeyVerifier;
        _expectedFingerprintProvider = expectedFingerprintProvider;
        Sftp = new SftpViewModel();
        Monitor = new MonitorViewModel();
        _session = CreateBackend(password, passphrase);
    }

    public string Id { get; } = Guid.NewGuid().ToString("N");
    public ConnectionProfile Profile { get; }
    public SftpViewModel Sftp { get; }
    public MonitorViewModel Monitor { get; }
    public event Action<SessionViewModel, byte[]>? DataReceived;
    public event Action<SessionViewModel, string>? Disconnected;

    public string Title { get; }
    public string HostDisplay => Profile.Endpoint;
    public string? TerminalDirectory { get; set; }

    public string Status
    {
        get => _status;
        private set
        {
            if (SetProperty(ref _status, value))
                OnPropertiesChanged(nameof(IsConnected), nameof(IsConnecting), nameof(StatusText));
        }
    }

    public string StatusDetail
    {
        get => _statusDetail;
        private set => SetProperty(ref _statusDetail, value);
    }

    public string StatusText => Status switch
    {
        "已连接" => "已连接",
        "连接中" => "连接中",
        "错误" => "连接失败",
        _ => "已断开"
    };

    public bool IsConnected => Status == "已连接";
    public bool IsConnecting => Status == "连接中";
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public bool MonitorVisible { get; private set; }

    private SshSession CreateBackend(string password, string passphrase)
    {
        var backend = new SshSession(Profile, password, passphrase, _hostKeyVerifier,
            _expectedFingerprintProvider, _privateKeyContent);
        backend.DataReceived += OnDataReceived;
        backend.Disconnected += OnBackendDisconnected;
        Sftp.Attach(backend);
        Monitor.Attach(backend, _settings.LowMemoryMode);
        return backend;
    }

    public async Task ConnectAsync(int columns, int rows)
    {
        if (_session is null) throw new InvalidOperationException("会话未初始化。");
        IsBusy = true;
        Status = "连接中";
        StatusDetail = "正在建立 SSH 连接…";
        try
        {
            await _session.ConnectAsync(columns, rows);
            Status = "已连接";
            StatusDetail = "终端已就绪";
            Profile.LastConnectedUtc = DateTimeOffset.UtcNow;
            if (MonitorVisible) await Monitor.StartAsync();
        }
        catch (Exception ex)
        {
            Status = "错误";
            StatusDetail = ex.Message;
            AppLog.Write($"Connection to {Profile.Host}:{Profile.Port} failed.", ex);
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ReconnectAsync(int columns, int rows, string password, string passphrase)
    {
        if (IsBusy) return;
        IsBusy = true;
        Status = "连接中";
        StatusDetail = "正在重新连接…";
        try
        {
            Monitor.Stop();
            Sftp.Reset();
            var old = _session;
            _session = null;
            if (old is not null)
            {
                old.DataReceived -= OnDataReceived;
                old.Disconnected -= OnBackendDisconnected;
                old.Dispose();
            }

            _session = CreateBackend(password, passphrase);
            await _session.ConnectAsync(columns, rows);
            Status = "已连接";
            StatusDetail = "已重新连接";
        }
        catch (Exception ex)
        {
            Status = "错误";
            StatusDetail = ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task WriteAsync(byte[] data)
    {
        if (_session is not null) await _session.WriteAsync(data);
    }

    public async Task SendTextAsync(string text)
    {
        if (_session is not null) await _session.WriteTextAsync(text);
    }

    public async Task SendCommandAsync(string command, bool appendNewLine = true)
    {
        if (!IsConnected || string.IsNullOrWhiteSpace(command)) return;
        var payload = appendNewLine ? command.TrimEnd('\r', '\n') + "\r" : command;
        await SendTextAsync(payload);
    }

    public void Resize(int columns, int rows) => _session?.Resize(columns, rows);

    public async Task SetMonitorVisibleAsync(bool visible)
    {
        MonitorVisible = visible;
        if (visible && IsConnected) await Monitor.StartAsync();
        else Monitor.Stop();
    }

    private void OnDataReceived(byte[] data) => DataReceived?.Invoke(this, data);

    private void OnBackendDisconnected(string reason)
    {
        Status = "已断开";
        StatusDetail = reason;
        Monitor.Stop();
        Disconnected?.Invoke(this, reason);
    }

    public void Disconnect()
    {
        Monitor.Stop();
        Sftp.Reset();
        _session?.Dispose();
        Status = "已断开";
        StatusDetail = "已手动断开";
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        Monitor.Dispose();
        Sftp.Dispose();
        if (_session is not null)
        {
            _session.DataReceived -= OnDataReceived;
            _session.Disconnected -= OnBackendDisconnected;
            _session.Dispose();
        }
        _session = null;
    }
}
