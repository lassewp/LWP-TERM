using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using AxMSTSCLib;
using LwpTerm.App.ViewModels.Tabs;
using LwpTerm.Core.Sessions;

namespace LwpTerm.App.Views.Tabs;

public partial class RdpView : UserControl
{
    private AxMsRdpClient9NotSafeForScripting? _rdp;
    private RdpTabViewModel? _vm;
    private bool _started;

    public RdpView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _vm = DataContext as RdpTabViewModel;
        if (_vm is null || _started)
        {
            return;
        }

        _started = true;

        try
        {
            _rdp = new AxMsRdpClient9NotSafeForScripting();
            ((ISupportInitialize)_rdp).BeginInit();
            Host.Child = _rdp;
            ((ISupportInitialize)_rdp).EndInit();

            _rdp.OnConnected += (_, _) => Dispatcher.Invoke(() =>
            {
                _vm.NotifyConnected();
                Overlay.Visibility = Visibility.Collapsed;
            });
            _rdp.OnDisconnected += OnRdpDisconnected;

            Configure(_rdp, _vm);
            _vm.NotifyConnecting();
            _rdp.Connect();
        }
        catch (Exception ex)
        {
            _vm.NotifyFailed(ex.Message);
            OverlayText.Text = "Embedded RDP could not start:\n" + ex.Message;
        }
    }

    private static void Configure(AxMsRdpClient9NotSafeForScripting rdp, RdpTabViewModel vm)
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
            rdp.AdvancedSettings9.SmartSizing = true;
            var w = Math.Max(640, (int)rdp.Width);
            var h = Math.Max(480, (int)rdp.Height);
            rdp.DesktopWidth = w;
            rdp.DesktopHeight = h;
        }
        else
        {
            rdp.AdvancedSettings9.SmartSizing = false;
            rdp.DesktopWidth = Math.Max(640, s.Width);
            rdp.DesktopHeight = Math.Max(480, s.Height);
        }

        rdp.AdvancedSettings9.RedirectClipboard = s.RedirectClipboard;
        rdp.AdvancedSettings9.RedirectDrives = s.RedirectDrives;
        rdp.AdvancedSettings9.AuthenticationLevel = 2;   // warn, don't fail, on cert mismatch
        rdp.AdvancedSettings9.EnableCredSspSupport = true;
        rdp.AdvancedSettings9.GrabFocusOnConnect = false;
    }

    private void OnRdpDisconnected(object? sender, IMsTscAxEvents_OnDisconnectedEvent e)
    {
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
        }
    }
}
