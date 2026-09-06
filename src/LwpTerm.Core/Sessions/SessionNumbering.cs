using System;
using System.Net;
using System.Text.RegularExpressions;

namespace LwpTerm.Core.Sessions;

/// <summary>
/// Guesses the "next" name and address when duplicating a session repeatedly,
/// so bulk-adding similar devices only needs a keystroke or two per copy.
/// </summary>
public static partial class SessionNumbering
{
    [GeneratedRegex(@"^(?<prefix>.*?)(?<num>\d+)(?<suffix>\D*)$")]
    private static partial Regex TrailingNumber();

    /// <summary>
    /// "SW13" → "SW14", "web-09" → "web-10", "rtr 1" → "rtr 2". If there is no
    /// number to bump, appends " 2". Zero-padding width is preserved.
    /// </summary>
    public static string NextName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        var m = TrailingNumber().Match(name);
        if (!m.Success)
        {
            return name.TrimEnd() + " 2";
        }

        var digits = m.Groups["num"].Value;
        var next = (long.Parse(digits) + 1).ToString();
        if (next.Length < digits.Length)
        {
            next = next.PadLeft(digits.Length, '0');
        }

        return m.Groups["prefix"].Value + next + m.Groups["suffix"].Value;
    }

    /// <summary>
    /// Bumps the last octet of a dotted-quad IPv4 ("10.0.0.13" → "10.0.0.14"),
    /// wrapping/clamping so it stays 1–254. Anything that is not a bare IPv4 is
    /// returned unchanged.
    /// </summary>
    public static string NextHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return host ?? string.Empty;
        }

        var trimmed = host.Trim();
        if (!IPAddress.TryParse(trimmed, out var ip)
            || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork
            || trimmed.Split('.').Length != 4)
        {
            return trimmed;
        }

        var bytes = ip.GetAddressBytes();
        bytes[3] = (byte)(bytes[3] >= 254 ? 1 : bytes[3] + 1);
        return new IPAddress(bytes).ToString();
    }
}
