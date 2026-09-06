using System.ComponentModel;
using System.Windows;
using AvalonDock;
using AvalonDock.Themes;
using LwpTerm.App.Services;
using LwpTerm.App.ViewModels;
using LwpTerm.App.ViewModels.Tabs;
using LwpTerm.Core.Settings;

namespace LwpTerm.App;

public partial class MainWindow : Window
{
    private readonly ILayoutPersistenceService _layout;

    public MainWindow(MainWindowViewModel viewModel, ILayoutPersistenceService layout, ISettingsStore settings)
    {
        _layout = layout;
        DataContext = viewModel;
        InitializeComponent();

        Dock.Theme = settings.Current.Theme == AppTheme.Light
            ? new Vs2013LightTheme()
            : new Vs2013DarkTheme();

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
}
