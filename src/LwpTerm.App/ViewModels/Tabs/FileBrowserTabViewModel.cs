using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using LwpTerm.App.Services;
using LwpTerm.App.ViewModels.FileBrowser;
using LwpTerm.App.ViewModels.Panels;
using LwpTerm.App.Views.Dialogs;
using LwpTerm.Core.Transfer;
using Microsoft.Extensions.Logging;

namespace LwpTerm.App.ViewModels.Tabs;

/// <summary>
/// Dual-pane file browser tab: local file system on the left, an SFTP/FTP store
/// on the right, with drag-drop transfers queued into the Transfers panel.
/// </summary>
public sealed partial class FileBrowserTabViewModel : SessionTabViewModel
{
    private readonly IFileTransferConnection _connection;
    private readonly TransferQueue _queue;
    private readonly ILogger _log;
    private bool _initialised;

    public FileBrowserTabViewModel(
        string title,
        IFileTransferConnection connection,
        RemoteDirectorySource remoteSource,
        TransferQueue queue,
        ILogger log)
        : base(title)
    {
        _connection = connection;
        _queue = queue;
        _log = log;
        ToolTip = title;
        StatusText = "Not connected";

        Func<string, string?> prompt = p => InputDialog.Ask(p, "LWP-TERM");
        Func<string, string, bool> confirm = (msg, cap) =>
            MessageBox.Show(Application.Current.MainWindow!, msg, cap, MessageBoxButton.YesNo, MessageBoxImage.Warning)
            == MessageBoxResult.Yes;

        Local = new FilePaneViewModel(new LocalDirectorySource(), log, prompt, confirm);
        Remote = new FilePaneViewModel(remoteSource, log, prompt, confirm);
    }

    public FilePaneViewModel Local { get; }

    public FilePaneViewModel Remote { get; }

    public async void Initialize()
    {
        if (_initialised)
        {
            return;
        }

        _initialised = true;
        State = SessionTabState.Connecting;
        StatusText = "Connecting…";

        try
        {
            await Remote.InitializeAsync().ConfigureAwait(true);
            await Local.InitializeAsync().ConfigureAwait(true);
            State = SessionTabState.Connected;
            StatusText = $"Connected — {Remote.CurrentPath}";
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "File browser '{Title}' failed to connect", Title);
            State = SessionTabState.Faulted;
            StatusText = "Failed: " + ex.Message;
            Remote.ErrorText = ex.Message;
        }
    }

    [RelayCommand]
    private void Upload(IList<FileEntryViewModel>? selection)
    {
        foreach (var entry in Selected(selection ?? Local.Entries.Where(e => e.IsSelected).ToList()))
        {
            if (entry.IsDirectory)
            {
                _log.LogInformation("Folder upload is not supported yet: {Name}", entry.Name);
                continue;
            }

            var remotePath = _connection.Combine(Remote.CurrentPath, entry.Name);
            _queue.Enqueue(new TransferRequest(
                _connection, TransferDirection.Upload, entry.Node.FullPath, remotePath,
                entry.Name, entry.Node.Size, OnCompleted: () => Dispatch(() => _ = Remote.RefreshCommand.ExecuteAsync(null))));
        }
    }

    [RelayCommand]
    private void Download(IList<FileEntryViewModel>? selection)
    {
        foreach (var entry in Selected(selection ?? Remote.Entries.Where(e => e.IsSelected).ToList()))
        {
            if (entry.IsDirectory)
            {
                _log.LogInformation("Folder download is not supported yet: {Name}", entry.Name);
                continue;
            }

            var localPath = Path.Combine(Local.CurrentPath, entry.Name);
            _queue.Enqueue(new TransferRequest(
                _connection, TransferDirection.Download, localPath, entry.Node.FullPath,
                entry.Name, entry.Node.Size, OnCompleted: () => Dispatch(() => _ = Local.RefreshCommand.ExecuteAsync(null))));
        }
    }

    /// <summary>Drag-drop entry point: move the given entries in the indicated direction.</summary>
    public void TransferDropped(bool toRemote, IEnumerable<FileEntryViewModel> entries)
    {
        var list = entries.ToList();
        if (toRemote)
        {
            Upload(list);
        }
        else
        {
            Download(list);
        }
    }

    private static IEnumerable<FileEntryViewModel> Selected(IList<FileEntryViewModel> items) =>
        items.Where(e => e is not null);

    private static void Dispatch(Action action) =>
        Application.Current?.Dispatcher.BeginInvoke(action);

    protected override void DisposeCore() => _ = _connection.DisposeAsync();
}
