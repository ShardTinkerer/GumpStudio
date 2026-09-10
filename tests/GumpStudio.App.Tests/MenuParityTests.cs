using Avalonia.Controls;
using Avalonia.LogicalTree;

using GumpStudio.App;
using GumpStudio.App.Controls;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Primitives;
using GumpStudio.TestSupport;

using Xunit;

namespace GumpStudio.App.Tests;

/// <summary>
/// That the menu bar and the context menu agree.
/// </summary>
/// <remarks>
/// <para>
/// They did not, and nothing caught it. Every action was registered three times
/// over — a <c>Click</c> handler keyed on a control name, a keyboard binding,
/// and a hand-built context-menu item — and the enabled state was worked out
/// twice, in <c>RefreshEditMenu</c> for the menu bar and in the context menu's
/// <c>Opening</c> handler. The menu bar's copy was added later and the two
/// drifted: for a while the bar showed every item available whatever was
/// selected, and undo and redo never named what they would reverse, while the
/// context menu did both.
/// </para>
/// <para>
/// Both now bind the same view-model properties, so the drift is not fixed so
/// much as made impossible. This is the guard for that.
/// </para>
/// </remarks>
[Collection("Headless")]
public class MenuParityTests
{
    private static EditorSession SessionIn(TempDirectory directory) =>
        new(AppSettings.Load(Path.Combine(directory.Path, "settings.json")));

    private static LabelElement Label(string text) =>
        new() { Text = text, Location = new GumpPoint(3, 4) };

    /// <summary>The context menu the canvas carries.</summary>
    private static EditorContextMenu CanvasMenu(MainWindow window) => window.ContextMenus[0];

    private static MenuItem BarItem(MainWindow window, string name) =>
        window.FindControl<MenuItem>(name)!;

    private static MenuItem ContextItem(EditorContextMenu menu, string header) =>
        menu.GetLogicalDescendants()
            .OfType<MenuItem>()
            .First(item => item.Header?.ToString()?.StartsWith(header, StringComparison.Ordinal) is true);

    /// <summary>
    /// Each pair is one action, as the bar names it and as the context menu does.
    /// </summary>
    private static readonly (string BarName, string ContextHeader)[] SharedActions =
    [
        ("MenuUndo", "Undo"),
        ("MenuRedo", "Redo"),
        ("MenuCut", "Cut"),
        ("MenuCopy", "Copy"),
        ("MenuDelete", "Delete"),
        ("MenuGroup", "Group selection"),
        ("MenuUngroup", "Ungroup"),
        ("MenuBringToFront", "Bring to front"),
        ("MenuBringForward", "Bring forward"),
        ("MenuSendBackward", "Send backward"),
        ("MenuSendToBack", "Send to back"),
        ("MenuArrange", "Arrange"),
    ];

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void TheTwoMenusAgreeOnWhatIsAvailable(int selected)
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using EditorSession session = SessionIn(directory);
            using MainWindow window = new(session);

            for (int i = 0; i < 2; i++)
            {
                session.ActivePage.Root.Add(Label($"element {i}"));
            }

            if (selected > 0)
            {
                session.Canvas.SelectAll();
            }

            if (selected == 1)
            {
                session.Canvas.Select(session.ActivePage.Root.Children[0]);
            }

            EditorContextMenu menu = CanvasMenu(window);

            List<string> disagreements =
            [
                .. SharedActions
                    .Where(action => BarItem(window, action.BarName).IsEnabled
                        != ContextItem(menu, action.ContextHeader).IsEnabled)
                    .Select(action =>
                        $"{action.BarName} is {BarItem(window, action.BarName).IsEnabled} "
                        + $"but \"{action.ContextHeader}\" is "
                        + $"{ContextItem(menu, action.ContextHeader).IsEnabled}"),
            ];

            Assert.Empty(disagreements);
        });
    }

    /// <summary>
    /// Both menus name what undo would reverse, differing only in the
    /// accelerator the bar paints and the context menu cannot.
    /// </summary>
    [Fact]
    public void BothMenusNameWhatUndoWillReverse()
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using EditorSession session = SessionIn(directory);
            using MainWindow window = new(session);

            session.Apply(new Core.Commands.AddElementCommand(
                session.ActivePage.Root, Label("one")));

            EditorContextMenu menu = CanvasMenu(window);

            Assert.Equal("_Undo Add Label", BarItem(window, "MenuUndo").Header);
            Assert.Equal("Undo Add Label", ContextItem(menu, "Undo").Header);
        });
    }

    /// <summary>
    /// That the context menu found a view model at all.
    /// </summary>
    /// <remarks>
    /// Dock builds a tool's content outside the window's name scope, so nothing
    /// inherits a DataContext down to a panel — the window hands each one over.
    /// A context menu whose bindings silently resolved against nothing would
    /// leave every item enabled, which is exactly the state this whole exercise
    /// is undoing, so it is worth asserting rather than assuming.
    /// </remarks>
    [Fact]
    public void TheContextMenuIsBoundToTheShell()
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using EditorSession session = SessionIn(directory);
            using MainWindow window = new(session);

            EditorContextMenu menu = CanvasMenu(window);

            Assert.NotNull(menu.DataContext);

            // Nothing is selected, so the selection actions must be refused -
            // which they cannot be unless the bindings resolved.
            Assert.False(ContextItem(menu, "Cut").IsEnabled);
            Assert.False(ContextItem(menu, "Delete").IsEnabled);
        });
    }

    /// <summary>
    /// The canvas and the element list get their own instance, because a
    /// <see cref="ContextMenu"/> belongs to exactly one control.
    /// </summary>
    [Fact]
    public void EachHostHasItsOwnContextMenu()
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using EditorSession session = SessionIn(directory);
            using MainWindow window = new(session);

            IReadOnlyList<EditorContextMenu> menus = window.ContextMenus;

            Assert.Equal(2, menus.Count);
            Assert.NotSame(menus[0], menus[1]);
        });
    }
}
