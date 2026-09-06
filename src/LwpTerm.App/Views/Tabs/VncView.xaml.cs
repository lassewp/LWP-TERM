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

        if (!ReferenceEquals(Slot.Content, _vm.Host.View))
        {
            if (_vm.Host.View.Parent is ContentPresenter previous)
            {
                previous.Content = null;
            }

            Slot.Content = _vm.Host.View;
        }

        _vm.NotifyConnecting();
        _vm.Host.EnsureStarted();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Detach only; the session lives in the view model across dock / float.
        if (ReferenceEquals(Slot.Content, _vm?.Host.View))
        {
            Slot.Content = null;
        }
    }
}
