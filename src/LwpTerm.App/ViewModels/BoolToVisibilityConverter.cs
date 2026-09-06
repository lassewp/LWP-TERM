using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LwpTerm.App.ViewModels;

/// <summary>True -&gt; Visible, False -&gt; Collapsed.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public static readonly BoolToVisibilityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}
