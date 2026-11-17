using System.Text.Json;

namespace MistShell.Services;

public static class AppPaths
{
    private static string? _root;
    private static string? _webViewRoot;
    private static string? _downloadsRoot;
    private static bool _migrationChecked;

    public static string Root
    {
        get
        {
            if (_root is not null) return _root;

            var overridePath = Environment.GetEnvironmentVariable("MISTSHELL_HOME");
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                _root = overridePath;
                Directory.CreateDirectory(_root);
                return _root;
            }

            var portable = Path.Combine(AppContext.BaseDirectory, "data");
            if (TryPrepare(portable))
            {
                _root = portable;
                return _root;
            }

            var local = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MistShell");
            if (TryPrepare(local))
            {
                _root = local;
                return _root;
            }

            _root = Path.Combine(Path.GetTempPath(), "MistShell");
            Directory.CreateDirectory(_root);
            return _root;
        }
    }

    public static string ConnectionsFile => Path.Combine(Root, "connections.json");
    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string LogFile => Path.Combine(Root, "mistshell.log");
    public static string WebViewDataFolder => _webViewRoot ??= PrepareSubfolder("webview");
    public static string DownloadsFolder => _downloadsRoot ??= PrepareSubfolder("downloads");
    public static string TerminalAssets => Path.Combine(AppContext.BaseDirectory, "Assets", "terminal", "web");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        TryMigrateLegacyData();
        Directory.CreateDirectory(WebViewDataFolder);
        Directory.CreateDirectory(DownloadsFolder);
    }

    private static void TryMigrateLegacyData()
    {
        if (_migrationChecked) return;
        _migrationChecked = true;

        // A fresh extraction should not start with an empty connection list if
        // the previous version's portable data folder is still next to it.
        if (HasNonEmptyConnections(ConnectionsFile)) return;

        var candidates = new List<string>();
        var parent = Directory.GetParent(AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))?.FullName;
        if (!string.IsNullOrWhiteSpace(parent))
        {
            try
            {
                candidates.AddRange(Directory.EnumerateDirectories(parent, "MistShell-*", SearchOption.TopDirectoryOnly)
                    .Select(dir => Path.Combine(dir, "data"))
                    .Where(dir => !string.Equals(dir, Root, StringComparison.OrdinalIgnoreCase)));
            }
            catch { }
        }

        var local = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MistShell");
        if (!string.Equals(local, Root, StringComparison.OrdinalIgnoreCase)) candidates.Add(local);

        var source = candidates
            .Where(HasNonEmptyConnectionsFile)
            .OrderByDescending(dir => File.GetLastWriteTimeUtc(Path.Combine(dir, "connections.json")))
            .FirstOrDefault();
        if (source is null) return;

        try
        {
            File.Copy(Path.Combine(source, "connections.json"), ConnectionsFile, true);
            var settings = Path.Combine(source, "settings.json");
            if (File.Exists(settings) && !File.Exists(SettingsFile))
                File.Copy(settings, SettingsFile, true);
        }
        catch
        {
            // Migration is best-effort; a read-only/empty new folder must still start.
        }
    }

    private static bool HasNonEmptyConnectionsFile(string dataDirectory) =>
        HasNonEmptyConnections(Path.Combine(dataDirectory, "connections.json"));

    private static bool HasNonEmptyConnections(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.ValueKind == JsonValueKind.Array &&
                   doc.RootElement.GetArrayLength() > 0;
        }
        catch
        {
            return false;
        }
    }

    private static string PrepareSubfolder(string name)
    {
        var preferred = Path.Combine(Root, name);
        if (TryPrepare(preferred)) return preferred;
        var fallback = Path.Combine(Path.GetTempPath(), "MistShell", name);
        Directory.CreateDirectory(fallback);
        return fallback;
    }

    private static bool TryPrepare(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            var probe = Path.Combine(path, ".write-test");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
