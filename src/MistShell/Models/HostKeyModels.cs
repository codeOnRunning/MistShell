namespace MistShell.Models;

public sealed record HostKeyInfo(
    string Host,
    int Port,
    string KeyType,
    string Fingerprint,
    string? ExpectedFingerprint)
{
    public bool IsChanged => !string.IsNullOrWhiteSpace(ExpectedFingerprint) &&
                             !string.Equals(ExpectedFingerprint, Fingerprint, StringComparison.OrdinalIgnoreCase);
}

public enum HostKeyDecision
{
    Reject,
    TrustOnce,
    TrustAndSave
}
