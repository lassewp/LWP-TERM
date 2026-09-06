using System.Windows;
using System.Windows.Media;
using LwpTerm.Core.Settings;

namespace LwpTerm.App.Services;

/// <summary>
/// Applies a light/dark palette by overriding the shared <c>Brush.*</c> / <c>Color.*</c>
/// application resources. Windows resolve these at load, so the choice is applied
/// at startup; switching in the Settings dialog takes effect after a restart.
/// </summary>
public sealed class ThemeService
{
    private static readonly (string Key, string Dark, string Light)[] Palette =
    {
        ("Bg",          "#1E1E1E", "#F5F5F5"),
        ("BgAlt",       "#252526", "#ECECEC"),
        ("Panel",       "#2D2D30", "#E1E1E1"),
        ("Border",      "#3F3F46", "#C4C4C4"),
        ("Text",        "#F1F1F1", "#1B1B1B"),
        ("TextDim",     "#A0A0A0", "#5A5A5A"),
        ("Accent",      "#0E7C3A", "#0E7C3A"),
        ("AccentHover", "#12A34B", "#12A34B"),
    };

    public void Apply(AppTheme theme)
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        foreach (var (key, dark, light) in Palette)
        {
            var color = (Color)ColorConverter.ConvertFromString(theme == AppTheme.Light ? light : dark)!;
            app.Resources["Color." + key] = color;
            app.Resources["Brush." + key] = new SolidColorBrush(color);
        }
    }
}
