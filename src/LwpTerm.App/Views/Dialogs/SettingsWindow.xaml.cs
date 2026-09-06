using System.Windows;
using LwpTerm.App.ViewModels;

namespace LwpTerm.App.Views.Dialogs;

public partial class SettingsWindow : Window
{
    public SettingsWindow() => InitializeComponent();

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            vm.SaveCommand.Execute(null);
            if (vm.Saved)
            {
                DialogResult = true;
            }
        }
    }
}
