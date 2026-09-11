using System;
using System.Windows;
using System.Windows.Controls;
using LwpTerm.App.ViewModels.Tabs;

namespace LwpTerm.App.Views.Tabs;

public partial class VncView : UserControl
{
    private VncTabViewModel? _vm;

    public VncView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _vm ??= DataContext as VncTabViewModel;
        if (_vm is null)
        {
            return;
        }

        _vm.SurfaceReclaimRequested -= OnSurfaceReclaimRequested;
        _vm.SurfaceReclaimRequested += OnSurfaceReclaimRequested;

        AttachSurface();
        _vm.NotifyConnecting();
        _vm.Host.EnsureStarted();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.SurfaceReclaimRequested -= OnSurfaceReclaimRequested;
        }

        // Detach only; the session lives in the view model across dock / float / full-screen.
        if (ReferenceEquals(Slot.Content, _vm?.Host.View))
        {
            Slot.Content = null;
        }
    }

    private void OnSurfaceReclaimRequested(object? sender, EventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        AttachSurface();
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
}
