using GumpStudio.Core.Commands;
using GumpStudio.Core.Document;
using GumpStudio.Core.Editing;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Geometry;
using GumpStudio.Core.Primitives;

using Xunit;

namespace GumpStudio.Core.Tests;

/// <summary>
/// Behaviour of the canvas gestures.
/// </summary>
/// <remarks>
/// Testable at all only because the state machine lives in Core. In the original
/// this was ~500 lines of nested pointer handlers inside the designer form.
/// </remarks>
public class CanvasInteractionTests
{
    private static (CanvasInteractionController Controller, UndoHistory History, GumpPage Page) Setup()
    {
        UndoHistory history = new();
        GumpPage page = new();
        CanvasInteractionController controller = new(history) { Page = page };

        return (controller, history, page);
    }

    private static AlphaElement AddBox(GumpPage page, int x, int y, int w = 20, int h = 20)
    {
        AlphaElement element = new()
        {
            Location = new GumpPoint(x, y),
            Size = new GumpSize(w, h),
        };

        page.Root.Add(element);

        return element;
    }

    [Fact]
    public void ClickingAnElementSelectsIt()
    {
        (CanvasInteractionController controller, _, GumpPage page) = Setup();

        AlphaElement box = AddBox(page, 10, 10);

        controller.PointerPressed(new GumpPoint(15, 15));
        controller.PointerReleased(new GumpPoint(15, 15));

        Assert.Same(box, Assert.Single(controller.Selection));
        Assert.True(box.IsSelected);
    }

    [Fact]
    public void ClickingEmptyCanvasClearsTheSelection()
    {
        (CanvasInteractionController controller, _, GumpPage page) = Setup();

        AlphaElement box = AddBox(page, 10, 10);

        controller.Select(box);

        controller.PointerPressed(new GumpPoint(200, 200));
        controller.PointerReleased(new GumpPoint(200, 200));

        Assert.Empty(controller.Selection);
        Assert.False(box.IsSelected);
    }

    [Fact]
    public void ExtendModifierTogglesMembership()
    {
        (CanvasInteractionController controller, _, GumpPage page) = Setup();

        AlphaElement first = AddBox(page, 0, 0);
        AlphaElement second = AddBox(page, 40, 0);

        controller.PointerPressed(new GumpPoint(5, 5));
        controller.PointerReleased(new GumpPoint(5, 5));

        controller.PointerPressed(new GumpPoint(45, 5), InputModifiers.Extend);
        controller.PointerReleased(new GumpPoint(45, 5));

        Assert.Equal(2, controller.Selection.Count);

        controller.PointerPressed(new GumpPoint(45, 5), InputModifiers.Extend);
        controller.PointerReleased(new GumpPoint(45, 5));

        Assert.Same(first, Assert.Single(controller.Selection));
        Assert.False(second.IsSelected);
    }

    [Fact]
    public void HitTestingPrefersTheTopmostElement()
    {
        (CanvasInteractionController controller, _, GumpPage page) = Setup();

        AddBox(page, 0, 0, 50, 50);
        AlphaElement front = AddBox(page, 0, 0, 50, 50);

        Assert.Same(front, controller.HitTest(new GumpPoint(25, 25)));
    }

    [Fact]
    public void DraggingMovesTheWholeSelectionAsOneUndoEntry()
    {
        (CanvasInteractionController controller, UndoHistory history, GumpPage page) = Setup();

        AlphaElement first = AddBox(page, 0, 0);
        AlphaElement second = AddBox(page, 40, 0);

        controller.SelectAll();

        controller.PointerPressed(new GumpPoint(5, 5));
        controller.PointerMoved(new GumpPoint(15, 25));
        controller.PointerReleased(new GumpPoint(15, 25));

        Assert.Equal(new GumpPoint(10, 20), first.Location);
        Assert.Equal(new GumpPoint(50, 20), second.Location);

        Assert.Equal(1, history.Count);

        history.Undo();

        Assert.Equal(GumpPoint.Origin, first.Location);
        Assert.Equal(new GumpPoint(40, 0), second.Location);
    }

    [Fact]
    public void AClickThatDoesNotMoveAnythingPushesNoUndoEntry()
    {
        (CanvasInteractionController controller, UndoHistory history, GumpPage page) = Setup();

        AddBox(page, 10, 10);

        controller.PointerPressed(new GumpPoint(15, 15));
        controller.PointerReleased(new GumpPoint(15, 15));

        Assert.Equal(0, history.Count);
    }

