using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using AvalonDock;
using AvalonDock.Layout;
using AvalonDock.Themes;
using LwpTerm.App.Services;
using LwpTerm.App.ViewModels;
using LwpTerm.App.ViewModels.Tabs;
using LwpTerm.App.Views.Dialogs;
using LwpTerm.Core.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace LwpTerm.App;

public partial class MainWindow : Window
{
    private readonly ILayoutPersistenceService _layout;
    private readonly MainWindowViewModel _viewModel;
    private string? _pristineLayout;

    public MainWindow(MainWindowViewModel viewModel, ILayoutPersistenceService layout, ISettingsStore settings)
    {
        _layout = layout;
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        Dock.Theme = settings.Current.Theme == AppTheme.Light
            ? new Vs2013LightTheme()
            : new Vs2013DarkTheme();

        _pristineLayout = _layout.Capture(Dock);

        Dock.DocumentClosed += OnDocumentClosed;
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => _layout.TryLoad(Dock);

    private void OnClosing(object? sender, CancelEventArgs e) => _layout.Save(Dock);

    private void OnDocumentClosed(object? sender, DocumentClosedEventArgs e)
    {
        if (e.Document.Content is SessionTabViewModel tab)
        {
            tab.Dispose();
        }
    }

    // ---- View / layout menu ---------------------------------------------

    private void OnViewMenu(object sender, RoutedEventArgs e)
    {
        MenuVersion.Header = $"LWP-TERM {App.Services.GetRequiredService<IUpdateService>().CurrentVersion}";
        MenuPaneSessions.IsChecked = SessionsPane.IsVisible;
        MenuPaneTransfers.IsChecked = TransfersPane.IsVisible;
        MenuPaneLog.IsChecked = LogPane.IsVisible;
        RebuildLayoutsSubmenu();

        ViewMenu.PlacementTarget = ViewMenuButton;
        ViewMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        ViewMenu.IsOpen = true;
    }

    private void OnMenuSettings(object sender, RoutedEventArgs e) => _viewModel.OpenSettingsCommand.Execute(null);

    private void OnImportPutty(object sender, RoutedEventArgs e) => _viewModel.Sessions.ImportPuttyCommand.Execute(null);

    private async void OnCheckUpdates(object sender, RoutedEventArgs e)
    {
        var updates = App.Services.GetRequiredService<IUpdateService>();

        if (!updates.IsInstalled)
        {
            MessageBox.Show(this,
                "Updates are only available in the installed build.\n\n" +
                $"Current version: {updates.CurrentVersion}",
                "Check for updates", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var version = await updates.CheckAsync();
        if (version is null)
        {
            MessageBox.Show(this,
                $"You're on the latest version ({updates.CurrentVersion}).",
                "Check for updates", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var answer = MessageBox.Show(this,
            $"LWP-TERM {version} has been downloaded.\n\nRestart now to update?",
            "Update ready", MessageBoxButton.YesNo, MessageBoxImage.Information);

        if (answer == MessageBoxResult.Yes)
        {
            updates.ApplyAndRestart();
        }
    }

    private void OnTogglePane(object sender, RoutedEventArgs e)
    {
        var pane = (sender as MenuItem)?.Tag switch
        {
            "Sessions" => SessionsPane,
            "Transfers" => TransfersPane,
            "Log" => LogPane,
            _ => null
        };

        if (pane is null)
        {
            return;
        }

        if (pane.IsVisible)
        {
            pane.Hide();
        }
        else
        {
            pane.Show();
        }
    }

    private void OnResetLayout(object sender, RoutedEventArgs e)
    {
        if (_pristineLayout is not null)
        {
            _layout.Apply(Dock, _pristineLayout);
        }
    }

    private void OnSaveLayoutAs(object sender, RoutedEventArgs e)
    {
        var name = InputDialog.Ask("Name for this layout:", "Save layout");
        if (!string.IsNullOrWhiteSpace(name))
        {
            _layout.SaveNamed(Dock, name.Trim());
        }
    }

    private void RebuildLayoutsSubmenu()
    {
        MenuLayouts.Items.Clear();
        var names = _layout.ListNamed();

        if (names.Count == 0)
        {
            MenuLayouts.Items.Add(new MenuItem { Header = "(none saved)", IsEnabled = false });
            return;
        }

        foreach (var name in names)
        {
            var apply = new MenuItem { Header = name };
            apply.Click += (_, _) => _layout.ApplyNamed(Dock, name);
            MenuLayouts.Items.Add(apply);
        }

        MenuLayouts.Items.Add(new Separator());
        var delete = new MenuItem { Header = "Delete a saved layout" };
        foreach (var name in names)
        {
            var item = new MenuItem { Header = name };
            item.Click += (_, _) => _layout.DeleteNamed(name);
            delete.Items.Add(item);
        }

        MenuLayouts.Items.Add(delete);
    }
}
