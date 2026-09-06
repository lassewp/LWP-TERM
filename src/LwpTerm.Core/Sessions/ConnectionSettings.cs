using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LwpTerm.Core.Sessions;

/// <summary>
/// Base for the per-protocol connection parameters attached to a
/// <see cref="SessionItem"/>. Serialised polymorphically via a <c>kind</c>
/// discriminator. Secret fields hold opaque blobs produced by
/// <c>ICredentialStore.Protect</c>, never plaintext.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(SshConnectionSettings), "ssh")]
[JsonDerivedType(typeof(TelnetConnectionSettings), "telnet")]
[JsonDerivedType(typeof(SerialConnectionSettings), "serial")]
[JsonDerivedType(typeof(LocalShellConnectionSettings), "localShell")]
[JsonDerivedType(typeof(SftpConnectionSettings), "sftp")]
[JsonDerivedType(typeof(FtpConnectionSettings), "ftp")]
[JsonDerivedType(typeof(RdpConnectionSettings), "rdp")]
[JsonDerivedType(typeof(VncConnectionSettings), "vnc")]
public abstract class ConnectionSettings
{
    [JsonIgnore]
    public abstract ProtocolType Protocol { get; }

    /// <summary>Short, human-readable target shown next to the session name.</summary>
    [JsonIgnore]
    public abstract string Summary { get; }

    public static ConnectionSettings CreateDefault(ProtocolType protocol) => protocol switch
    {
        ProtocolType.Ssh => new SshConnectionSettings(),
        ProtocolType.Telnet => new TelnetConnectionSettings(),
        ProtocolType.Serial => new SerialConnectionSettings(),
        ProtocolType.LocalShell => new LocalShellConnectionSettings(),
        ProtocolType.Sftp => new SftpConnectionSettings(),
        ProtocolType.Ftp => new FtpConnectionSettings(),
        ProtocolType.Rdp => new RdpConnectionSettings(),
        ProtocolType.Vnc => new VncConnectionSettings(),
        _ => new LocalShellConnectionSettings()
    };
}

public sealed class SshConnectionSettings : ConnectionSettings
{
    public override ProtocolType Protocol => ProtocolType.Ssh;

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 22;
    public string Username { get; set; } = string.Empty;
    public SshAuthMethod AuthMethod { get; set; } = SshAuthMethod.Password;

    /// <summary>Opaque blob from ICredentialStore. Used when <see cref="AuthMethod"/> is Password.</summary>
    public string? ProtectedPassword { get; set; }

    public string? PrivateKeyPath { get; set; }

    /// <summary>Opaque blob from ICredentialStore. Passphrase for the private key.</summary>
    public string? ProtectedPassphrase { get; set; }

    public int KeepAliveSeconds { get; set; } = 30;
    public string TerminalType { get; set; } = "xterm-256color";
    public string? StartupCommand { get; set; }
    public Dictionary<string, string> Environment { get; set; } = new();

    public override string Summary =>
        string.IsNullOrWhiteSpace(Host) ? "ssh (unconfigured)" :
        $"{(string.IsNullOrWhiteSpace(Username) ? "" : Username + "@")}{Host}:{Port}";
}

public sealed class TelnetConnectionSettings : ConnectionSettings
{
    public override ProtocolType Protocol => ProtocolType.Telnet;

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 23;
    public string Encoding { get; set; } = "utf-8";

    public override string Summary =>
        string.IsNullOrWhiteSpace(Host) ? "telnet (unconfigured)" : $"{Host}:{Port}";
}

public sealed class SerialConnectionSettings : ConnectionSettings
{
    public override ProtocolType Protocol => ProtocolType.Serial;

    public string PortName { get; set; } = "COM1";
    public int BaudRate { get; set; } = 115200;
    public int DataBits { get; set; } = 8;
    public SerialParity Parity { get; set; } = SerialParity.None;
    public SerialStopBits StopBits { get; set; } = SerialStopBits.One;
    public SerialHandshake Handshake { get; set; } = SerialHandshake.None;
    public bool LocalEcho { get; set; }

