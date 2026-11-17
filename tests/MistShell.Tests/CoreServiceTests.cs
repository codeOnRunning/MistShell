using System.Text.Json;
using MistShell.Models;
using MistShell.Services;

namespace MistShell.Tests;

public class CoreServiceTests
{
    [Fact]
    public void SecretProtectorRoundTrips()
    {
        var cipher = SecretProtector.Protect("p@ssw0rd-测试");
        Assert.NotEqual("p@ssw0rd-测试", cipher);
        Assert.Equal("p@ssw0rd-测试", SecretProtector.Unprotect(cipher));
    }

    [Fact]
    public void UnixPathsAreCombinedCorrectly()
    {
        Assert.Equal("/home/a", MistShell.Models.SftpFileItem.CombineUnix("/", "home/a"));
        Assert.Equal("/home/a", MistShell.Models.SftpFileItem.CombineUnix("/home/", "a"));
        Assert.Equal("/a", MistShell.Models.SftpFileItem.CombineUnix("", "/a"));
    }

    [Fact]
    public void FinalShellImporterReadsConnFilesAndSkipsDuplicates()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "mistshell-import-" + Guid.NewGuid().ToString("N"));
        var conn = Path.Combine(root, "conn");
        Directory.CreateDirectory(conn);
        File.WriteAllText(Path.Combine(conn, "abc_connect_config.json"), """
        {
          "name": "Imported Server",
          "host": "10.0.0.8",
          "port": 22,
          "user_name": "deploy",
          "description": "from finalshell"
        }
        """);
        try
        {
            var result = FinalShellImporter.Import(root, Array.Empty<ConnectionProfile>());
            Assert.Single(result.Profiles);
            Assert.Equal("10.0.0.8", result.Profiles[0].Host);
            Assert.Equal("deploy", result.Profiles[0].Username);
            Assert.Empty(result.Profiles[0].EncryptedPassword);

            var duplicate = FinalShellImporter.Import(root, result.Profiles);
            Assert.Empty(duplicate.Profiles);
            Assert.Equal(1, duplicate.SkippedDuplicates);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void FinalShellImporterPreservesDirectoryGroups()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "mistshell-import-groups-" + Guid.NewGuid().ToString("N"));
        var conn = Path.Combine(root, "conn");
        var subgroup = Path.Combine(conn, "曹操传");
        Directory.CreateDirectory(subgroup);
        File.WriteAllText(Path.Combine(conn, "root_server_connect_config.json"), """
        { "name": "root-server", "host": "10.0.0.10", "port": 22, "user_name": "root" }
        """);
        File.WriteAllText(Path.Combine(subgroup, "game_connect_config.json"), """
        { "name": "game", "host": "10.0.0.11", "port": 22, "user_name": "root" }
        """);
        try
        {
            var result = FinalShellImporter.Import(root, Array.Empty<ConnectionProfile>());
            Assert.Equal(2, result.Profiles.Count);
            Assert.Equal("连接", result.Profiles.Single(p => p.Name == "root-server").Group);
            Assert.Equal("曹操传", result.Profiles.Single(p => p.Name == "game").Group);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void SettingsJsonDoesNotContainInfinity()
    {
        var settings = new AppSettings();
        var json = JsonSerializer.Serialize(settings, JsonStore.Options);
        Assert.DoesNotContain("Infinity", json);
        Assert.Contains("Mist", json);
    }
}
