using System;
using LwpTerm.Connections.Local;
using LwpTerm.Connections.Serial;
using LwpTerm.Connections.Ssh;
using LwpTerm.Connections.Telnet;
using LwpTerm.Core.Security;
using LwpTerm.Core.Sessions;
using LwpTerm.Core.Terminal;
using Microsoft.Extensions.Logging;

namespace LwpTerm.Connections;

public interface ITerminalConnectionFactory
{
    bool Supports(ProtocolType protocol);

    ITerminalConnection Create(ConnectionSettings settings);
}

/// <summary>
/// Maps a session's <see cref="ConnectionSettings"/> to a concrete
/// <see cref="ITerminalConnection"/>, decrypting any secrets on the way.
/// </summary>
public sealed class TerminalConnectionFactory : ITerminalConnectionFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ICredentialStore _credentials;
    private readonly IHostKeyVerifier _hostKeyVerifier;
    private readonly KnownHostsStore _knownHosts;

    public TerminalConnectionFactory(
        ILoggerFactory loggerFactory,
        ICredentialStore credentials,
        IHostKeyVerifier hostKeyVerifier,
        KnownHostsStore knownHosts)
    {
        _loggerFactory = loggerFactory;
        _credentials = credentials;
        _hostKeyVerifier = hostKeyVerifier;
        _knownHosts = knownHosts;
    }

    public bool Supports(ProtocolType protocol) => protocol is
        ProtocolType.LocalShell or ProtocolType.Ssh or ProtocolType.Telnet or ProtocolType.Serial;

    public ITerminalConnection Create(ConnectionSettings settings) => settings switch
    {
        LocalShellConnectionSettings local =>
            new LocalShellConnection(local, _loggerFactory.CreateLogger<LocalShellConnection>()),

        SshConnectionSettings ssh => new SshTerminalConnection(
            ssh,
            Reveal(ssh.ProtectedPassword),
            Reveal(ssh.ProtectedPassphrase),
            _hostKeyVerifier,
            _knownHosts,
            _loggerFactory.CreateLogger<SshTerminalConnection>()),

        TelnetConnectionSettings telnet =>
            new TelnetTerminalConnection(telnet, _loggerFactory.CreateLogger<TelnetTerminalConnection>()),

        SerialConnectionSettings serial =>
            new SerialTerminalConnection(serial, _loggerFactory.CreateLogger<SerialTerminalConnection>()),

        _ => throw new NotSupportedException(
            $"Terminal transport for '{settings.Protocol}' is not implemented yet.")
    };

    private string? Reveal(string? blob) =>
        string.IsNullOrEmpty(blob) ? null : _credentials.Unprotect(blob);
}
