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

        // Re-attach the (persistent) surface to this view instance.
        if (!ReferenceEquals(Slot.Content, _vm.Host.View))
        {
            DetachHostFromPreviousParent(_vm);
            Slot.Content = _vm.Host.View;
        }

        _vm.NotifyConnecting();
        Push();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => Push();

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Detach only — the session lives on in the view model so docking /
        // floating the tab does not drop it. Real teardown is in VM.Dispose().
        if (ReferenceEquals(Slot.Content, _vm?.Host.View))
        {
            Slot.Content = null;
        }
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

    private static void DetachHostFromPreviousParent(RdpTabViewModel vm)
    {
        if (vm.Host.View.Parent is ContentPresenter previous)
        {
            previous.Content = null;
        }
    }
}
