using System.Collections.ObjectModel;
using System.Linq;
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

    public ObservableCollection<SessionNodeViewModel> Children { get; } = new();

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isVisible = true;

    /// <summary>Optional accent colour (#RRGGBB) for the leaf row; null for folders / unset.</summary>
    public string? AccentColor => Item?.ColorHex;

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

    public string Glyph => Item?.IconGlyph ?? DefaultGlyph;

    /// <summary>Re-reads computed values after the underlying settings were edited.</summary>
    public void RefreshFromModel()
    {
        Name = Model.Name;
        OnPropertyChanged(nameof(Protocol));
        OnPropertyChanged(nameof(Target));
        OnPropertyChanged(nameof(Glyph));
        OnPropertyChanged(nameof(AccentColor));
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
