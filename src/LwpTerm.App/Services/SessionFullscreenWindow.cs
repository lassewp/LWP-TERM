using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using LwpTerm.App.ViewModels.Tabs;

namespace LwpTerm.App.Services;

/// <summary>
/// Hosts one session, borrowed from its docked tab, filling a monitor with no
/// app chrome — like an RDP client's full-screen mode. The main window stays
/// open and usable; closing this window hands the live surface back to the tab.
/// </summary>
internal sealed class SessionFullscreenWindow : Window
{
    private readonly (int Left, int Top, int Right, int Bottom) _px;
    private readonly FullscreenBar _bar;

    private IntPtr _kbHook = IntPtr.Zero;
    private LowLevelKeyboardProc? _kbHookProc;

    /// <summary>Raised when the bar's mode button (or the API) asks to swap
    /// windowed ⇄ borderless. The argument is the requested "borderless" state.</summary>
    public event Action<bool>? ModeToggleRequested;

    public SessionTabViewModel Session { get; }

    public bool Borderless { get; }

    public SessionFullscreenWindow(
        SessionTabViewModel session,
        bool borderless,
        Rect preBoundsDip,
        (int Left, int Top, int Right, int Bottom) pxBounds)
    {
        Session = session;
        Borderless = borderless;
        _px = pxBounds;

        Title = $"LWP-TERM — {session.Title}";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ShowInTaskbar = true;
        Background = Brushes.Black;
        Topmost = borderless;
        UseLayoutRounding = true;

        Left = preBoundsDip.Left;
        Top = preBoundsDip.Top;
        Width = preBoundsDip.Width;
        Height = preBoundsDip.Height;

        Content = new ContentControl
        {
            Focusable = false,
            Content = session,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };

        _bar = new FullscreenBar(this);
        _bar.ExitRequested += Close;
        _bar.MinimiseRequested += () => WindowState = WindowState.Minimized;
        _bar.ToggleModeRequested += () => ModeToggleRequested?.Invoke(!Borderless);

        Activated += (_, _) => { if (Borderless) Topmost = true; };
        Deactivated += (_, _) => { if (Borderless) Topmost = false; };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Now on the target monitor: convert its physical rect with this
        // window's own DPI, so it is exact even on a differently-scaled screen.
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = transform.Transform(new Point(_px.Left, _px.Top));
        var bottomRight = transform.Transform(new Point(_px.Right, _px.Bottom));

        Left = topLeft.X;
        Top = topLeft.Y;
        Width = bottomRight.X - topLeft.X;
        Height = bottomRight.Y - topLeft.Y;

        if (Borderless)
        {
            // Maximised + WindowStyle.None covers the whole monitor, taskbar included.
            WindowState = WindowState.Maximized;
        }

        InstallKeyboardHook();
        _bar.Begin(
            string.IsNullOrWhiteSpace(Session.ToolTip) ? Session.Title : $"{Session.Title}  —  {Session.ToolTip}",
            new Rect(Left, Top, Width, Height),
            (_px.Left, _px.Top, _px.Right, _px.Bottom),
            Borderless ? "Windowed full screen" : "Borderless full screen");
    }

    protected override void OnClosed(EventArgs e)
    {
        RemoveKeyboardHook();
        _bar.Close();
        base.OnClosed(e);
    }

    // ---- keyboard hook: Esc / F11 even while the child HWND has focus -----

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
        if (nCode >= 0 && IsActive && ((int)wParam == WM_KEYDOWN || (int)wParam == WM_SYSKEYDOWN))
        {
            var vk = Marshal.ReadInt32(lParam); // KBDLLHOOKSTRUCT.vkCode
            if (vk is VK_ESCAPE or VK_F11)
            {
                Dispatcher.BeginInvoke(new Action(Close));
                return (IntPtr)1;
            }
        }

        return CallNextHookEx(_kbHook, nCode, wParam, lParam);
    }

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_F11 = 0x7A;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
