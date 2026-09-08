using GumpStudio.Core.Commands;
using GumpStudio.Core.Editing;
using GumpStudio.Core.Geometry;
using GumpStudio.Core.Primitives;

using Xunit;

namespace GumpStudio.Core.Tests;

/// <summary>
/// Resize handles at a size the caller chooses.
/// </summary>
/// <remarks>
/// The canvas can be zoomed, and a handle should be the same size under the
/// pointer whatever the zoom. That means growing it in gump units as the view
/// shrinks — and the drawing and the hit testing must agree on the number, or a
/// handle would not be where it looks.
/// </remarks>
public class HandleScalingTests
{
    private static readonly GumpRect Bounds = new(100, 50, 40, 20);

    [Fact]
    public void TheDefaultSizeIsUnchanged()
    {
        // The overloads must agree with the originals, so nothing that does not
        // zoom sees a difference.
        foreach (DragMode handle in HandleGeometry.ResizeHandles)
        {
            Assert.Equal(
                HandleGeometry.GetHandleRect(Bounds, handle),
                HandleGeometry.GetHandleRect(Bounds, handle, HandleGeometry.HandleSize));
        }
    }

    [Theory]
    [InlineData(5)]
    [InlineData(11)]
    [InlineData(21)]
    public void AHandleStaysCentredOnItsCornerAtEverySize(int size)
    {
        GumpRect topLeft = HandleGeometry.GetHandleRect(Bounds, DragMode.ResizeTopLeft, size);

        Assert.Equal(size, topLeft.Width);
        Assert.Equal(size, topLeft.Height);

        // Centred on the corner: an odd side halves to equal overhang.
        Assert.Equal(Bounds.Left, topLeft.X + (size / 2));
        Assert.Equal(Bounds.Top, topLeft.Y + (size / 2));
    }

    [Fact]
    public void ALargerHandleIsEasierToHit()
    {
        // Four gump units out from the corner: outside a five-unit handle,
        // inside a twenty-one unit one.
        GumpPoint near = new(Bounds.Left - 4, Bounds.Top - 4);

        Assert.NotEqual(
            DragMode.ResizeTopLeft,
            HandleGeometry.HitTest(Bounds, near, resizable: true, 5));

        Assert.Equal(
            DragMode.ResizeTopLeft,
            HandleGeometry.HitTest(Bounds, near, resizable: true, 21));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(9)]
    [InlineData(21)]
    public void HitTestingAgreesWithWhereTheHandleIsDrawn(int size)
    {
        // A large element, so handles of every size stay clear of each other.
        // On a small one they genuinely overlap and the earliest in
        // ResizeHandles wins, which is the documented ordering rather than a
        // disagreement between drawing and hit testing.
        GumpRect large = new(100, 50, 200, 120);

        foreach (DragMode handle in HandleGeometry.ResizeHandles)
        {
            GumpRect box = HandleGeometry.GetHandleRect(large, handle, size);
            GumpPoint centre = new(box.X + (size / 2), box.Y + (size / 2));

            Assert.Equal(handle, HandleGeometry.HitTest(large, centre, resizable: true, size));
        }
    }

    /// <summary>
    /// Handles bigger than the element they belong to overlap. That is inherent,
    /// and the press still has to land on a resize handle rather than on the
    /// body.
    /// </summary>
    [Fact]
    public void OversizedHandlesOnASmallElementStillResolveToAResize()
    {
        foreach (DragMode handle in HandleGeometry.ResizeHandles)
        {
            GumpRect box = HandleGeometry.GetHandleRect(Bounds, handle, 21);
            GumpPoint centre = new(box.X + 10, box.Y + 10);

            Assert.True(
                HandleGeometry.IsResize(
                    HandleGeometry.HitTest(Bounds, centre, resizable: true, 21)),
                handle.ToString());
        }
    }

    [Fact]
    public void TheBodyGrabMarginGrowsWithTheHandles()
    {
        // Eight units clear of the element: beyond the default three-unit
        // margin, within the margin implied by large handles.
        GumpPoint outside = new(Bounds.Left - 8, Bounds.Top + 10);

        Assert.Equal(
            DragMode.None,
            HandleGeometry.HitTest(Bounds, outside, resizable: false, 5));

        Assert.Equal(
            DragMode.Move,
            HandleGeometry.HitTest(Bounds, outside, resizable: false, 21));
    }

    [Fact]
    public void AHandleSizeMustBePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => HandleGeometry.GetHandleRect(Bounds, DragMode.ResizeTopLeft, 0));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => HandleGeometry.GetHandleRect(Bounds, DragMode.ResizeTopLeft, -3));
    }

    [Fact]
    public void TheControllerHitTestsWithItsOwnHandleSize()
    {
        UndoHistory history = new();

        Core.Document.GumpDocument document = new();

        // A resizable element: only those grow handles at all.
        Core.Elements.AlphaElement element = new()
        {
            Location = new GumpPoint(100, 50),
            Size = new GumpSize(40, 20),
        };

        document.Pages[0].Root.Add(element);

        CanvasInteractionController controller = new(history)
        {
            Page = document.Pages[0],
        };

        Assert.Equal(HandleGeometry.HandleSize, controller.HandleSize);

        controller.Select(element);

        // Well outside the element, but within the reach of a zoomed-out
        // handle: the press only lands on it once the controller is told the
        // handles have grown.
        GumpPoint near = new(96, 46);

        controller.HandleSize = 21;
        controller.PointerPressed(near, InputModifiers.None);

        Assert.True(HandleGeometry.IsResize(controller.Mode));

        controller.CancelGesture();
    }
}
