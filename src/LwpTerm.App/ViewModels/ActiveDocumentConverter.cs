using System;
using System.Globalization;
using System.Windows.Data;
using LwpTerm.App.ViewModels.Tabs;

namespace LwpTerm.App.ViewModels;

/// <summary>
/// Sits on the <c>DockingManager.ActiveContent</c> / <c>ActiveDocument</c> binding. AvalonDock also
/// reports the tool windows (Sessions, Transfers, Log) as the active content; without this the
/// conversion to <see cref="SessionTabViewModel"/> fails, WPF flags a validation error and paints
/// its red error border around the whole dock area for as long as a tool window is active.
/// </summary>
public sealed class ActiveDocumentConverter : IValueConverter
{
    public static readonly ActiveDocumentConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null or SessionTabViewModel ? value : Binding.DoNothing;
}
