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
using System.Windows.Threading;
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
    private readonly DocumentPlacementStrategy _placement = new();
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

        Dock.LayoutUpdateStrategy = _placement;
        Dock.DocumentClosed += OnDocumentClosed;
        Loaded += OnLoaded;
        Closing += OnClosing;

        // Borderless full screen keeps the session window Topmost so it covers
        // the taskbar; that only drops via its OWN Deactivated, which isn't
        // reliably raised by every way you can bring this window forward
        // (taskbar click, Alt+Tab). Force it here too, directly, whenever this
        // window actually becomes active — otherwise there was no reliable way
        // to get the main app back on top of a borderless session.
        Activated += (_, _) =>
        {
            if (_fsWindow is { Borderless: true })
            {
                _fsWindow.Topmost = false;
            }
        };
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

    /// <summary>The tab's close button, the context-menu Close / Close All and a floating window's
    /// close box all end up here. AvalonDock never removes a closed document from
    /// <c>DocumentsSource</c> itself, so hand it to the view model — otherwise the disposed tab
    /// lingers in <c>Documents</c> and AvalonDock re-imports it as a dead tab on the next layout reset.</summary>
    private void OnDocumentClosed(object? sender, DocumentClosedEventArgs e)
    {
        if (e.Document.Content is SessionTabViewModel tab)
        {
            _viewModel.CloseTabCommand.Execute(tab);
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
        window.ShowMainWindowRequested += BringMainWindowForward;
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
        var session = _fsSession;
        _fsWindow = null;
        _fsSession = null;
        _fsMode = FullscreenMode.None;

        if (session is not null)
        {
            RecreateDockedView(session);
        }
    }

    private void OnFullscreenDocumentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_fsSession is not null && !_viewModel.Documents.Contains(_fsSession))
        {
            _fsWindow?.Close();
        }
    }

    /// <summary>
    /// Forces AvalonDock to rebuild an RDP / VNC tab's document view from scratch after full
    /// screen. Their native ActiveX / WinForms windows can go blank after a cross-window
    /// SetParent (see <see cref="RdpSessionHost.NotifyReattached"/>), while a view that goes through
    /// a fresh Loaded cycle always renders correctly. Removing and reinserting the tab at the same
    /// index makes AvalonDock do exactly that; Dock.DocumentClosed is suspended around it purely to
    /// stop OnDocumentClosed removing the tab. AvalonDock re-imports a source item at the end of the
    /// last-focused pane, so the tab's pane and slot are remembered first and
    /// <see cref="DocumentPlacementStrategy"/> puts it back. A WebView2 terminal or a plain WPF tab
    /// re-attaches on its own (SurfaceReclaimRequested) and so stays where it was, floating or docked.
    /// </summary>
    private void RecreateDockedView(SessionTabViewModel session)
    {
        if (session is not (RdpTabViewModel or VncTabViewModel))
        {
            return;
        }

        var documents = _viewModel.Documents;
        var index = documents.IndexOf(session);
        if (index < 0)
        {
            return; // the tab was closed while full screen
        }

        if (FindDocument(session) is { Parent: LayoutDocumentPane pane } doc)
        {
            _placement.Remember(session, pane, pane.IndexOfChild(doc));
        }

        Dock.DocumentClosed -= OnDocumentClosed;
        try
        {
            documents.RemoveAt(index);
            documents.Insert(index, session);
            _viewModel.ActiveDocument = session;

            // The binding above is a no-op if AvalonDock never cleared the active content.
            if (FindDocument(session) is { } restored)
            {
                restored.IsSelected = true;
                restored.IsActive = true;
            }
        }
        finally
        {
            _placement.Forget(session);
            Dock.DocumentClosed += OnDocumentClosed;
        }
    }

    private LayoutDocument? FindDocument(SessionTabViewModel session) =>
        Dock.Layout.Descendents().OfType<LayoutDocument>().FirstOrDefault(d => ReferenceEquals(d.Content, session));

    /// <summary>The bar's "Show LWP-TERM" button. Dropping Topmost and calling
    /// Activate() while the full-screen window stays visible was not enough —
    /// the mstsc ActiveX control appears to reclaim foreground focus for its own
    /// top-level window almost immediately, so nothing visibly changed. Minimise
    /// the full-screen window outright instead: with no competing visible window
    /// left, there is nothing for it to steal focus back to. Its taskbar entry
    /// still restores it normally.</summary>
    private void BringMainWindowForward()
    {
        if (_fsWindow is not null)
        {
            _fsWindow.WindowState = WindowState.Minimized;
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
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

        // Let the just-recreated docked view actually get a layout/render
        // pass before stealing its surface straight back out — doing both
        // re-parents in one synchronous call (as switching modes otherwise
        // would) leaves the RDP/VNC control's rendering pipeline stuck, even
        // though a "settled" fresh attach always works.
        Dispatcher.BeginInvoke(new Action(() => EnterFullscreen(target)), DispatcherPriority.Background);
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
