using System.Windows;

namespace LwpTerm.App.Views.Dialogs;

public sealed record QuickDuplicateResult(string Name, string Host, bool AddAnother);

public partial class QuickDuplicateWindow : Window
{
    public QuickDuplicateWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            // Caret at the end, nothing selected — ready to tweak the suffix.
            NameBox.CaretIndex = NameBox.Text.Length;
        };
    }

    public QuickDuplicateResult? Result { get; private set; }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            NameBox.Focus();
            return;
        }

        Result = new QuickDuplicateResult(
            NameBox.Text.Trim(),
            HostGroup.Visibility == Visibility.Visible ? HostBox.Text.Trim() : string.Empty,
            AddAnotherBox.IsChecked == true);
        DialogResult = true;
    }

    /// <summary>Shows the dialog seeded with a name (and host, when the protocol has one).</summary>
    public static QuickDuplicateResult? Prompt(string sourceName, string seedName, string? seedHost, bool addAnother)
    {
        var dlg = new QuickDuplicateWindow { Owner = Application.Current.MainWindow };
        dlg.SourceLine.Text = $"Copy of “{sourceName}”";
        dlg.NameBox.Text = seedName;
        dlg.AddAnotherBox.IsChecked = addAnother;

        if (seedHost is null)
        {
            dlg.HostGroup.Visibility = Visibility.Collapsed;
        }
        else
        {
            dlg.HostBox.Text = seedHost;
        }

        return dlg.ShowDialog() == true ? dlg.Result : null;
    }
}
