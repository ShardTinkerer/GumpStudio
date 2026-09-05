using GumpStudio.Core.Commands;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Geometry;
using GumpStudio.Core.Primitives;

using Xunit;

namespace GumpStudio.Core.Tests;

public class UndoHistoryTests
{
    [Fact]
    public void UndoAndRedoRestoreState()
    {
        GroupElement page = new();
        LabelElement label = new();
        UndoHistory history = new();

        history.Push(new AddElementCommand(page, label));

        Assert.Single(page.Children);

        Assert.True(history.Undo());
        Assert.Empty(page.Children);

        Assert.True(history.Redo());
        Assert.Single(page.Children);
    }

    /// <summary>
    /// The original's Redo guard was off by one and its Undo had no lower bound
    /// at all; both relied on menu state to avoid indexing out of range.
    /// </summary>
    [Fact]
    public void UndoAndRedoAreSafeAtTheEndsOfHistory()
    {
        UndoHistory history = new();

        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.False(history.Undo());
        Assert.False(history.Redo());

        history.Push(new AddElementCommand(new GroupElement(), new LabelElement()));

        Assert.True(history.Undo());

        // Calling repeatedly past either end must be a no-op, never a throw.
        for (int i = 0; i < 5; i++)
        {
            history.Undo();
            history.Redo();
            history.Redo();
        }

        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void PushingAfterUndoDiscardsTheRedoBranch()
    {
        GroupElement page = new();
        UndoHistory history = new();

        history.Push(new AddElementCommand(page, new LabelElement()));
        history.Push(new AddElementCommand(page, new AlphaElement()));
        history.Undo();

        Assert.True(history.CanRedo);

        history.Push(new AddElementCommand(page, new ItemElement()));

        Assert.False(history.CanRedo);
        Assert.Equal(2, page.Children.Count);
    }

    [Fact]
    public void ConsecutiveMovesOfOneElementMergeIntoASingleEntry()
    {
        GroupElement page = new();
        LabelElement label = new() { Location = new GumpPoint(0, 0) };

        page.Add(label);

        UndoHistory history = new();

        // A drag arrives as many small moves; they must undo as one.
        history.Push(new MoveElementCommand(label, new GumpPoint(0, 0), new GumpPoint(5, 0)));
        history.Push(new MoveElementCommand(label, new GumpPoint(5, 0), new GumpPoint(10, 0)));
        history.Push(new MoveElementCommand(label, new GumpPoint(10, 0), new GumpPoint(15, 0)));

        Assert.Equal(1, history.Count);
        Assert.Equal(new GumpPoint(15, 0), label.Location);

        history.Undo();

        Assert.Equal(new GumpPoint(0, 0), label.Location);
    }

    [Fact]
    public void MovesOfDifferentElementsDoNotMerge()
    {
        GroupElement page = new();
        LabelElement first = new();
        LabelElement second = new();

        page.Add(first);
        page.Add(second);

        UndoHistory history = new();

        history.Push(new MoveElementCommand(first, default, new GumpPoint(5, 0)));
        history.Push(new MoveElementCommand(second, default, new GumpPoint(9, 0)));

        Assert.Equal(2, history.Count);
    }

    [Fact]
    public void CapacityDropsTheOldestEntries()
    {
        GroupElement page = new();
        UndoHistory history = new(capacity: 3);

        for (int i = 0; i < 10; i++)
        {
            history.Push(new AddElementCommand(page, new LabelElement()));
        }

        Assert.Equal(3, history.Count);
        Assert.Equal(10, page.Children.Count);

        // Only the retained entries can be undone.
        while (history.Undo())
        {
        }

        Assert.Equal(7, page.Children.Count);
    }

    [Fact]
    public void ACompositeScopeUndoesAsOneStep()
    {
        GroupElement page = new();
        LabelElement a = new();
        LabelElement b = new();

        page.Add(a);
        page.Add(b);

        UndoHistory history = new();

        using (UndoHistory.CompositeScope scope = history.BeginComposite("Align"))
        {
            scope.Run(new MoveElementCommand(a, default, new GumpPoint(0, 10)));
            scope.Run(new MoveElementCommand(b, default, new GumpPoint(0, 10)));
        }

        Assert.Equal(1, history.Count);

        history.Undo();

        Assert.Equal(default, a.Location);
        Assert.Equal(default, b.Location);
    }

    [Fact]
    public void GroupingPreservesAbsolutePositions()
    {
        GroupElement page = new();
        LabelElement a = new() { Location = new GumpPoint(30, 40) };
        AlphaElement b = new() { Location = new GumpPoint(50, 70) };

        page.Add(a);
        page.Add(b);

        UndoHistory history = new();

        history.Push(new GroupElementsCommand([a, b]));

        Assert.Equal(new GumpPoint(30, 40), a.GetAbsolutePosition());
        Assert.Equal(new GumpPoint(50, 70), b.GetAbsolutePosition());
        Assert.IsType<GroupElement>(a.Parent);

        history.Undo();

        Assert.Equal(new GumpPoint(30, 40), a.Location);
        Assert.Same(page, a.Parent);
    }
}

public class HandleGeometryTests
{
    private static readonly GumpRect Box = new(10, 20, 100, 50);

