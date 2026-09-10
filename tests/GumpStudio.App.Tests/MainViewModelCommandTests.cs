using System.Windows.Input;

using GumpStudio.App;
using GumpStudio.App.ViewModels;
using GumpStudio.Core.Commands;
using GumpStudio.Core.Document;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Primitives;
using GumpStudio.TestSupport;

using Xunit;

namespace GumpStudio.App.Tests;

/// <summary>
/// The commands the menus, the context menus and the keyboard all share.
/// </summary>
/// <remarks>
/// Every one of these was previously reachable only by clicking a named menu
/// item on a dispatcher thread, or by driving a real file dialog. With the
/// dialogs and the clipboard behind interfaces they are ordinary unit tests, and
/// the paths that ask before discarding unsaved work have coverage for the first
/// time.
/// </remarks>
public class MainViewModelCommandTests
{
    private sealed class Shell : IDisposable
    {
        internal Shell(TempDirectory directory)
        {
            Session = new EditorSession(
                AppSettings.Load(Path.Combine(directory.Path, "settings.json")));

            ViewModel = new MainViewModel(Session, Dialogs, Clipboard, View);
        }

        internal EditorSession Session { get; }

        internal MainViewModel ViewModel { get; }

        internal FakeEditorDialogs Dialogs { get; } = new();

        internal FakeTextClipboard Clipboard { get; } = new();

        internal FakeShellView View { get; } = new();

        public void Dispose() => Session.Dispose();
    }

    private static LabelElement Label(string text) =>
        new() { Text = text, Location = new GumpPoint(3, 4) };

    // ---- Unsaved work --------------------------------------------------

    [Fact]
    public async Task StartingANewDocumentDoesNotAskWhenNothingIsUnsaved()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        await shell.ViewModel.NewCommand.ExecuteAsync(null);

