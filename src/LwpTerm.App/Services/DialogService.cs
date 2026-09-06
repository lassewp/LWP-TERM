using System.Windows;
using LwpTerm.App.ViewModels.Editor;
using LwpTerm.App.Views.Editor;
using LwpTerm.Core.Security;
using LwpTerm.Core.Sessions;

namespace LwpTerm.App.Services;

public interface IDialogService
{
    /// <summary>Show the session editor. Returns the edited item, or null if cancelled.</summary>
    SessionItem? EditSession(SessionItem? existing, string dialogTitle);

    bool Confirm(string message, string title);

    void Info(string message, string title);
}

public sealed class DialogService : IDialogService
{
    private readonly ICredentialStore _credentials;

    public DialogService(ICredentialStore credentials) => _credentials = credentials;

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
