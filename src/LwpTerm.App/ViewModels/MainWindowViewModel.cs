using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LwpTerm.App.Services;
using LwpTerm.App.ViewModels.Panels;
using LwpTerm.App.ViewModels.Tabs;

namespace LwpTerm.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly ISessionLauncher _launcher;

    public MainWindowViewModel(
        SessionTreeViewModel sessions,
        TransfersViewModel transfers,
        LogViewModel log,
        ISessionLauncher launcher)
    {
        Sessions = sessions;
        Transfers = transfers;
        Log = log;
        _launcher = launcher;
        _launcher.TabRequested += (_, tab) => AddTab(tab);
    }

    public SessionTreeViewModel Sessions { get; }

    public TransfersViewModel Transfers { get; }

    public LogViewModel Log { get; }

    public ObservableCollection<SessionTabViewModel> Documents { get; } = new();

    [ObservableProperty]
    private SessionTabViewModel? _activeDocument;

    public string Title => ActiveDocument is null
        ? "LWP-TERM"
        : $"LWP-TERM — {ActiveDocument.Title}";

    partial void OnActiveDocumentChanged(SessionTabViewModel? value) => OnPropertyChanged(nameof(Title));

    [RelayCommand]
    private void NewLocalShell() => _launcher.Launch("PowerShell", "PowerShell", "powershell.exe");

    [RelayCommand]
    private void CloseTab(SessionTabViewModel? tab)
    {
        tab ??= ActiveDocument;
        if (tab is null)
        {
            return;
        }

        RemoveTab(tab);
    }

    public void AddTab(SessionTabViewModel tab)
    {
        tab.CloseRequested += (_, _) => RemoveTab(tab);
        Documents.Add(tab);
        ActiveDocument = tab;
    }

    private void RemoveTab(SessionTabViewModel tab)
    {
        if (!Documents.Contains(tab))
        {
            return;
        }

        var index = Documents.IndexOf(tab);
        Documents.Remove(tab);
        tab.Dispose();

        if (ReferenceEquals(ActiveDocument, tab))
        {
            ActiveDocument = Documents.ElementAtOrDefault(Math.Max(0, index - 1));
        }
    }
}
