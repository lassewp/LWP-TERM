using System;
using System.ComponentModel;
using System.Windows.Forms.Integration;
using System.Windows.Threading;
using AxMSTSCLib;
using LwpTerm.Core.Sessions;
using WinFormsDockStyle = System.Windows.Forms.DockStyle;
using WinFormsPanel = System.Windows.Forms.Panel;

namespace LwpTerm.App.Services;

/// <summary>
/// A long-lived RDP surface owned by the tab view model. The <see cref="View"/>
/// (a <see cref="WindowsFormsHost"/>) is re-parented as the tab docks / floats,
/// so the session survives without a reconnect.
/// </summary>
public sealed class RdpSessionHost : IDisposable
{
    private const int MinAxis = 200;
    private const int MaxAxis = 8192;

    private readonly RdpConnectionSettings _settings;
    private readonly string? _password;
    private readonly DispatcherTimer _resizeDebounce;

    private AxMsRdpClient9NotSafeForScripting? _rdp;
    private WinFormsPanel? _panel;
    private bool _started;
    private bool _connected;
    private bool _disposed;
    private int _pendingW;
    private int _pendingH;
    private int _lastW;
    private int _lastH;

    public RdpSessionHost(RdpConnectionSettings settings, string? password)
    {
        _settings = settings;
        _password = password;
        View = new WindowsFormsHost();

        _resizeDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _resizeDebounce.Tick += (_, _) => { _resizeDebounce.Stop(); ApplyRemoteSize(); };
    }

    public WindowsFormsHost View { get; }

    public bool FitToWindow => _settings.ResolutionMode == RdpResolutionMode.FitToWindow;

    public event Action? Connected;

    public event Action<string>? Disconnected;

    /// <summary>Called by the view once it has a real pixel size, and again on resize.</summary>
    public void EnsureStartedOrResized(int widthPx, int heightPx)
    {
        if (_disposed || widthPx < MinAxis || heightPx < MinAxis)
        {
            return;
        }

        widthPx -= widthPx % 2;
        heightPx -= heightPx % 2;
        _lastW = widthPx;
        _lastH = heightPx;

        if (!_started)
        {
            Start(widthPx, heightPx);
            return;
        }

        if (_connected && FitToWindow)
        {
            _pendingW = widthPx;
            _pendingH = heightPx;
            _resizeDebounce.Stop();
            _resizeDebounce.Start();
        }
    }

    /// <summary>Tear down the dropped ActiveX control and dial the host again at the last known size.</summary>
    public void Reconnect()
    {
        if (_disposed)
        {
            return;
        }

        _resizeDebounce.Stop();
        TeardownClient();
        _started = false;
        _connected = false;

        var w = _lastW >= MinAxis ? _lastW : Clamp(_settings.Width);
        var h = _lastH >= MinAxis ? _lastH : Clamp(_settings.Height);
        Start(w, h);
    }

    private void Start(int widthPx, int heightPx)
    {
        _started = true;

        _rdp = new AxMsRdpClient9NotSafeForScripting { Dock = WinFormsDockStyle.Fill };
        _panel = new WinFormsPanel();
        _panel.Controls.Add(_rdp);

        ((ISupportInitialize)_rdp).BeginInit();
        View.Child = _panel;
        ((ISupportInitialize)_rdp).EndInit();

        _rdp.OnConnected += (_, _) => { _connected = true; Connected?.Invoke(); };
        _rdp.OnDisconnected += OnRdpDisconnected;

        var s = _settings;
        _rdp.Server = s.Host;
        _rdp.AdvancedSettings9.RDPPort = s.Port <= 0 ? 3389 : s.Port;
        _rdp.UserName = s.Username;
        _rdp.AdvancedSettings9.ClearTextPassword = _password ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(s.Domain))
        {
            _rdp.Domain = s.Domain;
        }

        if (FitToWindow)
        {
            _rdp.AdvancedSettings9.SmartSizing = true;
            _rdp.DesktopWidth = Clamp(widthPx);
            _rdp.DesktopHeight = Clamp(heightPx);
        }
        else
        {
            _rdp.AdvancedSettings9.SmartSizing = false;
            _rdp.DesktopWidth = Clamp(s.Width);
            _rdp.DesktopHeight = Clamp(s.Height);
        }

        _rdp.AdvancedSettings9.RedirectClipboard = s.RedirectClipboard;
        _rdp.AdvancedSettings9.RedirectDrives = s.RedirectDrives;
        _rdp.AdvancedSettings9.AuthenticationLevel = 2;
        _rdp.AdvancedSettings9.EnableCredSspSupport = true;
        _rdp.AdvancedSettings9.GrabFocusOnConnect = false;
        _rdp.AdvancedSettings9.DisplayConnectionBar = false;

        _rdp.Connect();
    }

    private void ApplyRemoteSize()
    {
        if (_rdp is null || !_connected || !FitToWindow || _pendingW < MinAxis || _pendingH < MinAxis)
        {
            return;
        }

        var physW = (uint)Math.Clamp((int)Math.Round(_pendingW / 96.0 * 25.4), 10, 10000);
        var physH = (uint)Math.Clamp((int)Math.Round(_pendingH / 96.0 * 25.4), 10, 10000);

        try
        {
            _rdp.UpdateSessionDisplaySettings(
                (uint)Clamp(_pendingW), (uint)Clamp(_pendingH),
                physW, physH, 0, 100, 100);
        }
        catch
        {
            // Older host / session busy — SmartSizing keeps the view filled.
        }
    }

    private void OnRdpDisconnected(object? sender, IMsTscAxEvents_OnDisconnectedEvent e)
    {
        _connected = false;
        var text = e.discReason.ToString();
        try
        {
            var ext = _rdp!.GetErrorDescription((uint)e.discReason, (uint)_rdp.ExtendedDisconnectReason);
            if (!string.IsNullOrWhiteSpace(ext))
            {
                text = ext;
            }
        }
        catch
        {
            // GetErrorDescription throws for some benign local disconnects.
        }

        Disconnected?.Invoke(text);
    }

    private static int Clamp(int value) => Math.Clamp(value, MinAxis, MaxAxis);

    private void TeardownClient()
    {
        if (_rdp is not null)
        {
            try
            {
                _rdp.OnDisconnected -= OnRdpDisconnected;
                if (_rdp.Connected != 0)
                {
                    _rdp.Disconnect();
                }
            }
            catch
            {
                // ignore
            }

            _rdp.Dispose();
            _rdp = null;
        }

        try { View.Child = null; } catch { /* ignore */ }
        _panel?.Dispose();
        _panel = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _resizeDebounce.Stop();
        TeardownClient();
    }
}
