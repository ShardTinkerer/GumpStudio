using GumpStudio.Core.Commands;
using GumpStudio.Core.Document;
using GumpStudio.Core.Editing;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Primitives;

using Xunit;

namespace GumpStudio.Core.Tests;

/// <summary>
/// Aligning and evenly spacing a multiple selection.
/// </summary>
/// <remarks>
/// The rules are the original's: everything moves to the anchor, which is the
/// element last pressed or right-clicked and which does not move itself; and
/// spacing evens out the <em>centres</em> rather than the gaps, so elements of
/// differing sizes read as evenly placed.
/// </remarks>
public class AlignmentTests
{
    private static (CanvasInteractionController Canvas, UndoHistory History, GumpPage Page) Build(
        params (int X, int Y, int W, int H)[] boxes)
    {
        UndoHistory history = new();
        CanvasInteractionController canvas = new(history);
        GumpPage page = new();

        foreach ((int x, int y, int w, int h) in boxes)
        {
            page.Root.Add(new BackgroundElement
            {
                Location = new GumpPoint(x, y),
                Size = new GumpSize(w, h),
            });
        }

        canvas.Page = page;

        foreach (Element element in page.Root.Children)
        {
            canvas.Toggle(element);
        }

        return (canvas, history, page);
    }

    private static Element At(GumpPage page, int index) => page.Root.Children[index];

    [Fact]
    public void AligningLeftsMovesEverythingToTheAnchor()
    {
        (CanvasInteractionController canvas, _, GumpPage page) = Build((10, 0, 20, 10), (50, 30, 40, 10));

        canvas.Anchor = At(page, 1);

        Assert.True(canvas.Align(AlignMode.Left));
        Assert.Equal(new GumpPoint(50, 0), At(page, 0).Location);

        // The anchor itself never moves.
        Assert.Equal(new GumpPoint(50, 30), At(page, 1).Location);
    }

    [Fact]
    public void AligningRightsUsesTheAnchorsFarEdge()
    {
        (CanvasInteractionController canvas, _, GumpPage page) = Build((0, 0, 20, 10), (50, 30, 40, 10));

        canvas.Anchor = At(page, 1);

        Assert.True(canvas.Align(AlignMode.Right));

        // The anchor spans 50 to 90, so a 20-wide element lands at 70.
        Assert.Equal(70, At(page, 0).X);
    }

    [Fact]
    public void AligningTopsAndBottomsWorksOnTheOtherAxis()
    {
        (CanvasInteractionController canvas, _, GumpPage page) = Build((0, 5, 20, 10), (0, 40, 20, 30));

        canvas.Anchor = At(page, 1);

        Assert.True(canvas.Align(AlignMode.Top));
        Assert.Equal(40, At(page, 0).Y);

        Assert.True(canvas.Align(AlignMode.Bottom));

        // The anchor spans 40 to 70, so a 10-tall element lands at 60.
        Assert.Equal(60, At(page, 0).Y);
    }

    [Fact]
    public void CentringMatchesTheAnchorsMidpoint()
    {
        (CanvasInteractionController canvas, _, GumpPage page) = Build((0, 0, 20, 10), (100, 200, 40, 60));

        canvas.Anchor = At(page, 1);

        Assert.True(canvas.Align(AlignMode.CenterHorizontally));
        Assert.True(canvas.Align(AlignMode.CenterVertically));

        // The anchor's centre is (120, 230); a 20x10 element centres there at
        // (110, 225).
        Assert.Equal(new GumpPoint(110, 225), At(page, 0).Location);
    }

    [Fact]
    public void TheAnchorFallsBackToTheFrontmostSelectionWhenUnset()
    {
        (CanvasInteractionController canvas, _, GumpPage page) = Build((10, 0, 20, 10), (50, 0, 20, 10));

        Assert.Null(canvas.Anchor);
        Assert.True(canvas.Align(AlignMode.Left));

        // The frontmost element is the last child, so that is what the rest line
        // up on.
        Assert.Equal(50, At(page, 0).X);
    }

