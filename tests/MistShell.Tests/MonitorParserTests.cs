using MistShell.Services;

namespace MistShell.Tests;

public class MonitorParserTests
{
    private const string Fixture = """
@@UP
86400.00 12345.00
@@LOAD
0.42 0.35 0.28 2/123 4567
@@CPU
cpu  12000 100 3000 50000 1000 0 200 0 0 0
cpu  12010 100 3010 50020 1000 0 200 0 0 0
@@MEM
MemTotal: 16384000 kB
MemFree: 4096000 kB
MemAvailable: 8192000 kB
Buffers: 200000 kB
Cached: 2000000 kB
SwapTotal: 2097152 kB
SwapFree: 1048576 kB
@@DISK
/dev/sda1 104857600 52428800 52428800 50% /
@@NET
  eth0: 1000000 1000 0 0 0 0 0 0 500000 900 0 0 0 0 0 0
@@HOST
mistshell-test
@@TOP
  821 nginx 4.2 1.1
  921 node 2.1 3.4
""";

    [Fact]
    public void ParsesSystemMetrics()
    {
        var metrics = new SystemMonitor().Parse(Fixture, DateTime.UtcNow);
        Assert.Equal(50, metrics.CpuPercent, 0);
        Assert.Equal(50, metrics.MemoryPercent, 0.01);
        Assert.Equal(50, metrics.DiskPercent, 0.01);
        Assert.Equal(50, metrics.SwapPercent, 0.01);
        Assert.Equal("mistshell-test", metrics.HostName);
        Assert.Equal(TimeSpan.FromHours(24), metrics.Uptime);
        Assert.Equal(2, metrics.TopProcesses.Count);
        Assert.Equal("nginx", metrics.TopProcesses[0].Name);
    }

    [Fact]
    public void HandlesEmptyOutputWithoutThrowing()
    {
        var metrics = new SystemMonitor().Parse("", DateTime.UtcNow);
        Assert.Equal(0, metrics.CpuPercent);
        Assert.Empty(metrics.TopProcesses);
    }
}
