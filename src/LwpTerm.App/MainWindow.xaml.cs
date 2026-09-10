using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
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

        // Catches F11 / Esc even while a child HWND (RDP, VNC, WebView2 terminal)
        // has keyboard focus and would otherwise swallow the keystroke.
        ComponentDispatcher.ThreadPreprocessMessage += OnThreadPreprocessMessage;
        Activated += (_, _) => { if (_fsMode == FullscreenMode.Borderless) Topmost = true; };
        Deactivated += (_, _) => { if (_fsMode == FullscreenMode.Borderless) Topmost = false; };
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
        MenuPaneSessions.IsChecked = SessionsPane.IsVisible;
        MenuPaneTransfers.IsChecked = TransfersPane.IsVisible;
        MenuPaneLog.IsChecked = LogPane.IsVisible;
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

    // ---- Full screen ---------------------------------------------------

    private enum FullscreenMode
    {
        None,

        /// <summary>Borderless, fills the monitor work area — the taskbar stays reachable.</summary>
        FullScreen,

        /// <summary>Borderless, covers the whole monitor including the taskbar; top-most while focused.</summary>
        Borderless,
    }

    private FullscreenMode _fsMode = FullscreenMode.None;
    private FullscreenHintWindow? _fsHint;
    private WindowStyle _fsSavedStyle;
    private ResizeMode _fsSavedResize;
    private WindowState _fsSavedState;
    private bool _fsSavedTopmost;
    private Rect _fsSavedBounds;

    private void OnMenuFullScreen(object sender, RoutedEventArgs e)
    {
        var mode = (Keyboard.Modifiers & ModifierKeys.Shift) != 0
            ? FullscreenMode.Borderless
            : FullscreenMode.FullScreen;
        ToggleFullscreen(mode);
    }

    private void OnMenuBorderless(object sender, RoutedEventArgs e) => ToggleFullscreen(FullscreenMode.Borderless);

    private void OnDocumentHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2)
        {
            e.Handled = true;
            ToggleFullscreen(FullscreenMode.FullScreen);
        }
    }

    private void OnThreadPreprocessMessage(ref MSG msg, ref bool handled)
    {
        const int WM_KEYDOWN = 0x0100;
        const int WM_SYSKEYDOWN = 0x0104;
        const int VK_F11 = 0x7A;
        const int VK_ESCAPE = 0x1B;
        const int VK_SHIFT = 0x10;

        if ((msg.message != WM_KEYDOWN && msg.message != WM_SYSKEYDOWN) || !IsActive)
        {
            return;
        }

        switch ((int)msg.wParam)
        {
            case VK_F11:
                var shift = (GetKeyState(VK_SHIFT) & 0x8000) != 0;
                ToggleFullscreen(shift ? FullscreenMode.Borderless : FullscreenMode.FullScreen);
                handled = true;
                break;

            case VK_ESCAPE when _fsMode != FullscreenMode.None:
                ExitFullscreen();
                handled = true;
                break;
        }
    }

    private void ToggleFullscreen(FullscreenMode mode)
    {
        if (_fsMode == mode)
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
        if (_viewModel.ActiveDocument is null)
        {
            return;
        }

        // Read the target monitor before un-maximizing (that would move the window).
        var bounds = GetMonitorRectDip(workArea: mode == FullscreenMode.FullScreen);

        if (_fsMode == FullscreenMode.None)
        {
            _fsSavedStyle = WindowStyle;
            _fsSavedResize = ResizeMode;
            _fsSavedState = WindowState;
            _fsSavedTopmost = Topmost;
            _fsSavedBounds = new Rect(Left, Top, Width, Height);
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        _fsMode = mode;

        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
        }

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Topmost = mode == FullscreenMode.Borderless;

        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;

        ChromeRoot.Visibility = Visibility.Collapsed;
        FullscreenHost.Content = _viewModel.ActiveDocument;
        FullscreenHost.Visibility = Visibility.Visible;

        _fsHint ??= new FullscreenHintWindow(this, ExitFullscreen);
        _fsHint.Flash(bounds);
    }

    private void ExitFullscreen()
    {
        if (_fsMode == FullscreenMode.None)
        {
            return;
        }

        _fsMode = FullscreenMode.None;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _fsHint?.Hide();

        FullscreenHost.Visibility = Visibility.Collapsed;
        FullscreenHost.Content = null;
        ChromeRoot.Visibility = Visibility.Visible;

        Topmost = _fsSavedTopmost;
        WindowStyle = _fsSavedStyle;
        ResizeMode = _fsSavedResize;
        Left = _fsSavedBounds.Left;
        Top = _fsSavedBounds.Top;
        Width = _fsSavedBounds.Width;
        Height = _fsSavedBounds.Height;
        WindowState = _fsSavedState;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.ActiveDocument) || _fsMode == FullscreenMode.None)
        {
            return;
        }

        if (_viewModel.ActiveDocument is null)
        {
            ExitFullscreen();
        }
        else
        {
            FullscreenHost.Content = _viewModel.ActiveDocument;
        }
    }

    private Rect GetMonitorRectDip(bool workArea)
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);

        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            var fallback = SystemParameters.WorkArea;
            return new Rect(fallback.Left, fallback.Top, fallback.Width, fallback.Height);
        }

        var r = workArea ? info.rcWork : info.rcMonitor;
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = transform.Transform(new Point(r.left, r.top));
        var bottomRight = transform.Transform(new Point(r.right, r.bottom));
        return new Rect(topLeft, bottomRight);
    }

    private const int MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }
}
