using System.Text.Json.Serialization;
using MistShell.Infrastructure;

namespace MistShell.Models;

public enum SshAuthMethod
{
    Password,
    PrivateKey
}

public sealed class ConnectionProfile : ObservableObject
{
    private string _name = "新连接";
    private string _group = "默认";
    private string _host = "";
    private int _port = 22;
    private string _username = "root";
    private SshAuthMethod _authMethod = SshAuthMethod.Password;
    private string _color = "#3D7C73";
    private bool _isFavorite;
    private string? _notes;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string Group { get => _group; set => SetProperty(ref _group, value); }
    public string Host { get => _host; set { if (SetProperty(ref _host, value)) OnPropertiesChanged(nameof(Endpoint)); } }
    public int Port { get => _port; set { if (SetProperty(ref _port, value)) OnPropertiesChanged(nameof(Endpoint)); } }
    public string Username { get => _username; set { if (SetProperty(ref _username, value)) OnPropertiesChanged(nameof(Endpoint)); } }
    public SshAuthMethod AuthMethod { get => _authMethod; set { if (SetProperty(ref _authMethod, value)) OnPropertiesChanged(nameof(AuthLabel)); } }
    public string EncryptedPassword { get; set; } = "";
    public string PrivateKeyPath { get; set; } = "";
    public string PrivateKeyId { get; set; } = "";
    public string EncryptedPassphrase { get; set; } = "";
    public string Color { get => _color; set => SetProperty(ref _color, value); }
    public bool IsFavorite { get => _isFavorite; set => SetProperty(ref _isFavorite, value); }
    public string? Notes { get => _notes; set => SetProperty(ref _notes, value); }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastConnectedUtc { get; set; }

    [JsonIgnore] public string Endpoint => $"{Username}@{Host}:{Port}";
    [JsonIgnore] public string AuthLabel => AuthMethod == SshAuthMethod.Password ? "密码" : "私钥";
    [JsonIgnore] public string Initial => string.IsNullOrWhiteSpace(Name) ? "?" : Name.Trim()[0].ToString().ToUpperInvariant();

    public ConnectionProfile Clone() => new()
    {
        Id = Id,
        Name = Name,
        Group = Group,
        Host = Host,
        Port = Port,
        Username = Username,
        AuthMethod = AuthMethod,
        EncryptedPassword = EncryptedPassword,
        PrivateKeyPath = PrivateKeyPath,
        PrivateKeyId = PrivateKeyId,
        EncryptedPassphrase = EncryptedPassphrase,
        Color = Color,
        IsFavorite = IsFavorite,
        Notes = Notes,
        CreatedUtc = CreatedUtc,
        UpdatedUtc = UpdatedUtc,
        LastConnectedUtc = LastConnectedUtc
    };
}
