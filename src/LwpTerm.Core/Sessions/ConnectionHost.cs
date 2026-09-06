namespace LwpTerm.Core.Sessions;

/// <summary>Reads / writes the primary host of a connection, for the protocols that have one.</summary>
public static class ConnectionHost
{
    public static bool HasHost(ConnectionSettings settings) => Get(settings) is not null;

    public static string? Get(ConnectionSettings settings) => settings switch
    {
        SshConnectionSettings s => s.Host,
        TelnetConnectionSettings s => s.Host,
        SftpConnectionSettings s => s.Host,
        FtpConnectionSettings s => s.Host,
        RdpConnectionSettings s => s.Host,
        VncConnectionSettings s => s.Host,
        _ => null
    };

    public static void Set(ConnectionSettings settings, string host)
    {
        switch (settings)
        {
            case SshConnectionSettings s: s.Host = host; break;
            case TelnetConnectionSettings s: s.Host = host; break;
            case SftpConnectionSettings s: s.Host = host; break;
            case FtpConnectionSettings s: s.Host = host; break;
            case RdpConnectionSettings s: s.Host = host; break;
            case VncConnectionSettings s: s.Host = host; break;
        }
    }
}
