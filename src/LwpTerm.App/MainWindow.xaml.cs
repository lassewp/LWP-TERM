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

        Activated += (_, _) => { if (_fsMode == FullscreenMode.Borderless) Topmost = true; };
        Deactivated += (_, _) => { if (_fsMode == FullscreenMode.Borderless) Topmost = false; };
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        // Works when a WPF element has focus. When an RDP / VNC / terminal child
        // HWND has focus the low-level keyboard hook (installed while full screen)
        // is what catches F11 / Esc instead.
        if (e.Key == Key.F11)
        {
            var mode = (Keyboard.Modifiers & ModifierKeys.Shift) != 0
                ? FullscreenMode.Borderless
                : FullscreenMode.FullScreen;
            ToggleFullscreen(mode);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _fsMode != FullscreenMode.None)
        {
            ExitFullscreen();
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
    private FullscreenBar? _fsBar;
    private WindowStyle _fsSavedStyle;
    private ResizeMode _fsSavedResize;
    private WindowState _fsSavedState;
    private bool _fsSavedTopmost;
    private Rect _fsSavedBounds;
    private Visibility _fsSavedToolbar;
    private Visibility _fsSavedStatus;
    private bool _fsSavedDocHeader;
    private (bool Sessions, bool Transfers, bool Log) _fsSavedPanes;

    private void OnMenuFullScreen(object sender, RoutedEventArgs e)
    {
        var mode = (Keyboard.Modifiers & ModifierKeys.Shift) != 0
            ? FullscreenMode.Borderless
            : FullscreenMode.FullScreen;
        ToggleFullscreen(mode);
    }

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

        var mode = (Keyboard.Modifiers & ModifierKeys.Shift) != 0
            ? FullscreenMode.Borderless
            : FullscreenMode.FullScreen;
        ToggleFullscreen(mode);
    }

    private void OnDocumentHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2)
        {
            e.Handled = true;
            ToggleFullscreen(FullscreenMode.FullScreen);
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

        var switching = _fsMode != FullscreenMode.None;

        // Resolve the target monitor before un-maximizing (that would move the window).
        if (!TryGetMonitorInfo(out var mi))
        {
            return;
        }

        var px = mode == FullscreenMode.FullScreen ? mi.rcWork : mi.rcMonitor;
        var dip = ToDip(px);

        if (!switching)
        {
            _fsSavedStyle = WindowStyle;
            _fsSavedResize = ResizeMode;
            _fsSavedState = WindowState;
            _fsSavedTopmost = Topmost;
            _fsSavedBounds = new Rect(Left, Top, Width, Height);
            _fsSavedToolbar = ToolbarBar.Visibility;
            _fsSavedStatus = StatusBar.Visibility;
            _fsSavedDocHeader = DocPane.ShowHeader;
            _fsSavedPanes = (SessionsPane.IsVisible, TransfersPane.IsVisible, LogPane.IsVisible);
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

        Left = dip.Left;
        Top = dip.Top;
        Width = dip.Width;
        Height = dip.Height;

        // Hide the chrome *in place* — the session surface never moves, so its
        // child HWND (RDP / VNC / WebView2) is never dropped or re-parented.
        ToolbarBar.Visibility = Visibility.Collapsed;
        StatusBar.Visibility = Visibility.Collapsed;
        DocPane.ShowHeader = false;
        if (SessionsPane.IsVisible)
        {
            SessionsPane.Hide();
        }

        if (TransfersPane.IsVisible)
        {
            TransfersPane.Hide();
        }

        if (LogPane.IsVisible)
        {
            LogPane.Hide();
        }

        InstallKeyboardHook();

        _fsBar ??= CreateFullscreenBar();
        _fsBar.Begin(BarTitle(), dip, (px.left, px.top, px.right, px.bottom), ModeActionLabel(mode));
    }

    private void ExitFullscreen()
    {
        if (_fsMode == FullscreenMode.None)
        {
            return;
        }

        _fsMode = FullscreenMode.None;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        RemoveKeyboardHook();
        _fsBar?.End();

        ToolbarBar.Visibility = _fsSavedToolbar;
        StatusBar.Visibility = _fsSavedStatus;
        DocPane.ShowHeader = _fsSavedDocHeader;
        if (_fsSavedPanes.Sessions)
        {
            SessionsPane.Show();
        }

        if (_fsSavedPanes.Transfers)
        {
            TransfersPane.Show();
        }

        if (_fsSavedPanes.Log)
        {
            LogPane.Show();
        }

        Topmost = _fsSavedTopmost;
        WindowStyle = _fsSavedStyle;
        ResizeMode = _fsSavedResize;
        Left = _fsSavedBounds.Left;
        Top = _fsSavedBounds.Top;
        Width = _fsSavedBounds.Width;
        Height = _fsSavedBounds.Height;
        WindowState = _fsSavedState;
    }

    private FullscreenBar CreateFullscreenBar()
    {
        var bar = new FullscreenBar(this);
        bar.ExitRequested += ExitFullscreen;
        bar.MinimiseRequested += () => WindowState = WindowState.Minimized;
        bar.ToggleModeRequested += () => EnterFullscreen(
            _fsMode == FullscreenMode.Borderless ? FullscreenMode.FullScreen : FullscreenMode.Borderless);
        return bar;
    }

    private static string ModeActionLabel(FullscreenMode mode) =>
        mode == FullscreenMode.Borderless ? "Windowed full screen" : "Borderless full screen";

    private string BarTitle()
    {
        var doc = _viewModel.ActiveDocument;
        if (doc is null)
        {
            return "LWP-TERM";
        }

        return string.IsNullOrWhiteSpace(doc.ToolTip) ? doc.Title : $"{doc.Title}  —  {doc.ToolTip}";
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
            _fsBar?.SetTitle(BarTitle());
        }
    }

    // ---- monitor geometry --------------------------------------------------

    private bool TryGetMonitorInfo(out MONITORINFO info)
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        return GetMonitorInfo(monitor, ref info);
    }

    private Rect ToDip(RECT r)
    {
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = transform.Transform(new Point(r.left, r.top));
        var bottomRight = transform.Transform(new Point(r.right, r.bottom));
        return new Rect(topLeft, bottomRight);
    }

    // ---- low-level keyboard hook (F11 / Esc while a child HWND has focus) --

    private IntPtr _kbHook = IntPtr.Zero;
    private LowLevelKeyboardProc? _kbHookProc;

    private void InstallKeyboardHook()
    {
        if (_kbHook != IntPtr.Zero)
        {
            return;
        }

        _kbHookProc = KeyboardHookCallback;
        _kbHook = SetWindowsHookEx(WH_KEYBOARD_LL, _kbHookProc, GetModuleHandle(null), 0);
    }

    private void RemoveKeyboardHook()
    {
        if (_kbHook == IntPtr.Zero)
        {
            return;
        }

        UnhookWindowsHookEx(_kbHook);
        _kbHook = IntPtr.Zero;
        _kbHookProc = null;
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _fsMode != FullscreenMode.None &&
            ((int)wParam == WM_KEYDOWN || (int)wParam == WM_SYSKEYDOWN))
        {
            var vk = Marshal.ReadInt32(lParam); // KBDLLHOOKSTRUCT.vkCode

            if (vk is VK_F11 or VK_ESCAPE)
            {
                var toBorderless = vk == VK_F11 && (GetKeyState(VK_SHIFT) & 0x8000) != 0;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (toBorderless)
                    {
                        ToggleFullscreen(FullscreenMode.Borderless);
                    }
                    else
                    {
                        ExitFullscreen();
                    }
                }));

                return (IntPtr)1; // swallow so it never reaches the remote session
            }
        }

        return CallNextHookEx(_kbHook, nCode, wParam, lParam);
    }

    // ---- native ----------------------------------------------------------

    private const int MONITOR_DEFAULTTONEAREST = 2;
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int VK_SHIFT = 0x10;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_F11 = 0x7A;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

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
