using GumpStudio.Core.Commands;
using GumpStudio.Core.Document;
using GumpStudio.Core.Editing;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Primitives;

using Xunit;

namespace GumpStudio.Core.Tests;

/// <summary>
/// Grouping, ungrouping and drawing order.
/// </summary>
/// <remarks>
/// Drawing order is the whole of layering in a gump — the last child of a group
/// draws in front — and the original offered no way to change it, nor any way to
/// undo a grouping short of deleting the group and placing its contents again.
/// </remarks>
public class ZOrderTests
{
    private static (CanvasInteractionController Canvas, UndoHistory History, GumpPage Page) Build(
        params string[] names)
    {
        UndoHistory history = new();
        CanvasInteractionController canvas = new(history);
        GumpPage page = new();

        foreach (string name in names)
        {
            page.Root.Add(new ImageElement { Name = name });
        }

        canvas.Page = page;

        return (canvas, history, page);
    }

    private static string Order(GumpPage page) =>
        string.Join(' ', page.Root.Children.Select(c => c.Name));

    private static Element Find(GumpPage page, string name) =>
        page.Root.Children.Single(c => c.Name == name);

    [Fact]
    public void BringToFrontMovesTheSelectionToTheEnd()
    {
        (CanvasInteractionController canvas, _, GumpPage page) = Build("a", "b", "c", "d");

        canvas.Select(Find(page, "b"));

        Assert.True(canvas.BringToFront());
        Assert.Equal("a c d b", Order(page));
    }

    [Fact]
    public void SendToBackMovesTheSelectionToTheStart()
    {
        (CanvasInteractionController canvas, _, GumpPage page) = Build("a", "b", "c", "d");

        canvas.Select(Find(page, "c"));

        Assert.True(canvas.SendToBack());
        Assert.Equal("c a b d", Order(page));
    }

    [Fact]
    public void SteppingForwardMovesOnePlace()
    {
        (CanvasInteractionController canvas, _, GumpPage page) = Build("a", "b", "c", "d");

        canvas.Select(Find(page, "a"));

        Assert.True(canvas.BringForward());
        Assert.Equal("b a c d", Order(page));

        Assert.True(canvas.BringForward());
        Assert.Equal("b c a d", Order(page));
    }

    [Fact]
    public void SteppingBackwardMovesOnePlace()
    {
        (CanvasInteractionController canvas, _, GumpPage page) = Build("a", "b", "c", "d");

        canvas.Select(Find(page, "d"));

        Assert.True(canvas.SendBackward());
        Assert.Equal("a b d c", Order(page));
    }

    [Fact]
    public void AnElementAlreadyAtTheEndDoesNotMove()
    {
        (CanvasInteractionController canvas, _, GumpPage page) = Build("a", "b");

        canvas.Select(Find(page, "b"));

        Assert.False(canvas.BringForward());
        Assert.False(canvas.BringToFront());
        Assert.Equal("a b", Order(page));

        canvas.Select(Find(page, "a"));

        Assert.False(canvas.SendBackward());
        Assert.False(canvas.SendToBack());
        Assert.Equal("a b", Order(page));
    }

    /// <summary>
    /// A multi-element move must not let the members shuffle past each other, or
    /// two elements sent forward together come back swapped.
    /// </summary>
    [Fact]
    public void AMultipleSelectionKeepsItsOwnRelativeOrder()
    {
        (CanvasInteractionController canvas, _, GumpPage page) = Build("a", "b", "c", "d", "e");

        // Two non-adjacent elements, b before d.
        canvas.Select(Find(page, "b"));
        canvas.Toggle(Find(page, "d"));

        Assert.True(canvas.BringToFront());
        Assert.Equal("a c e b d", Order(page));
    }

    [Fact]
    public void AMultipleSelectionSentToTheBackKeepsItsOrderToo()
    {
        (CanvasInteractionController canvas, _, GumpPage page) = Build("a", "b", "c", "d", "e");

        canvas.Select(Find(page, "b"));
        canvas.Toggle(Find(page, "d"));

        Assert.True(canvas.SendToBack());
        Assert.Equal("b d a c e", Order(page));
    }

    [Fact]
    public void SteppingAGroupOfElementsForwardMovesThemAsABlock()
    {
        (CanvasInteractionController canvas, _, GumpPage page) = Build("a", "b", "c", "d");

        canvas.Select(Find(page, "a"));
        canvas.Toggle(Find(page, "b"));

        Assert.True(canvas.BringForward());
        Assert.Equal("c a b d", Order(page));
    }

