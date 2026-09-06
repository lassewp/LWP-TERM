using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LwpTerm.App.Services;
using Microsoft.Extensions.Logging;

namespace LwpTerm.App.ViewModels.FileBrowser;

/// <summary>One navigable pane (local or remote) in the file browser.</summary>
public sealed partial class FilePaneViewModel : ObservableObject
{
    private readonly IDirectorySource _source;
    private readonly ILogger _log;
    private readonly Func<string, string?> _prompt;
    private readonly Func<string, string, bool> _confirm;

    public FilePaneViewModel(
        IDirectorySource source,
        ILogger log,
        Func<string, string?> prompt,
        Func<string, string, bool> confirm)
    {
        _source = source;
        _log = log;
        _prompt = prompt;
        _confirm = confirm;
    }

    public string Kind => _source.Kind;

    public bool IsRemote => _source.IsRemote;

    public IDirectorySource Source => _source;

    public ObservableCollection<FileEntryViewModel> Entries { get; } = new();

    [ObservableProperty]
    private string _currentPath = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorText;

    public async Task InitializeAsync()
    {
        var start = await _source.InitializeAsync(CancellationToken.None).ConfigureAwait(true);
        await NavigateToAsync(start).ConfigureAwait(true);
    }

    public async Task NavigateToAsync(string path)
    {
        IsBusy = true;
        ErrorText = null;
        try
        {
            var nodes = await _source.ListAsync(path, CancellationToken.None).ConfigureAwait(true);
            Entries.Clear();
            foreach (var node in nodes.Select(n => new FileEntryViewModel(n)).OrderBy(e => e.SortKey))
            {
                Entries.Add(node);
            }

            CurrentPath = path;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to list {Path}", path);
            ErrorText = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task OpenAsync(FileEntryViewModel entry) =>
        entry.IsDirectory ? NavigateToAsync(entry.Node.FullPath) : Task.CompletedTask;

    [RelayCommand]
    private Task Up()
    {
        var parent = _source.Parent(CurrentPath);
        return string.Equals(parent, CurrentPath, StringComparison.Ordinal)
            ? Task.CompletedTask
            : NavigateToAsync(parent);
    }

    [RelayCommand]
    private Task Refresh() => NavigateToAsync(CurrentPath);

    [RelayCommand]
    private async Task NewFolder()
    {
        var name = _prompt("New folder name:");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        try
        {
            await _source.CreateDirectoryAsync(_source.Combine(CurrentPath, name.Trim()), CancellationToken.None)
                .ConfigureAwait(true);
            await Refresh().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
        }
    }

    [RelayCommand]
    private async Task Rename(FileEntryViewModel? entry)
    {
        if (entry is null)
        {
            return;
        }

        var name = _prompt($"Rename \"{entry.Name}\" to:");
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == entry.Name)
        {
            return;
        }

        try
        {
            await _source.RenameAsync(entry.Node, name.Trim(), CancellationToken.None).ConfigureAwait(true);
            await Refresh().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
        }
    }

    [RelayCommand]
    private async Task Delete(IList<FileEntryViewModel>? items)
    {
        var targets = (items ?? Array.Empty<FileEntryViewModel>()).ToList();
        if (targets.Count == 0)
        {
            return;
        }

        var label = targets.Count == 1 ? $"\"{targets[0].Name}\"" : $"{targets.Count} items";
        if (!_confirm($"Delete {label}? This cannot be undone.", "Confirm delete"))
        {
            return;
        }

        foreach (var entry in targets)
        {
            try
            {
                await _source.DeleteAsync(entry.Node, CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                ErrorText = ex.Message;
            }
        }

        await Refresh().ConfigureAwait(true);
    }
}
