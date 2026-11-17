using System.IO;
using MistShell.Models;
using MistShell.Services;

var failures = new List<string>();
void Check(string name, Action action)
{
    try
    {
        action();
        Console.WriteLine("PASS  " + name);
    }
    catch (Exception ex)
    {
        failures.Add(name + ": " + ex.Message);
        Console.WriteLine("FAIL  " + name + " -> " + ex.Message);
    }
}

Check("DPAPI round trip", () =>
{
    const string plain = "p@ssw0rd-test-123";
    var cipher = SecretProtector.Protect(plain);
    if (cipher == plain || SecretProtector.Unprotect(cipher) != plain) throw new Exception("cipher mismatch");
});

Check("Monitor parser", () =>
{
    const string sample = """
@@UP
86400.00 12345.00
@@LOAD
0.42 0.35 0.28 2/123 4567
@@CPU
cpu  12000 100 3000 50000 1000 0 200 0 0 0
cpu  12010 100 3010 50020 1000 0 200 0 0 0
@@MEM
MemTotal: 16384000 kB
MemAvailable: 8192000 kB
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
""";
    var metrics = new SystemMonitor().Parse(sample, DateTime.UtcNow);
    if (Math.Abs(metrics.CpuPercent - 50) > 0.1) throw new Exception("CPU parse");
    if (Math.Abs(metrics.MemoryPercent - 50) > 0.1) throw new Exception("memory parse");
    if (Math.Abs(metrics.DiskPercent - 50) > 0.1) throw new Exception("disk parse");
    if (metrics.HostName != "mistshell-test") throw new Exception("host parse");
    if (metrics.TopProcesses.Count != 1) throw new Exception("process parse");
});

Check("FinalShell import", () =>
{
    var root = Path.Combine(AppContext.BaseDirectory, "mistshell-smoke-" + Guid.NewGuid().ToString("N"));
    var conn = Path.Combine(root, "conn");
    Directory.CreateDirectory(conn);
    File.WriteAllText(Path.Combine(conn, "abc_connect_config.json"),
        """{"name":"Imported","host":"10.0.0.8","port":22,"user_name":"deploy"}""");
    try
    {
        var result = FinalShellImporter.Import(root, Array.Empty<ConnectionProfile>());
        if (result.Profiles.Count != 1) throw new Exception("import count");
        if (result.Profiles[0].Host != "10.0.0.8") throw new Exception("import host");
    }
    finally
    {
        Directory.Delete(root, true);
    }
});

Check("Settings JSON", () =>
{
    var json = System.Text.Json.JsonSerializer.Serialize(new AppSettings(), JsonStore.Options);
    if (json.Contains("Infinity", StringComparison.Ordinal)) throw new Exception("infinity");
    if (!json.Contains("Mist", StringComparison.Ordinal)) throw new Exception("theme");
});

Console.WriteLine();
Console.WriteLine(failures.Count == 0 ? "ALL SMOKE TESTS PASSED" : $"{failures.Count} FAILURE(S)");
return failures.Count == 0 ? 0 : 1;
