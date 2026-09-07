using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;

using GumpStudio.App;
using GumpStudio.TestSupport;

using Xunit;

namespace GumpStudio.App.Tests;

/// <summary>
/// That every shortcut the menus advertise is one the window actually listens
/// for.
/// </summary>
/// <remarks>
/// <c>InputGesture</c> on a <see cref="MenuItem"/> only <em>draws</em> the
/// shortcut beside the label — it binds nothing. Phase 9 discovered that every
/// gesture in the menu bar was decorative and bound them all by hand in
/// <c>BindShortcuts</c>, and the zoom items added later reintroduced exactly the
/// same defect: three painted labels with no binding behind them, so the menu
/// worked and the keys did nothing.
///
/// This is the guard for the whole class. A painted gesture with no binding is a
/// promise the application does not keep, and nothing else would catch it.
/// </remarks>
[Collection("Headless")]
public class ShortcutBindingTests
{
    private static EditorSession SessionIn(TempDirectory directory) =>
        new(AppSettings.Load(Path.Combine(directory.Path, "settings.json")));

    /// <summary>Every gesture painted on a menu item, with the item's name.</summary>
    private static List<(string Name, KeyGesture Gesture)> PaintedGestures(MainWindow window) =>
        [.. window.GetLogicalDescendants()
            .OfType<MenuItem>()
            .Where(item => item.InputGesture is not null)
            .Select(item => (item.Name ?? item.Header?.ToString() ?? "?", item.InputGesture!))];

    private static List<KeyGesture> BoundGestures(MainWindow window) =>
        [.. window.KeyBindings.Select(binding => binding.Gesture).OfType<KeyGesture>()];

    [Fact]
    public void EveryGestureTheMenusAdvertiseIsBound()
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using EditorSession session = SessionIn(directory);
            using MainWindow window = new(session);

            List<(string Name, KeyGesture Gesture)> painted = PaintedGestures(window);
            List<KeyGesture> bound = BoundGestures(window);

            // If this is empty the menus were not found at all and the test is
            // proving nothing.
            Assert.NotEmpty(painted);

            List<string> decorative =
            [
                .. painted
                    .Where(p => !bound.Contains(p.Gesture))
                    .Select(p => $"{p.Name} paints {p.Gesture} with no binding"),
            ];

            Assert.Empty(decorative);
        });
    }

    /// <summary>
    /// The zoom gestures specifically, since these are the ones that shipped
    /// decorative.
    /// </summary>
    [Theory]
    [InlineData("Ctrl+OemPlus")]
    [InlineData("Ctrl+OemMinus")]
    [InlineData("Ctrl+D0")]
    public void TheZoomGesturesAreBound(string gesture)
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using EditorSession session = SessionIn(directory);
            using MainWindow window = new(session);

            Assert.Contains(KeyGesture.Parse(gesture), BoundGestures(window));
        });
    }

    /// <summary>
    /// The keys people actually press, which differ from the printed label:
    /// zooming in is Ctrl and the <c>+</c> key, which is a shifted
    /// <c>OemPlus</c>, and the numeric keypad has its own key codes entirely.
    /// </summary>
    [Theory]
    [InlineData("Ctrl+Shift+OemPlus")]
    [InlineData("Ctrl+Add")]
    [InlineData("Ctrl+Subtract")]
    [InlineData("Ctrl+NumPad0")]
    public void TheAlternativeZoomGesturesAreBoundToo(string gesture)
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using EditorSession session = SessionIn(directory);
            using MainWindow window = new(session);

            Assert.Contains(KeyGesture.Parse(gesture), BoundGestures(window));
        });
    }

    [Fact]
    public void NoGestureIsBoundTwice()
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using EditorSession session = SessionIn(directory);
            using MainWindow window = new(session);

            List<KeyGesture> bound = BoundGestures(window);

            // Two bindings for one gesture both fire, so an accidental duplicate
            // runs its action twice.
            List<string> duplicates =
            [
                .. bound
                    .GroupBy(g => g)
                    .Where(group => group.Count() > 1)
                    .Select(group => $"{group.Key} bound {group.Count()} times"),
            ];

            Assert.Empty(duplicates);
        });
    }
}
