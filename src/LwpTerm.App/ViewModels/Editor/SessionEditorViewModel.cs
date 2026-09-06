using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Ports;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using LwpTerm.Core.Security;
using LwpTerm.Core.Sessions;

namespace LwpTerm.App.ViewModels.Editor;

/// <summary>One entry in the editor's protocol picker strip.</summary>
public sealed record ProtocolChoice(ProtocolType Protocol, string Glyph, string Label);

/// <summary>
/// Backs <c>SessionEditorWindow</c>. Holds a flat, editable projection of every
/// protocol's fields; <see cref="BuildResult"/> collapses them back into a typed
/// <see cref="ConnectionSettings"/> and protects any entered secrets.
/// </summary>
public sealed partial class SessionEditorViewModel : ObservableObject
{
    public IReadOnlyList<ProtocolChoice> ProtocolChoices { get; } = new[]
    {
        new ProtocolChoice(ProtocolType.Ssh, "", "SSH"),
        new ProtocolChoice(ProtocolType.Telnet, "", "Telnet"),
        new ProtocolChoice(ProtocolType.Serial, "", "Serial"),
        new ProtocolChoice(ProtocolType.LocalShell, "", "Shell"),
        new ProtocolChoice(ProtocolType.Sftp, "", "SFTP"),
        new ProtocolChoice(ProtocolType.Ftp, "", "FTP"),
        new ProtocolChoice(ProtocolType.Rdp, "", "RDP"),
        new ProtocolChoice(ProtocolType.Vnc, "", "VNC"),
    };

    private readonly ICredentialStore _credentials;
    private readonly SessionItem? _existing;

    public SessionEditorViewModel(ICredentialStore credentials, SessionItem? existing)
    {
        _credentials = credentials;
        _existing = existing;

        AvailableProtocols = new ObservableCollection<ProtocolType>(Enum.GetValues<ProtocolType>());
        SshAuthMethods = new ObservableCollection<SshAuthMethod>(Enum.GetValues<SshAuthMethod>());
        LocalShellKinds = new ObservableCollection<LocalShellKind>(Enum.GetValues<LocalShellKind>());
        Parities = new ObservableCollection<SerialParity>(Enum.GetValues<SerialParity>());
        StopBitsOptions = new ObservableCollection<SerialStopBits>(Enum.GetValues<SerialStopBits>());
        Handshakes = new ObservableCollection<SerialHandshake>(Enum.GetValues<SerialHandshake>());
        FtpEncryptionModes = new ObservableCollection<FtpEncryptionMode>(Enum.GetValues<FtpEncryptionMode>());
        FtpDataModes = new ObservableCollection<FtpDataConnectionMode>(Enum.GetValues<FtpDataConnectionMode>());
        RdpResolutionModes = new ObservableCollection<RdpResolutionMode>(Enum.GetValues<RdpResolutionMode>());
        SerialPorts = new ObservableCollection<string>(SafeGetPortNames());
        CommonBaudRates = new ObservableCollection<int> { 9600, 19200, 38400, 57600, 115200, 230400, 460800, 921600 };

        if (existing is null)
        {
            _name = "New session";
            _selectedProtocol = ProtocolType.Ssh;
            ApplyDefaultsFor(ProtocolType.Ssh);
        }
        else
        {
            _name = existing.Name;
            _selectedProtocol = existing.Protocol;
            _notes = existing.Notes ?? string.Empty;
            _colorHex = existing.ColorHex ?? string.Empty;
            LoadFrom(existing.Settings);
        }
    }

    public ObservableCollection<ProtocolType> AvailableProtocols { get; }
    public ObservableCollection<SshAuthMethod> SshAuthMethods { get; }
    public ObservableCollection<LocalShellKind> LocalShellKinds { get; }
    public ObservableCollection<SerialParity> Parities { get; }
    public ObservableCollection<SerialStopBits> StopBitsOptions { get; }
    public ObservableCollection<SerialHandshake> Handshakes { get; }
    public ObservableCollection<FtpEncryptionMode> FtpEncryptionModes { get; }
    public ObservableCollection<FtpDataConnectionMode> FtpDataModes { get; }
    public ObservableCollection<RdpResolutionMode> RdpResolutionModes { get; }
    public ObservableCollection<string> SerialPorts { get; }
    public ObservableCollection<int> CommonBaudRates { get; }

