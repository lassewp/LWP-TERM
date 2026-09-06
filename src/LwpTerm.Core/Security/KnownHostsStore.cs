using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using LwpTerm.Core;

namespace LwpTerm.Core.Security;

/// <summary>
/// A tiny known-hosts file: one line per host as
/// <c>host:port SHA256base64 keyAlgorithm</c>. Not the OpenSSH format — this is
/// LWP-TERM's own trust store under <c>%APPDATA%\LwpTerm\known_hosts</c>.
/// </summary>
public sealed class KnownHostsStore
{
    private readonly string _path;
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public KnownHostsStore(AppPaths paths)
    {
        _path = paths.KnownHostsFile;
        Load();
    }

    public readonly record struct Entry(string Fingerprint, string Algorithm);

    public enum MatchResult
    {
        Unknown,
        Trusted,
        Changed
    }

    public MatchResult Check(string host, int port, string fingerprintSha256)
    {
        if (!_entries.TryGetValue(Key(host, port), out var entry))
        {
            return MatchResult.Unknown;
        }

        return string.Equals(entry.Fingerprint, fingerprintSha256, StringComparison.Ordinal)
            ? MatchResult.Trusted
            : MatchResult.Changed;
    }

    public void Trust(string host, int port, string fingerprintSha256, string algorithm)
    {
        _entries[Key(host, port)] = new Entry(fingerprintSha256, algorithm);
        Save();
    }

    private static string Key(string host, int port) => $"{host}:{port}";

    private void Load()
    {
        if (!File.Exists(_path))
        {
            return;
        }

        foreach (var raw in File.ReadAllLines(_path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                _entries[parts[0]] = new Entry(parts[1], parts.Length >= 3 ? parts[2] : "unknown");
            }
        }
    }

    private void Save()
    {
        var lines = _entries
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => $"{kv.Key} {kv.Value.Fingerprint} {kv.Value.Algorithm}");

        var tmp = _path + ".tmp";
        File.WriteAllLines(tmp, lines);
        if (File.Exists(_path))
        {
            File.Replace(tmp, _path, null);
        }
        else
        {
            File.Move(tmp, _path);
        }
    }
}
