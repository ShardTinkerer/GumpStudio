using System.ComponentModel;

using GumpStudio.App;
using GumpStudio.App.ViewModels;
using GumpStudio.Core.Commands;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Primitives;
using GumpStudio.TestSupport;

using Xunit;

namespace GumpStudio.App.Tests;

/// <summary>
/// The state the shell derives from the session: what is available, what undo
/// would reverse, and what the title says.
/// </summary>
/// <remarks>
/// No <c>[Collection("Headless")]</c> and no window. That is the point of having
/// a view model at all — this was previously only reachable by opening a menu on
/// a dispatcher thread, so these facts run in parallel with everything else.
/// <see cref="EditMenuStateTests"/> still proves the same rules reach the
/// rendered menu.
/// </remarks>
public class MainViewModelStateTests
{
    private static MainViewModel ViewModelIn(TempDirectory directory, out EditorSession session)
    {
        session = new EditorSession(AppSettings.Load(Path.Combine(directory.Path, "settings.json")));

        return new MainViewModel(
            session, new FakeEditorDialogs(), new FakeTextClipboard(), new FakeShellView());
    }

    private static LabelElement Label(string text) =>
        new() { Text = text, Location = new GumpPoint(3, 4) };

    [Fact]
    public void NothingSelectedOffersNoneOfTheSelectionActions()
    {
        using TempDirectory directory = new();
        MainViewModel viewModel = ViewModelIn(directory, out EditorSession session);

        using (session)
        {
            Assert.False(viewModel.CanCut);
            Assert.False(viewModel.CanCopy);
            Assert.False(viewModel.CanDelete);
            Assert.False(viewModel.CanReorder);
            Assert.False(viewModel.CanGroup);
            Assert.False(viewModel.CanUngroup);
            Assert.False(viewModel.CanArrange);
        }
    }

    [Fact]
    public void OneSelectedElementOffersTheSingleSelectionActions()
    {
        using TempDirectory directory = new();
        MainViewModel viewModel = ViewModelIn(directory, out EditorSession session);

        using (session)
        {
            LabelElement label = Label("one");
            session.ActivePage.Root.Add(label);
            session.Canvas.Select(label);

            Assert.True(viewModel.CanCut);
            Assert.True(viewModel.CanCopy);
            Assert.True(viewModel.CanDelete);
            Assert.True(viewModel.CanReorder);

            // Grouping and aligning both need at least two.
            Assert.False(viewModel.CanGroup);
            Assert.False(viewModel.CanArrange);
        }
    }

    [Fact]
    public void TwoSelectedElementsOfferGroupingAndArranging()
    {
        using TempDirectory directory = new();
        MainViewModel viewModel = ViewModelIn(directory, out EditorSession session);

        using (session)
        {
            session.ActivePage.Root.Add(Label("one"));
            session.ActivePage.Root.Add(Label("two"));
            session.Canvas.SelectAll();

            Assert.True(viewModel.CanGroup);
            Assert.True(viewModel.CanArrange);
        }
    }

    /// <summary>
    /// A page's root is a group as well, and dissolving it would empty the page.
    /// </summary>
    [Fact]
    public void UngroupIsOnlyOfferedForAGroupThatIsNotAPageRoot()
    {
        using TempDirectory directory = new();
        MainViewModel viewModel = ViewModelIn(directory, out EditorSession session);

        using (session)
        {
            GroupElement group = new();
            group.Add(Label("inside"));
            session.ActivePage.Root.Add(group);

            session.Canvas.Select(group);

            Assert.True(viewModel.CanUngroup);

            session.Canvas.ClearSelection();

            Assert.False(viewModel.CanUngroup);
        }
    }

    [Fact]
    public void UndoAndRedoAreUnavailableOnAFreshDocument()
    {
        using TempDirectory directory = new();
        MainViewModel viewModel = ViewModelIn(directory, out EditorSession session);

        using (session)
        {
            Assert.False(viewModel.CanUndo);
            Assert.False(viewModel.CanRedo);
            Assert.Equal("_Undo", viewModel.UndoHeader);
            Assert.Equal("_Redo", viewModel.RedoHeader);
        }
    }

