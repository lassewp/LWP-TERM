using System;
using LwpTerm.App.ViewModels.Tabs;
using Microsoft.Extensions.Logging;

namespace LwpTerm.App.Services;

public interface ISessionLauncher
{
    /// <summary>Raised when a new document tab should be opened and activated.</summary>
    event EventHandler<SessionTabViewModel>? TabRequested;

    /// <summary>Open a session. During M0–M1 this produces a placeholder tab.</summary>
    void Launch(string name, string protocol, string target);

    /// <summary>Open an already-constructed tab view model.</summary>
    void Open(SessionTabViewModel tab);
}

public sealed class SessionLauncher : ISessionLauncher
{
    private readonly ILogger<SessionLauncher> _log;

    public SessionLauncher(ILogger<SessionLauncher> log) => _log = log;

    public event EventHandler<SessionTabViewModel>? TabRequested;

    public void Launch(string name, string protocol, string target)
    {
        _log.LogInformation("Launch requested: {Name} ({Protocol} -> {Target})", name, protocol, target);
        Open(new PlaceholderTabViewModel(name, protocol, target));
    }

    public void Open(SessionTabViewModel tab) => TabRequested?.Invoke(this, tab);
}
