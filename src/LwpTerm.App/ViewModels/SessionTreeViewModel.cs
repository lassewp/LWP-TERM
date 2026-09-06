using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LwpTerm.App.Services;

namespace LwpTerm.App.ViewModels;

/// <summary>
/// Backs the docked "Sessions" panel: a folder tree of saved connections.
/// M0 seeds it with sample data; M1 replaces the seed with the persisted store.
/// </summary>
public sealed partial class SessionTreeViewModel : ObservableObject
{
    private readonly ISessionLauncher _launcher;

    public SessionTreeViewModel(ISessionLauncher launcher)
    {
        _launcher = launcher;
        SeedSampleData();
    }

    public ObservableCollection<SessionNodeViewModel> Roots { get; } = new();

    [ObservableProperty]
    private SessionNodeViewModel? _selectedNode;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private void Connect(SessionNodeViewModel? node)
    {
        node ??= SelectedNode;
        if (node is null || node.IsFolder)
        {
            return;
        }

        _launcher.Launch(node.Name, node.Protocol ?? "Unknown", node.Target ?? string.Empty);
    }

    private bool CanConnect(SessionNodeViewModel? node)
    {
        node ??= SelectedNode;
        return node is { IsFolder: false };
    }

    partial void OnSelectedNodeChanged(SessionNodeViewModel? value) => ConnectCommand.NotifyCanExecuteChanged();

    private void SeedSampleData()
    {
        var prod = new SessionNodeViewModel("Production", isFolder: true);
        prod.Children.Add(Leaf("web-01", "SSH", "deploy@web-01.example.com:22"));
        prod.Children.Add(Leaf("web-02", "SSH", "deploy@web-02.example.com:22"));
        prod.Children.Add(Leaf("db-01 (SFTP)", "SFTP", "deploy@db-01.example.com:22"));

        var net = new SessionNodeViewModel("Network gear", isFolder: true);
        net.Children.Add(Leaf("core-switch", "Telnet", "10.0.0.1:23"));
        net.Children.Add(Leaf("console (COM3)", "Serial", "COM3 @ 115200 8N1"));

        var remote = new SessionNodeViewModel("Desktops", isFolder: true);
        remote.Children.Add(Leaf("jump-host", "RDP", "10.0.0.50:3389"));
        remote.Children.Add(Leaf("lab-linux", "VNC", "10.0.0.60:5900"));

        var local = new SessionNodeViewModel("Local shells", isFolder: true);
        local.Children.Add(Leaf("PowerShell", "PowerShell", "powershell.exe"));
        local.Children.Add(Leaf("Ubuntu (WSL)", "WSL", "wsl.exe -d Ubuntu"));

        Roots.Add(prod);
        Roots.Add(net);
        Roots.Add(remote);
        Roots.Add(local);
    }

    private static SessionNodeViewModel Leaf(string name, string protocol, string target) =>
        new(name, isFolder: false) { Protocol = protocol, Target = target };
}
