using System.Windows;

namespace LwpTerm.App.Views.Dialogs;

public partial class InputDialog : Window
{
    public InputDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => { ValueBox.Focus(); ValueBox.SelectAll(); };
    }

    public string Value => ValueBox.Text;

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;

    public static string? Ask(string prompt, string title, string initial = "")
    {
        var dlg = new InputDialog
        {
            Title = title,
            Owner = Application.Current.MainWindow
        };
        dlg.PromptText.Text = prompt;
        dlg.ValueBox.Text = initial;
        return dlg.ShowDialog() == true ? dlg.Value : null;
    }
}
