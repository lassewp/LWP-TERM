using System;

namespace LwpTerm.Core.Sessions;

/// <summary>
/// Parses a quick-connect string into a transient <see cref="SessionItem"/>.
/// Accepts <c>scheme://user@host:port</c>, <c>user@host:port</c>, or bare
/// <c>host</c>. Recognised schemes: ssh (default), telnet, rdp, vnc, ftp, sftp.
/// </summary>
public static class QuickConnectParser
{
    public static bool TryParse(string? input, out SessionItem session)
    {
        session = null!;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var text = input.Trim();
        var scheme = "ssh";

        var schemeSplit = text.IndexOf("://", StringComparison.Ordinal);
        if (schemeSplit > 0)
        {
            scheme = text[..schemeSplit].ToLowerInvariant();
            text = text[(schemeSplit + 3)..];
        }

        string? user = null;
        var at = text.LastIndexOf('@');
        if (at >= 0)
        {
            user = text[..at];
            text = text[(at + 1)..];
        }

        var host = text;
        int? port = null;
        var colon = text.LastIndexOf(':');
        if (colon >= 0 && int.TryParse(text[(colon + 1)..], out var p))
        {
            host = text[..colon];
            port = p;
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        var name = user is null ? $"{host} ({scheme})" : $"{user}@{host}";
        session = new SessionItem { Name = name, Protocol = MapProtocol(scheme) };
        session.Settings = Build(session.Protocol, host, port, user);
        return true;
    }

    private static ProtocolType MapProtocol(string scheme) => scheme switch
    {
        "telnet" => ProtocolType.Telnet,
        "rdp" => ProtocolType.Rdp,
        "vnc" => ProtocolType.Vnc,
        "ftp" => ProtocolType.Ftp,
        "sftp" => ProtocolType.Sftp,
        _ => ProtocolType.Ssh
    };

    private static ConnectionSettings Build(ProtocolType protocol, string host, int? port, string? user) => protocol switch
    {
        ProtocolType.Telnet => new TelnetConnectionSettings { Host = host, Port = port ?? 23 },
        ProtocolType.Rdp => new RdpConnectionSettings { Host = host, Port = port ?? 3389, Username = user ?? "" },
        ProtocolType.Vnc => new VncConnectionSettings { Host = host, Port = port ?? 5900 },
        ProtocolType.Ftp => new FtpConnectionSettings { Host = host, Port = port ?? 21, Username = user ?? "anonymous" },
        ProtocolType.Sftp => new SftpConnectionSettings { Host = host, Port = port ?? 22, Username = user ?? "" },
        _ => new SshConnectionSettings { Host = host, Port = port ?? 22, Username = user ?? "" }
    };
}
