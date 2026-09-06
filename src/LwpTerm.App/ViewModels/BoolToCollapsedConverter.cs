using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LwpTerm.App.ViewModels;

/// <summary>True -&gt; Collapsed, False -&gt; Visible. Used to hide the target text on folder rows.</summary>
public sealed class BoolToCollapsedConverter : IValueConverter
{
    public static readonly BoolToCollapsedConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