    public override string Summary =>
        $"{PortName} @ {BaudRate} {DataBits}{ParityLetter}{StopBitsDigits}";

    private char ParityLetter => Parity switch
    {
        SerialParity.None => 'N',
        SerialParity.Odd => 'O',
        SerialParity.Even => 'E',
        SerialParity.Mark => 'M',
        SerialParity.Space => 'S',
        _ => 'N'
    };

    private string StopBitsDigits => StopBits switch
    {
        SerialStopBits.One => "1",
        SerialStopBits.OnePointFive => "1.5",
        SerialStopBits.Two => "2",
        _ => "1"
    };
}

public sealed class LocalShellConnectionSettings : ConnectionSettings
{
    public override ProtocolType Protocol => ProtocolType.LocalShell;

    public LocalShellKind ShellKind { get; set; } = LocalShellKind.PowerShell;
    public string? WslDistro { get; set; }
    public string? StartingDirectory { get; set; }
    public string? Arguments { get; set; }

    public override string Summary => ShellKind switch
    {
        LocalShellKind.PowerShell => "powershell.exe",
        LocalShellKind.Pwsh => "pwsh.exe",
        LocalShellKind.Cmd => "cmd.exe",
        LocalShellKind.Wsl => string.IsNullOrWhiteSpace(WslDistro) ? "wsl.exe" : $"wsl.exe -d {WslDistro}",
        _ => "shell"
    };
}

public sealed class SftpConnectionSettings : ConnectionSettings
{
    public override ProtocolType Protocol => ProtocolType.Sftp;

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 22;
    public string Username { get; set; } = string.Empty;
    public SshAuthMethod AuthMethod { get; set; } = SshAuthMethod.Password;
    public string? ProtectedPassword { get; set; }
    public string? PrivateKeyPath { get; set; }
    public string? ProtectedPassphrase { get; set; }
    public string InitialRemotePath { get; set; } = ".";

    public override string Summary =>
        string.IsNullOrWhiteSpace(Host) ? "sftp (unconfigured)" :
        $"{(string.IsNullOrWhiteSpace(Username) ? "" : Username + "@")}{Host}:{Port}";
}

public sealed class FtpConnectionSettings : ConnectionSettings
{
    public override ProtocolType Protocol => ProtocolType.Ftp;

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 21;
    public string Username { get; set; } = "anonymous";
    public string? ProtectedPassword { get; set; }
    public FtpEncryptionMode EncryptionMode { get; set; } = FtpEncryptionMode.None;
    public FtpDataConnectionMode DataConnectionMode { get; set; } = FtpDataConnectionMode.Passive;
    public string InitialRemotePath { get; set; } = "/";

    public override string Summary =>
        string.IsNullOrWhiteSpace(Host) ? "ftp (unconfigured)" : $"{Host}:{Port}";
}

public sealed class RdpConnectionSettings : ConnectionSettings
{
    public override ProtocolType Protocol => ProtocolType.Rdp;

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 3389;
    public string Username { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string? ProtectedPassword { get; set; }
    public RdpResolutionMode ResolutionMode { get; set; } = RdpResolutionMode.FitToWindow;
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 800;
    public bool RedirectClipboard { get; set; } = true;
    public bool RedirectDrives { get; set; }

    public override string Summary =>
        string.IsNullOrWhiteSpace(Host) ? "rdp (unconfigured)" : $"{Host}:{Port}";
}

public sealed class VncConnectionSettings : ConnectionSettings
{
    public override ProtocolType Protocol => ProtocolType.Vnc;

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 5900;
    public string? ProtectedPassword { get; set; }
    public bool ViewOnly { get; set; }

    /// <summary>0 = best compression … 9 = best quality. Advisory only.</summary>
    public int Quality { get; set; } = 6;

    public override string Summary =>
        string.IsNullOrWhiteSpace(Host) ? "vnc (unconfigured)" : $"{Host}:{Port}";
}