    [Fact]
    public void DraggingAHandleResizesRatherThanMoves()
    {
        (CanvasInteractionController controller, UndoHistory history, GumpPage page) = Setup();

        AlphaElement box = AddBox(page, 10, 10, 40, 40);

        controller.Select(box);

        GumpRect handle = HandleGeometry.GetHandleRect(box.GetAbsoluteBounds(), DragMode.ResizeBottomRight);

        controller.PointerPressed(handle.Center);

        Assert.True(HandleGeometry.IsResize(controller.Mode));

        controller.PointerMoved(handle.Center.Offset(10, 5));
        controller.PointerReleased(handle.Center.Offset(10, 5));

        Assert.Equal(new GumpPoint(10, 10), box.Location);
        Assert.Equal(new GumpSize(50, 45), box.Size);

        history.Undo();

        Assert.Equal(new GumpSize(40, 40), box.Size);
    }

    [Fact]
    public void AMarqueeSelectsEverythingItTouches()
    {
        (CanvasInteractionController controller, _, GumpPage page) = Setup();

        AlphaElement inside = AddBox(page, 10, 10);
        AlphaElement alsoInside = AddBox(page, 25, 25);
        AlphaElement outside = AddBox(page, 200, 200);

        controller.PointerPressed(new GumpPoint(5, 5));
        controller.PointerMoved(new GumpPoint(60, 60));

        Assert.NotNull(controller.Marquee);

        controller.PointerReleased(new GumpPoint(60, 60));

        Assert.Null(controller.Marquee);
        Assert.Equal(2, controller.Selection.Count);
        Assert.Contains(inside, controller.Selection);
        Assert.Contains(alsoInside, controller.Selection);
        Assert.DoesNotContain(outside, controller.Selection);
    }

    [Fact]
    public void NudgingMovesTheSelectionAndUndoesInOneStep()
    {
        (CanvasInteractionController controller, UndoHistory history, GumpPage page) = Setup();

        AlphaElement first = AddBox(page, 10, 10);
        AlphaElement second = AddBox(page, 50, 10);

        controller.SelectAll();
        controller.Nudge(1, 0);

        Assert.Equal(new GumpPoint(11, 10), first.Location);
        Assert.Equal(new GumpPoint(51, 10), second.Location);
        Assert.Equal(1, history.Count);

        history.Undo();

        Assert.Equal(new GumpPoint(10, 10), first.Location);
    }

    [Fact]
    public void DeletingRemovesTheSelectionAndCanBeUndone()
    {
        (CanvasInteractionController controller, UndoHistory history, GumpPage page) = Setup();

        AddBox(page, 10, 10);
        AddBox(page, 50, 10);

        controller.SelectAll();
        controller.DeleteSelection();

        Assert.Empty(page.Root.Children);
        Assert.Empty(controller.Selection);

        history.Undo();

        Assert.Equal(2, page.Root.Children.Count);
    }

    [Fact]
    public void CancellingAGestureRestoresTheStartingGeometry()
    {
        (CanvasInteractionController controller, UndoHistory history, GumpPage page) = Setup();

        AlphaElement box = AddBox(page, 10, 10);

        controller.Select(box);

        controller.PointerPressed(new GumpPoint(15, 15));
        controller.PointerMoved(new GumpPoint(80, 90));

        Assert.NotEqual(new GumpPoint(10, 10), box.Location);

        controller.CancelGesture();

        Assert.Equal(new GumpPoint(10, 10), box.Location);
        Assert.Equal(0, history.Count);
        Assert.False(controller.IsDragging);
    }

    [Fact]
    public void SwitchingPagesClearsTheSelection()
    {
        (CanvasInteractionController controller, _, GumpPage page) = Setup();

        AlphaElement box = AddBox(page, 10, 10);

        controller.Select(box);

        controller.Page = new GumpPage();

        Assert.Empty(controller.Selection);
        Assert.False(box.IsSelected);
    }

    [Fact]
    public void GesturesOnAnEmptyControllerAreHarmless()
    {
        CanvasInteractionController controller = new(new UndoHistory());

        controller.PointerPressed(GumpPoint.Origin);
        controller.PointerMoved(new GumpPoint(10, 10));
        controller.PointerReleased(new GumpPoint(10, 10));
        controller.Nudge(1, 1);
        controller.DeleteSelection();
        controller.SelectAll();

        Assert.Empty(controller.Selection);
    }
}

/// <summary>
/// The design grid, which was the SnapToGrid plugin in 1.8 and is a built-in
/// feature here.
/// </summary>
public class GridSettingsTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 5)]
    [InlineData(7, 5)]
    [InlineData(8, 10)]
    [InlineData(12, 10)]
    [InlineData(13, 15)]
    public void SnapsToTheNearestGridLine(int input, int expected)
    {
        GridSettings grid = new() { Width = 5, Height = 5 };

        Assert.Equal(expected, grid.SnapX(input));
        Assert.Equal(expected, grid.SnapY(input));
    }

    /// <summary>
    /// Integer division truncates toward zero, which would round negatives the
    /// wrong way. Elements can sit at negative coordinates inside a group.
    /// </summary>
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(-2, 0)]
    [InlineData(-3, -5)]
    [InlineData(-7, -5)]
    [InlineData(-8, -10)]
    public void SnapsNegativeCoordinatesCorrectly(int input, int expected)
    {
        GridSettings grid = new() { Width = 5, Height = 5 };

        Assert.Equal(expected, grid.SnapX(input));
    }

    [Fact]
    public void SpacingIsAlwaysAtLeastOne()
    {
        GridSettings grid = new() { Width = 0, Height = -4 };

        Assert.Equal(1, grid.Width);
        Assert.Equal(1, grid.Height);
    }

    [Fact]
    public void SnappingARectangleKeepsItAtLeastOneCell()
    {
        GridSettings grid = new() { Width = 10, Height = 10 };

        GumpRect snapped = grid.Snap(new GumpRect(2, 2, 1, 1));

        Assert.Equal(10, snapped.Width);
        Assert.Equal(10, snapped.Height);
    }
}

