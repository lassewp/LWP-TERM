using System;
using LwpTerm.Connections.Local;
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
/// <see cref="ITerminalConnection"/>. SSH / Telnet / Serial are added in M3.
/// </summary>
public sealed class TerminalConnectionFactory : ITerminalConnectionFactory
{
    private readonly ILoggerFactory _loggerFactory;

    public TerminalConnectionFactory(ILoggerFactory loggerFactory) => _loggerFactory = loggerFactory;

    public bool Supports(ProtocolType protocol) => protocol is ProtocolType.LocalShell;

    public ITerminalConnection Create(ConnectionSettings settings) => settings switch
    {
        LocalShellConnectionSettings local =>
            new LocalShellConnection(local, _loggerFactory.CreateLogger<LocalShellConnection>()),
        _ => throw new NotSupportedException(
            $"Terminal transport for '{settings.Protocol}' is not implemented yet.")
    };
}
