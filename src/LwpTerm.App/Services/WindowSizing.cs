using System;
using System.Windows;

namespace LwpTerm.App.Services;

/// <summary>Keeps startup window sizes sensible on small screens (laptops).</summary>
public static class WindowSizing
{
    /// <summary>
    /// Clamp a window's <em>initial</em> size to a fraction of the current work
    /// area, never exceeding the given design maximums. Only the startup size is
    /// touched: <see cref="Window.MaxWidth"/> / <see cref="Window.MaxHeight"/> are
    /// deliberately left alone so the window can still maximize to fill whichever
    /// monitor it is on (setting them makes a maximized window stop short of the
    /// screen edges, leaving dead space on the right and bottom).
    /// Call from the window constructor (after <c>InitializeComponent</c>).
    /// </summary>
    public static void ClampToWorkArea(Window window, double maxWidth, double maxHeight, double fraction = 0.92)
    {
        var area = SystemParameters.WorkArea;
        if (area.Width <= 0 || area.Height <= 0)
        {
            return;
        }

        window.Width = Math.Min(maxWidth, area.Width * fraction);
        window.Height = Math.Min(maxHeight, area.Height * fraction);
    }
}
