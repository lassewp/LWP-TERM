using System;
using System.Windows;
using System.Windows.Controls;
using LwpTerm.App.ViewModels.Tabs;
using VncSharp;

namespace LwpTerm.App.Views.Tabs;

public partial class VncView : UserControl
{
    private RemoteDesktop? _vnc;
    private VncTabViewModel? _vm;
    private bool _started;

    public VncView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _vm = DataContext as VncTabViewModel;
        if (_vm is null || _started)
        {
            return;
        }

        _started = true;
        _vm.SendCtrlAltDelRequested += OnSendCtrlAltDel;

        try
        {
            var s = _vm.Settings;
            var password = _vm.Password ?? string.Empty;

            _vnc = new RemoteDesktop
            {
                VncPort = s.Port <= 0 ? 5900 : s.Port,
                GetPassword = () => password
            };
            _vnc.ConnectComplete += (_, _) => Dispatcher.Invoke(() =>
            {
                _vm.NotifyConnected();
                OverlayText.Visibility = Visibility.Collapsed;
            });
            _vnc.ConnectionLost += (_, _) => Dispatcher.Invoke(() =>
            {
                _vm.NotifyDisconnected(null);
                OverlayText.Text = "Connection lost.";
                OverlayText.Visibility = Visibility.Visible;
            });

            Host.Child = _vnc;

            _vm.NotifyConnecting();
            _vnc.Connect(s.Host, s.ViewOnly, scaled: true);
        }
        catch (Exception ex)
        {
            _vm.NotifyFailed(ex.Message);
            OverlayText.Text = "Embedded VNC could not start:\n" + ex.Message;
        }
    }

    private void OnSendCtrlAltDel()
    {
        try
        {
            _vnc?.SendSpecialKeys(SpecialKeys.CtrlAltDel);
        }
        catch (Exception)
        {
            // control not connected
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.SendCtrlAltDelRequested -= OnSendCtrlAltDel;
        }

        if (_vnc is null)
        {
            return;
        }

        try
        {
            if (_vnc.IsConnected)
            {
                _vnc.Disconnect();
            }
        }
        catch
        {
            // ignore
        }
        finally
        {
            try { Host.Child = null; } catch { /* ignore */ }
            _vnc.Dispose();
            _vnc = null;
        }
    }
}
