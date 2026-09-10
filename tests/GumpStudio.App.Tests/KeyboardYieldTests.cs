using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;

using GumpStudio.App;
using GumpStudio.Core.Commands;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Primitives;
using GumpStudio.TestSupport;

using Xunit;

namespace GumpStudio.App.Tests;

/// <summary>
/// Which shortcuts step aside while text is being edited.
/// </summary>
/// <remarks>
/// An Avalonia 12 window <c>KeyBinding</c> fires <em>even over a key the focused
/// control has already marked handled</em>, so a shortcut cannot rely on a
/// <see cref="TextBox"/> swallowing its keystroke. Worse, a binding marks the key
/// handled whenever it <em>executes</em>, so a command that runs and does nothing
/// still eats the keystroke — which is why yielding is expressed as
/// <c>CanExecute</c> rather than as an early return, and why Ctrl+C in a
/// property field once copied nothing at all instead of copying the element.
///
/// That behaviour was worked out by hand and never committed as a test. These
/// drive real keystrokes through the headless window, so the table is pinned to
/// what the application does rather than to how it is written.
/// </remarks>
[Collection("Headless")]
public class KeyboardYieldTests
{
    private static EditorSession SessionIn(TempDirectory directory) =>
        new(AppSettings.Load(Path.Combine(directory.Path, "settings.json")));

    private static LabelElement Label(string text) =>
        new() { Text = text, Location = new GumpPoint(3, 4) };

    /// <summary>A window with two elements on its page, shown and focusable.</summary>
    private static MainWindow Shown(EditorSession session)
    {
        MainWindow window = new(session);

        session.ActivePage.Root.Add(Label("one"));
        session.ActivePage.Root.Add(Label("two"));

        window.Show();

        return window;
    }

    /// <summary>Puts the keyboard in a text box, as editing a property does.</summary>
    private static TextBox FocusATextBox(MainWindow window)
    {
        TextBox box = new();

        // Parented into the window so it can take focus, but the control itself
        // is what matters: IsEditingText only asks what kind of thing has the
        // keyboard.
        if (window.GetLogicalDescendants().OfType<Panel>().FirstOrDefault() is { } host)
        {
            host.Children.Add(box);
        }

        box.Focus();

        return box;
    }

    /// <summary>
    /// Arranges for every selection- and history-dependent action to be
    /// available.
    /// </summary>
    /// <remarks>
    /// The two theories below ask a binding whether it <em>would</em> run, and
    /// that is now a real question. Every action is defined once and bound to the
    /// menu bar, the context menu and the keyboard alike, so a shortcut answers
    /// with the same <c>CanExecute</c> the greyed-out menu item does: Ctrl+C with
    /// nothing selected declines, and the keystroke goes to whatever should have
    /// had it.
    ///
    /// Before that, a shortcut's only condition was where the keyboard was, so
    /// these theories could ask an empty document. They set the preconditions up
    /// instead, which leaves what they are actually testing - that a yielding
    /// gesture steps aside for a text box, and an ignoring one does not - exactly
    /// as it was.
    /// </remarks>
    private static void MakeEveryActionAvailable(EditorSession session)
    {
        // Two commands and one undo, so undo and redo are both possible.
        session.Apply(new AddElementCommand(session.ActivePage.Root, Label("three")));
        session.Apply(new AddElementCommand(session.ActivePage.Root, Label("four")));
        session.History.Undo();

        // More than one element, which grouping and aligning need.
        session.Canvas.SelectAll();
    }

    private static void Press(MainWindow window, Key key, RawInputModifiers modifiers)
    {
        window.KeyPress(key, modifiers, PhysicalKey.None, string.Empty);
        window.KeyRelease(key, modifiers, PhysicalKey.None, string.Empty);
    }

    [Fact]
    public void CtrlAOnTheCanvasSelectsEverything()
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using EditorSession session = SessionIn(directory);
            using MainWindow window = Shown(session);

            Press(window, Key.A, RawInputModifiers.Control);

            Assert.Equal(2, session.Canvas.Selection.Count);

