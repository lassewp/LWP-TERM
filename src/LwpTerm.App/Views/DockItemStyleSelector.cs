using System.Windows;
using System.Windows.Controls;
using LwpTerm.App.ViewModels.Tabs;

namespace LwpTerm.App.Views;

/// <summary>
/// Picks the AvalonDock layout-item style per content. Session tabs get a style that pushes their
/// title into the layout model (floating-window caption, Ctrl+Tab navigator); the Sessions /
/// Transfers / Log tool windows set their titles in XAML and keep the default.
/// </summary>
public sealed class DockItemStyleSelector : StyleSelector
{
    public Style? DocumentStyle { get; set; }

    public override Style? SelectStyle(object? item, DependencyObject container) =>
        item is SessionTabViewModel ? DocumentStyle : null;
}
