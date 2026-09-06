using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace LwpTerm.Core.Sessions;

/// <summary>
/// Reads saved PuTTY sessions from <c>HKCU\Software\SimonTatham\PuTTY\Sessions</c>
/// and converts the ones LWP-TERM understands (SSH / Telnet / Serial) into
/// <see cref="SessionItem"/>s.
/// </summary>
public static class PuttyImporter
{
    private const string SessionsKey = @"Software\SimonTatham\PuTTY\Sessions";

    public static bool IsAvailable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SessionsKey);
        return key is { SubKeyCount: > 0 };
    }

    public static IReadOnlyList<SessionItem> Import()
    {
        var results = new List<SessionItem>();

        using var sessions = Registry.CurrentUser.OpenSubKey(SessionsKey);
        if (sessions is null)
        {
            return results;
        }

        foreach (var rawName in sessions.GetSubKeyNames())
        {
            using var s = sessions.OpenSubKey(rawName);
            if (s is null)
            {
                continue;
            }

            var name = Uri.UnescapeDataString(rawName);
            var protocol = (s.GetValue("Protocol") as string ?? "ssh").ToLowerInvariant();
            var host = s.GetValue("HostName") as string ?? string.Empty;
            var port = ToInt(s.GetValue("PortNumber"), protocol == "telnet" ? 23 : 22);
            var user = s.GetValue("UserName") as string ?? string.Empty;
            var keyFile = s.GetValue("PublicKeyFile") as string;

            SessionItem? item = protocol switch
            {
                "ssh" => new SessionItem
                {
                    Name = name,
                    Protocol = ProtocolType.Ssh,
                    Settings = new SshConnectionSettings
                    {
                        Host = host,
                        Port = port,
                        Username = user,
                        AuthMethod = string.IsNullOrWhiteSpace(keyFile) ? SshAuthMethod.Password : SshAuthMethod.PrivateKey,
                        PrivateKeyPath = string.IsNullOrWhiteSpace(keyFile) ? null : keyFile
                    }
                },
                "telnet" => new SessionItem
                {
                    Name = name,
                    Protocol = ProtocolType.Telnet,
                    Settings = new TelnetConnectionSettings { Host = host, Port = port }
                },
                "serial" => new SessionItem
                {
                    Name = name,
                    Protocol = ProtocolType.Serial,
                    Settings = new SerialConnectionSettings
                    {
                        PortName = s.GetValue("SerialLine") as string ?? "COM1",
                        BaudRate = ToInt(s.GetValue("SerialSpeed"), 115200)
                    }
                },
                _ => null
            };

            if (item is null)
            {
                continue;
            }

            var usable = item.Settings is SerialConnectionSettings || !string.IsNullOrWhiteSpace(host);
            if (usable)
            {
                results.Add(item);
            }
        }

        return results;
    }

    private static int ToInt(object? value, int fallback) => value switch
    {
        int i => i,
        long l => (int)l,
        string str when int.TryParse(str, out var parsed) => parsed,
        _ => fallback
    };
}
