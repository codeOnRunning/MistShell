using MistShell.Models;

namespace MistShell.Services;

public sealed class ProfileStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public List<ConnectionProfile> Load()
    {
        var connections = JsonStore.Load(AppPaths.ConnectionsFile, () => new List<ConnectionProfile>());
        foreach (var profile in connections)
        {
            profile.PropertyChanged += (_, _) =>
            {
                profile.UpdatedUtc = DateTimeOffset.UtcNow;
            };
        }
        return connections;
    }

    public async Task SaveAsync(IEnumerable<ConnectionProfile> connections)
    {
        await _gate.WaitAsync();
        try
        {
            var snapshot = connections.Select(c => c.Clone()).ToList();
            await Task.Run(() => JsonStore.Save(AppPaths.ConnectionsFile, snapshot));
        }
        finally
        {
            _gate.Release();
        }
    }
}

public sealed class SettingsStore
{
    public AppSettings Load()
    {
        var settings = JsonStore.Load(AppPaths.SettingsFile, () => new AppSettings());
        if (settings.Snippets.Count == 0)
            settings.Snippets = AppSettings.CreateDefaultSnippets();

        if (settings.SettingsVersion < 2)
        {
            var defaults = AppSettings.CreateDefaultSnippets();
            if (settings.Snippets.Count == 0)
            {
                settings.Snippets = defaults;
            }
            else
            {
                var colorSnippet = defaults.FirstOrDefault(s => s.Name == "彩色 ls / zsh 高亮");
                if (colorSnippet is not null &&
                    !settings.Snippets.Any(s => s.Name == colorSnippet.Name))
                    settings.Snippets.Add(colorSnippet);
            }
            settings.SettingsVersion = 2;
        }
        return settings;
    }

    public void Save(AppSettings settings) => JsonStore.Save(AppPaths.SettingsFile, settings);

    public static (string Password, string Passphrase) ResolveCredentials(ConnectionProfile profile) =>
        (SecretProtector.Unprotect(profile.EncryptedPassword), SecretProtector.Unprotect(profile.EncryptedPassphrase));

    public static string? ResolvePrivateKeyContent(AppSettings settings, ConnectionProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.PrivateKeyId)) return null;
        var saved = (settings.PrivateKeys ?? new List<SavedPrivateKey>())
            .FirstOrDefault(key => key.Id == profile.PrivateKeyId);
        if (saved is null || string.IsNullOrWhiteSpace(saved.EncryptedContent)) return null;
        try { return SecretProtector.Unprotect(saved.EncryptedContent); }
        catch { return null; }
    }
}

public static class AppLog
{
    private const long MaxLogBytes = 5 * 1024 * 1024;
    private const int MaxArchiveFiles = 3;
    private static readonly object Gate = new();

    public static void Write(string message, Exception? exception = null)
    {
        try
        {
            AppPaths.EnsureCreated();
            var line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
            if (exception is not null) line += Environment.NewLine + exception;
            lock (Gate)
            {
                RotateIfNeeded(AppPaths.LogFile);
                File.AppendAllText(AppPaths.LogFile, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never crash the app.
        }
    }

    private static void RotateIfNeeded(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length < MaxLogBytes) return;

            var archive3 = path + ".3";
            var archive2 = path + ".2";
            var archive1 = path + ".1";
            if (File.Exists(archive3)) File.Delete(archive3);
            if (File.Exists(archive2)) File.Move(archive2, archive3);
            if (File.Exists(archive1)) File.Move(archive1, archive2);
            File.Move(path, archive1);
        }
        catch
        {
            // If rotation fails, continue appending to the current log.
        }
    }
}
