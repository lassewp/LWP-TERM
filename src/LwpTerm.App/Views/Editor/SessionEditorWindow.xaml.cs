using System.Windows;
using Microsoft.Win32;
using LwpTerm.App.ViewModels.Editor;

namespace LwpTerm.App.Views.Editor;

public partial class SessionEditorWindow : Window
{
    public SessionEditorWindow()
    {
        InitializeComponent();
    }

    private SessionEditorViewModel Vm => (SessionEditorViewModel)DataContext;

    private void OnBrowseKey(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select private key file",
            Filter = "Private keys (*.pem;*.ppk;*.key;id_*)|*.pem;*.ppk;*.key;id_*|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dlg.ShowDialog(this) == true)
        {
            Vm.PrivateKeyPath = dlg.FileName;
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        // PasswordBox.Password is not bindable — lift the entered secrets into the VM now.
        var pwd = FirstNonEmpty(PasswordBox.Password, FtpPasswordBox.Password, RdpPasswordBox.Password, VncPasswordBox.Password);
        Vm.Password = pwd;
        Vm.Passphrase = PassphraseBox.Password;

        var error = Vm.Validate();
        if (error is not null)
        {
            MessageBox.Show(this, error, "Incomplete session", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrEmpty(v))
            {
                return v;
            }
        }

        return string.Empty;
    }
}
