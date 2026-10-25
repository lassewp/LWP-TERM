using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LwpTerm.App.Views.Tabs;

/// <summary>
/// Moves a tab's live surface (WebView2 / RDP / VNC host) between the views that show it — the docked
/// tab, a floating window, the full-screen window. The surface is not a logical child of its
/// <see cref="ContentPresenter"/> (<c>Parent</c> stays <c>null</c>), and a presenter that loses it to
/// another one keeps a stale <c>Content</c> reference, so neither is a reliable record of where the
/// surface really is; the visual parent is.
/// </summary>
internal static class SurfaceSlot
{
    public static void Attach(ContentPresenter slot, FrameworkElement surface)
    {
        var holder = VisualTreeHelper.GetParent(surface);
        if (ReferenceEquals(holder, slot) && ReferenceEquals(slot.Content, surface))
        {
            return;
        }

        if (holder is ContentPresenter previous && !ReferenceEquals(previous, slot))
        {
            previous.Content = null;
        }

        // Clear first so a stale reference to the surface cannot turn the assignment into a no-op.
        slot.Content = null;
        slot.Content = surface;
    }
}
