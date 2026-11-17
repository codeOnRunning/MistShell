namespace MistShell.Models;

public sealed class SystemMetrics
{
    public double CpuPercent { get; set; }
    public double MemoryPercent { get; set; }
    public long MemoryUsedBytes { get; set; }
    public long MemoryTotalBytes { get; set; }
    public double SwapPercent { get; set; }
    public long SwapUsedBytes { get; set; }
    public long SwapTotalBytes { get; set; }
    public double DiskPercent { get; set; }
    public long DiskUsedBytes { get; set; }
    public long DiskTotalBytes { get; set; }
    public double NetworkReceiveBytesPerSecond { get; set; }
    public double NetworkSendBytesPerSecond { get; set; }
    public double Load1 { get; set; }
    public double Load5 { get; set; }
    public double Load15 { get; set; }
    public TimeSpan Uptime { get; set; }
    public string HostName { get; set; } = "";
    public List<ProcessMetric> TopProcesses { get; } = new();
}

public sealed class ProcessMetric
{
    public int Pid { get; set; }
    public string Name { get; set; } = "";
    public double CpuPercent { get; set; }
    public double MemoryPercent { get; set; }
    public string CpuText => $"{CpuPercent:0.0}%";
    public string MemoryText => $"{MemoryPercent:0.0}%";
}
