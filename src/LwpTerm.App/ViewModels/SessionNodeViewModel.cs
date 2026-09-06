using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LwpTerm.App.ViewModels;

/// <summary>
/// A node in the Sessions tree. In M0 these are populated from sample data;
/// from M1 they wrap the persisted <c>LwpTerm.Core</c> session model.
/// </summary>
public sealed partial class SessionNodeViewModel : ObservableObject
{
    public SessionNodeViewModel(string name, bool isFolder)
    {
        _name = name;
        IsFolder = isFolder;
    }

    public bool IsFolder { get; }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Protocol label for leaf nodes (e.g. "SSH", "RDP"). Null for folders.</summary>
    [ObservableProperty]
    private string? _protocol;

    /// <summary>Human-readable connection target for leaf nodes (e.g. "user@host:22").</summary>
    [ObservableProperty]
    private string? _target;

    public ObservableCollection<SessionNodeViewModel> Children { get; } = new();

    /// <summary>Segoe MDL2 Assets glyph (private-use code points) shown next to the node.</summary>
    public string Glyph => IsFolder
        ? "" // Folder
        : Protocol switch
        {
            "SSH" => "",                          // CommandPrompt
            "Telnet" => "",                       // Globe
            "Serial" => "",                       // USB
            "PowerShell" or "CMD" or "WSL" => "", // CommandPrompt
            "SFTP" or "FTP" => "",                // NetworkTower
            "RDP" => "",                          // Remote
            "VNC" => "",                          // TVMonitor
            _ => ""                               // AllApps
        };
}
