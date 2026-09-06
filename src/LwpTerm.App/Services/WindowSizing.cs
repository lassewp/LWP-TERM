using System;
using System.Windows;

namespace LwpTerm.App.Services;

/// <summary>Keeps startup window sizes sensible on small screens (laptops).</summary>
public static class WindowSizing
{
    /// <summary>
    /// Clamp a window's size to a fraction of the current work area, never
    /// exceeding the given design maximums, and cap <see cref="Window.MaxHeight"/> /
    /// <see cref="Window.MaxWidth"/> so it can't grow past the screen later.
    /// Call from the window constructor (after <c>InitializeComponent</c>).
    /// </summary>
    public static void ClampToWorkArea(Window window, double maxWidth, double maxHeight, double fraction = 0.92)
    {
        var area = SystemParameters.WorkArea;
        if (area.Width <= 0 || area.Height <= 0)
        {
            return;
        }

        window.MaxWidth = area.Width;
        window.MaxHeight = area.Height;

        window.Width = Math.Min(maxWidth, area.Width * fraction);
        window.Height = Math.Min(maxHeight, area.Height * fraction);
    }
}
