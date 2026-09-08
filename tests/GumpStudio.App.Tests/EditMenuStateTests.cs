using Avalonia.Controls;

using GumpStudio.App;
using GumpStudio.Core.Commands;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Primitives;
using GumpStudio.TestSupport;

using Xunit;

namespace GumpStudio.App.Tests;

/// <summary>
/// The Edit menu's enabled state and its undo labels.
/// </summary>
/// <remarks>
/// Every item in the menu bar used to be permanently enabled whatever was
/// selected, and undo and redo never named what they would reverse — the
/// context menu did both, the menu bar did neither.
/// </remarks>
[Collection("Headless")]
public class EditMenuStateTests
{
    private static readonly string[] SelectionItems =
    [
        "MenuCut",
        "MenuCopy",
        "MenuDelete",
        "MenuBringToFront",
        "MenuBringForward",
        "MenuSendBackward",
        "MenuSendToBack",
    ];

    private static EditorSession SessionIn(TempDirectory directory) =>
        new(AppSettings.Load(Path.Combine(directory.Path, "settings.json")));

    private static LabelElement Label(string text) =>
        new() { Text = text, Location = new GumpPoint(3, 4) };

    private static MenuItem Item(MainWindow window, string name) =>
        window.FindControl<MenuItem>(name)!;

    /// <summary>
    /// Opens the Edit submenu, which is when its state is refreshed.
    /// </summary>
    /// <remarks>
    /// Closed first: reopening a menu that is already open raises no
    /// <c>SubmenuOpened</c>, so a second call would read the previous state.
    /// </remarks>
    private static void OpenEditMenu(MainWindow window)
    {
        MenuItem edit = Item(window, "MenuEditRoot");

        edit.Close();
        edit.Open();
    }

    [Fact]
    public void WithNothingSelectedTheSelectionItemsAreDisabled()
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using EditorSession session = SessionIn(directory);
            using MainWindow window = new(session);

            OpenEditMenu(window);

            foreach (string name in SelectionItems)
            {
                Assert.False(Item(window, name).IsEnabled, name);
            }

            Assert.False(Item(window, "MenuGroup").IsEnabled);
            Assert.False(Item(window, "MenuUngroup").IsEnabled);
            Assert.False(Item(window, "MenuArrange").IsEnabled);
        });
    }

    [Fact]
    public void OneSelectedElementEnablesTheSingleSelectionItems()
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using EditorSession session = SessionIn(directory);
            using MainWindow window = new(session);

            LabelElement label = Label("one");
            session.ActivePage.Root.Add(label);
            session.Canvas.Select(label);

            OpenEditMenu(window);

            foreach (string name in SelectionItems)
            {
                Assert.True(Item(window, name).IsEnabled, name);
            }

            // Grouping and aligning both need at least two.
            Assert.False(Item(window, "MenuGroup").IsEnabled);
            Assert.False(Item(window, "MenuArrange").IsEnabled);
        });
    }

    [Fact]
    public void TwoSelectedElementsEnableGroupingAndAligning()
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using EditorSession session = SessionIn(directory);
            using MainWindow window = new(session);

            session.ActivePage.Root.Add(Label("one"));
            session.ActivePage.Root.Add(Label("two"));
            session.Canvas.SelectAll();

            OpenEditMenu(window);

            Assert.True(Item(window, "MenuGroup").IsEnabled);
            Assert.True(Item(window, "MenuArrange").IsEnabled);
        });
    }

    [Fact]
    public void UngroupIsOnlyOfferedWhenAGroupIsSelected()
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using EditorSession session = SessionIn(directory);
            using MainWindow window = new(session);

            GroupElement group = new();
            group.Add(Label("inside"));
            session.ActivePage.Root.Add(group);

            session.Canvas.Select(group);
            OpenEditMenu(window);

            Assert.True(Item(window, "MenuUngroup").IsEnabled);

            session.Canvas.ClearSelection();
            OpenEditMenu(window);

            Assert.False(Item(window, "MenuUngroup").IsEnabled);
        });
    }

    [Fact]
    public void UndoAndRedoAreDisabledOnAFreshDocument()
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using EditorSession session = SessionIn(directory);
            using MainWindow window = new(session);

            OpenEditMenu(window);

            Assert.False(Item(window, "MenuUndo").IsEnabled);
            Assert.False(Item(window, "MenuRedo").IsEnabled);
        });
    }

    [Fact]
    public void UndoNamesWhatItWillReverse()
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using EditorSession session = SessionIn(directory);
            using MainWindow window = new(session);

            session.Apply(new AddElementCommand(session.ActivePage.Root, Label("one")));

            OpenEditMenu(window);

            Assert.True(Item(window, "MenuUndo").IsEnabled);
            Assert.Equal("_Undo Add Label", Item(window, "MenuUndo").Header);

            session.History.Undo();
            OpenEditMenu(window);

            Assert.False(Item(window, "MenuUndo").IsEnabled);
            Assert.Equal("_Undo", Item(window, "MenuUndo").Header);
            Assert.True(Item(window, "MenuRedo").IsEnabled);
            Assert.Equal("_Redo Add Label", Item(window, "MenuRedo").Header);
        });
    }

    /// <summary>
    /// Removing a page is undoable now, so the menu has to say so.
    /// </summary>
    [Fact]
    public void UndoNamesAPageRemoval()
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using EditorSession session = SessionIn(directory);
            using MainWindow window = new(session);

            session.Apply(new AddPageCommand(session.Document, "Page 1"));
            session.Apply(new RemovePageCommand(session.Document, 1));

            OpenEditMenu(window);

            Assert.Equal("_Undo Remove Page 1", Item(window, "MenuUndo").Header);
        });
    }
}
