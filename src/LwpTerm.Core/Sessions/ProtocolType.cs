namespace LwpTerm.Core.Sessions;

public enum ProtocolType
{
    Ssh,
    Telnet,
    Serial,
    LocalShell,
    Sftp,
    Ftp,
    Rdp,
    Vnc
}

public enum SshAuthMethod
{
    Password,
    PrivateKey,
    Agent
}

public enum LocalShellKind
{
    PowerShell,
    Pwsh,
    Cmd,
    Wsl
}

public enum SerialParity
{
    None,
    Odd,
    Even,
    Mark,
    Space
}

public enum SerialStopBits
{
    One,
    OnePointFive,
    Two
}

public enum SerialHandshake
{
    None,
    XOnXOff,
    RequestToSend,
    RequestToSendXOnXOff
}

public enum FtpEncryptionMode
{
    None,
    Explicit,
    Implicit
}

public enum FtpDataConnectionMode
{
    Passive,
    Active
}

public enum RdpResolutionMode
{
    FitToWindow,
    Fixed
}