public class SnapToGridInteractionTests
{
    private static (CanvasInteractionController Controller, UndoHistory History, GumpPage Page) Setup()
    {
        UndoHistory history = new();
        GumpPage page = new();
        CanvasInteractionController controller = new(history) { Page = page };

        controller.Grid.Width = 10;
        controller.Grid.Height = 10;
        controller.Grid.SnapEnabled = true;

        return (controller, history, page);
    }

    private static AlphaElement AddBox(GumpPage page, int x, int y, int w = 20, int h = 20)
    {
        AlphaElement element = new() { Location = new GumpPoint(x, y), Size = new GumpSize(w, h) };

        page.Root.Add(element);

        return element;
    }

    [Fact]
    public void DraggingLandsOnTheGrid()
    {
        (CanvasInteractionController controller, _, GumpPage page) = Setup();

        AlphaElement box = AddBox(page, 0, 0);

        controller.Select(box);

        controller.PointerPressed(new GumpPoint(5, 5));
        controller.PointerMoved(new GumpPoint(28, 33));
        controller.PointerReleased(new GumpPoint(28, 33));

        // Moved by (23, 28) from origin, which snaps to (20, 30).
        Assert.Equal(new GumpPoint(20, 30), box.Location);
    }

    /// <summary>
    /// Snapping each element on its own would collapse a deliberately spaced
    /// row; only the grabbed element snaps and the rest follow by the same delta.
    /// </summary>
    [Fact]
    public void AMultiSelectionKeepsItsRelativeLayout()
    {
        (CanvasInteractionController controller, _, GumpPage page) = Setup();

        AlphaElement grabbed = AddBox(page, 0, 0);
        AlphaElement offGrid = AddBox(page, 33, 7);

        controller.SelectAll();

        controller.PointerPressed(new GumpPoint(5, 5));
        controller.PointerMoved(new GumpPoint(28, 33));
        controller.PointerReleased(new GumpPoint(28, 33));

        Assert.Equal(new GumpPoint(20, 30), grabbed.Location);

        // The second element kept its 33,7 offset relative to the first.
        Assert.Equal(new GumpPoint(53, 37), offGrid.Location);
    }

    [Fact]
    public void ResizingLandsOnTheGrid()
    {
        (CanvasInteractionController controller, _, GumpPage page) = Setup();

        AlphaElement box = AddBox(page, 0, 0, 20, 20);

        controller.Select(box);

        GumpRect handle = HandleGeometry.GetHandleRect(box.GetAbsoluteBounds(), DragMode.ResizeBottomRight);

        controller.PointerPressed(handle.Center);
        controller.PointerMoved(handle.Center.Offset(13, 6));
        controller.PointerReleased(handle.Center.Offset(13, 6));

        Assert.Equal(new GumpSize(30, 30), box.Size);
    }

    [Fact]
    public void NudgingStepsOneCellWhenSnapping()
    {
        (CanvasInteractionController controller, _, GumpPage page) = Setup();

        AlphaElement box = AddBox(page, 0, 0);

        controller.Select(box);
        controller.Nudge(1, 0);

        Assert.Equal(new GumpPoint(10, 0), box.Location);
    }

    [Fact]
    public void NudgingStepsOnePixelWhenNotSnapping()
    {
        (CanvasInteractionController controller, _, GumpPage page) = Setup();

        controller.Grid.SnapEnabled = false;

        AlphaElement box = AddBox(page, 0, 0);

        controller.Select(box);
        controller.Nudge(1, 0);

        Assert.Equal(new GumpPoint(1, 0), box.Location);
    }

    [Fact]
    public void SnappingOffLeavesPositionsExact()
    {
        (CanvasInteractionController controller, _, GumpPage page) = Setup();

        controller.Grid.SnapEnabled = false;

        AlphaElement box = AddBox(page, 0, 0);

        controller.Select(box);

        controller.PointerPressed(new GumpPoint(5, 5));
        controller.PointerMoved(new GumpPoint(28, 33));
        controller.PointerReleased(new GumpPoint(28, 33));

        Assert.Equal(new GumpPoint(23, 28), box.Location);
    }
}
