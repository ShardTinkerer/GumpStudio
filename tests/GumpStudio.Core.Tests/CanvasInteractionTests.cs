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