    [Fact]
    public void AnAnchorThatIsNoLongerSelectedIsIgnored()
    {
        (CanvasInteractionController canvas, _, GumpPage page) = Build((10, 0, 20, 10), (50, 0, 20, 10));

        canvas.Anchor = At(page, 0);
        canvas.Toggle(At(page, 0));

        // One element left selected, so there is nothing to align it to.
        Assert.False(canvas.Align(AlignMode.Left));
        Assert.Equal(10, At(page, 0).X);
    }

    [Fact]
    public void AligningNeedsTwoElements()
    {
        (CanvasInteractionController canvas, UndoHistory history, _) = Build((10, 0, 20, 10));

        Assert.False(canvas.Align(AlignMode.Left));
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void AligningIsOneUndoEntry()
    {
        (CanvasInteractionController canvas, UndoHistory history, GumpPage page) =
            Build((10, 0, 20, 10), (30, 0, 20, 10), (70, 0, 20, 10));

        canvas.Anchor = At(page, 2);
        canvas.Align(AlignMode.Left);

        Assert.Equal(70, At(page, 0).X);
        Assert.Equal(70, At(page, 1).X);

        history.Undo();

        Assert.Equal(10, At(page, 0).X);
        Assert.Equal(30, At(page, 1).X);
    }

    [Fact]
    public void SpacingEvensOutTheCentresAndLeavesTheOutermostAlone()
    {
        // Centres start at 10, 25 and 110; evenly spread they become 10, 60, 110.
        (CanvasInteractionController canvas, _, GumpPage page) =
            Build((0, 0, 20, 10), (15, 0, 20, 10), (100, 0, 20, 10));

        Assert.True(canvas.Distribute(DistributeMode.Horizontally));

        Assert.Equal(0, At(page, 0).X);
        Assert.Equal(50, At(page, 1).X);
        Assert.Equal(100, At(page, 2).X);
    }

    /// <summary>
    /// Spacing by centre rather than by gap is what keeps differently sized
    /// elements looking evenly placed.
    /// </summary>
    [Fact]
    public void SpacingUsesCentresNotGaps()
    {
        (CanvasInteractionController canvas, _, GumpPage page) =
            Build((0, 0, 10, 10), (20, 0, 60, 10), (200, 0, 10, 10));

        Assert.True(canvas.Distribute(DistributeMode.Horizontally));

        // Centres 5 and 205 bracket the run, so the middle centre lands at 105
        // and a 60-wide element starts at 75.
        Assert.Equal(75, At(page, 1).X);
    }

    [Fact]
    public void SpacingWorksVerticallyToo()
    {
        (CanvasInteractionController canvas, _, GumpPage page) =
            Build((0, 0, 10, 10), (0, 10, 10, 10), (0, 100, 10, 10));

        Assert.True(canvas.Distribute(DistributeMode.Vertically));

        Assert.Equal(50, At(page, 1).Y);
    }

    /// <summary>
    /// Ordering is by position, not by the order the elements happen to sit in
    /// the page — otherwise spacing would shuffle them about.
    /// </summary>
    [Fact]
    public void SpacingOrdersByPositionNotByPageOrder()
    {
        (CanvasInteractionController canvas, _, GumpPage page) =
            Build((0, 0, 10, 10), (200, 0, 10, 10), (20, 0, 10, 10));

        Assert.True(canvas.Distribute(DistributeMode.Horizontally));

        // The one at 20 is the middle by position, so it takes the middle slot,
        // and the outermost two stay where they were.
        Assert.Equal(0, At(page, 0).X);
        Assert.Equal(200, At(page, 1).X);
        Assert.Equal(100, At(page, 2).X);
    }

    [Fact]
    public void SpacingNeedsThreeElements()
    {
        (CanvasInteractionController canvas, UndoHistory history, _) =
            Build((0, 0, 10, 10), (50, 0, 10, 10));

        Assert.False(canvas.Distribute(DistributeMode.Horizontally));
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void AlreadyAlignedElementsAreNotAnUndoEntry()
    {
        (CanvasInteractionController canvas, UndoHistory history, GumpPage page) =
            Build((10, 0, 20, 10), (10, 40, 20, 10));

        canvas.Anchor = At(page, 1);

        Assert.False(canvas.Align(AlignMode.Left));
        Assert.False(history.CanUndo);
    }
}
