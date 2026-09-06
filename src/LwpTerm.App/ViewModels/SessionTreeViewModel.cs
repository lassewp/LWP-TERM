using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LwpTerm.App.Services;
using LwpTerm.Core.Sessions;
using Microsoft.Extensions.Logging;

namespace LwpTerm.App.ViewModels;

/// <summary>
/// Backs the docked "Sessions" panel: a persisted folder tree of saved
/// connections with add / edit / duplicate / delete / reorder and connect.
/// </summary>
public sealed partial class SessionTreeViewModel : ObservableObject
{
    private readonly ISessionStore _store;
    private readonly ISessionLauncher _launcher;
    private readonly IDialogService _dialogs;
    private readonly ILogger<SessionTreeViewModel> _log;

    private SessionTree _tree = new();
    private bool _loaded;

    public SessionTreeViewModel(
        ISessionStore store,
        ISessionLauncher launcher,
        IDialogService dialogs,
        ILogger<SessionTreeViewModel> log)
    {
        _store = store;
        _launcher = launcher;
        _dialogs = dialogs;
        _log = log;
    }

    public ObservableCollection<SessionNodeViewModel> Roots { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(EditCommand))]
    [NotifyCanExecuteChangedFor(nameof(DuplicateCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddSessionCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddFolderCommand))]
    private SessionNodeViewModel? _selectedNode;

    [ObservableProperty]
    private string _filterText = string.Empty;

    public async Task LoadAsync()
    {
        _tree = await _store.LoadAsync().ConfigureAwait(true);
        Roots.Clear();
        foreach (var node in _tree.Roots)
        {
            Roots.Add(new SessionNodeViewModel(node, parent: null));
        }

        _loaded = true;
        _log.LogInformation("Loaded {Count} root nodes from the session store", Roots.Count);
    }

    // ---- Connect -----------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanActOnSession))]
    private void Connect(SessionNodeViewModel? node)
    {
        node ??= SelectedNode;
        if (node?.Item is not { } item)
        {
            return;
        }

        _launcher.Launch(item.Name, item.Protocol.ToString(), item.Settings.Summary);
    }

    private bool CanActOnSession(SessionNodeViewModel? node) => (node ?? SelectedNode) is { IsFolder: false };

    // ---- Add folder ------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task AddFolder(SessionNodeViewModel? anchor)
    {
        var parentVm = ResolveContainer(anchor ?? SelectedNode);
        var folder = new SessionFolder { Name = "New folder" };
        InsertChild(parentVm, folder);
        await SaveAsync().ConfigureAwait(true);
    }

    private bool CanAdd(SessionNodeViewModel? _) => _loaded;

    // ---- Add session ---------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task AddSession(SessionNodeViewModel? anchor)
    {
        var result = _dialogs.EditSession(existing: null, "New session");
        if (result is null)
        {
            return;
        }

        var parentVm = ResolveContainer(anchor ?? SelectedNode);
        InsertChild(parentVm, result);
        await SaveAsync().ConfigureAwait(true);
    }

    // ---- Edit --------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanActOnSession))]
    private async Task Edit(SessionNodeViewModel? node)
    {
        node ??= SelectedNode;
        if (node?.Item is not { } item)
        {
            return;
        }

        var result = _dialogs.EditSession(item, $"Edit — {item.Name}");
        if (result is null)
        {
            return;
        }

        node.RefreshFromModel();
        await SaveAsync().ConfigureAwait(true);
    }

    // ---- Duplicate ---------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanActOnSession))]
    private async Task Duplicate(SessionNodeViewModel? node)
    {
        node ??= SelectedNode;
        if (node?.Item is not { } item)
        {
            return;
        }

        var clone = CloneItem(item);
        clone.Name = item.Name + " (copy)";
        InsertChild(node.Parent, clone, afterSibling: node);
        await SaveAsync().ConfigureAwait(true);
    }

    // ---- Delete ----------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task Delete(SessionNodeViewModel? node)
    {
        node ??= SelectedNode;
        if (node is null)
        {
            return;
        }

        var kind = node.IsFolder ? "folder and everything in it" : "session";
        if (!_dialogs.Confirm($"Delete the {kind} \"{node.Name}\"?", "Confirm delete"))
        {
            return;
        }

        RemoveNode(node);
        await SaveAsync().ConfigureAwait(true);
    }

    private bool CanDelete(SessionNodeViewModel? node) => _loaded && (node ?? SelectedNode) is not null;

    // ---- Reorder / move (drag-drop) --------------------------------

    public bool CanDrop(SessionNodeViewModel source, SessionNodeViewModel? target)
    {
        if (ReferenceEquals(source, target))
        {
            return false;
        }

        // Disallow dropping a folder into its own subtree.
        for (var t = target; t is not null; t = t.Parent)
        {
            if (ReferenceEquals(t, source))
            {
                return false;
            }
        }

        return true;
    }

    public async Task MoveAsync(SessionNodeViewModel source, SessionNodeViewModel? target)
    {
        if (!CanDrop(source, target))
        {
            return;
        }

        RemoveNode(source, persistModel: false);

        if (target is null)
        {
            source.Parent = null;
            Roots.Add(source);
        }
        else if (target.IsFolder)
        {
            source.Parent = target;
            target.Children.Add(source);
            target.IsExpanded = true;
        }
        else
        {
            var siblings = target.Parent?.Children ?? Roots;
            source.Parent = target.Parent;
            siblings.Insert(siblings.IndexOf(target) + 1, source);
        }

        RebuildModelFromTree();
        await SaveAsync().ConfigureAwait(true);
    }

    // ---- Persistence --------------------------------------------------

    private async Task SaveAsync()
    {
        RebuildModelFromTree();
        try
        {
            await _store.SaveAsync(_tree).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to save the session tree");
            _dialogs.Info("The session list could not be saved:\n" + ex.Message, "Save failed");
        }
    }

    private void RebuildModelFromTree()
    {
        _tree.Roots.Clear();
        foreach (var root in Roots)
        {
            SyncRecursive(root);
            _tree.Roots.Add(root.Model);
        }
    }

    private static void SyncRecursive(SessionNodeViewModel node)
    {
        if (!node.IsFolder)
        {
            return;
        }

        foreach (var child in node.Children)
        {
            SyncRecursive(child);
        }

        node.SyncChildrenOrderToModel();
    }

    // ---- Helpers ----------------------------------------------------

    private SessionNodeViewModel? ResolveContainer(SessionNodeViewModel? node) => node switch
    {
        null => null,
        { IsFolder: true } => node,
        _ => node.Parent
    };

    private void InsertChild(SessionNodeViewModel? parentVm, SessionNode model, SessionNodeViewModel? afterSibling = null)
    {
        var vm = new SessionNodeViewModel(model, parentVm);
        var collection = parentVm?.Children ?? Roots;

        if (afterSibling is not null && collection.Contains(afterSibling))
        {
            collection.Insert(collection.IndexOf(afterSibling) + 1, vm);
        }
        else
        {
            collection.Add(vm);
        }

        if (parentVm is not null)
        {
            parentVm.IsExpanded = true;
        }

        vm.IsSelected = true;
        SelectedNode = vm;
    }

    private void RemoveNode(SessionNodeViewModel node, bool persistModel = true)
    {
        var collection = node.Parent?.Children ?? Roots;
        collection.Remove(node);
        if (ReferenceEquals(SelectedNode, node))
        {
            SelectedNode = null;
        }

        if (persistModel)
        {
            RebuildModelFromTree();
        }
    }

    private static SessionItem CloneItem(SessionItem source)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(source, JsonSessionStore.SerializerOptions);
        var clone = System.Text.Json.JsonSerializer.Deserialize<SessionItem>(json, JsonSessionStore.SerializerOptions)!;
        clone.Id = Guid.NewGuid().ToString("N");
        return clone;
    }
}
