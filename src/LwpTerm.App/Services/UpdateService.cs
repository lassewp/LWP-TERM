using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Sources;

namespace LwpTerm.App.Services;

public interface IUpdateService
{
    /// <summary>True only when running from a Velopack install (not `dotnet run` or a raw copy).</summary>
    bool IsInstalled { get; }

    /// <summary>Human-readable current version, e.g. "0.2.0".</summary>
    string CurrentVersion { get; }

    /// <summary>Check the feed and, if newer, download it. Returns the new version string, or null.</summary>
    Task<string?> CheckAsync(CancellationToken cancellationToken = default);

    /// <summary>Apply the update downloaded by <see cref="CheckAsync"/> and relaunch.</summary>
    void ApplyAndRestart();
}

public sealed class UpdateService : IUpdateService
{
    // TODO: point this at your PUBLIC GitHub repo so friends can pull releases without a token.
    private const string RepoUrl = "https://github.com/Lassewp/LWP-TERM";

    private readonly ILogger<UpdateService> _log;
    private readonly UpdateManager _manager;
    private UpdateInfo? _pending;

    public UpdateService(ILogger<UpdateService> log)
    {
        _log = log;
        _manager = new UpdateManager(new GithubSource(RepoUrl, null, prerelease: false));
    }

    public bool IsInstalled => _manager.IsInstalled;

    public string CurrentVersion =>
        (_manager.IsInstalled ? _manager.CurrentVersion?.ToString() : null)
        ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3)
        ?? "dev";

    public async Task<string?> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!_manager.IsInstalled)
        {
            return null;
        }

        try
        {
            var info = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (info is null)
            {
                return null;
            }

            await _manager.DownloadUpdatesAsync(info, cancelToken: cancellationToken).ConfigureAwait(false);
            _pending = info;

            var version = info.TargetFullRelease.Version.ToString();
            _log.LogInformation("Update {Version} downloaded and staged for next restart", version);
            return version;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Update check failed");
            return null;
        }
    }

    public void ApplyAndRestart()
    {
        if (_pending is not null)
        {
            _manager.ApplyUpdatesAndRestart(_pending.TargetFullRelease);
        }
    }
}
