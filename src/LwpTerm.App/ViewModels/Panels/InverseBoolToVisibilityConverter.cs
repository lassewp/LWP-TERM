using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LwpTerm.App.ViewModels.Panels;

/// <summary>True -&gt; Collapsed, False -&gt; Visible.</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public static readonly InverseBoolToVisibilityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
