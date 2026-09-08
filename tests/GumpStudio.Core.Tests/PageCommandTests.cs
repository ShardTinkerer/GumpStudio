using GumpStudio.Core.Commands;
using GumpStudio.Core.Document;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Primitives;

using Xunit;

namespace GumpStudio.Core.Tests;

/// <summary>
/// Page structure changes, all of which go through the undo history.
/// </summary>
/// <remarks>
/// The defect these guard: adding and removing a page used to mutate the
/// document directly, so removing one destroyed every element on it with no
/// way back.
/// </remarks>
public class PageCommandTests
{
    private static LabelElement Label(string text) =>
        new() { Text = text, Location = new GumpPoint(1, 2) };

    [Fact]
    public void AddingAPageIsUndoable()
    {
        GumpDocument document = new();
        UndoHistory history = new();

        history.Push(new AddPageCommand(document));

        Assert.Equal(2, document.PageCount);

        history.Undo();

        Assert.Equal(1, document.PageCount);

        history.Redo();

        Assert.Equal(2, document.PageCount);
    }

    [Fact]
    public void RedoingAnAddRestoresTheSamePageWithItsElements()
    {
        GumpDocument document = new();
        UndoHistory history = new();

        AddPageCommand command = new(document);
        history.Push(command);

        command.Page.Root.Add(Label("kept"));

        history.Undo();
        history.Redo();

        Assert.Same(command.Page, document.Pages[1]);
        Assert.Single(document.Pages[1].Root.Children);
    }

    [Fact]
    public void RemovingAPageBringsItsElementsBackOnUndo()
    {
        GumpDocument document = new();
        GumpPage second = document.AddPage();

        second.Root.Add(Label("first"));
        second.Root.Add(Label("second"));

        UndoHistory history = new();

        history.Push(new RemovePageCommand(document, 1));

        Assert.Equal(1, document.PageCount);

        history.Undo();

        Assert.Equal(2, document.PageCount);
        Assert.Same(second, document.Pages[1]);
        Assert.Equal(2, document.Pages[1].Root.Children.Count);
        Assert.Equal(
            ["first", "second"],
            document.Pages[1].Root.Children.Cast<LabelElement>().Select(l => l.Text));
    }

    [Fact]
    public void RemovingAPageRestoresItAtItsOriginalIndex()
    {
        GumpDocument document = new();
        GumpPage middle = document.AddPage("middle");
        GumpPage last = document.AddPage("last");

        UndoHistory history = new();

        history.Push(new RemovePageCommand(document, 1));

        Assert.Same(last, document.Pages[1]);

        history.Undo();

        Assert.Same(middle, document.Pages[1]);
        Assert.Same(last, document.Pages[2]);
    }

    [Fact]
    public void TheFollowOnActiveIndexIsTheSlotTheRemovedPageVacated()
    {
        GumpDocument document = new();
        document.AddPage();
        document.AddPage();

        // Removing the middle of three leaves the page that slid into its slot
        // active, rather than jumping one further back as the original did.
        Assert.Equal(1, new RemovePageCommand(document, 1).ActiveIndexAfterRemoval);

        // Removing the last leaves the new last page active.
        Assert.Equal(1, new RemovePageCommand(document, 2).ActiveIndexAfterRemoval);
    }

    [Fact]
    public void TheFollowOnIndexIsTheSameWhetherReadBeforeOrAfterExecuting()
    {
        GumpDocument document = new();
        document.AddPage();
        document.AddPage();

        RemovePageCommand command = new(document, 2);
        int before = command.ActiveIndexAfterRemoval;

        command.Execute();

        Assert.Equal(before, command.ActiveIndexAfterRemoval);
    }

    [Fact]
    public void TheLastPageCannotBeRemoved()
    {
        GumpDocument document = new();

        Assert.Throws<InvalidOperationException>(() => new RemovePageCommand(document, 0));
    }

    [Fact]
    public void InsertingAPageShiftsTheRestAlongAndUndoesCleanly()
    {
        GumpDocument document = new();
        GumpPage second = document.AddPage("second");

        UndoHistory history = new();
        InsertPageCommand command = new(document, 1);

        history.Push(command);

        Assert.Equal(3, document.PageCount);
        Assert.Same(command.Page, document.Pages[1]);
        Assert.Same(second, document.Pages[2]);

        history.Undo();

        Assert.Equal(2, document.PageCount);
        Assert.Same(second, document.Pages[1]);
    }

    [Fact]
    public void ClearingAPageKeepsThePageAndRestoresDrawingOrder()
    {
        GumpDocument document = new();
        GumpPage page = document.Pages[0];

        page.Root.Add(Label("back"));
        page.Root.Add(Label("middle"));
        page.Root.Add(Label("front"));

        UndoHistory history = new();
        ClearPageCommand command = new(page);

        Assert.True(command.HasContent);

        history.Push(command);

        Assert.Equal(1, document.PageCount);
        Assert.Empty(page.Root.Children);

        history.Undo();

        Assert.Equal(
            ["back", "middle", "front"],
            page.Root.Children.Cast<LabelElement>().Select(l => l.Text));
    }

    [Fact]
    public void ClearingAnEmptyPageReportsThereIsNothingToDo()
    {
        GumpDocument document = new();

        Assert.False(new ClearPageCommand(document.Pages[0]).HasContent);
    }

    [Fact]
    public void EveryPageCommandDescribesItselfForTheUndoMenu()
    {
        GumpDocument document = new();
        document.AddPage("Page 1");

        Assert.Equal("Add page", new AddPageCommand(document).Description);
        Assert.Equal("Insert page", new InsertPageCommand(document, 0).Description);
        Assert.Equal("Remove Page 1", new RemovePageCommand(document, 1).Description);
        Assert.Equal("Clear Page 0", new ClearPageCommand(document.Pages[0]).Description);
    }
}
