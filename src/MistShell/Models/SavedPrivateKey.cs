using System.Text.Json.Serialization;

namespace MistShell.Models;

public sealed class SavedPrivateKey
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string EncryptedContent { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "(未命名私钥)" : Name;

    public override string ToString() => DisplayName;
}
