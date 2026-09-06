using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using LwpTerm.Core;
using Microsoft.Extensions.Logging;

namespace LwpTerm.Core.Sessions;

public interface ISessionStore
{
    Task<SessionTree> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(SessionTree tree, CancellationToken ct = default);
}

/// <summary>
/// Persists the session tree to <c>sessions.json</c> as indented, polymorphic
/// JSON. Writes are atomic (temp file then replace) so a crash mid-write cannot
/// corrupt the store.
/// </summary>
public sealed class JsonSessionStore : ISessionStore
{
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AppPaths _paths;
    private readonly ILogger<JsonSessionStore> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonSessionStore(AppPaths paths, ILogger<JsonSessionStore> log)
    {
        _paths = paths;
        _log = log;
    }

    public async Task<SessionTree> LoadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_paths.SessionsFile))
            {
                _log.LogInformation("No sessions file at {Path}; starting with a seed tree", _paths.SessionsFile);
                return SessionTree.CreateSeed();
            }

            await using var stream = File.OpenRead(_paths.SessionsFile);
            var tree = await JsonSerializer.DeserializeAsync<SessionTree>(stream, SerializerOptions, ct)
                       .ConfigureAwait(false);
            return tree ?? SessionTree.CreateSeed();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to read {Path}; backing it up and starting fresh", _paths.SessionsFile);
            TryBackupCorruptFile();
            return SessionTree.CreateSeed();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(SessionTree tree, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var tmp = _paths.SessionsFile + ".tmp";
            await using (var stream = File.Create(tmp))
            {
                await JsonSerializer.SerializeAsync(stream, tree, SerializerOptions, ct).ConfigureAwait(false);
            }

            if (File.Exists(_paths.SessionsFile))
            {
                File.Replace(tmp, _paths.SessionsFile, null);
            }
            else
            {
                File.Move(tmp, _paths.SessionsFile);
            }

            _log.LogDebug("Saved session tree to {Path}", _paths.SessionsFile);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void TryBackupCorruptFile()
    {
        try
        {
            var backup = $"{_paths.SessionsFile}.corrupt-{DateTime.Now:yyyyMMddHHmmss}";
            File.Copy(_paths.SessionsFile, backup, overwrite: true);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not back up the corrupt sessions file");
        }
    }
}