    // ---- Common ---------------------------------------------------------------
    [ObservableProperty] private string _name;
    [ObservableProperty] private string _notes = string.Empty;
    [ObservableProperty] private string _colorHex = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSsh), nameof(IsTelnet), nameof(IsSerial), nameof(IsLocalShell),
        nameof(IsSftp), nameof(IsFtp), nameof(IsRdp), nameof(IsVnc), nameof(IsSshLike), nameof(NeedsHost),
        nameof(ShowUsername))]
    private ProtocolType _selectedProtocol;

    public bool IsSsh => SelectedProtocol == ProtocolType.Ssh;
    public bool IsTelnet => SelectedProtocol == ProtocolType.Telnet;
    public bool IsSerial => SelectedProtocol == ProtocolType.Serial;
    public bool IsLocalShell => SelectedProtocol == ProtocolType.LocalShell;
    public bool IsSftp => SelectedProtocol == ProtocolType.Sftp;
    public bool IsFtp => SelectedProtocol == ProtocolType.Ftp;
    public bool IsRdp => SelectedProtocol == ProtocolType.Rdp;
    public bool IsVnc => SelectedProtocol == ProtocolType.Vnc;
    public bool IsSshLike => IsSsh || IsSftp;
    public bool NeedsHost => !IsSerial && !IsLocalShell;
    public bool ShowUsername => IsSsh || IsSftp || IsFtp || IsRdp;

    // ---- Host / port / identity (shared by ssh/telnet/sftp/ftp/rdp/vnc) ------
    [ObservableProperty] private string _host = string.Empty;
    [ObservableProperty] private int _port = 22;
    [ObservableProperty] private string _username = string.Empty;

    // ---- Secrets (plaintext in-flight; pushed here by the view's PasswordBoxes)
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _passphrase = string.Empty;
    public bool HasExistingPassword { get; private set; }
    public bool HasExistingPassphrase { get; private set; }

    // ---- SSH / SFTP ---------------------------------------------------------
    [ObservableProperty] private SshAuthMethod _sshAuthMethod = SshAuthMethod.Password;
    [ObservableProperty] private string _privateKeyPath = string.Empty;
    [ObservableProperty] private int _keepAliveSeconds = 30;
    [ObservableProperty] private string _terminalType = "xterm-256color";
    [ObservableProperty] private string _startupCommand = string.Empty;
    [ObservableProperty] private string _initialRemotePath = ".";

    // ---- Telnet -----------------------------------------------------------
    [ObservableProperty] private string _encoding = "utf-8";

    // ---- Serial ---------------------------------------------------------
    [ObservableProperty] private string _serialPortName = "COM1";
    [ObservableProperty] private int _baudRate = 115200;
    [ObservableProperty] private int _dataBits = 8;
    [ObservableProperty] private SerialParity _parity = SerialParity.None;
    [ObservableProperty] private SerialStopBits _stopBits = SerialStopBits.One;
    [ObservableProperty] private SerialHandshake _handshake = SerialHandshake.None;
    [ObservableProperty] private bool _localEcho;

    // ---- Local shell ---------------------------------------------------
    [ObservableProperty] private LocalShellKind _shellKind = LocalShellKind.PowerShell;
    [ObservableProperty] private string _wslDistro = string.Empty;
    [ObservableProperty] private string _startingDirectory = string.Empty;
    [ObservableProperty] private string _shellArguments = string.Empty;

    // ---- FTP -----------------------------------------------------------
    [ObservableProperty] private FtpEncryptionMode _ftpEncryption = FtpEncryptionMode.None;
    [ObservableProperty] private FtpDataConnectionMode _ftpDataMode = FtpDataConnectionMode.Passive;

    // ---- RDP -----------------------------------------------------------
    [ObservableProperty] private string _domain = string.Empty;
    [ObservableProperty] private RdpResolutionMode _rdpResolutionMode = RdpResolutionMode.FitToWindow;
    [ObservableProperty] private int _rdpWidth = 1280;
    [ObservableProperty] private int _rdpHeight = 800;
    [ObservableProperty] private bool _redirectClipboard = true;
    [ObservableProperty] private bool _redirectDrives;

    // ---- VNC -----------------------------------------------------------
    [ObservableProperty] private bool _viewOnly;
    [ObservableProperty] private int _vncQuality = 6;

    partial void OnSelectedProtocolChanged(ProtocolType value)
    {
        // When the user switches protocol on a brand-new session, refresh sensible
        // defaults for anything they have not touched (port in particular).
        if (_existing is null || _existing.Protocol != value)
        {
            ApplyDefaultsFor(value);
        }
    }

    private void ApplyDefaultsFor(ProtocolType protocol)
    {
        Port = protocol switch
        {
            ProtocolType.Ssh or ProtocolType.Sftp => 22,
            ProtocolType.Telnet => 23,
            ProtocolType.Ftp => 21,
            ProtocolType.Rdp => 3389,
            ProtocolType.Vnc => 5900,
            _ => Port
        };
    }

    private void LoadFrom(ConnectionSettings settings)
    {
        switch (settings)
        {
            case SshConnectionSettings s:
                Host = s.Host; Port = s.Port; Username = s.Username;
                SshAuthMethod = s.AuthMethod; PrivateKeyPath = s.PrivateKeyPath ?? string.Empty;
                KeepAliveSeconds = s.KeepAliveSeconds; TerminalType = s.TerminalType;
                StartupCommand = s.StartupCommand ?? string.Empty;
                HasExistingPassword = !string.IsNullOrEmpty(s.ProtectedPassword);
                HasExistingPassphrase = !string.IsNullOrEmpty(s.ProtectedPassphrase);
                break;
            case SftpConnectionSettings s:
                Host = s.Host; Port = s.Port; Username = s.Username;
                SshAuthMethod = s.AuthMethod; PrivateKeyPath = s.PrivateKeyPath ?? string.Empty;
                InitialRemotePath = s.InitialRemotePath;
                HasExistingPassword = !string.IsNullOrEmpty(s.ProtectedPassword);
                HasExistingPassphrase = !string.IsNullOrEmpty(s.ProtectedPassphrase);
                break;
            case TelnetConnectionSettings s:
                Host = s.Host; Port = s.Port; Encoding = s.Encoding;
                break;
            case SerialConnectionSettings s:
                SerialPortName = s.PortName; BaudRate = s.BaudRate; DataBits = s.DataBits;
                Parity = s.Parity; StopBits = s.StopBits; Handshake = s.Handshake; LocalEcho = s.LocalEcho;
                break;
            case LocalShellConnectionSettings s:
                ShellKind = s.ShellKind; WslDistro = s.WslDistro ?? string.Empty;
                StartingDirectory = s.StartingDirectory ?? string.Empty;
                ShellArguments = s.Arguments ?? string.Empty;
                break;
            case FtpConnectionSettings s:
                Host = s.Host; Port = s.Port; Username = s.Username;
                FtpEncryption = s.EncryptionMode; FtpDataMode = s.DataConnectionMode;
                InitialRemotePath = s.InitialRemotePath;
                HasExistingPassword = !string.IsNullOrEmpty(s.ProtectedPassword);
                break;
            case RdpConnectionSettings s:
                Host = s.Host; Port = s.Port; Username = s.Username; Domain = s.Domain;
                RdpResolutionMode = s.ResolutionMode; RdpWidth = s.Width; RdpHeight = s.Height;
                RedirectClipboard = s.RedirectClipboard; RedirectDrives = s.RedirectDrives;
                HasExistingPassword = !string.IsNullOrEmpty(s.ProtectedPassword);
                break;
            case VncConnectionSettings s:
                Host = s.Host; Port = s.Port; ViewOnly = s.ViewOnly; VncQuality = s.Quality;
                HasExistingPassword = !string.IsNullOrEmpty(s.ProtectedPassword);
                break;
        }
    }

    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            return "Name is required.";
        }

        if (NeedsHost && string.IsNullOrWhiteSpace(Host))
        {
            return "Host is required.";
        }

        if (NeedsHost && Port is <= 0 or > 65535)
        {
            return "Port must be between 1 and 65535.";
        }

        if (IsSshLike && SshAuthMethod == SshAuthMethod.PrivateKey)
        {
            if (string.IsNullOrWhiteSpace(PrivateKeyPath))
            {
                return "A private key file is required for key authentication.";
            }

            if (!File.Exists(PrivateKeyPath))
            {
                return $"Private key file not found:\n{PrivateKeyPath}";
            }
        }

        if (IsSerial && string.IsNullOrWhiteSpace(SerialPortName))
        {
            return "A serial port is required.";
        }

        if (IsLocalShell && ShellKind == LocalShellKind.Wsl && string.IsNullOrWhiteSpace(WslDistro))
        {
            // Allowed — wsl.exe with no distro launches the default.
        }

        return null;
    }

    /// <summary>Produce the persisted item. Call only after <see cref="Validate"/> passes.</summary>
    public SessionItem BuildResult()
    {
        var item = _existing ?? new SessionItem();
        item.Name = Name.Trim();
        item.Protocol = SelectedProtocol;
        item.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes;
        item.ColorHex = string.IsNullOrWhiteSpace(ColorHex) ? null : ColorHex.Trim();
        item.Settings = BuildSettings();
        return item;
    }

    private ConnectionSettings BuildSettings() => SelectedProtocol switch
    {
        ProtocolType.Ssh => new SshConnectionSettings
        {
            Host = Host.Trim(), Port = Port, Username = Username.Trim(), AuthMethod = SshAuthMethod,
            PrivateKeyPath = NullIfBlank(PrivateKeyPath), KeepAliveSeconds = KeepAliveSeconds,
            TerminalType = string.IsNullOrWhiteSpace(TerminalType) ? "xterm-256color" : TerminalType.Trim(),
            StartupCommand = NullIfBlank(StartupCommand),
            ProtectedPassword = ResolveSecret(Password, HasExistingPassword, ExistingSsh?.ProtectedPassword),
            ProtectedPassphrase = ResolveSecret(Passphrase, HasExistingPassphrase, ExistingSsh?.ProtectedPassphrase)
        },
        ProtocolType.Sftp => new SftpConnectionSettings
        {
            Host = Host.Trim(), Port = Port, Username = Username.Trim(), AuthMethod = SshAuthMethod,
            PrivateKeyPath = NullIfBlank(PrivateKeyPath),
            InitialRemotePath = string.IsNullOrWhiteSpace(InitialRemotePath) ? "." : InitialRemotePath.Trim(),
            ProtectedPassword = ResolveSecret(Password, HasExistingPassword, ExistingSftp?.ProtectedPassword),
            ProtectedPassphrase = ResolveSecret(Passphrase, HasExistingPassphrase, ExistingSftp?.ProtectedPassphrase)
        },
        ProtocolType.Telnet => new TelnetConnectionSettings
        {
            Host = Host.Trim(), Port = Port,
            Encoding = string.IsNullOrWhiteSpace(Encoding) ? "utf-8" : Encoding.Trim()
        },
        ProtocolType.Serial => new SerialConnectionSettings
        {
            PortName = SerialPortName.Trim(), BaudRate = BaudRate, DataBits = DataBits,
            Parity = Parity, StopBits = StopBits, Handshake = Handshake, LocalEcho = LocalEcho
        },
        ProtocolType.LocalShell => new LocalShellConnectionSettings
        {
            ShellKind = ShellKind, WslDistro = NullIfBlank(WslDistro),
            StartingDirectory = NullIfBlank(StartingDirectory), Arguments = NullIfBlank(ShellArguments)
        },
        ProtocolType.Ftp => new FtpConnectionSettings
        {
            Host = Host.Trim(), Port = Port, Username = Username.Trim(),
            EncryptionMode = FtpEncryption, DataConnectionMode = FtpDataMode,
            InitialRemotePath = string.IsNullOrWhiteSpace(InitialRemotePath) ? "/" : InitialRemotePath.Trim(),
            ProtectedPassword = ResolveSecret(Password, HasExistingPassword, ExistingFtp?.ProtectedPassword)
        },
        ProtocolType.Rdp => new RdpConnectionSettings
        {
            Host = Host.Trim(), Port = Port, Username = Username.Trim(), Domain = Domain.Trim(),
            ResolutionMode = RdpResolutionMode, Width = RdpWidth, Height = RdpHeight,
            RedirectClipboard = RedirectClipboard, RedirectDrives = RedirectDrives,
            ProtectedPassword = ResolveSecret(Password, HasExistingPassword, ExistingRdp?.ProtectedPassword)
        },
        ProtocolType.Vnc => new VncConnectionSettings
        {
            Host = Host.Trim(), Port = Port, ViewOnly = ViewOnly, Quality = VncQuality,
            ProtectedPassword = ResolveSecret(Password, HasExistingPassword, ExistingVnc?.ProtectedPassword)
        },
        _ => ConnectionSettings.CreateDefault(SelectedProtocol)
    };

    private SshConnectionSettings? ExistingSsh => _existing?.Settings as SshConnectionSettings;
    private SftpConnectionSettings? ExistingSftp => _existing?.Settings as SftpConnectionSettings;
    private FtpConnectionSettings? ExistingFtp => _existing?.Settings as FtpConnectionSettings;
    private RdpConnectionSettings? ExistingRdp => _existing?.Settings as RdpConnectionSettings;
    private VncConnectionSettings? ExistingVnc => _existing?.Settings as VncConnectionSettings;

    /// <summary>
    /// Blank input + an existing secret ⇒ keep the old blob. Non-blank input ⇒
    /// protect and store it. Blank input + no existing ⇒ null.
    /// </summary>
    private string? ResolveSecret(string plaintext, bool hadExisting, string? existingBlob)
    {
        if (!string.IsNullOrEmpty(plaintext))
        {
            return _credentials.Protect(plaintext);
        }

        return hadExisting ? existingBlob : null;
    }

    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IEnumerable<string> SafeGetPortNames()
    {
        try
        {
            return SerialPort.GetPortNames().OrderBy(p => p);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
