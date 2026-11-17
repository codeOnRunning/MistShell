using System.Globalization;
using MistShell.Models;

namespace MistShell.Services;

public sealed class SystemMonitor
{
    private NetworkSample? _previousNetwork;

    public async Task<SystemMetrics> CollectAsync(SshSession session, CancellationToken cancellationToken = default)
    {
        const string command = """
LANG=C LC_ALL=C sh -c '
echo "@@UP"; cat /proc/uptime 2>/dev/null
echo "@@LOAD"; cat /proc/loadavg 2>/dev/null
echo "@@CPU"; head -n 1 /proc/stat 2>/dev/null; sleep 0.15; head -n 1 /proc/stat 2>/dev/null
echo "@@MEM"; cat /proc/meminfo 2>/dev/null
echo "@@DISK"; df -Pk / 2>/dev/null | tail -n 1
echo "@@NET"; cat /proc/net/dev 2>/dev/null
echo "@@HOST"; hostname 2>/dev/null
echo "@@TOP"; ps -eo pid=,comm=,pcpu=,pmem= --sort=-pcpu 2>/dev/null | head -n 11
'
""";
        var result = await session.RunCommandAsync(command, TimeSpan.FromSeconds(15), cancellationToken);
        return Parse(result.Output, DateTime.UtcNow);
    }

    public SystemMetrics Parse(string output, DateTime now)
    {
        var sections = SplitSections(output);
        var metrics = new SystemMetrics();

        if (sections.TryGetValue("UP", out var up) && up.Count > 0)
        {
            var token = up[0].Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                metrics.Uptime = TimeSpan.FromSeconds(seconds);
        }

        if (sections.TryGetValue("LOAD", out var load) && load.Count > 0)
        {
            var parts = load[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3)
            {
                metrics.Load1 = ParseDouble(parts[0]);
                metrics.Load5 = ParseDouble(parts[1]);
                metrics.Load15 = ParseDouble(parts[2]);
            }
        }

        if (sections.TryGetValue("CPU", out var cpu) && cpu.Count >= 2)
        {
            metrics.CpuPercent = CalculateCpu(cpu[0], cpu[1]);
        }

        if (sections.TryGetValue("MEM", out var mem))
        {
            var values = ParseKeyValues(mem);
            var total = GetLong(values, "MemTotal");
            var available = GetLong(values, "MemAvailable");
            if (available <= 0) available = GetLong(values, "MemFree") + GetLong(values, "Buffers") + GetLong(values, "Cached");
            metrics.MemoryTotalBytes = total;
            metrics.MemoryUsedBytes = Math.Max(0, total - available);
            metrics.MemoryPercent = total > 0 ? metrics.MemoryUsedBytes * 100d / total : 0;

            var swapTotal = GetLong(values, "SwapTotal");
            var swapFree = GetLong(values, "SwapFree");
            metrics.SwapTotalBytes = swapTotal;
            metrics.SwapUsedBytes = Math.Max(0, swapTotal - swapFree);
            metrics.SwapPercent = swapTotal > 0 ? metrics.SwapUsedBytes * 100d / swapTotal : 0;
        }

        if (sections.TryGetValue("DISK", out var disk) && disk.Count > 0)
        {
            var parts = disk[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 6)
            {
                if (long.TryParse(parts[1], out var blocks)) metrics.DiskTotalBytes = blocks * 1024;
                if (long.TryParse(parts[2], out var used)) metrics.DiskUsedBytes = used * 1024;
                var pct = parts[4].TrimEnd('%');
                if (double.TryParse(pct, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
                    metrics.DiskPercent = percent;
            }
        }

        if (sections.TryGetValue("NET", out var net))
        {
            var current = ParseNetwork(net);
            if (_previousNetwork is not null)
            {
                var seconds = Math.Max(0.1, (now - _previousNetwork.Timestamp).TotalSeconds);
                metrics.NetworkReceiveBytesPerSecond = Math.Max(0, (current.Received - _previousNetwork.Received) / seconds);
                metrics.NetworkSendBytesPerSecond = Math.Max(0, (current.Sent - _previousNetwork.Sent) / seconds);
            }
            _previousNetwork = current with { Timestamp = now };
        }

        if (sections.TryGetValue("HOST", out var host) && host.Count > 0)
            metrics.HostName = host[0].Trim();

        if (sections.TryGetValue("TOP", out var top))
        {
            foreach (var line in top)
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 4) continue;
                if (!int.TryParse(parts[0], out var pid)) continue;
                metrics.TopProcesses.Add(new ProcessMetric
                {
                    Pid = pid,
                    Name = string.Join(' ', parts.Skip(1).Take(parts.Length - 3)),
                    CpuPercent = ParseDouble(parts[^2]),
                    MemoryPercent = ParseDouble(parts[^1])
                });
                if (metrics.TopProcesses.Count >= 10) break;
            }
        }

        return metrics;
    }

    private static Dictionary<string, List<string>> SplitSections(string output)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        List<string>? current = null;
        foreach (var raw in output.Replace("\r", "").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                var key = line[2..].Trim();
                current = new List<string>();
                result[key] = current;
            }
            else if (current is not null && !string.IsNullOrWhiteSpace(line))
            {
                current.Add(line);
            }
        }
        return result;
    }

    private static double CalculateCpu(string first, string second)
    {
        var a = ParseCpuLine(first);
        var b = ParseCpuLine(second);
        if (a is null || b is null) return 0;
        var total = b.Value.Total - a.Value.Total;
        var idle = b.Value.Idle - a.Value.Idle;
        if (total <= 0) return 0;
        return Math.Clamp((total - idle) * 100d / total, 0, 100);
    }

    private static (double Total, double Idle)? ParseCpuLine(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 5 || !parts[0].Equals("cpu", StringComparison.OrdinalIgnoreCase)) return null;
        var values = new List<double>();
        for (var i = 1; i < parts.Length && i <= 8; i++) values.Add(ParseDouble(parts[i]));
        if (values.Count < 4) return null;
        var idle = values[3] + (values.Count > 4 ? values[4] : 0);
        return (values.Sum(), idle);
    }

    private static NetworkSample ParseNetwork(List<string> lines)
    {
        double received = 0, sent = 0;
        foreach (var line in lines)
        {
            var index = line.IndexOf(':');
            if (index <= 0) continue;
            var name = line[..index].Trim();
            if (name == "lo" || name.StartsWith("veth", StringComparison.Ordinal) ||
                name.StartsWith("docker", StringComparison.Ordinal) || name.StartsWith("br-", StringComparison.Ordinal))
                continue;
            var parts = line[(index + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 9) continue;
            received += ParseDouble(parts[0]);
            sent += ParseDouble(parts[8]);
        }
        return new NetworkSample(received, sent, DateTime.UtcNow);
    }

    private static Dictionary<string, long> ParseKeyValues(IEnumerable<string> lines)
    {
        var values = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            var index = line.IndexOf(':');
            if (index <= 0) continue;
            var key = line[..index].Trim();
            var rest = line[(index + 1)..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (rest.Length > 0 && long.TryParse(rest[0], out var value)) values[key] = value * 1024;
        }
        return values;
    }

    private static long GetLong(Dictionary<string, long> values, string key)
        => values.TryGetValue(key, out var value) ? value : 0;

    private static double ParseDouble(string text)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private sealed record NetworkSample(double Received, double Sent, DateTime Timestamp);
}
