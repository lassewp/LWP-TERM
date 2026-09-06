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
        //  key            dark        light
        ("Bg",          "#1E1E1E", "#FBFBFB"),
        ("BgAlt",       "#252526", "#F0F0F0"),
        ("Panel",       "#2D2D30", "#E6E6E6"),
        ("Border",      "#3F3F46", "#B4B4B4"),
        ("Text",        "#F1F1F1", "#151515"),
        ("TextDim",     "#B4B4B4", "#565656"),
        ("Accent",      "#3FA65B", "#0E7C3A"),
        ("AccentHover", "#54C271", "#12A34B"),
        ("Error",       "#E06C75", "#B3261E"),
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