        Assert.Equal(0, shell.Dialogs.Confirmations);
    }

    [Fact]
    public async Task StartingANewDocumentAsksBeforeDiscardingUnsavedWork()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        shell.Session.Apply(new AddElementCommand(shell.Session.ActivePage.Root, Label("one")));
        shell.Dialogs.ConfirmAnswer = false;

        await shell.ViewModel.NewCommand.ExecuteAsync(null);

        Assert.Equal(1, shell.Dialogs.Confirmations);
        Assert.Contains("unsaved changes", shell.Dialogs.LastConfirmMessage, StringComparison.Ordinal);

        // Declining keeps the document, elements and all.
        Assert.Single(shell.Session.ActivePage.Root.Children);
        Assert.True(shell.Session.IsModified);
    }

    [Fact]
    public async Task AcceptingTheQuestionDiscardsTheDocument()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        shell.Session.Apply(new AddElementCommand(shell.Session.ActivePage.Root, Label("one")));
        shell.Dialogs.ConfirmAnswer = true;

        await shell.ViewModel.NewCommand.ExecuteAsync(null);

        Assert.Empty(shell.Session.ActivePage.Root.Children);
        Assert.False(shell.Session.IsModified);
    }

    /// <summary>
    /// The saved document is named in the question, so it is clear what is at
    /// stake.
    /// </summary>
    [Fact]
    public async Task TheQuestionNamesTheFileWhenThereIsOne()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        shell.Session.Save(Path.Combine(directory.Path, "shield.gump"));
        shell.Session.Apply(new AddElementCommand(shell.Session.ActivePage.Root, Label("one")));
        shell.Dialogs.ConfirmAnswer = false;

        await shell.ViewModel.NewCommand.ExecuteAsync(null);

        Assert.Contains("shield.gump", shell.Dialogs.LastConfirmMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExitingAsksAndThenClosesTheShell()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        shell.Session.Apply(new AddElementCommand(shell.Session.ActivePage.Root, Label("one")));
        shell.Dialogs.ConfirmAnswer = false;

        await shell.ViewModel.ExitCommand.ExecuteAsync(null);

        Assert.False(shell.View.Closed);

        shell.Dialogs.ConfirmAnswer = true;

        await shell.ViewModel.ExitCommand.ExecuteAsync(null);

        Assert.True(shell.View.Closed);
    }

    // ---- Files ---------------------------------------------------------

    [Fact]
    public async Task CancellingTheOpenPickerChangesNothing()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        shell.Dialogs.OpenPathAnswer = null;

        await shell.ViewModel.OpenCommand.ExecuteAsync(null);

        Assert.Equal(0, shell.View.Refreshes);
    }

    [Fact]
    public async Task SavingWithNoPathAsksForOneAndRemembersIt()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        string path = Path.Combine(directory.Path, "asked.gump");
        shell.Dialogs.SavePathAnswer = path;

        await shell.ViewModel.SaveCommand.ExecuteAsync(null);

        Assert.True(File.Exists(path));
        Assert.Equal(path, shell.Session.DocumentPath);
    }

    /// <summary>Saving again writes where it wrote before, without asking.</summary>
    [Fact]
    public async Task SavingAgainDoesNotAskForAPath()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        string path = Path.Combine(directory.Path, "again.gump");
        shell.Session.Save(path);

        shell.Dialogs.SavePathAnswer = null;

        await shell.ViewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(path, shell.Session.DocumentPath);
    }

    /// <summary>Save As asks even when the document already has a path.</summary>
    [Fact]
    public async Task SaveAsAlwaysAsks()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        shell.Session.Save(Path.Combine(directory.Path, "first.gump"));

        string second = Path.Combine(directory.Path, "second.gump");
        shell.Dialogs.SavePathAnswer = second;

        await shell.ViewModel.SaveAsCommand.ExecuteAsync(null);

        Assert.Equal(second, shell.Session.DocumentPath);
    }

    [Fact]
    public async Task OpeningReadsTheChosenDocument()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        string path = Path.Combine(directory.Path, "round.gump");
        shell.Session.ActivePage.Root.Add(Label("kept"));
        shell.Session.Save(path);

        shell.Session.NewDocument();

        Assert.Empty(shell.Session.ActivePage.Root.Children);

        shell.Dialogs.OpenPathAnswer = path;

        await shell.ViewModel.OpenCommand.ExecuteAsync(null);

        Assert.Single(shell.Session.ActivePage.Root.Children);
        Assert.Equal(path, shell.Session.DocumentPath);
    }

    // ---- Clipboard -----------------------------------------------------

    [Fact]
    public async Task CopyingPutsTheSelectionOnTheClipboardAsXml()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        LabelElement label = Label("copied");
        shell.Session.ActivePage.Root.Add(label);
        shell.Session.Canvas.Select(label);

        await shell.ViewModel.CopyCommand.ExecuteAsync(null);

        Assert.NotNull(shell.Clipboard.Text);
        Assert.Contains("copied", shell.Clipboard.Text, StringComparison.Ordinal);

        // Copying leaves the document alone.
        Assert.Single(shell.Session.ActivePage.Root.Children);
    }

    [Fact]
    public async Task CuttingCopiesAndThenRemoves()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        LabelElement label = Label("cut");
        shell.Session.ActivePage.Root.Add(label);
        shell.Session.Canvas.Select(label);

        await shell.ViewModel.CutCommand.ExecuteAsync(null);

        Assert.NotNull(shell.Clipboard.Text);
        Assert.Empty(shell.Session.ActivePage.Root.Children);
    }

    [Fact]
    public async Task PastingAddsWhatWasCopied()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        LabelElement label = Label("twice");
        shell.Session.ActivePage.Root.Add(label);
        shell.Session.Canvas.Select(label);

        await shell.ViewModel.CopyCommand.ExecuteAsync(null);
        await shell.ViewModel.PasteCommand.ExecuteAsync(null);

        Assert.Equal(2, shell.Session.ActivePage.Root.Children.Count);
    }

    /// <summary>
    /// The clipboard holds whatever was last copied anywhere, so text that is
    /// not ours is an ordinary outcome rather than a failure.
    /// </summary>
    [Fact]
    public async Task PastingSomethingElseSaysSoWithoutFailing()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        shell.Clipboard.Text = "a shopping list";

        await shell.ViewModel.PasteCommand.ExecuteAsync(null);

        Assert.Empty(shell.Session.ActivePage.Root.Children);
        Assert.False(shell.ViewModel.IsStatusError);
        Assert.Contains("Nothing on the clipboard", shell.ViewModel.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithNoClipboardCopyingSaysSo()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        LabelElement label = Label("one");
        shell.Session.ActivePage.Root.Add(label);
        shell.Session.Canvas.Select(label);

        shell.Clipboard.IsAvailable = false;

        await shell.ViewModel.CopyCommand.ExecuteAsync(null);

        Assert.Contains("No clipboard", shell.ViewModel.Status, StringComparison.Ordinal);
    }

    // ---- Editing -------------------------------------------------------

    [Fact]
    public void GroupingTwoElementsMakesAGroup()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        shell.Session.ActivePage.Root.Add(Label("one"));
        shell.Session.ActivePage.Root.Add(Label("two"));
        shell.Session.Canvas.SelectAll();

        shell.ViewModel.GroupCommand.Execute(null);

        Assert.Single(shell.Session.ActivePage.Root.Children);
        Assert.IsType<GroupElement>(shell.Session.ActivePage.Root.Children[0]);
    }

    [Fact]
    public void AligningReportsWhenThereIsNothingToDo()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        LabelElement one = Label("one");
        LabelElement two = new() { Text = "two", Location = new GumpPoint(3, 40) };

        shell.Session.ActivePage.Root.Add(one);
        shell.Session.ActivePage.Root.Add(two);
        shell.Session.Canvas.SelectAll();

        shell.ViewModel.AlignLeftCommand.Execute(null);

        // Both already share a left edge.
        Assert.Equal("Already arranged.", shell.ViewModel.Status);
    }

    [Fact]
    public void AligningMovesTheSelectionAndSaysWhatItDid()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        LabelElement one = Label("one");
        LabelElement two = new() { Text = "two", Location = new GumpPoint(50, 40) };

        shell.Session.ActivePage.Root.Add(one);
        shell.Session.ActivePage.Root.Add(two);
        shell.Session.Canvas.SelectAll();

        shell.ViewModel.AlignLeftCommand.Execute(null);

        Assert.Equal(one.X, two.X);
        Assert.Equal("Aligned lefts.", shell.ViewModel.Status);
    }

    /// <summary>
    /// Silence when nothing moves is ambiguous: an element already at the front
    /// looks exactly like a shortcut that is not wired up.
    /// </summary>
    [Fact]
    public void ReorderingSaysWhenTheElementIsAlreadyThere()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        LabelElement label = Label("alone");
        shell.Session.ActivePage.Root.Add(label);
        shell.Session.Canvas.Select(label);

        shell.ViewModel.BringToFrontCommand.Execute(null);

        Assert.Equal("Already at the front.", shell.ViewModel.Status);
    }

    // ---- Pages ---------------------------------------------------------

    [Fact]
    public void AddingAPageSwitchesToIt()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        shell.ViewModel.AddPageCommand.Execute(null);

        Assert.Equal(2, shell.Session.Document.PageCount);
        Assert.Equal(1, shell.Session.ActivePageIndex);
    }

    /// <summary>
    /// Removing a page takes every element on it, and was once the one action
    /// here with no way back.
    /// </summary>
    [Fact]
    public void RemovingAPageIsUndoable()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        shell.ViewModel.AddPageCommand.Execute(null);
        shell.Session.ActivePage.Root.Add(Label("on page one"));

        shell.ViewModel.RemovePageCommand.Execute(null);

        Assert.Equal(1, shell.Session.Document.PageCount);
        Assert.True(shell.ViewModel.CanUndo);

        shell.Session.History.Undo();

        Assert.Equal(2, shell.Session.Document.PageCount);
        Assert.Single(shell.Session.Document.Pages[1].Root.Children);
    }

    [Fact]
    public void TheLastPageCannotBeRemoved()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        shell.ViewModel.RemovePageCommand.Execute(null);

        Assert.Equal(1, shell.Session.Document.PageCount);
        Assert.Contains("at least one page", shell.ViewModel.Status, StringComparison.Ordinal);
    }

    // ---- View ----------------------------------------------------------

    [Fact]
    public void ZoomingGoesThroughTheView()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        shell.ViewModel.ZoomInCommand.Execute(null);
        shell.ViewModel.ZoomInCommand.Execute(null);
        shell.ViewModel.ZoomOutCommand.Execute(null);

        Assert.Equal(1, shell.View.ZoomSteps);

        shell.ViewModel.ZoomResetCommand.Execute(null);

        Assert.Equal(1.0, shell.View.Zoom);

        shell.ViewModel.ZoomToFitCommand.Execute(null);

        Assert.True(shell.View.FitRequested);
    }

    /// <summary>
    /// The grid settings object is mutated, never replaced.
    /// </summary>
    /// <remarks>
    /// The canvas decides whether to rebuild its render options by comparing that
    /// object by reference, so handing it a new instance would stop it noticing
    /// grid changes at all.
    /// </remarks>
    [Fact]
    public async Task ChangingTheGridKeepsTheSameSettingsObject()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        object before = shell.Session.Canvas.Grid;

        shell.Dialogs.GridSizeAnswer = new GumpSize(16, 24);

        await shell.ViewModel.ChooseGridSizeCommand.ExecuteAsync(null);

        Assert.Same(before, shell.Session.Canvas.Grid);
        Assert.Equal(16, shell.Session.Canvas.Grid.Width);
        Assert.Equal(24, shell.Session.Canvas.Grid.Height);
    }

    [Fact]
    public void TheGridTogglesAreRememberedAndReadBack()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        shell.ViewModel.ShowGrid = true;
        shell.ViewModel.SnapToGrid = true;

        Assert.True(shell.Session.Settings.GridVisible);
        Assert.True(shell.Session.Settings.GridSnap);

        shell.ViewModel.ShowGrid = false;
        shell.ViewModel.LoadGridSettings();

        Assert.False(shell.ViewModel.ShowGrid);
        Assert.True(shell.ViewModel.SnapToGrid);
    }

    [Fact]
    public void TogglingAPanelTellsTheViewWhichOne()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        shell.ViewModel.ClilocVisible = false;

        Assert.Equal(("ClilocTool", false), shell.View.PanelChanges[^1]);

        shell.ViewModel.ClilocVisible = true;

        Assert.Equal(("ClilocTool", true), shell.View.PanelChanges[^1]);
    }

    /// <summary>
    /// Seeding a remembered layout must not act on the toggles, or the restore
    /// would undo itself and then save over what it was restoring.
    /// </summary>
    [Fact]
    public void AdoptingARememberedLayoutDoesNotTouchDock()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        shell.ViewModel.AdoptPanelVisibility(
            toolbox: true, elements: false, properties: true, cliloc: false);

        Assert.Empty(shell.View.PanelChanges);
        Assert.False(shell.ViewModel.ElementsVisible);
        Assert.False(shell.ViewModel.ClilocVisible);
    }

    [Fact]
    public async Task TheAboutBoxIsShown()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        await shell.ViewModel.ShowAboutCommand.ExecuteAsync(null);

        Assert.Equal(1, shell.Dialogs.AboutsShown);
    }

    // ---- Gump properties ------------------------------------------------

    [Fact]
    public async Task CancellingTheGumpPropertiesDialogChangesNothing()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        shell.Dialogs.GumpPropertiesAnswer = null;

        await shell.ViewModel.EditGumpPropertiesCommand.ExecuteAsync(null);

        Assert.False(shell.ViewModel.CanUndo);
    }

    [Fact]
    public async Task AcceptingTheGumpPropertiesDialogIsUndoable()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        shell.Dialogs.GumpPropertiesAnswer = new GumpProperties { TypeId = 4231, Movable = false };

        await shell.ViewModel.EditGumpPropertiesCommand.ExecuteAsync(null);

        Assert.Equal(4231, shell.Session.Document.Properties.TypeId);
        Assert.False(shell.Session.Document.Properties.Movable);
        Assert.True(shell.ViewModel.CanUndo);
    }

    // ---- The toolbox ----------------------------------------------------

    [Fact]
    public void AddingAnElementSelectsItAndIsUndoable()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        LabelElement label = Label("new");

        shell.ViewModel.AddElement(label);

        Assert.Same(label, Assert.Single(shell.Session.Canvas.Selection));
        Assert.True(shell.ViewModel.CanUndo);
    }

    // ---- The command surface itself -------------------------------------

    /// <summary>
    /// That a command exists for every action, and that asking whether it can
    /// run never throws.
    /// </summary>
    /// <remarks>
    /// A menu item binds <c>CanExecute</c> as well as <c>Execute</c>, and every
    /// item in the menu bar is live from the moment the window loads — so a
    /// <c>CanExecute</c> that threw would take the window down before anything
    /// was clicked.
    /// </remarks>
    [Fact]
    public void EveryCommandCanBeAskedWhetherItWouldRun()
    {
        using TempDirectory directory = new();
        using Shell shell = new(directory);

        MainViewModel vm = shell.ViewModel;

        List<ICommand> commands =
        [
            vm.NewCommand, vm.OpenCommand, vm.SaveCommand, vm.SaveAsCommand,
            vm.ImportLegacyCommand, vm.ImportLayoutCommand, vm.SetClientCommand, vm.ExitCommand,
            vm.UndoCommand, vm.RedoCommand, vm.CutCommand, vm.CopyCommand, vm.PasteCommand,
            vm.SelectAllCommand, vm.DeleteCommand, vm.GroupCommand, vm.UngroupCommand,
            vm.BringToFrontCommand, vm.BringForwardCommand, vm.SendBackwardCommand,
            vm.SendToBackCommand, vm.AlignLeftCommand, vm.AlignRightCommand, vm.AlignTopCommand,
            vm.AlignBottomCommand, vm.CentreHorizontallyCommand, vm.CentreVerticallyCommand,
            vm.SpaceHorizontallyCommand, vm.SpaceVerticallyCommand, vm.ChooseGridSizeCommand,
            vm.ZoomInCommand, vm.ZoomOutCommand, vm.ZoomResetCommand, vm.ZoomToFitCommand,
            vm.ResetLayoutCommand, vm.EditGumpPropertiesCommand, vm.AddPageCommand,
            vm.InsertPageCommand, vm.RemovePageCommand, vm.ClearPageCommand, vm.ShowAboutCommand,
        ];

        Assert.All(commands, command => Assert.NotNull(command));
        Assert.All(commands, command => command.CanExecute(null));
    }
}
