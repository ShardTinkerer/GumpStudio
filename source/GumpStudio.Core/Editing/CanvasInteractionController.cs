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
            ToggleCore(hit);
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

    /// <summary>
    /// Wraps the selection in a new group.
    /// </summary>
    /// <returns>The group, or null when there was nothing to group.</returns>
    public GroupElement? Group()
    {
        if (_selection.Count < 2)
        {
            return null;
        }

        GroupElementsCommand command = new([.. _selection]);

        history.Push(command);
        Select(command.Group);

        return command.Group;
    }

    /// <summary>
    /// Dissolves every group in the selection, one undo entry for the lot.
    /// </summary>
    /// <returns>How many groups were dissolved.</returns>
    /// <remarks>
    /// The counterpart to <see cref="Group"/>, which the original never had: once
    /// elements were grouped the only way back was to delete the group and place
    /// its contents again.
    /// </remarks>
    public int Ungroup()
    {
        List<GroupElement> groups = [.. _selection.OfType<GroupElement>().Where(g => !g.IsPageRoot)];

        if (groups.Count == 0)
        {
            return 0;
        }

        List<Element> freed = [];

        using (UndoHistory.CompositeScope scope = history.BeginComposite("Ungroup"))
        {
            foreach (GroupElement group in groups)
            {
                freed.AddRange(group.Children);

                scope.Run(new UngroupElementsCommand(group));
            }
        }

        // Selecting what came out keeps the user's attention on the same pixels,
        // and lets them immediately group a different subset.
        ClearSelection();

        foreach (Element element in freed)
        {
            Add(element);
        }

        OnChanged();

        return groups.Count;
    }

    /// <summary>Moves the selection to the front of its parent's drawing order.</summary>
    public bool BringToFront() => Reorder(ZOrder.Front);

    /// <summary>Moves the selection to the back of its parent's drawing order.</summary>
    public bool SendToBack() => Reorder(ZOrder.Back);

    /// <summary>Moves the selection one step towards the front.</summary>
    public bool BringForward() => Reorder(ZOrder.Forward);

    /// <summary>Moves the selection one step towards the back.</summary>
    public bool SendBackward() => Reorder(ZOrder.Backward);

    private enum ZOrder
    {
        Front,
        Back,
        Forward,
        Backward,
    }

    /// <summary>
    /// Applies a drawing-order change to the whole selection.
    /// </summary>
    /// <returns>True when anything actually moved.</returns>
    /// <remarks>
    /// <para>
    /// Drawing order is the whole of layering in a gump — the last child of a
    /// group draws in front — and the original had no way to change it. A
    /// background dropped in after an image covered it permanently, and the only
    /// recovery was to delete everything and place it again in the right order.
    /// </para>
    /// <para>
    /// A multi-element move keeps the selection's own relative order, and steps
    /// from the destination end so the elements do not shuffle past each other on
    /// the way. An element already against the end it is being moved towards
    /// stays put rather than pushing its neighbours around.
    /// </para>
    /// </remarks>
    private bool Reorder(ZOrder direction)
    {
        if (_selection.Count == 0)
        {
            return false;
        }

        bool moved = false;

        using UndoHistory.CompositeScope scope = history.BeginComposite("Reorder");

        // Grouped by parent, because "the front" means the end of the list the
        // element actually lives in.
        foreach (IGrouping<GroupElement, Element> family in _selection
            .Where(e => e.Parent is not null)
            .GroupBy(e => e.Parent!))
        {
            GroupElement parent = family.Key;
            List<Element> ordered = [.. family.OrderBy(parent.IndexOf)];

            // Towards the front, the topmost element moves first; towards the
            // back, the bottommost does.
            if (direction is ZOrder.Front or ZOrder.Forward)
            {
                ordered.Reverse();
            }

            int edge = direction switch
            {
                ZOrder.Front => parent.Children.Count - 1,
                ZOrder.Back => 0,
                _ => -1,
            };

            foreach (Element element in ordered)
            {
                int from = parent.IndexOf(element);
                int to = direction switch
                {
                    ZOrder.Front => edge--,
                    ZOrder.Back => edge++,
                    ZOrder.Forward => from + 1,
                    _ => from - 1,
                };

                if (to == from || to < 0 || to >= parent.Children.Count)
                {
                    continue;
                }

                scope.Run(new ReorderElementCommand(element, to));

                moved = true;
            }
        }

        if (moved)
        {
            OnChanged();
        }

        return moved;
    }

    /// <summary>
    /// Moves the selection onto another page.
    /// </summary>
    /// <returns>How many elements moved.</returns>
    /// <remarks>
    /// Each element keeps where it sits on screen, so one nested in a group is
    /// rebased on the way out. The selection is cleared afterwards because the
    /// elements are no longer on the page this controller is editing.
    /// </remarks>
    public int MoveSelectionToPage(GumpPage target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (_page is null || ReferenceEquals(_page, target) || _selection.Count == 0)
        {
            return 0;
        }

        // Copied, because the loop reparents each element and the selection is
        // cleared at the end.
        List<Element> moving = [.. _selection.Where(e => e.Parent is not null)];

        if (moving.Count == 0)
        {
            return 0;
        }

        using (UndoHistory.CompositeScope scope = history.BeginComposite("Move to page"))
        {
            foreach (Element element in moving)
            {
                scope.Run(new MoveToPageCommand(element, target));
            }
        }

        ClearSelection();
        OnChanged();

        return moving.Count;
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

    /// <summary>Adds an element to the selection, or removes it if already there.</summary>
    /// <remarks>What a ctrl-click does, exposed so the element list can do it too.</remarks>
    public void Toggle(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);

        ToggleCore(element);
        OnChanged();
    }

    private void ToggleCore(Element element)
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
