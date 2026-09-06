using System.Windows;

namespace LwpTerm.App.Views.Dialogs;

public partial class InputDialog : Window
{
    private bool _isPassword;

    public InputDialog()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (_isPassword)
            {
                SecretBox.Focus();
            }
            else
            {
                ValueBox.Focus();
                ValueBox.SelectAll();
            }
        };
    }

    public string Value => _isPassword ? SecretBox.Password : ValueBox.Text;

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;

    public static string? Ask(string prompt, string title, string initial = "", bool isPassword = false)
    {
        var dlg = new InputDialog
        {
            Title = title,
            Owner = Application.Current.MainWindow
        };
        dlg._isPassword = isPassword;
        dlg.PromptText.Text = prompt;

        if (isPassword)
        {
            dlg.ValueBox.Visibility = Visibility.Collapsed;
            dlg.SecretBox.Visibility = Visibility.Visible;
        }
        else
        {
            dlg.ValueBox.Text = initial;
        }

        return dlg.ShowDialog() == true ? dlg.Value : null;
    }
}
