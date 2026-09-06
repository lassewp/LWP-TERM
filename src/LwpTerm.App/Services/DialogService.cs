using System.Windows;
using LwpTerm.App.ViewModels;
using LwpTerm.App.ViewModels.Editor;
using LwpTerm.App.Views.Dialogs;
using LwpTerm.App.Views.Editor;
using LwpTerm.Core.Security;
using LwpTerm.Core.Sessions;
using LwpTerm.Core.Settings;

namespace LwpTerm.App.Services;

public interface IDialogService
{
    /// <summary>Show the session editor. Returns the edited item, or null if cancelled.</summary>
    SessionItem? EditSession(SessionItem? existing, string dialogTitle);

    /// <summary>Show the settings dialog. Returns true if the user saved.</summary>
    bool ShowSettings();

    /// <summary>Ask for a single line of text. Returns null if cancelled.</summary>
    string? Prompt(string message, string title, string initial = "");

    bool Confirm(string message, string title);

    void Info(string message, string title);
}

public sealed class DialogService : IDialogService
{
    private readonly ICredentialStore _credentials;
    private readonly ISettingsStore _settings;

    public DialogService(ICredentialStore credentials, ISettingsStore settings)
    {
        _credentials = credentials;
        _settings = settings;
    }

    public SessionItem? EditSession(SessionItem? existing, string dialogTitle)
    {
        var vm = new SessionEditorViewModel(_credentials, existing);
        var window = new SessionEditorWindow
        {
            DataContext = vm,
            Title = dialogTitle,
            Owner = Application.Current.MainWindow
        };

        return window.ShowDialog() == true ? vm.BuildResult() : null;
    }

    public bool ShowSettings()
    {
        var window = new SettingsWindow
        {
            DataContext = new SettingsViewModel(_settings, _credentials),
            Owner = Application.Current.MainWindow
        };
        return window.ShowDialog() == true;
    }

    public string? Prompt(string message, string title, string initial = "") =>
        Views.Dialogs.InputDialog.Ask(message, title, initial);

    public bool Confirm(string message, string title) =>
        MessageBox.Show(
            Application.Current.MainWindow!,
            message, title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void Info(string message, string title) =>
        MessageBox.Show(
            Application.Current.MainWindow!,
            message, title,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
}