    [Fact]
    public void ReorderingIsOneUndoEntryForTheWholeSelection()
    {
        (CanvasInteractionController canvas, UndoHistory history, GumpPage page) =
            Build("a", "b", "c", "d");

        canvas.Select(Find(page, "a"));
        canvas.Toggle(Find(page, "b"));
        canvas.BringToFront();

        Assert.Equal("c d a b", Order(page));

        history.Undo();

        Assert.Equal("a b c d", Order(page));

        history.Redo();

        Assert.Equal("c d a b", Order(page));
    }

    [Fact]
    public void ReorderingNothingIsNotAnUndoEntry()
    {
        (CanvasInteractionController canvas, UndoHistory history, _) = Build("a", "b");

        Assert.False(canvas.BringToFront());
        Assert.False(history.CanUndo);
    }

    // -- grouping -----------------------------------------------------------

    [Fact]
    public void GroupingNeedsAtLeastTwoElements()
    {
        (CanvasInteractionController canvas, UndoHistory history, GumpPage page) = Build("a", "b");

        canvas.Select(Find(page, "a"));

        Assert.Null(canvas.Group());
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void GroupingSelectsTheNewGroup()
    {
        (CanvasInteractionController canvas, _, GumpPage page) = Build("a", "b");

        canvas.Select(page.Root.Children[0]);
        canvas.Toggle(page.Root.Children[1]);

        GroupElement group = Assert.IsType<GroupElement>(canvas.Group());

        Assert.Same(group, Assert.Single(canvas.Selection));
        Assert.Equal(2, group.Children.Count);
    }

    [Fact]
    public void UngroupingReturnsTheChildrenToTheParent()
    {
        UndoHistory history = new();
        CanvasInteractionController canvas = new(history);
        GumpPage page = new();

        GroupElement group = new() { Name = "g", Location = new GumpPoint(100, 200) };

        group.Add(new ImageElement { Name = "a", Location = new GumpPoint(5, 6) });
        group.Add(new ImageElement { Name = "b", Location = new GumpPoint(7, 8) });

        page.Root.Add(new ImageElement { Name = "behind" });
        page.Root.Add(group);
        page.Root.Add(new ImageElement { Name = "front" });

        canvas.Page = page;
        canvas.Select(group);

        Assert.Equal(1, canvas.Ungroup());

        // Children land where the group sat, keeping their depth relative to
        // everything around it.
        Assert.Equal("behind a b front", Order(page));

        // And they keep their place on screen.
        Assert.Equal(new GumpPoint(105, 206), Find(page, "a").Location);
        Assert.Equal(new GumpPoint(107, 208), Find(page, "b").Location);
    }

    [Fact]
    public void UngroupingSelectsWhatCameOut()
    {
        UndoHistory history = new();
        CanvasInteractionController canvas = new(history);
        GumpPage page = new();

        GroupElement group = new();

        group.Add(new ImageElement { Name = "a" });
        group.Add(new ImageElement { Name = "b" });
        page.Root.Add(group);

        canvas.Page = page;
        canvas.Select(group);
        canvas.Ungroup();

        Assert.Equal(["a", "b"], canvas.Selection.Select(e => e.Name));
    }

    [Fact]
    public void UngroupingUndoesBackToTheGroup()
    {
        UndoHistory history = new();
        CanvasInteractionController canvas = new(history);
        GumpPage page = new();

        GroupElement group = new() { Name = "g", Location = new GumpPoint(10, 20) };

        group.Add(new ImageElement { Name = "a", Location = new GumpPoint(1, 2) });
        group.Add(new ImageElement { Name = "b", Location = new GumpPoint(3, 4) });
        page.Root.Add(group);

        canvas.Page = page;
        canvas.Select(group);
        canvas.Ungroup();

        history.Undo();

        Assert.Equal("g", Order(page));
        Assert.Equal(2, group.Children.Count);
        Assert.Equal(new GumpPoint(1, 2), group.Children[0].Location);
    }

    [Fact]
    public void UngroupingSomethingThatIsNotAGroupDoesNothing()
    {
        (CanvasInteractionController canvas, UndoHistory history, GumpPage page) = Build("a");

        canvas.Select(Find(page, "a"));

        Assert.Equal(0, canvas.Ungroup());
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void GroupingThenUngroupingRestoresThePositions()
    {
        UndoHistory history = new();
        CanvasInteractionController canvas = new(history);
        GumpPage page = new();

        page.Root.Add(new ImageElement { Name = "a", Location = new GumpPoint(30, 40) });
        page.Root.Add(new ImageElement { Name = "b", Location = new GumpPoint(50, 70) });

        canvas.Page = page;
        canvas.Select(page.Root.Children[0]);
        canvas.Toggle(page.Root.Children[1]);
        canvas.Group();
        canvas.Ungroup();

        Assert.Equal(new GumpPoint(30, 40), Find(page, "a").Location);
        Assert.Equal(new GumpPoint(50, 70), Find(page, "b").Location);
    }
}

/// <summary>
/// Moving elements between pages.
/// </summary>
/// <remarks>
/// The original could not do this at all: an element placed on the wrong page
/// had to be deleted and built again on the right one.
/// </remarks>
public class MoveToPageTests
{
    private static string Order(GumpPage page) =>
        string.Join(' ', page.Root.Children.Select(c => c.Name));

    [Fact]
    public void TheSelectionMovesAndTheSourcePageLosesIt()
    {
        UndoHistory history = new();
        CanvasInteractionController canvas = new(history);
        GumpPage first = new();
        GumpPage second = new();

        first.Root.Add(new ImageElement { Name = "a", Location = new GumpPoint(10, 20) });
        first.Root.Add(new ImageElement { Name = "b" });

        canvas.Page = first;
        canvas.Select(first.Root.Children[0]);

        Assert.Equal(1, canvas.MoveSelectionToPage(second));

        Assert.Equal("b", Order(first));
        Assert.Equal("a", Order(second));
        Assert.Equal(new GumpPoint(10, 20), second.Root.Children[0].Location);
    }

    /// <summary>
    /// Every page is its own coordinate space rooted at the gump's origin, so an
    /// element leaving a group has to be rebased or it jumps by the group's
    /// offset.
    /// </summary>
    [Fact]
    public void AnElementLeavingAGroupKeepsItsPlaceOnScreen()
    {
        UndoHistory history = new();
        CanvasInteractionController canvas = new(history);
        GumpPage first = new();
        GumpPage second = new();

        GroupElement group = new() { Location = new GumpPoint(100, 200) };
        ImageElement inner = new() { Name = "a", Location = new GumpPoint(5, 6) };

        group.Add(inner);
        first.Root.Add(group);

        canvas.Page = first;
        canvas.Select(inner);

        Assert.Equal(1, canvas.MoveSelectionToPage(second));
        Assert.Equal(new GumpPoint(105, 206), inner.Location);
    }

    [Fact]
    public void MovingIsOneUndoEntryAndPutsEverythingBack()
    {
        UndoHistory history = new();
        CanvasInteractionController canvas = new(history);
        GumpPage first = new();
        GumpPage second = new();

        first.Root.Add(new ImageElement { Name = "a" });
        first.Root.Add(new ImageElement { Name = "b" });
        first.Root.Add(new ImageElement { Name = "c" });

        canvas.Page = first;
        canvas.Select(first.Root.Children[0]);
        canvas.Toggle(first.Root.Children[2]);

        Assert.Equal(2, canvas.MoveSelectionToPage(second));
        Assert.Equal("b", Order(first));
        Assert.Equal("a c", Order(second));

        history.Undo();

        // Back in their original slots, not appended to the end.
        Assert.Equal("a b c", Order(first));
        Assert.Equal(string.Empty, Order(second));
    }

    [Fact]
    public void TheSelectionIsClearedBecauseItLeftThePage()
    {
        UndoHistory history = new();
        CanvasInteractionController canvas = new(history);
        GumpPage first = new();
        GumpPage second = new();

        first.Root.Add(new ImageElement { Name = "a" });

        canvas.Page = first;
        canvas.Select(first.Root.Children[0]);
        canvas.MoveSelectionToPage(second);

        Assert.Empty(canvas.Selection);
    }

    [Fact]
    public void MovingToTheCurrentPageDoesNothing()
    {
        UndoHistory history = new();
        CanvasInteractionController canvas = new(history);
        GumpPage page = new();

        page.Root.Add(new ImageElement { Name = "a" });

        canvas.Page = page;
        canvas.Select(page.Root.Children[0]);

        Assert.Equal(0, canvas.MoveSelectionToPage(page));
        Assert.False(history.CanUndo);
        Assert.Single(canvas.Selection);
    }

    [Fact]
    public void MovingNothingIsNotAnUndoEntry()
    {
        UndoHistory history = new();
        CanvasInteractionController canvas = new(history);

        canvas.Page = new GumpPage();

        Assert.Equal(0, canvas.MoveSelectionToPage(new GumpPage()));
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void AWholeGroupCanMoveWithItsContents()
    {
        UndoHistory history = new();
        CanvasInteractionController canvas = new(history);
        GumpPage first = new();
        GumpPage second = new();

        GroupElement group = new() { Name = "g", Location = new GumpPoint(30, 40) };

        group.Add(new ImageElement { Name = "a", Location = new GumpPoint(1, 2) });
        first.Root.Add(group);

        canvas.Page = first;
        canvas.Select(group);
        canvas.MoveSelectionToPage(second);

        Assert.Equal("g", Order(second));
        Assert.Equal(new GumpPoint(30, 40), group.Location);
        Assert.Equal(new GumpPoint(1, 2), group.Children[0].Location);
    }
}
