using MistShell.Infrastructure;

namespace MistShell.Models;

public enum TransferDirection
{
    Upload,
    Download
}

public sealed class TransferItem : ObservableObject
{
    private long _transferredBytes;
    private string _status = "等待中";
    private bool _isRunning;
    private string? _error;
    private DateTime _startedAt;

    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public required string Name { get; init; }
    public required string LocalPath { get; init; }
    public required string RemotePath { get; init; }
    public required TransferDirection Direction { get; init; }
    public long TotalBytes { get; init; }
    public bool IsFolder { get; init; }
    public int TotalFiles { get; init; }
    private int _completedFiles;
    public int CompletedFiles
    {
        get => _completedFiles;
        set
        {
            if (SetProperty(ref _completedFiles, value))
                OnPropertiesChanged(nameof(ProgressText));
        }
    }

    public CancellationTokenSource Cancellation { get; private set; } = new();

    public long TransferredBytes
    {
        get => _transferredBytes;
        set
        {
            if (!SetProperty(ref _transferredBytes, value)) return;
            OnPropertiesChanged(nameof(Progress), nameof(ProgressText), nameof(SpeedText));
        }
    }

    public double Progress => TotalBytes <= 0 ? 0 : Math.Clamp(TransferredBytes * 100d / TotalBytes, 0, 100);
    public string ProgressText => IsFolder
        ? $"{CompletedFiles}/{TotalFiles} 个文件 · {SftpFileItem.FormatSize(TransferredBytes)} / {SftpFileItem.FormatSize(TotalBytes)}"
        : TotalBytes <= 0
            ? SftpFileItem.FormatSize(TransferredBytes)
            : $"{SftpFileItem.FormatSize(TransferredBytes)} / {SftpFileItem.FormatSize(TotalBytes)}";

    public string SpeedText
    {
        get
        {
            if (!IsRunning || _startedAt == default) return "";
            var seconds = Math.Max(0.2, (DateTime.UtcNow - _startedAt).TotalSeconds);
            return SftpFileItem.FormatSize((long)(TransferredBytes / seconds)) + "/s";
        }
    }

    public string Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
                OnPropertiesChanged(nameof(CanRetry), nameof(IsFailed));
        }
    }

    public bool IsRunning { get => _isRunning; set { if (SetProperty(ref _isRunning, value)) OnPropertiesChanged(nameof(SpeedText)); } }
    public string? Error { get => _error; set => SetProperty(ref _error, value); }
    public bool CanRetry => Status is "失败" or "已取消";
    public bool IsFailed => Status == "失败";
    public string DirectionGlyph => IsFolder ? "\uE8B7" : Direction == TransferDirection.Upload ? "\uE898" : "\uE896";
    public string DirectionText => Direction == TransferDirection.Upload ? "上传" : "下载";

    public void MarkStarted()
    {
        _startedAt = DateTime.UtcNow;
        Error = null;
        IsRunning = true;
        Status = "传输中";
    }

    public void ResetForRetry()
    {
        try { Cancellation.Dispose(); } catch { }
        Cancellation = new CancellationTokenSource();
        _startedAt = default;
        TransferredBytes = 0;
        CompletedFiles = 0;
        Error = null;
        IsRunning = false;
        Status = "等待中";
    }

    public void MarkCompleted()
    {
        IsRunning = false;
        Status = "已完成";
        TransferredBytes = TotalBytes;
        CompletedFiles = TotalFiles;
    }

    public void MarkFailed(string error)
    {
        IsRunning = false;
        Error = error;
        Status = "失败";
    }
}
