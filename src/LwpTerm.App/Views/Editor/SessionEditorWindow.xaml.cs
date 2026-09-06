using System;
using System.Globalization;
using System.Windows;
using Microsoft.Win32;
using LwpTerm.App.Services;
using LwpTerm.App.ViewModels.Editor;
using WinFormsColor = System.Drawing.Color;
using WinFormsColorDialog = System.Windows.Forms.ColorDialog;
using WinFormsDialogResult = System.Windows.Forms.DialogResult;

namespace LwpTerm.App.Views.Editor;

public partial class SessionEditorWindow : Window
{
    public SessionEditorWindow()
    {
        InitializeComponent();
        // Fits a 768px-tall laptop; the Advanced tab keeps its own scrollbar.
        WindowSizing.ClampToWorkArea(this, maxWidth: 680, maxHeight: 748);
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

    private void OnPickColour(object sender, RoutedEventArgs e)
    {
        using var dlg = new WinFormsColorDialog { FullOpen = true, AnyColor = true };

        if (TryParseHex(Vm.ColorHex, out var r, out var g, out var b))
        {
            dlg.Color = WinFormsColor.FromArgb(r, g, b);
        }

        if (dlg.ShowDialog() == WinFormsDialogResult.OK)
        {
            Vm.ColorHex = $"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}";
        }
    }

    private void OnClearColour(object sender, RoutedEventArgs e) => Vm.ColorHex = string.Empty;

    private static bool TryParseHex(string? value, out int r, out int g, out int b)
    {
        r = g = b = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var hex = value.Trim().TrimStart('#');
        if (hex.Length != 6
            || !int.TryParse(hex.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r)
            || !int.TryParse(hex.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g)
            || !int.TryParse(hex.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b))
        {
            return false;
        }

        return true;
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
