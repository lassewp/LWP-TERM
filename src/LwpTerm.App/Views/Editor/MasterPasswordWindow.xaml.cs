using System.Windows;
using System.Windows.Input;
using LwpTerm.Core.Security;

namespace LwpTerm.App.Views.Editor;

public partial class MasterPasswordWindow : Window
{
    private readonly ICredentialStore _credentials;

    public MasterPasswordWindow(ICredentialStore credentials)
    {
        _credentials = credentials;
        InitializeComponent();
        Loaded += (_, _) => PasswordBox.Focus();
    }

    /// <summary>True when the vault was unlocked; false when the user chose to quit.</summary>
    public bool Unlocked { get; private set; }

    private void OnUnlock(object sender, RoutedEventArgs e)
    {
        try
        {
            _credentials.Unlock(PasswordBox.Password);
            Unlocked = true;
            DialogResult = true;
        }
        catch (InvalidMasterPasswordException)
        {
            ErrorText.Text = "Incorrect master password. Try again.";
            ErrorText.Visibility = Visibility.Visible;
            PasswordBox.SelectAll();
            PasswordBox.Focus();
        }
    }

    private void OnPasswordKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnUnlock(sender, e);
        }
    }

    private void OnQuit(object sender, RoutedEventArgs e)
    {
        Unlocked = false;
        DialogResult = false;
    }
}
