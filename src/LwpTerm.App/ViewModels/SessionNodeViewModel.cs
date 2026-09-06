using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using LwpTerm.Core.Sessions;

namespace LwpTerm.App.ViewModels;

/// <summary>
/// A node in the Sessions tree, wrapping a persisted <see cref="SessionNode"/>.
/// Folders own a live <see cref="Children"/> collection; leaves expose the
/// protocol and a target summary.
/// </summary>
public sealed partial class SessionNodeViewModel : ObservableObject
{
    public SessionNodeViewModel(SessionNode model, SessionNodeViewModel? parent)
    {
        Model = model;
        Parent = parent;
        _name = model.Name;

        if (model is SessionFolder folder)
        {
            _isExpanded = folder.IsExpanded;
            foreach (var child in folder.Children)
            {
                Children.Add(new SessionNodeViewModel(child, this));
            }
        }
    }

    public SessionNode Model { get; }

    /// <summary>Container folder VM, or null when this node sits at the tree root. Updated on move.</summary>
    public SessionNodeViewModel? Parent { get; set; }

    public bool IsFolder => Model is SessionFolder;

    public SessionItem? Item => Model as SessionItem;

    public SessionFolder? Folder => Model as SessionFolder;

    public ObservableCollection<SessionNodeViewModel> Children { get; } = new();

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isVisible = true;

    /// <summary>Accent colour for the row: own colour, else the parent folder's, else none.</summary>
    public string? AccentColor =>
        Item?.ColorHex
        ?? Folder?.ColorHex
        ?? Parent?.Folder?.ColorHex;

    /// <summary>Label weight: folders are bold at the tree root unless overridden; leaves stay regular.</summary>
    public FontWeight LabelFontWeight => Folder switch
    {
        null => FontWeights.Normal,
        { LabelWeight: FolderLabelWeight.Bold } => FontWeights.Bold,
        { LabelWeight: FolderLabelWeight.Normal } => FontWeights.Normal,
        _ => Parent is null ? FontWeights.Bold : FontWeights.Normal
    };

    partial void OnNameChanged(string value) => Model.Name = value;

    partial void OnIsExpandedChanged(bool value)
    {
        if (Model is SessionFolder folder)
        {
            folder.IsExpanded = value;
        }
    }

    public string? Protocol => Item?.Protocol.ToString();

    public string? Target => Item?.Settings.Summary;

    public string Glyph => IsFolder
        ? Folder?.IconGlyph ?? ""          // Folder
        : Item?.IconGlyph ?? DefaultGlyph;

    /// <summary>Re-reads computed values after the underlying model was edited.</summary>
    public void RefreshFromModel()
    {
        Name = Model.Name;
        OnPropertyChanged(nameof(Protocol));
        OnPropertyChanged(nameof(Target));
        OnPropertyChanged(nameof(Glyph));
        OnPropertyChanged(nameof(AccentColor));
        OnPropertyChanged(nameof(LabelFontWeight));
    }

    public void SyncChildrenOrderToModel()
    {
        if (Model is not SessionFolder folder)
        {
            return;
        }

        folder.Children.Clear();
        folder.Children.AddRange(Children.Select(c => c.Model));
    }

    private string DefaultGlyph => !IsFolder && Item is not null
        ? Item.Protocol switch
        {
            ProtocolType.Ssh => "",
            ProtocolType.Telnet => "",
            ProtocolType.Serial => "",
            ProtocolType.LocalShell => "",
            ProtocolType.Sftp => "",
            ProtocolType.Ftp => "",
            ProtocolType.Rdp => "",
            ProtocolType.Vnc => "",
            _ => ""
        }
        : "";
}
