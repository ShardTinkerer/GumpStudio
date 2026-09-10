using GumpStudio.App;
using GumpStudio.App.ViewModels;
using GumpStudio.Core.Commands;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Primitives;
using GumpStudio.TestSupport;

using Xunit;

namespace GumpStudio.App.Tests;

/// <summary>
/// The page strip and the elements list, kept in step with the document.
/// </summary>
/// <remarks>
/// Both were rebuilt wholesale after every edit — the strip cleared a panel and
/// made a fresh button per page, and the list reassigned its item source.
/// Reassigning is what made the list reset its own selection on each refresh,
/// and the resulting event raced the guard flag that existed to suppress it: the
/// visible symptom was a selected element whose properties never appeared. These
/// synchronise in place instead, which is what these facts pin down.
/// </remarks>
public class ElementListSyncTests
{
    private sealed class Shell : IDisposable
    {
        internal Shell(TempDirectory directory)
        {
            Session = new EditorSession(
                AppSettings.Load(Path.Combine(directory.Path, "settings.json")));

            ViewModel = new MainViewModel(
                Session, new FakeEditorDialogs(), new FakeTextClipboard(), new FakeShellView());
        }

        internal EditorSession Session { get; }

        internal MainViewModel ViewModel { get; }

        public void Dispose() => Session.Dispose();
    }

    private static LabelElement Label(string text) =>
        new() { Text = text, Location = new GumpPoint(3, 4) };

    // ---- Elements ------------------------------------------------------

    [Fact]
    public void AddingAnElementAddsARow()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        Assert.Empty(shell.ViewModel.Elements);

        LabelElement label = Label("one");
        label.Name = "greeting";

        shell.ViewModel.AddElement(label);

        ElementRowViewModel row = Assert.Single(shell.ViewModel.Elements);

