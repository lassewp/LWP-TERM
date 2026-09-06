using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LwpTerm.App.ViewModels.FileBrowser;

/// <summary>Null / empty string -&gt; Collapsed, otherwise Visible.</summary>
public sealed class NullToCollapsedConverter : IValueConverter
{
    public static readonly NullToCollapsedConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null || (value is string s && string.IsNullOrWhiteSpace(s))
            ? Visibility.Collapsed
            : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
