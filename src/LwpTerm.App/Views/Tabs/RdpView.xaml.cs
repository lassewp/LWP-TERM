using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AxMSTSCLib;
using LwpTerm.App.ViewModels.Tabs;
using LwpTerm.Core.Sessions;
using WinFormsDockStyle = System.Windows.Forms.DockStyle;
using WinFormsPanel = System.Windows.Forms.Panel;

namespace LwpTerm.App.Views.Tabs;

public partial class RdpView : UserControl
{
    private const int MinAxis = 200;
    private const int MaxAxis = 8192;

    private readonly DispatcherTimer _resizeDebounce;

    private AxMsRdpClient9NotSafeForScripting? _rdp;
    private WinFormsPanel? _panel;
    private RdpTabViewModel? _vm;
    private bool _started;
    private bool _connected;

    public RdpView()
    {
        InitializeComponent();
        _resizeDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _resizeDebounce.Tick += (_, _) => { _resizeDebounce.Stop(); ApplyRemoteSize(); };

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
    }

    private bool FitToWindow => _vm?.Settings.ResolutionMode == RdpResolutionMode.FitToWindow;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _vm ??= DataContext as RdpTabViewModel;
        TryStart();
    }

    private void TryStart()
    {
        if (_started || _vm is null || !IsLoaded)
        {
            return;
        }

        // Wait until the tab actually has a size before negotiating a resolution.
        var (w, h) = CurrentPixelSize();
        if (w < MinAxis || h < MinAxis)
        {
            return;
        }

        _started = true;
        StartSession(w, h);
    }

    private void StartSession(int width, int height)
    {
        try
        {
            _rdp = new AxMsRdpClient9NotSafeForScripting { Dock = WinFormsDockStyle.Fill };
            _panel = new WinFormsPanel();
            _panel.Controls.Add(_rdp);

            ((ISupportInitialize)_rdp).BeginInit();
            Host.Child = _panel;
            ((ISupportInitialize)_rdp).EndInit();

            _rdp.OnConnected += (_, _) => Dispatcher.Invoke(() =>
            {
                _connected = true;
                _vm!.NotifyConnected();
                Overlay.Visibility = Visibility.Collapsed;
            });
            _rdp.OnDisconnected += OnRdpDisconnected;

            Configure(_rdp, _vm!, width, height);
            _vm!.NotifyConnecting();
            _rdp.Connect();
        }
        catch (Exception ex)
        {
            _vm!.NotifyFailed(ex.Message);
            OverlayText.Text = "Embedded RDP could not start:\n" + ex.Message;
        }
    }

    private static void Configure(AxMsRdpClient9NotSafeForScripting rdp, RdpTabViewModel vm, int width, int height)
    {
        var s = vm.Settings;
        rdp.Server = s.Host;
        rdp.AdvancedSettings9.RDPPort = s.Port <= 0 ? 3389 : s.Port;
        rdp.UserName = s.Username;
        rdp.AdvancedSettings9.ClearTextPassword = vm.Password ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(s.Domain))
        {
            rdp.Domain = s.Domain;
        }

        if (s.ResolutionMode == RdpResolutionMode.FitToWindow)
        {
            // SmartSizing scales the framebuffer to the control immediately;
            // UpdateSessionDisplaySettings later reflows the *remote* desktop.
            rdp.AdvancedSettings9.SmartSizing = true;
            rdp.DesktopWidth = Clamp(width);
            rdp.DesktopHeight = Clamp(height);
        }
        else
        {
            rdp.AdvancedSettings9.SmartSizing = false;
            rdp.DesktopWidth = Clamp(s.Width);
            rdp.DesktopHeight = Clamp(s.Height);
        }

        rdp.AdvancedSettings9.RedirectClipboard = s.RedirectClipboard;
        rdp.AdvancedSettings9.RedirectDrives = s.RedirectDrives;
        rdp.AdvancedSettings9.AuthenticationLevel = 2;   // warn, don't fail, on cert mismatch
        rdp.AdvancedSettings9.EnableCredSspSupport = true;
        rdp.AdvancedSettings9.GrabFocusOnConnect = false;
        rdp.AdvancedSettings9.DisplayConnectionBar = false;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_started)
        {
            TryStart();
            return;
        }

        if (_connected && FitToWindow)
        {
            _resizeDebounce.Stop();
            _resizeDebounce.Start();
        }
    }

    /// <summary>Ask the remote session to match the current tab size (RDP 8.1+ hosts).</summary>
    private void ApplyRemoteSize()
    {
        if (_rdp is null || !_connected || !FitToWindow)
        {
            return;
        }

        var (w, h) = CurrentPixelSize();
        if (w < MinAxis || h < MinAxis)
        {
            return;
        }

        // Physical size in millimetres (assume ~96 DPI); the API requires 10–10000.
        var physW = (uint)Math.Clamp((int)Math.Round(w / 96.0 * 25.4), 10, 10000);
        var physH = (uint)Math.Clamp((int)Math.Round(h / 96.0 * 25.4), 10, 10000);

        try
        {
            _rdp.UpdateSessionDisplaySettings(
                (uint)Clamp(w), (uint)Clamp(h),
                physW, physH,
                ulOrientation: 0,
                ulDesktopScaleFactor: 100, ulDeviceScaleFactor: 100);
        }
        catch
        {
            // Older host / session busy — SmartSizing keeps the view filled anyway.
        }
    }

    private (int Width, int Height) CurrentPixelSize()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var w = (int)Math.Round(ActualWidth * dpi.DpiScaleX);
        var h = (int)Math.Round(ActualHeight * dpi.DpiScaleY);
        // RDP wants even dimensions.
        return (w - (w % 2), h - (h % 2));
    }

    private static int Clamp(int value) => Math.Clamp(value, MinAxis, MaxAxis);

    private void OnRdpDisconnected(object? sender, IMsTscAxEvents_OnDisconnectedEvent e)
    {
        _connected = false;
        var reason = e.discReason;
        var text = reason.ToString();
        try
        {
            var ext = _rdp!.GetErrorDescription((uint)reason, (uint)_rdp.ExtendedDisconnectReason);
            if (!string.IsNullOrWhiteSpace(ext))
            {
                text = ext;
            }
        }
        catch
        {
            // GetErrorDescription can throw for benign local disconnects — keep the code.
        }

        Dispatcher.Invoke(() =>
        {
            _vm?.NotifyDisconnected(text);
            OverlayText.Text = "Disconnected: " + text;
            Overlay.Visibility = Visibility.Visible;
        });
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _resizeDebounce.Stop();

        if (_rdp is null)
        {
            return;
        }

        try
        {
            if (_rdp.Connected != 0)
            {
                _rdp.Disconnect();
            }
        }
        catch
        {
            // ignore
        }
        finally
        {
            try { Host.Child = null; } catch { /* ignore */ }
            _rdp.Dispose();
            _rdp = null;
            _panel?.Dispose();
            _panel = null;
        }
    }
}
