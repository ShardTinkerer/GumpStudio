using GumpStudio.Core.Commands;
using GumpStudio.Core.Document;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Geometry;
using GumpStudio.Core.Primitives;

namespace GumpStudio.Core.Editing;

/// <summary>Keyboard modifiers that change what a pointer gesture means.</summary>
[Flags]
public enum InputModifiers
{
    None = 0,

    /// <summary>Add to or toggle within the selection rather than replacing it.</summary>
    Extend = 1,
}

/// <summary>
/// The canvas interaction state machine: selection, dragging, resizing and
/// marquee selection.
/// </summary>
/// <remarks>
/// Deliberately in Core with no UI dependency, so the behaviour can be tested
/// directly. In the original this logic was roughly 500 lines of nested pointer
/// handlers inside <c>DesignerForm</c> and could only be exercised by driving a
/// live WinForms window.
/// </remarks>
public sealed class CanvasInteractionController(UndoHistory history)
{
    private readonly List<Element> _selection = [];
    private readonly Dictionary<Element, GumpRect> _dragStart = [];

    private GumpPage? _page;
    private GumpPoint _pressedAt;
    private GumpRect _primaryStart;
    private Element? _primary;

    /// <summary>The page being edited.</summary>
    public GumpPage? Page
    {
        get => _page;
        set
        {
            if (ReferenceEquals(_page, value))
            {
                return;
            }

            _page = value;
            ClearSelection();
        }
    }

    /// <summary>Currently selected elements.</summary>
    public IReadOnlyList<Element> Selection => _selection;

    /// <summary>What the in-progress gesture is doing.</summary>
    public DragMode Mode { get; private set; } = DragMode.None;

    /// <summary>The marquee rectangle while one is being dragged.</summary>
    public GumpRect? Marquee { get; private set; }

    /// <summary>The design grid. Moving and resizing snap to it when enabled.</summary>
    public GridSettings Grid { get; } = new();

    /// <summary>True while a gesture is in progress.</summary>
    public bool IsDragging => Mode != DragMode.None;

    /// <summary>Raised whenever the selection or an element's geometry changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Topmost element at a point, or null.</summary>
    public Element? HitTest(GumpPoint at)
    {
        if (_page is null)
        {
            return null;
        }

        // Reversed: later children draw in front, so they are hit first.
        foreach (Element element in Descending())
        {
            if (HandleGeometry.HitTest(element.GetAbsoluteBounds(), at, element.IsResizable) != DragMode.None)
            {
                return element;
            }
        }

        return null;
    }

    /// <summary>Begins a gesture.</summary>
    public void PointerPressed(GumpPoint at, InputModifiers modifiers = InputModifiers.None)
    {
        if (_page is null)
        {
            return;
        }

        _pressedAt = at;

        // A handle on something already selected takes priority, so grabbing a
        // corner resizes rather than selecting whatever sits behind it.
        foreach (Element selected in _selection)
        {
            if (!selected.IsResizable)
            {
                continue;
            }

            DragMode handle = HandleGeometry.HitTest(selected.GetAbsoluteBounds(), at, resizable: true);

            if (HandleGeometry.IsResize(handle))
            {
                _primary = selected;
                _primaryStart = selected.Bounds;
                Mode = handle;

                CaptureDragStart();

                return;
            }
        }

        Element? hit = HitTest(at);

        if (hit is null)
        {
            if (modifiers is not InputModifiers.Extend)
            {
                ClearSelection();
            }

            Mode = DragMode.SelectionMarquee;
            Marquee = new GumpRect(at.X, at.Y, 0, 0);

            return;
        }

        if (modifiers.HasFlag(InputModifiers.Extend))
        {
            Toggle(hit);
        }
        else if (!_selection.Contains(hit))
        {
            // Pressing an already-selected element keeps the whole selection, so
            // a multi-element drag is possible.
            ClearSelection();
            Add(hit);
        }

        _primary = hit;
        _primaryStart = hit.Bounds;
        Mode = DragMode.Move;

        CaptureDragStart();
    }

    /// <summary>Updates an in-progress gesture.</summary>
    public void PointerMoved(GumpPoint at)
    {
        if (Mode == DragMode.None)
        {
            return;
        }

        int dx = at.X - _pressedAt.X;
        int dy = at.Y - _pressedAt.Y;

        if (Mode == DragMode.SelectionMarquee)
        {
            Marquee = GumpRect.FromCorners(_pressedAt, at);
            OnChanged();

            return;
        }

        if (Mode == DragMode.Move)
        {
            if (Grid.SnapEnabled && _primary is not null)
            {
                // Snap the element the user grabbed, then move the rest of the
                // selection by the same corrected delta. Snapping each element
                // independently would pull a carefully spaced row together.
                GumpRect anchor = _dragStart[_primary];
                GumpPoint snapped = Grid.Snap(new GumpPoint(anchor.X + dx, anchor.Y + dy));

                dx = snapped.X - anchor.X;
                dy = snapped.Y - anchor.Y;
            }

            foreach ((Element element, GumpRect start) in _dragStart)
            {
                element.Location = new GumpPoint(start.X + dx, start.Y + dy);
            }
        }
        else if (_primary is not null)
        {
            GumpRect resized = HandleGeometry.Resize(_primaryStart, Mode, dx, dy);

            if (Grid.SnapEnabled)
            {
                resized = Grid.Snap(resized);
            }

            _primary.Location = resized.Location;
            _primary.Size = resized.Size;
        }

        OnChanged();
    }

