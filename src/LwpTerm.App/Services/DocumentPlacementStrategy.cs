using System;
using System.Collections.Generic;
using AvalonDock.Layout;

namespace LwpTerm.App.Services;

/// <summary>
/// AvalonDock appends every document it imports from <c>DocumentsSource</c> to the end of the
/// last-focused pane. A tab that is removed and re-added on purpose (to rebuild its view) would
/// therefore hop to the end of the strip, or out of its pane. <see cref="Remember"/> lets it come
/// back to the pane and slot it left; everything else keeps AvalonDock's default placement.
/// </summary>
internal sealed class DocumentPlacementStrategy : ILayoutUpdateStrategy
{
    private readonly Dictionary<object, (LayoutDocumentPane Pane, int Index)> _pending =
        new(ReferenceEqualityComparer.Instance);

    public void Remember(object content, LayoutDocumentPane pane, int index) => _pending[content] = (pane, index);

    public void Forget(object content) => _pending.Remove(content);

    public bool BeforeInsertDocument(LayoutRoot layout, LayoutDocument anchorableToShow, ILayoutContainer destinationContainer)
    {
        if (anchorableToShow.Content is not { } content
            || !_pending.Remove(content, out var spot)
            || !ReferenceEquals(spot.Pane.Root, layout))
        {
            return false;
        }

        spot.Pane.InsertChildAt(Math.Min(spot.Index, spot.Pane.ChildrenCount), anchorableToShow);
        return true;
    }

    public void AfterInsertDocument(LayoutRoot layout, LayoutDocument anchorableShown)
    {
    }

    public bool BeforeInsertAnchorable(LayoutRoot layout, LayoutAnchorable anchorableToShow, ILayoutContainer destinationContainer) => false;

    public void AfterInsertAnchorable(LayoutRoot layout, LayoutAnchorable anchorableShown)
    {
    }
}
