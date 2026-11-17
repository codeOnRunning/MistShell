using System.Collections.ObjectModel;
using MistShell.Infrastructure;
using MistShell.Models;
using MistShell.Services;

namespace MistShell.ViewModels;

public sealed class MonitorViewModel : ObservableObject, IDisposable
{
    private readonly SystemMonitor _monitor = new();
    private SshSession? _session;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private bool _isActive;
    private bool _isRefreshing;
    private string? _errorMessage;
    private string _lastUpdated = "尚未采集";
    private double _cpuPercent;
    private double _memoryPercent;
    private double _swapPercent;
    private double _diskPercent;
    private long _memoryUsed;
    private long _memoryTotal;
    private long _diskUsed;
    private long _diskTotal;
    private string _networkReceive = "0 B/s";
    private string _networkSend = "0 B/s";
    private string _loadText = "—";
    private string _uptimeText = "—";
    private string _hostName = "";
    private long _latencyMs;
    private int _intervalSeconds = 3;
    private int _disposed;

    public ObservableCollection<ProcessMetric> TopProcesses { get; } = new();

    public double CpuPercent { get => _cpuPercent; set { if (SetProperty(ref _cpuPercent, value)) OnPropertyChanged(nameof(CpuText)); } }
    public double MemoryPercent { get => _memoryPercent; set { if (SetProperty(ref _memoryPercent, value)) OnPropertiesChanged(nameof(MemoryText), nameof(MemoryDetail)); } }
    public double SwapPercent { get => _swapPercent; set => SetProperty(ref _swapPercent, value); }
    public double DiskPercent { get => _diskPercent; set { if (SetProperty(ref _diskPercent, value)) OnPropertiesChanged(nameof(DiskText), nameof(DiskDetail)); } }
    public long MemoryUsed { get => _memoryUsed; set { if (SetProperty(ref _memoryUsed, value)) OnPropertiesChanged(nameof(MemoryText), nameof(MemoryDetail)); } }
    public long MemoryTotal { get => _memoryTotal; set { if (SetProperty(ref _memoryTotal, value)) OnPropertiesChanged(nameof(MemoryDetail)); } }
    public long DiskUsed { get => _diskUsed; set { if (SetProperty(ref _diskUsed, value)) OnPropertiesChanged(nameof(DiskDetail)); } }
    public long DiskTotal { get => _diskTotal; set { if (SetProperty(ref _diskTotal, value)) OnPropertiesChanged(nameof(DiskDetail)); } }
    public string NetworkReceive { get => _networkReceive; set => SetProperty(ref _networkReceive, value); }
    public string NetworkSend { get => _networkSend; set => SetProperty(ref _networkSend, value); }
    public string LoadText { get => _loadText; set => SetProperty(ref _loadText, value); }
    public string UptimeText { get => _uptimeText; set => SetProperty(ref _uptimeText, value); }
    public string HostName { get => _hostName; set => SetProperty(ref _hostName, value); }
    public long LatencyMs { get => _latencyMs; set { if (SetProperty(ref _latencyMs, value)) OnPropertyChanged(nameof(LatencyText)); } }
    public string LatencyText => _latencyMs <= 0 ? "—" : $"{_latencyMs} ms";
    public string CpuText => $"{CpuPercent:0}%";
    public string MemoryText => $"{MemoryPercent:0}%";
    public string MemoryDetail => $"已用 {SftpFileItem.FormatSize(MemoryUsed)} / 共 {SftpFileItem.FormatSize(MemoryTotal)}";
    public string DiskText => $"{DiskPercent:0}%";
    public string DiskDetail => $"已用 {SftpFileItem.FormatSize(DiskUsed)} / 共 {SftpFileItem.FormatSize(DiskTotal)}";
    public string LastUpdated { get => _lastUpdated; set => SetProperty(ref _lastUpdated, value); }
    public string? ErrorMessage { get => _errorMessage; set { if (SetProperty(ref _errorMessage, value)) OnPropertyChanged(nameof(HasError)); } }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool IsRefreshing { get => _isRefreshing; set => SetProperty(ref _isRefreshing, value); }
    public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }

    public void Attach(SshSession session, bool lowMemoryMode)
    {
        _session = session;
        _intervalSeconds = lowMemoryMode ? 5 : 2;
    }

    public async Task StartAsync()
    {
        if (_session is null || IsActive) return;
        IsActive = true;
        _loopCts = new CancellationTokenSource();
        _loopTask = RunLoopAsync(_loopCts.Token);
        await Task.CompletedTask;
    }

    public void Stop()
    {
        IsActive = false;
        try { _loopCts?.Cancel(); } catch { }
        _loopCts?.Dispose();
        _loopCts = null;
        _loopTask = null;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_session is null || IsRefreshing || !_session.IsConnected) return;
        IsRefreshing = true;
        try
        {
            var metrics = await _monitor.CollectAsync(_session, cancellationToken);
            Apply(metrics);
            ErrorMessage = null;
            LastUpdated = "更新于 " + DateTime.Now.ToString("HH:mm:ss");
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsRefreshing = false;
        }

        if (_session.IsConnected)
        {
            try { LatencyMs = await _session.MeasureLatencyAsync(cancellationToken); }
            catch { /* monitoring continues */ }
        }
    }

    private async Task RunLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await RefreshAsync(token);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_intervalSeconds), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public void Apply(SystemMetrics metrics)
    {
        CpuPercent = metrics.CpuPercent;
        MemoryPercent = metrics.MemoryPercent;
        MemoryUsed = metrics.MemoryUsedBytes;
        MemoryTotal = metrics.MemoryTotalBytes;
        SwapPercent = metrics.SwapPercent;
        DiskPercent = metrics.DiskPercent;
        DiskUsed = metrics.DiskUsedBytes;
        DiskTotal = metrics.DiskTotalBytes;
        NetworkReceive = SftpFileItem.FormatSize((long)metrics.NetworkReceiveBytesPerSecond) + "/s";
        NetworkSend = SftpFileItem.FormatSize((long)metrics.NetworkSendBytesPerSecond) + "/s";
        LoadText = $"{metrics.Load1:0.00}  {metrics.Load5:0.00}  {metrics.Load15:0.00}";
        UptimeText = FormatUptime(metrics.Uptime);
        HostName = metrics.HostName;

        TopProcesses.Clear();
        foreach (var process in metrics.TopProcesses) TopProcesses.Add(process);
    }

    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime <= TimeSpan.Zero) return "—";
        if (uptime.TotalDays >= 1) return $"{(int)uptime.TotalDays} 天 {uptime.Hours} 小时";
        if (uptime.TotalHours >= 1) return $"{(int)uptime.TotalHours} 小时 {uptime.Minutes} 分";
        return $"{uptime.Minutes} 分 {uptime.Seconds} 秒";
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        Stop();
    }
}
