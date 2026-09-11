using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using AvalonDock;
using AvalonDock.Layout;
using AvalonDock.Themes;
using LwpTerm.App.Services;
using LwpTerm.App.ViewModels;
using LwpTerm.App.ViewModels.Tabs;
using LwpTerm.App.Views.Dialogs;
using LwpTerm.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using WinFormsScreen = System.Windows.Forms.Screen;

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

        WindowSizing.ClampToWorkArea(this, maxWidth: 1280, maxHeight: 820);

        Dock.Theme = settings.Current.Theme == AppTheme.Light
            ? new Vs2013LightTheme()
            : new Vs2013DarkTheme();

        _pristineLayout = _layout.Capture(Dock);

        Dock.DocumentClosed += OnDocumentClosed;
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        // Entry point when a WPF element in the main window has focus. Once a
        // session is full screen it lives in its own window with its own hook.
        if (e.Key == Key.F11)
        {
            ToggleFullscreen(ShiftHeld());
            e.Handled = true;
        }

        base.OnPreviewKeyDown(e);
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => _layout.TryLoad(Dock);

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        ExitFullscreen();
        _layout.Save(Dock);
    }

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
        MenuPaneSessions.IsChecked = LivePane("sessions")?.IsVisible ?? false;
        MenuPaneTransfers.IsChecked = LivePane("transfers")?.IsVisible ?? false;
        MenuPaneLog.IsChecked = LivePane("log")?.IsVisible ?? false;
        MenuFullScreen.IsChecked = _fsMode == FullscreenMode.FullScreen;
        MenuBorderless.IsChecked = _fsMode == FullscreenMode.Borderless;
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
        var contentId = (sender as MenuItem)?.Tag switch
        {
            "Sessions" => "sessions",
            "Transfers" => "transfers",
            "Log" => "log",
            _ => null
        };

        if (contentId is null || LivePane(contentId) is not { } pane)
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

    /// <summary>Finds a panel in the <em>current</em> layout by its ContentId. The
    /// x:Name'd LayoutAnchorable fields go stale once a saved layout.xml is
    /// restored (deserialization swaps in new objects).</summary>
    private LayoutAnchorable? LivePane(string contentId) => LivePanes(contentId).FirstOrDefault();

    private IEnumerable<LayoutAnchorable> LivePanes(params string[] contentIds) =>
        Dock.Layout.Descendents().OfType<LayoutAnchorable>()
            .Where(a => a.ContentId is { } id && contentIds.Contains(id));

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

    // ---- Full screen (session in its own window) -----------------------

    private enum FullscreenMode
    {
        None,

        /// <summary>Fills the monitor work area — the taskbar stays reachable.</summary>
        FullScreen,

        /// <summary>Covers the whole monitor, taskbar included; top-most while focused.</summary>
        Borderless,
    }

    private FullscreenMode _fsMode = FullscreenMode.None;
    private SessionFullscreenWindow? _fsWindow;
    private SessionTabViewModel? _fsSession;

    private void OnMenuFullScreen(object sender, RoutedEventArgs e) => ToggleFullscreen(ShiftHeld());

    private void OnMenuBorderless(object sender, RoutedEventArgs e) => ToggleFullscreen(FullscreenMode.Borderless);

    /// <summary>The ⛶ affordance on a document tab header. Handled on MouseDown
    /// because AvalonDock captures the mouse for tab drag and eats the MouseUp.</summary>
    private void OnHeaderFullScreen(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        e.Handled = true;

        if ((sender as FrameworkElement)?.DataContext is LayoutContent { Content: SessionTabViewModel vm })
        {
            _viewModel.ActiveDocument = vm;
        }

        ToggleFullscreen(ShiftHeld());
    }

    private void OnDocumentHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2)
        {
            e.Handled = true;
            ToggleFullscreen(FullscreenMode.FullScreen);
        }
    }

    private static FullscreenMode ShiftHeld() =>
        (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? FullscreenMode.Borderless : FullscreenMode.FullScreen;

    private void ToggleFullscreen(FullscreenMode mode)
    {
        if (_fsWindow is not null)
        {
            ExitFullscreen();
        }
        else
        {
            EnterFullscreen(mode);
        }
    }

    private void EnterFullscreen(FullscreenMode mode)
    {
        if (_fsWindow is not null || _viewModel.ActiveDocument is not { } session)
        {
            return;
        }

        var screen = PickFullscreenScreen();
        var area = mode == FullscreenMode.Borderless ? screen.Bounds : screen.WorkingArea;
        var px = (area.Left, area.Top, area.Right, area.Bottom);
        var dip = ToDip(area.Left, area.Top, area.Right, area.Bottom);

        _fsSession = session;
        _fsMode = mode;

        var window = new SessionFullscreenWindow(session, mode == FullscreenMode.Borderless, dip, px)
        {
            Owner = this,
        };
        window.ModeToggleRequested += SwitchFullscreenMode;
        window.Closed += (_, _) => OnFullscreenWindowClosed();
        _fsWindow = window;

        _viewModel.Documents.CollectionChanged += OnFullscreenDocumentsChanged;

        window.Show();
        window.Activate();
    }

    private void ExitFullscreen() => _fsWindow?.Close(); // -> OnFullscreenWindowClosed

    private void OnFullscreenWindowClosed()
    {
        // The surface has already been handed back to the docked tab by
        // SessionFullscreenWindow.OnClosing — this just clears our own state.
        _viewModel.Documents.CollectionChanged -= OnFullscreenDocumentsChanged;
        _fsWindow = null;
        _fsSession = null;
        _fsMode = FullscreenMode.None;
    }

    private void OnFullscreenDocumentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_fsSession is not null && !_viewModel.Documents.Contains(_fsSession))
        {
            _fsWindow?.Close();
        }
    }

    private void SwitchFullscreenMode(bool borderless)
    {
        var session = _fsSession;
        var target = borderless ? FullscreenMode.Borderless : FullscreenMode.FullScreen;
        if (session is null || target == _fsMode)
        {
            return;
        }

        ExitFullscreen();
        _viewModel.ActiveDocument = session;
        EnterFullscreen(target);
    }

    /// <summary>The monitor the main window is currently on — full screen opens there,
    /// same as every other app's F11, rather than hopping to a different screen.</summary>
    private WinFormsScreen PickFullscreenScreen() =>
        WinFormsScreen.FromHandle(new WindowInteropHelper(this).Handle);

    private Rect ToDip(double left, double top, double right, double bottom)
    {
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = transform.Transform(new Point(left, top));
        var bottomRight = transform.Transform(new Point(right, bottom));
        return new Rect(topLeft, bottomRight);
    }
}