    [Fact]
    public void EveryHandleIsHitAtItsOwnRectangle()
    {
        foreach (DragMode handle in HandleGeometry.ResizeHandles)
        {
            GumpRect rect = HandleGeometry.GetHandleRect(Box, handle);

            // The rendered handle and the hit-tested handle must be the same
            // rectangle; the original computed them from separate literals.
            Assert.Equal(handle, HandleGeometry.HitTest(Box, rect.Center, resizable: true));
        }
    }

    [Fact]
    public void TheInteriorIsAMove()
    {
        Assert.Equal(DragMode.Move, HandleGeometry.HitTest(Box, Box.Center, resizable: true));
        Assert.Equal(DragMode.Move, HandleGeometry.HitTest(Box, Box.Center, resizable: false));
    }

    [Fact]
    public void ANonResizableElementNeverReportsAResizeHandle()
    {
        foreach (DragMode handle in HandleGeometry.ResizeHandles)
        {
            GumpPoint point = HandleGeometry.GetHandleRect(Box, handle).Center;
            DragMode hit = HandleGeometry.HitTest(Box, point, resizable: false);

            Assert.False(HandleGeometry.IsResize(hit));
        }
    }

    [Fact]
    public void PointsWellOutsideMissEntirely()
    {
        Assert.Equal(DragMode.None, HandleGeometry.HitTest(Box, new GumpPoint(-100, -100), true));
        Assert.Equal(DragMode.None, HandleGeometry.HitTest(Box, new GumpPoint(500, 500), true));
    }

    [Theory]
    [InlineData(DragMode.ResizeRight, 20, 0, 10, 20, 120, 50)]
    [InlineData(DragMode.ResizeBottom, 0, 10, 10, 20, 100, 60)]
    [InlineData(DragMode.ResizeLeft, 20, 0, 30, 20, 80, 50)]
    [InlineData(DragMode.ResizeTop, 0, 10, 10, 30, 100, 40)]
    [InlineData(DragMode.ResizeTopLeft, 5, 5, 15, 25, 95, 45)]
    [InlineData(DragMode.ResizeBottomRight, 5, 5, 10, 20, 105, 55)]
    public void ResizeMovesOnlyTheDraggedEdges(
        DragMode handle, int dx, int dy, int x, int y, int width, int height)
    {
        GumpRect result = HandleGeometry.Resize(Box, handle, dx, dy);

        Assert.Equal(new GumpRect(x, y, width, height), result);
    }

    [Fact]
    public void ResizeClampsInsteadOfInvertingTheRectangle()
    {
        // Dragging the left edge far past the right one must stop at the minimum,
        // not produce a negative width.
        GumpRect result = HandleGeometry.Resize(Box, DragMode.ResizeLeft, 5000, 0, minimum: 4);

        Assert.Equal(4, result.Width);
        Assert.Equal(Box.Right, result.Right);

        result = HandleGeometry.Resize(Box, DragMode.ResizeTop, 0, 5000, minimum: 4);

        Assert.Equal(4, result.Height);
        Assert.Equal(Box.Bottom, result.Bottom);
    }
}
