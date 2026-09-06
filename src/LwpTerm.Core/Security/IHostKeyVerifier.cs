namespace LwpTerm.Core.Security;

public sealed record HostKeyPrompt(
    string Host,
    int Port,
    string KeyAlgorithm,
    string FingerprintSha256,
    string FingerprintMd5,
    bool IsChanged);

/// <summary>
/// Decides whether an SSH host key should be trusted. The connection layer calls
/// this synchronously from a background thread during connect; the App
/// implementation marshals to the UI to prompt, and records the answer.
/// </summary>
public interface IHostKeyVerifier
{
    bool Verify(HostKeyPrompt prompt);
}

/// <summary>Trusts every host and remembers nothing. For tests and headless use only.</summary>
public sealed class TrustAllHostKeyVerifier : IHostKeyVerifier
{
    public bool Verify(HostKeyPrompt prompt) => true;
}