    [Fact]
    public void TheUndoHeaderNamesWhatItWillReverse()
    {
        using TempDirectory directory = new();
        MainViewModel viewModel = ViewModelIn(directory, out EditorSession session);

        using (session)
        {
            session.Apply(new AddElementCommand(session.ActivePage.Root, Label("one")));

            Assert.True(viewModel.CanUndo);
            Assert.Equal("_Undo Add Label", viewModel.UndoHeader);

            session.History.Undo();

            Assert.False(viewModel.CanUndo);
            Assert.Equal("_Undo", viewModel.UndoHeader);
            Assert.True(viewModel.CanRedo);
            Assert.Equal("_Redo Add Label", viewModel.RedoHeader);
        }
    }

    /// <summary>
    /// A context menu paints no accelerators, so it cannot carry the underscore.
    /// </summary>
    [Fact]
    public void TheContextHeadersCarryNoAccelerator()
    {
        using TempDirectory directory = new();
        MainViewModel viewModel = ViewModelIn(directory, out EditorSession session);

        using (session)
        {
            session.Apply(new AddElementCommand(session.ActivePage.Root, Label("one")));

            Assert.Equal("Undo Add Label", viewModel.UndoContextHeader);

            session.History.Undo();

            Assert.Equal("Redo Add Label", viewModel.RedoContextHeader);
        }
    }

