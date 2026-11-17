using System.Text.Json;
using MistShell.Models;

namespace MistShell.Services;

public sealed class ImportResult
{
    public List<ConnectionProfile> Profiles { get; } = new();
    public int TotalFiles { get; set; }
    public int SkippedDuplicates { get; set; }
    public int FailedFiles { get; set; }
}

public static class FinalShellImporter
{
    public static ImportResult Import(string selectedFolder, IEnumerable<ConnectionProfile> existing)
    {
        var result = new ImportResult();
        var connFolder = selectedFolder;
        var nested = Path.Combine(selectedFolder, "conn");
        if (Directory.Exists(nested)) connFolder = nested;
        if (!Directory.Exists(connFolder)) return result;

        var existingKeys = new HashSet<string>(
            existing.Select(p => $"{p.Host}:{p.Port}:{p.Username}".ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase);
        var files = Directory.GetFiles(connFolder, "*_connect_config.json", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        result.TotalFiles = files.Length;

        foreach (var file in files)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var root = doc.RootElement;
                var host = GetString(root, "host");
                var username = GetString(root, "user_name");
                if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(username))
                {
                    result.FailedFiles++;
                    continue;
                }

                var port = GetInt(root, "port", 22);
                var key = $"{host}:{port}:{username}".ToLowerInvariant();
                if (!existingKeys.Add(key))
                {
                    result.SkippedDuplicates++;
                    continue;
                }

                var name = GetString(root, "name");
                if (string.IsNullOrWhiteSpace(name)) name = host;
                var relativeDirectory = Path.GetDirectoryName(Path.GetRelativePath(connFolder, file));
                var group = string.IsNullOrWhiteSpace(relativeDirectory)
                    ? "连接"
                    : relativeDirectory.Replace('\\', '/');
                var profile = new ConnectionProfile
                {
                    Name = name,
                    Group = group,
                    Host = host,
                    Port = port,
                    Username = username,
                    AuthMethod = SshAuthMethod.Password,
                    Notes = GetString(root, "description"),
                    Color = PickColor(host)
                };
                result.Profiles.Add(profile);
            }
            catch
            {
                result.FailedFiles++;
            }
        }

        return result;
    }

    private static string GetString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)) return "";
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number => value.ToString(),
            _ => ""
        };
    }

    private static int GetInt(JsonElement root, string name, int fallback)
    {
        if (!root.TryGetProperty(name, out var value)) return fallback;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed)) return parsed;
        return fallback;
    }

    private static string PickColor(string host)
    {
        string[] colors = ["#3D7C73", "#8A6245", "#4C6382", "#9A5B5B", "#6B5B7A", "#5F7B4C"];
        return colors[Math.Abs(host.GetHashCode()) % colors.Length];
    }
}
