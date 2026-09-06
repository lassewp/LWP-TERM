using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace LwpTerm.App.ViewModels;

/// <summary>"#RRGGBB" -&gt; SolidColorBrush; null/invalid -&gt; Transparent.</summary>
public sealed class StringToBrushConverter : IValueConverter
{
    public static readonly StringToBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s && !string.IsNullOrWhiteSpace(s))
        {
            try
            {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(s)!);
            }
            catch
            {
                // fall through
            }
        }

        return Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