            window.Close();
        });
    }

    [Fact]
    public void CtrlAInATextBoxLeavesTheSelectionAlone()
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using EditorSession session = SessionIn(directory);
            using MainWindow window = Shown(session);

            FocusATextBox(window);

            Press(window, Key.A, RawInputModifiers.Control);

            // The text box owns this key: selecting all of its text is what the
            // user meant, not selecting every element on the page.
            Assert.Empty(session.Canvas.Selection);

            window.Close();
        });
    }

    [Fact]
    public void DeleteOnTheCanvasRemovesTheSelection()
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using EditorSession session = SessionIn(directory);
            using MainWindow window = Shown(session);

            session.Canvas.SelectAll();

            Press(window, Key.Delete, RawInputModifiers.None);

            Assert.Empty(session.ActivePage.Root.Children);

            window.Close();
        });
    }

    [Fact]
    public void DeleteInATextBoxDoesNotDeleteElements()
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using EditorSession session = SessionIn(directory);
            using MainWindow window = Shown(session);

            session.Canvas.SelectAll();
            FocusATextBox(window);

            Press(window, Key.Delete, RawInputModifiers.None);

            Assert.Equal(2, session.ActivePage.Root.Children.Count);

            window.Close();
        });
    }

    [Fact]
    public void CtrlZOnTheCanvasUndoes()
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using EditorSession session = SessionIn(directory);
            using MainWindow window = Shown(session);

            session.Apply(new AddElementCommand(session.ActivePage.Root, Label("three")));

            Assert.Equal(3, session.ActivePage.Root.Children.Count);

            Press(window, Key.Z, RawInputModifiers.Control);

            Assert.Equal(2, session.ActivePage.Root.Children.Count);

            window.Close();
        });
    }

    [Fact]
    public void CtrlZInATextBoxDoesNotUndoTheDocument()
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using EditorSession session = SessionIn(directory);
            using MainWindow window = Shown(session);

            session.Apply(new AddElementCommand(session.ActivePage.Root, Label("three")));

            FocusATextBox(window);

            Press(window, Key.Z, RawInputModifiers.Control);

            // Undo belongs to the text box while it has the caret.
            Assert.Equal(3, session.ActivePage.Root.Children.Count);

            window.Close();
        });
    }

    /// <summary>
    /// The whole yield table, read off the bindings.
    /// </summary>
    /// <remarks>
    /// Asserted through <c>CanExecute</c> as well as by keystroke, because the
    /// clipboard gestures cannot be driven headlessly and this is the mechanism
    /// that makes them yield.
    /// </remarks>
    [Theory]
    [InlineData("Ctrl+C")]
    [InlineData("Ctrl+X")]
    [InlineData("Ctrl+V")]
    [InlineData("Ctrl+A")]
    [InlineData("Ctrl+Z")]
    [InlineData("Ctrl+Y")]
    [InlineData("Ctrl+Shift+Z")]
    [InlineData("Delete")]
    public void AGestureThatYieldsRefusesToRunWhileTextIsEdited(string gesture)
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using EditorSession session = SessionIn(directory);
            using MainWindow window = Shown(session);

            MakeEveryActionAvailable(session);

            KeyBinding binding = Binding(window, gesture);

            Assert.True(binding.Command.CanExecute(null));

            FocusATextBox(window);

            // Refusing to execute is what lets the key reach the text box. A
            // command that ran and did nothing would still swallow it.
            Assert.False(binding.Command.CanExecute(null));

            window.Close();
        });
    }

    [Theory]
    [InlineData("Ctrl+N")]
    [InlineData("Ctrl+O")]
    [InlineData("Ctrl+S")]
    [InlineData("Ctrl+G")]
    [InlineData("Ctrl+OemPlus")]
    [InlineData("Ctrl+OemMinus")]
    [InlineData("Ctrl+D0")]
    public void AGestureThatIgnoresTextRunsWhereverTheKeyboardIs(string gesture)
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using EditorSession session = SessionIn(directory);
            using MainWindow window = Shown(session);

            MakeEveryActionAvailable(session);

            KeyBinding binding = Binding(window, gesture);

            Assert.True(binding.Command.CanExecute(null));

            FocusATextBox(window);

            Assert.True(binding.Command.CanExecute(null));

            window.Close();
        });
    }

    /// <summary>
    /// The shortcut and the menu item for one action must agree about whether
    /// the document is safe.
    /// </summary>
    /// <remarks>
    /// They did not: File ▸ New asked before discarding unsaved work and Ctrl+N
    /// called straight through to <c>NewDocument</c>.
    /// </remarks>
    [Fact]
    public void CtrlNGoesThroughTheSameUnsavedChangesGuardAsTheMenu()
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using EditorSession session = SessionIn(directory);
            using MainWindow window = Shown(session);

            session.Apply(new AddElementCommand(session.ActivePage.Root, Label("unsaved")));

            Assert.True(session.IsModified);

            Press(window, Key.N, RawInputModifiers.Control);

            // The confirmation is modal and nothing answered it, so the document
            // must still be here rather than silently replaced.
            Assert.True(session.IsModified);
            Assert.Equal(3, session.ActivePage.Root.Children.Count);

            window.Close();
        });
    }

    private static KeyBinding Binding(MainWindow window, string gesture)
    {
        KeyGesture parsed = KeyGesture.Parse(gesture);
        KeyBinding? found = window.KeyBindings.FirstOrDefault(b => Equals(b.Gesture, parsed));

        Assert.NotNull(found);

        return found!;
    }
}
