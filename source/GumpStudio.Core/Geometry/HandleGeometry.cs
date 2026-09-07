using GumpStudio.Core.Primitives;

namespace GumpStudio.Core.Geometry;

/// <summary>What a drag starting at a given point will do.</summary>
public enum DragMode
{
    None,

    /// <summary>Dragging a selection rectangle over empty canvas.</summary>
    SelectionMarquee,

    Move,
    ResizeLeft,
    ResizeTop,
    ResizeRight,
    ResizeBottom,
    ResizeTopLeft,
    ResizeTopRight,
    ResizeBottomLeft,
    ResizeBottomRight,
}

/// <summary>
/// The single source of truth for selection-handle placement and hit testing.
/// </summary>
/// <remarks>
/// The original computed handle rectangles twice from independent literals —
/// once to draw them (offsets of -2, -3 and 6) and once to hit-test them
/// (inflate by 4, then eight hard-coded 5x5 boxes) — so the visible handles and
/// the clickable ones were not guaranteed to line up, and changing the handle
/// size meant editing two disjoint blocks of magic numbers. Both the renderer
/// and the interaction controller call into here instead.
/// </remarks>
public static class HandleGeometry
{
    /// <summary>Side length of a selection handle, in gump pixels.</summary>
    public const int HandleSize = 5;

    /// <summary>How far outside its bounds an element still counts as hit.</summary>
    public const int SelectionPadding = 3;

    /// <summary>The eight resize handles, in a fixed order.</summary>
    public static readonly DragMode[] ResizeHandles =
    [
        DragMode.ResizeTopLeft,
        DragMode.ResizeTop,
        DragMode.ResizeTopRight,
        DragMode.ResizeRight,
        DragMode.ResizeBottomRight,
        DragMode.ResizeBottom,
        DragMode.ResizeBottomLeft,
        DragMode.ResizeLeft,
    ];

    /// <summary>The rectangle a given handle occupies for an element of these bounds.</summary>
    public static GumpRect GetHandleRect(GumpRect bounds, DragMode handle) =>
        GetHandleRect(bounds, handle, HandleSize);

    /// <summary>
    /// The rectangle of one resize handle, at a given size.
    /// </summary>
    /// <param name="bounds">The element's bounds.</param>
    /// <param name="handle">Which handle.</param>
    /// <param name="handleSize">
    /// The handle's side, in the same units as <paramref name="bounds"/>.
    /// </param>
    /// <remarks>
    /// The size is a parameter because the canvas can be zoomed. A handle is
    /// meant to be the same size under the pointer whatever the zoom, so the
    /// caller grows it in gump units as the view shrinks — and both the drawing
    /// and the hit testing have to agree on that, or the handle would be
    /// somewhere other than where it looks.
    /// </remarks>
    public static GumpRect GetHandleRect(GumpRect bounds, DragMode handle, int handleSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(handleSize);

        int half = handleSize / 2;

        (int x, int y) = handle switch
        {
            DragMode.ResizeTopLeft => (bounds.Left, bounds.Top),
            DragMode.ResizeTop => (bounds.Left + (bounds.Width / 2), bounds.Top),
            DragMode.ResizeTopRight => (bounds.Right, bounds.Top),
            DragMode.ResizeRight => (bounds.Right, bounds.Top + (bounds.Height / 2)),
            DragMode.ResizeBottomRight => (bounds.Right, bounds.Bottom),
            DragMode.ResizeBottom => (bounds.Left + (bounds.Width / 2), bounds.Bottom),
            DragMode.ResizeBottomLeft => (bounds.Left, bounds.Bottom),
            DragMode.ResizeLeft => (bounds.Left, bounds.Top + (bounds.Height / 2)),
            _ => throw new ArgumentOutOfRangeException(nameof(handle), handle, "Not a resize handle."),
        };

        return new GumpRect(x - half, y - half, handleSize, handleSize);
    }

    /// <summary>
    /// Decides what a press at <paramref name="point"/> would start.
    /// </summary>
    /// <param name="bounds">The element's bounds, in the same space as the point.</param>
    /// <param name="point">Where the pointer is.</param>
    /// <param name="resizable">False for elements that can only be moved.</param>
    public static DragMode HitTest(GumpRect bounds, GumpPoint point, bool resizable) =>
        HitTest(bounds, point, resizable, HandleSize);