        // The row reads the element's type and its name, which is what the panel
        // shows - not the text a label happens to draw.
        Assert.Equal("Label \"greeting\"", row.Label);
    }

    [Fact]
    public void RemovingAnElementDropsItsRow()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        LabelElement label = Label("one");
        shell.ViewModel.AddElement(label);

        shell.ViewModel.DeleteCommand.Execute(null);

        Assert.Empty(shell.ViewModel.Elements);
    }

    /// <summary>
    /// A row survives a z-order change rather than being made again.
    /// </summary>
    /// <remarks>
    /// The reason for moving entries rather than removing and re-adding them: a
    /// list rebuilds the container for an added item but keeps it for a moved
    /// one, so the panel neither flickers nor drops what it had selected.
    /// </remarks>
    [Fact]
    public void ReorderingMovesTheRowsRatherThanRecreatingThem()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        LabelElement first = Label("first");
        LabelElement second = Label("second");

        shell.ViewModel.AddElement(first);
        shell.ViewModel.AddElement(second);

        ElementRowViewModel firstRow = shell.ViewModel.Elements[0];
        ElementRowViewModel secondRow = shell.ViewModel.Elements[1];

        Assert.Same(first, firstRow.Element);

        // "second" is selected, having just been added.
        shell.ViewModel.SendToBackCommand.Execute(null);

        Assert.Same(secondRow, shell.ViewModel.Elements[0]);
        Assert.Same(firstRow, shell.ViewModel.Elements[1]);
    }

    /// <summary>
    /// Renaming an element updates its row without anything rebuilding the list.
    /// </summary>
    [Fact]
    public void RenamingAnElementUpdatesItsRowInPlace()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        LabelElement label = Label("before");
        shell.ViewModel.AddElement(label);

        ElementRowViewModel row = shell.ViewModel.Elements[0];

        List<string> raised = [];
        row.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);

        label.Name = "renamed";

        Assert.Contains(nameof(ElementRowViewModel.Label), raised);
        Assert.Same(row, shell.ViewModel.Elements[0]);
    }

    // ---- Selection -----------------------------------------------------

    [Fact]
    public void SelectingOnTheCanvasMarksTheRow()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        LabelElement one = Label("one");
        LabelElement two = Label("two");

        shell.ViewModel.AddElement(one);
        shell.ViewModel.AddElement(two);

        shell.Session.Canvas.Select(one);

        Assert.True(shell.ViewModel.Elements[0].IsSelected);
        Assert.False(shell.ViewModel.Elements[1].IsSelected);
    }

    /// <summary>
    /// The row announces it, which is what a bound list needs — the element used
    /// to change its selected state silently.
    /// </summary>
    [Fact]
    public void ARowAnnouncesThatItBecameSelected()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        LabelElement label = Label("one");
        shell.ViewModel.AddElement(label);

        shell.Session.Canvas.ClearSelection();

        ElementRowViewModel row = shell.ViewModel.Elements[0];

        List<string> raised = [];
        row.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);

        shell.Session.Canvas.Select(label);

        Assert.Contains(nameof(ElementRowViewModel.IsSelected), raised);
        Assert.True(row.IsSelected);
    }

    /// <summary>
    /// Selecting through the row reaches the controller, so the canvas hears it.
    /// </summary>
    /// <remarks>
    /// A row reports only its own change, and adding one to the selection is
    /// what that means. The list allows several and does the replacing itself:
    /// see <see cref="ReplacingTheSelectionFromTheListLeavesOneSelected"/>.
    /// </remarks>
    [Fact]
    public void SelectingARowAddsItToTheCanvasSelection()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        LabelElement one = Label("one");
        LabelElement two = Label("two");

        shell.ViewModel.AddElement(one);
        shell.ViewModel.AddElement(two);

        shell.Session.Canvas.ClearSelection();

        shell.ViewModel.Elements[0].IsSelected = true;

        Assert.Same(one, Assert.Single(shell.Session.Canvas.Selection));

        shell.ViewModel.Elements[1].IsSelected = true;

        Assert.Equal(2, shell.Session.Canvas.Selection.Count);
    }

    /// <summary>
    /// What a click in the list actually does, in the order the list does it.
    /// </summary>
    /// <remarks>
    /// A <c>ListBox</c> that allows several deselects the rows it is replacing
    /// and then selects the one that was clicked, and each of those arrives as
    /// its own change. Treating a pick as "replace the selection" instead would
    /// leave the just-deselected rows selected on the canvas — which is exactly
    /// what the first attempt at this did.
    /// </remarks>
    [Fact]
    public void ReplacingTheSelectionFromTheListLeavesOneSelected()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        LabelElement one = Label("one");
        LabelElement two = Label("two");

        shell.ViewModel.AddElement(one);
        shell.ViewModel.AddElement(two);
        shell.ViewModel.SelectAllCommand.Execute(null);

        Assert.Equal(2, shell.Session.Canvas.Selection.Count);

        foreach (ElementRowViewModel row in shell.ViewModel.Elements)
        {
            row.IsSelected = false;
        }

        shell.ViewModel.Elements[0].IsSelected = true;

        Assert.Same(one, Assert.Single(shell.Session.Canvas.Selection));
    }

    [Fact]
    public void DeselectingTheOnlySelectedRowClearsTheSelection()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        LabelElement label = Label("one");
        shell.ViewModel.AddElement(label);

        Assert.True(shell.ViewModel.Elements[0].IsSelected);

        shell.ViewModel.Elements[0].IsSelected = false;

        Assert.Empty(shell.Session.Canvas.Selection);
    }

    /// <summary>
    /// Selecting everything marks every row, which the list's own
    /// single-selection <c>SelectedItem</c> could never have shown.
    /// </summary>
    [Fact]
    public void SelectingEverythingMarksEveryRow()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        shell.ViewModel.AddElement(Label("one"));
        shell.ViewModel.AddElement(Label("two"));
        shell.ViewModel.AddElement(Label("three"));

        shell.ViewModel.SelectAllCommand.Execute(null);

        Assert.All(shell.ViewModel.Elements, row => Assert.True(row.IsSelected));
    }

    // ---- Pages ---------------------------------------------------------

    [Fact]
    public void AFreshDocumentHasOneTab()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        PageTabViewModel tab = Assert.Single(shell.ViewModel.Pages);

        Assert.Equal(0, tab.Index);
        Assert.Equal("Page 0", tab.Label);
        Assert.True(tab.IsActive);
    }

    [Fact]
    public void AddingAPageAddsATabAndMovesTheActiveMark()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        shell.ViewModel.AddPageCommand.Execute(null);

        Assert.Equal(2, shell.ViewModel.Pages.Count);
        Assert.False(shell.ViewModel.Pages[0].IsActive);
        Assert.True(shell.ViewModel.Pages[1].IsActive);
    }

    [Fact]
    public void RemovingAPageDropsItsTab()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        shell.ViewModel.AddPageCommand.Execute(null);
        shell.ViewModel.RemovePageCommand.Execute(null);

        Assert.Single(shell.ViewModel.Pages);
        Assert.True(shell.ViewModel.Pages[0].IsActive);
    }

    [Fact]
    public void ActivatingATabSwitchesPage()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        shell.ViewModel.AddPageCommand.Execute(null);

        shell.ViewModel.ActivatePageCommand.Execute(shell.ViewModel.Pages[0]);

        Assert.Equal(0, shell.Session.ActivePageIndex);
        Assert.True(shell.ViewModel.Pages[0].IsActive);
        Assert.False(shell.ViewModel.Pages[1].IsActive);
    }

    /// <summary>
    /// The list follows the page, since the elements on it are what it shows.
    /// </summary>
    [Fact]
    public void SwitchingPageShowsThatPagesElements()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        shell.ViewModel.AddElement(Label("on page zero"));

        shell.ViewModel.AddPageCommand.Execute(null);

        Assert.Empty(shell.ViewModel.Elements);

        shell.ViewModel.ActivatePageCommand.Execute(shell.ViewModel.Pages[0]);

        Assert.Single(shell.ViewModel.Elements);
    }

    /// <summary>
    /// Opening a document replaces both collections.
    /// </summary>
    [Fact]
    public void AdoptingADocumentReplacesTheTabsAndTheRows()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        shell.ViewModel.AddElement(Label("original"));
        shell.ViewModel.AddPageCommand.Execute(null);

        shell.Session.NewDocument();

        Assert.Single(shell.ViewModel.Pages);
        Assert.Empty(shell.ViewModel.Elements);
    }

    /// <summary>
    /// Undo puts both collections back, since they follow the document rather
    /// than being told by whatever made the change.
    /// </summary>
    [Fact]
    public void UndoRestoresTheRows()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        shell.Session.Apply(new AddElementCommand(shell.Session.ActivePage.Root, Label("one")));

        Assert.Single(shell.ViewModel.Elements);

        shell.Session.History.Undo();

        Assert.Empty(shell.ViewModel.Elements);

        shell.Session.History.Redo();

        Assert.Single(shell.ViewModel.Elements);
    }
}