    /// <summary>Ends a gesture, pushing an undo entry when something changed.</summary>
    public void PointerReleased(GumpPoint at)
    {
        if (Mode == DragMode.None)
        {
            return;
        }

        if (Mode == DragMode.SelectionMarquee)
        {
            SelectWithin(GumpRect.FromCorners(_pressedAt, at));

            Marquee = null;
            Mode = DragMode.None;

            OnChanged();

            return;
        }

        CommitGesture();

        Mode = DragMode.None;
        _primary = null;
        _dragStart.Clear();

        OnChanged();
    }

    /// <summary>Abandons an in-progress gesture and restores the starting geometry.</summary>
    public void CancelGesture()
    {
        if (Mode is DragMode.None)
        {
            return;
        }

        foreach ((Element element, GumpRect start) in _dragStart)
        {
            element.Location = start.Location;

            if (element.IsResizable)
            {
                element.Size = start.Size;
            }
        }

        Marquee = null;
        Mode = DragMode.None;
        _primary = null;
        _dragStart.Clear();

        OnChanged();
    }

    /// <summary>
    /// Moves the selection by a keyboard step, as one undo entry per run.
    /// </summary>
    /// <remarks>
    /// With snapping on, a step is one grid cell rather than one pixel, so the
    /// keyboard and the mouse agree about where things can land.
    /// </remarks>
    public void Nudge(int dx, int dy)
    {
        if (_selection.Count == 0 || (dx == 0 && dy == 0))
        {
            return;
        }

        if (Grid.SnapEnabled)
        {
            dx *= Grid.Width;
            dy *= Grid.Height;
        }

        using UndoHistory.CompositeScope scope = history.BeginComposite("Nudge");

        foreach (Element element in _selection)
        {
            GumpPoint from = element.Location;

            scope.Run(new MoveElementCommand(element, from, from.Offset(dx, dy)));
        }

        OnChanged();
    }

    /// <summary>Deletes the selection.</summary>
    public void DeleteSelection()
    {
        if (_selection.Count == 0)
        {
            return;
        }

        using (UndoHistory.CompositeScope scope = history.BeginComposite("Delete"))
        {
            foreach (Element element in _selection.Where(e => e.Parent is not null))
            {
                scope.Run(new RemoveElementCommand(element));
            }
        }

        ClearSelection();
    }

    /// <summary>Selects every element on the page.</summary>
    public void SelectAll()
    {
        ClearSelection();

        if (_page is null)
        {
            return;
        }

        foreach (Element element in _page.Root.Children)
        {
            Add(element);
        }

        OnChanged();
    }

    /// <summary>Replaces the selection with one element, or clears it.</summary>
    public void Select(Element? element)
    {
        ClearSelection();

        if (element is not null)
        {
            Add(element);
        }

        OnChanged();
    }

    /// <summary>Empties the selection.</summary>
    public void ClearSelection()
    {
        foreach (Element element in _selection)
        {
            element.IsSelected = false;
        }

        _selection.Clear();
    }

    private void SelectWithin(GumpRect area)
    {
        if (_page is null)
        {
            return;
        }

        foreach (Element element in _page.Root.Children)
        {
            if (area.IntersectsWith(element.GetAbsoluteBounds()) && !_selection.Contains(element))
            {
                Add(element);
            }
        }
    }

    private void CommitGesture()
    {
        bool moved = _dragStart.Any(pair => pair.Key.Bounds != pair.Value);

        if (!moved)
        {
            return;
        }

        // The gesture already applied its effect live, so the command is pushed
        // with the final values and re-executing it is a no-op.
        using UndoHistory.CompositeScope scope = history.BeginComposite(
            Mode == DragMode.Move ? "Move" : "Resize");

        foreach ((Element element, GumpRect start) in _dragStart)
        {
            if (element.Bounds == start)
            {
                continue;
            }

            scope.Run(element.IsResizable && Mode != DragMode.Move
                ? new ResizeElementCommand(element, start, element.Bounds)
                : new MoveElementCommand(element, start.Location, element.Location));
        }
    }

    private void CaptureDragStart()
    {
        _dragStart.Clear();

        foreach (Element element in _selection)
        {
            _dragStart[element] = element.Bounds;
        }

        if (_primary is not null && !_dragStart.ContainsKey(_primary))
        {
            _dragStart[_primary] = _primary.Bounds;
        }
    }

    private IEnumerable<Element> Descending()
    {
        if (_page is null)
        {
            yield break;
        }

        IReadOnlyList<Element> children = _page.Root.Children;

        for (int i = children.Count - 1; i >= 0; i--)
        {
            yield return children[i];
        }
    }

    private void Add(Element element)
    {
        element.IsSelected = true;
        _selection.Add(element);
    }

    private void Toggle(Element element)
    {
        if (_selection.Remove(element))
        {
            element.IsSelected = false;
        }
        else
        {
            Add(element);
        }
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
