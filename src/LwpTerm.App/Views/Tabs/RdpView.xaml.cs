using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LwpTerm.App.ViewModels.Tabs;

namespace LwpTerm.App.Views.Tabs;

public partial class RdpView : UserControl
{
    private RdpTabViewModel? _vm;

    public RdpView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _vm ??= DataContext as RdpTabViewModel;
        if (_vm is null)
        {
            return;
        }

        _vm.SurfaceReclaimRequested -= OnSurfaceReclaimRequested;
        _vm.SurfaceReclaimRequested += OnSurfaceReclaimRequested;

        AttachSurface();
        _vm.NotifyConnecting();
        Push();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => Push();

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.SurfaceReclaimRequested -= OnSurfaceReclaimRequested;
        }

        // Detach only — the session lives on in the view model so docking /
        // floating / full-screen does not drop it. Real teardown is in VM.Dispose().
        if (ReferenceEquals(Slot.Content, _vm?.Host.View))
        {
            Slot.Content = null;
        }
    }

    /// <summary>A full-screen window that borrowed the surface has closed; take it back.</summary>
    private void OnSurfaceReclaimRequested(object? sender, EventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        AttachSurface();
        Push();
        _vm?.Host.NotifyReattached();
    }

    private void AttachSurface()
    {
        if (_vm is null || ReferenceEquals(Slot.Content, _vm.Host.View))
        {
            return;
        }

        if (_vm.Host.View.Parent is ContentPresenter previous)
        {
            previous.Content = null;
        }

        Slot.Content = _vm.Host.View;
    }

    private void Push()
    {
        if (_vm is null || !IsLoaded)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var w = (int)Math.Round(ActualWidth * dpi.DpiScaleX);
        var h = (int)Math.Round(ActualHeight * dpi.DpiScaleY);
        _vm.Host.EnsureStartedOrResized(w, h);
    }
}
