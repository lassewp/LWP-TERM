using System;
using System.Windows;
using System.Windows.Controls;
using LwpTerm.App.ViewModels.Tabs;

namespace LwpTerm.App.Views.Tabs;

public partial class TerminalView : UserControl
{
    private TerminalTabViewModel? _vm;

    public TerminalView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _vm ??= DataContext as TerminalTabViewModel;
        if (_vm is null)
        {
            return;
        }

        _vm.SurfaceReclaimRequested -= OnSurfaceReclaimRequested;
        _vm.SurfaceReclaimRequested += OnSurfaceReclaimRequested;

        AttachSurface();
        _vm.Host.EnsureInitialized();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.SurfaceReclaimRequested -= OnSurfaceReclaimRequested;
        }

        // Detach only — the surface and session live in the view model across
        // dock / float / full-screen. Real teardown happens in VM.Dispose().
        if (ReferenceEquals(Slot.Content, _vm?.Host.View))
        {
            Slot.Content = null;
        }
    }

    private void OnSurfaceReclaimRequested(object? sender, EventArgs e)
    {
        if (IsLoaded)
        {
            AttachSurface();
        }
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