    /// <summary>
    /// That the menu is told, not just that the answer would be right if asked.
    /// </summary>
    /// <remarks>
    /// The whole reason the enabled state used to be refreshed as the menu opened
    /// was that nothing announced the change. A bound menu is only correct if
    /// this fires.
    /// </remarks>
    [Fact]
    public void ChangingTheSelectionAnnouncesTheAvailabilityOfEveryAction()
    {
        using TempDirectory directory = new();
        MainViewModel viewModel = ViewModelIn(directory, out EditorSession session);

        using (session)
        {
            List<string> raised = [];
            viewModel.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);

            LabelElement label = Label("one");
            session.ActivePage.Root.Add(label);
            session.Canvas.Select(label);

            Assert.Contains(nameof(MainViewModel.CanCut), raised);
            Assert.Contains(nameof(MainViewModel.CanDelete), raised);
            Assert.Contains(nameof(MainViewModel.CanGroup), raised);
            Assert.Contains(nameof(MainViewModel.CanUngroup), raised);
            Assert.Contains(nameof(MainViewModel.CanArrange), raised);
            Assert.Contains(nameof(MainViewModel.CanReorder), raised);
        }
    }

    [Fact]
    public void PushingACommandAnnouncesTheUndoLabels()
    {
        using TempDirectory directory = new();
        MainViewModel viewModel = ViewModelIn(directory, out EditorSession session);

        using (session)
        {
            List<string> raised = [];
            viewModel.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);

            session.Apply(new AddElementCommand(session.ActivePage.Root, Label("one")));

            Assert.Contains(nameof(MainViewModel.UndoHeader), raised);
            Assert.Contains(nameof(MainViewModel.CanUndo), raised);
        }
    }

    [Fact]
    public void TheTitleNamesAnUnsavedDocumentAndMarksItModified()
    {
        using TempDirectory directory = new();
        MainViewModel viewModel = ViewModelIn(directory, out EditorSession session);

        using (session)
        {
            Assert.Equal("GumpStudio — Untitled", viewModel.Title);

            session.Apply(new AddElementCommand(session.ActivePage.Root, Label("one")));

            Assert.Equal("GumpStudio — Untitled*", viewModel.Title);
        }
    }

    /// <summary>
    /// The asterisk goes away again on undoing back to the saved state, because
    /// the flag is derived from the history rather than set by each mutation.
    /// </summary>
    [Fact]
    public void TheTitleDropsTheModifiedMarkOnUndoingBackToTheSavedState()
    {
        using TempDirectory directory = new();
        MainViewModel viewModel = ViewModelIn(directory, out EditorSession session);

        using (session)
        {
            string path = Path.Combine(directory.Path, "gump.gsx");
            session.Save(path);

            Assert.Equal("GumpStudio — gump.gsx", viewModel.Title);

            session.Apply(new AddElementCommand(session.ActivePage.Root, Label("one")));

            Assert.Equal("GumpStudio — gump.gsx*", viewModel.Title);

            session.History.Undo();

            Assert.Equal("GumpStudio — gump.gsx", viewModel.Title);
        }
    }

    [Fact]
    public void TheTitleIsAnnouncedWhenTheModifiedFlagChanges()
    {
        using TempDirectory directory = new();
        MainViewModel viewModel = ViewModelIn(directory, out EditorSession session);

        using (session)
        {
            List<string> raised = [];
            viewModel.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);

            session.Apply(new AddElementCommand(session.ActivePage.Root, Label("one")));

            Assert.Contains(nameof(MainViewModel.Title), raised);
        }
    }

    [Fact]
    public void SettingAStatusReportsItAndSaysWhetherItFailed()
    {
        using TempDirectory directory = new();
        MainViewModel viewModel = ViewModelIn(directory, out EditorSession session);

        using (session)
        {
            viewModel.SetStatus("2 page(s)");

            Assert.Equal("2 page(s)", viewModel.Status);
            Assert.False(viewModel.IsStatusError);

            viewModel.SetStatus("could not read it", isError: true);

            Assert.Equal("could not read it", viewModel.Status);
            Assert.True(viewModel.IsStatusError);
        }
    }

    /// <summary>
    /// The three exception types the shell treats as "tell the user and carry
    /// on" rather than as a defect.
    /// </summary>
    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(InvalidDataException))]
    [InlineData(typeof(InvalidOperationException))]
    public void AGuardedActionReportsAFailureInTheStatusLine(Type failure)
    {
        using TempDirectory directory = new();
        MainViewModel viewModel = ViewModelIn(directory, out EditorSession session);

        using (session)
        {
            viewModel.RunGuarded(
                () => throw (Exception)Activator.CreateInstance(failure, "it went wrong")!);

            Assert.Equal("it went wrong", viewModel.Status);
            Assert.True(viewModel.IsStatusError);
        }
    }

    /// <summary>
    /// Anything else is a defect and must not be swallowed.
    /// </summary>
    [Fact]
    public void AGuardedActionDoesNotSwallowAnUnexpectedFailure()
    {
        using TempDirectory directory = new();
        MainViewModel viewModel = ViewModelIn(directory, out EditorSession session);

        using (session)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => viewModel.RunGuarded(() => throw new ArgumentOutOfRangeException("paramName")));
        }
    }

    [Fact]
    public async Task AGuardedAsyncActionReportsAFailureInTheStatusLine()
    {
        using TempDirectory directory = new();
        MainViewModel viewModel = ViewModelIn(directory, out EditorSession session);

        using (session)
        {
            await viewModel.RunGuardedAsync(() => throw new IOException("the file is gone"));

            Assert.Equal("the file is gone", viewModel.Status);
            Assert.True(viewModel.IsStatusError);
        }
    }

    /// <summary>
    /// A successful action leaves whatever the status line already said.
    /// </summary>
    [Fact]
    public void AGuardedActionThatSucceedsChangesNothing()
    {
        using TempDirectory directory = new();
        MainViewModel viewModel = ViewModelIn(directory, out EditorSession session);

        using (session)
        {
            viewModel.SetStatus("ready");

            viewModel.RunGuarded(() => { });

            Assert.Equal("ready", viewModel.Status);
            Assert.False(viewModel.IsStatusError);
        }
    }
}
