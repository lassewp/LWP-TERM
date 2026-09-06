using System;
using LwpTerm.App.ViewModels.Tabs;
using LwpTerm.Connections;
using LwpTerm.Core;
using LwpTerm.Core.Sessions;
using Microsoft.Extensions.Logging;

namespace LwpTerm.App.Services;

public interface ISessionLauncher
{
    /// <summary>Raised when a new document tab should be opened and activated.</summary>
    event EventHandler<SessionTabViewModel>? TabRequested;

    /// <summary>Open a saved session.</summary>
    void Launch(SessionItem item);

    /// <summary>Open an ad-hoc local PowerShell tab (toolbar / Ctrl+T).</summary>
    void OpenAdHocLocalShell();

    /// <summary>Open an SFTP browser that reuses an SSH session's host and credentials.</summary>
    void OpenSftpForSsh(SessionItem sshItem);

    /// <summary>Open an already-constructed tab view model.</summary>
    void Open(SessionTabViewModel tab);
}

public sealed class SessionLauncher : ISessionLauncher
{
    private readonly ITerminalConnectionFactory _terminals;
    private readonly IFileTransferConnectionFactory _transfers;
    private readonly TransferQueue _transferQueue;
    private readonly ILoggerFactory _loggerFactory;
    private readonly AppPaths _paths;
    private readonly ILogger<SessionLauncher> _log;

    public SessionLauncher(
        ITerminalConnectionFactory terminals,
        IFileTransferConnectionFactory transfers,
        TransferQueue transferQueue,
        ILoggerFactory loggerFactory,
        AppPaths paths,
        ILogger<SessionLauncher> log)
    {
        _terminals = terminals;
        _transfers = transfers;
        _transferQueue = transferQueue;
        _loggerFactory = loggerFactory;
        _paths = paths;
        _log = log;
    }

    public event EventHandler<SessionTabViewModel>? TabRequested;

    public void Launch(SessionItem item)
    {
        _log.LogInformation("Launching '{Name}' ({Protocol})", item.Name, item.Protocol);

        if (_terminals.Supports(item.Protocol))
        {
            var connection = _terminals.Create(item.Settings);
            var logger = _loggerFactory.CreateLogger($"Terminal.{item.Protocol}");
            Open(new TerminalTabViewModel(item.Name, connection, logger, _paths));
            return;
        }

        if (_transfers.Supports(item.Protocol))
        {
            OpenFileBrowser(item.Name, _transfers.Create(item.Settings), item.Protocol.ToString().ToUpperInvariant());
            return;
        }

        Open(new PlaceholderTabViewModel(item.Name, item.Protocol.ToString(), item.Settings.Summary));
    }

    public void OpenAdHocLocalShell()
    {
        var settings = new LocalShellConnectionSettings { ShellKind = LocalShellKind.PowerShell };
        var connection = _terminals.Create(settings);
        var logger = _loggerFactory.CreateLogger("Terminal.LocalShell");
        Open(new TerminalTabViewModel("PowerShell", connection, logger, _paths));
    }

    public void OpenSftpForSsh(SessionItem sshItem)
    {
        if (sshItem.Settings is not SshConnectionSettings ssh)
        {
            return;
        }

        OpenFileBrowser($"{sshItem.Name} — SFTP", _transfers.CreateSftpForSsh(ssh), "SFTP");
    }

    public void Open(SessionTabViewModel tab) => TabRequested?.Invoke(this, tab);

    private void OpenFileBrowser(string title, Core.Transfer.IFileTransferConnection connection, string kind)
    {
        var remote = new RemoteDirectorySource(connection, kind);
        var logger = _loggerFactory.CreateLogger("FileBrowser");
        Open(new FileBrowserTabViewModel(title, connection, remote, _transferQueue, logger));
    }
}
