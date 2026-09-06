using System;
using System.Globalization;
using System.Windows;
using LwpTerm.App.ViewModels.Editor;
using WinFormsColor = System.Drawing.Color;
using WinFormsColorDialog = System.Windows.Forms.ColorDialog;
using WinFormsDialogResult = System.Windows.Forms.DialogResult;

namespace LwpTerm.App.Views.Editor;

public partial class FolderEditorWindow : Window
{
    public FolderEditorWindow() => InitializeComponent();

    private FolderEditorViewModel Vm => (FolderEditorViewModel)DataContext;

    private void OnSave(object sender, RoutedEventArgs e)
    {
        Vm.Save();
        DialogResult = Vm.Saved;
    }

    private void OnClearColour(object sender, RoutedEventArgs e) => Vm.ColorHex = string.Empty;

    private void OnPickColour(object sender, RoutedEventArgs e)
    {
        using var dlg = new WinFormsColorDialog { FullOpen = true, AnyColor = true };

        var hex = (Vm.ColorHex ?? string.Empty).Trim().TrimStart('#');
        if (hex.Length == 6
            && int.TryParse(hex.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
            && int.TryParse(hex.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
            && int.TryParse(hex.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            dlg.Color = WinFormsColor.FromArgb(r, g, b);
        }

        if (dlg.ShowDialog() == WinFormsDialogResult.OK)
        {
            Vm.ColorHex = $"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}";
        }
    }
}
