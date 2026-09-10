using GumpStudio.Core.Commands;
using GumpStudio.Core.Document;
using GumpStudio.Core.Editing;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Primitives;

using Xunit;

namespace GumpStudio.Core.Tests;

/// <summary>
/// That the controller announces a selection change, including to nothing.
/// </summary>
/// <remarks>
/// Emptying the selection is as much a change as making one, but
/// <c>ClearSelection</c> was silent — so deleting the selection, switching page
/// and clearing it outright all left anything watching <c>Changed</c> believing
/// the old selection still stood. The editor did not notice because the window
/// rebuilt everything it showed after each edit regardless. A menu item that
/// binds its enabled state does notice, and did: it went on offering Ungroup
/// for a group that was no longer selected.
/// </remarks>
public class SelectionNotificationTests
{
    private static (CanvasInteractionController Controller, GumpPage Page) Setup()
    {
        GumpPage page = new();

        return (new CanvasInteractionController(new UndoHistory()) { Page = page }, page);
    }

    private static AlphaElement AddBox(GumpPage page, int x = 0, int y = 0)
    {
        AlphaElement element = new()
        {
            Location = new GumpPoint(x, y),
            Size = new GumpSize(20, 20),
        };

        page.Root.Add(element);

        return element;
    }

    [Fact]
    public void ClearingTheSelectionAnnouncesIt()
    {
        (CanvasInteractionController controller, GumpPage page) = Setup();

        controller.Select(AddBox(page));

        int changes = 0;
        controller.Changed += (_, _) => changes++;

        controller.ClearSelection();

        Assert.Equal(1, changes);
        Assert.Empty(controller.Selection);
    }

    /// <summary>
    /// Guarded on there being something to clear, so the callers that clear and
    /// then rebuild a selection still report one change rather than two.
    /// </summary>
    [Fact]
    public void ClearingAnEmptySelectionAnnouncesNothing()
    {
        (CanvasInteractionController controller, _) = Setup();

        int changes = 0;
        controller.Changed += (_, _) => changes++;

        controller.ClearSelection();

        Assert.Equal(0, changes);
    }

    [Fact]
    public void DeletingTheSelectionAnnouncesThatItIsGone()
    {
        (CanvasInteractionController controller, GumpPage page) = Setup();

        controller.Select(AddBox(page));

        int changes = 0;
        controller.Changed += (_, _) => changes++;

        controller.DeleteSelection();

        Assert.True(changes > 0);
        Assert.Empty(controller.Selection);
        Assert.Empty(page.Root.Children);
    }

    [Fact]
    public void SelectingOneElementAnnouncesIt()
    {
        (CanvasInteractionController controller, GumpPage page) = Setup();

        AlphaElement element = AddBox(page);

        int changes = 0;
        controller.Changed += (_, _) => changes++;

        controller.Select(element);

        Assert.True(changes > 0);
        Assert.Same(element, Assert.Single(controller.Selection));
    }

    /// <summary>
    /// Switching page drops the selection, which belonged to the page that was
    /// open.
    /// </summary>
    [Fact]
    public void SwitchingPageAnnouncesTheDroppedSelection()
    {
        (CanvasInteractionController controller, GumpPage page) = Setup();

        controller.Select(AddBox(page));

        int changes = 0;
        controller.Changed += (_, _) => changes++;

        controller.Page = new GumpPage();

        Assert.Equal(1, changes);
        Assert.Empty(controller.Selection);
    }
}