    /// <summary>
    /// Decides what a press at <paramref name="point"/> would start, with
    /// handles of a given size.
    /// </summary>
    /// <param name="bounds">The element's bounds, in the same space as the point.</param>
    /// <param name="point">Where the pointer is.</param>
    /// <param name="resizable">False for elements that can only be moved.</param>
    /// <param name="handleSize">The handle's side, in the same units as the bounds.</param>
    public static DragMode HitTest(
        GumpRect bounds, GumpPoint point, bool resizable, int handleSize)
    {
        if (resizable)
        {
            // Handles win over the body: they overhang the edges, and a corner
            // handle overlaps its two neighbours' edges.
            foreach (DragMode handle in ResizeHandles)
            {
                if (GetHandleRect(bounds, handle, handleSize).Contains(point))
                {
                    return handle;
                }
            }
        }

        // The grab margin around the body grows with the handles, so a zoomed-out
        // element stays as easy to pick up as a zoomed-in one.
        int padding = Math.Max(SelectionPadding, handleSize * SelectionPadding / HandleSize);

        return bounds.Inflate(padding, padding).Contains(point)
            ? DragMode.Move
            : DragMode.None;
    }

    /// <summary>
    /// Applies a resize drag.
    /// </summary>
    /// <param name="bounds">The bounds when the drag started.</param>
    /// <param name="handle">Which handle is being dragged.</param>
    /// <param name="dx">Total horizontal movement since the drag started.</param>
    /// <param name="dy">Total vertical movement since the drag started.</param>
    /// <param name="minimum">Smallest allowed width and height.</param>
    /// <remarks>
    /// One anchor-relative implementation for all eight handles, replacing the
    /// original's ~260-line switch of eight near-duplicate branches, each with
    /// its own hand-tuned fudge offsets.
    /// </remarks>
    public static GumpRect Resize(GumpRect bounds, DragMode handle, int dx, int dy, int minimum = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimum);

        int left = bounds.Left;
        int top = bounds.Top;
        int right = bounds.Right;
        int bottom = bounds.Bottom;

        if (MovesLeftEdge(handle))
        {
            left += dx;
        }

        if (MovesRightEdge(handle))
        {
            right += dx;
        }

        if (MovesTopEdge(handle))
        {
            top += dy;
        }

        if (MovesBottomEdge(handle))
        {
            bottom += dy;
        }

        // Clamp against the opposite, stationary edge so dragging past it stops
        // rather than inverting the rectangle.
        if (right - left < minimum)
        {
            if (MovesLeftEdge(handle))
            {
                left = right - minimum;
            }
            else
            {
                right = left + minimum;
            }
        }

        if (bottom - top < minimum)
        {
            if (MovesTopEdge(handle))
            {
                top = bottom - minimum;
            }
            else
            {
                bottom = top + minimum;
            }
        }

        return new GumpRect(left, top, right - left, bottom - top);
    }

    /// <summary>True when the drag resizes rather than moves or selects.</summary>
    /// <remarks>Listed explicitly: an "everything except None and Move" test
    /// silently started including the marquee mode when it was added.</remarks>
    public static bool IsResize(DragMode mode) =>
        mode is DragMode.ResizeLeft
            or DragMode.ResizeTop
            or DragMode.ResizeRight
            or DragMode.ResizeBottom
            or DragMode.ResizeTopLeft
            or DragMode.ResizeTopRight
            or DragMode.ResizeBottomLeft
            or DragMode.ResizeBottomRight;

    private static bool MovesLeftEdge(DragMode handle) =>
        handle is DragMode.ResizeLeft or DragMode.ResizeTopLeft or DragMode.ResizeBottomLeft;

    private static bool MovesRightEdge(DragMode handle) =>
        handle is DragMode.ResizeRight or DragMode.ResizeTopRight or DragMode.ResizeBottomRight;

    private static bool MovesTopEdge(DragMode handle) =>
        handle is DragMode.ResizeTop or DragMode.ResizeTopLeft or DragMode.ResizeTopRight;

    private static bool MovesBottomEdge(DragMode handle) =>
        handle is DragMode.ResizeBottom or DragMode.ResizeBottomLeft or DragMode.ResizeBottomRight;
}
