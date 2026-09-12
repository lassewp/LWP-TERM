using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LwpTerm.App.ViewModels;

namespace LwpTerm.App.Views.Dialogs;

public partial class SettingsWindow : Window
{
    public SettingsWindow() => InitializeComponent();

    private SettingsViewModel Vm => (SettingsViewModel)DataContext;

    private void OnSave(object sender, RoutedEventArgs e)
    {
        Vm.SaveCommand.Execute(null);
        if (Vm.Saved)
        {
            DialogResult = true;
        }
    }

    // ---- numeric spinners (font size / scrollback lines) ------------------

    private void OnFontSizeUp(object sender, RoutedEventArgs e) => Vm.TerminalFontSize = Math.Clamp(Vm.TerminalFontSize + 1, 6, 48);

    private void OnFontSizeDown(object sender, RoutedEventArgs e) => Vm.TerminalFontSize = Math.Clamp(Vm.TerminalFontSize - 1, 6, 48);

    private void OnScrollbackUp(object sender, RoutedEventArgs e) => Vm.TerminalScrollback = Math.Clamp(Vm.TerminalScrollback + 100, 100, 500_000);

    private void OnScrollbackDown(object sender, RoutedEventArgs e) => Vm.TerminalScrollback = Math.Clamp(Vm.TerminalScrollback - 100, 100, 500_000);

    private void OnDigitsOnlyInput(object sender, TextCompositionEventArgs e) => e.Handled = !e.Text.All(char.IsDigit);

    private void OnDigitsOnlyPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(DataFormats.Text) ||
            !((string)e.DataObject.GetData(DataFormats.Text)).All(char.IsDigit))
        {
            e.CancelCommand();
        }
    }
}
