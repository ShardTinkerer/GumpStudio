using Avalonia.Controls;
using Avalonia.Interactivity;

using GumpStudio.Core.Elements;
using GumpStudio.TestSupport;
using GumpStudio.Uo.Data;

using Xunit;

namespace GumpStudio.App.Tests;

/// <summary>
/// Getting a chosen cliloc from the browser into the property that asked for it.
/// </summary>
/// <remarks>
/// Through the real window, because the hand-off is spread across the browse
/// button, the panel and the property panel, and the failures worth catching are
/// the ones where a target goes stale between them.
/// </remarks>
[Collection("Headless")]
public class ClilocHandoffTests
{
    private static readonly ClilocEntry[] Strings =
    [
        new(1044017, ClilocEntryKind.Original, "a sturdy pickaxe"),
        new(1062724, ClilocEntryKind.Original, "the vendor price"),
    ];

    private static EditorSession SessionIn(TempDirectory directory) =>
        new(AppSettings.Load(Path.Combine(directory.Path, "settings.json")));

    /// <summary>
    /// Adds an element from the toolbox and returns it, selected.
    /// </summary>
    /// <remarks>
    /// Through the toolbox rather than the page directly, because that is the
    /// path which also selects it and rebuilds the panels — the canvas raises
    /// its own selection event from pointer input, which a headless test cannot
    /// produce.
    /// </remarks>
    private static T Add<T>(MainWindow window, EditorSession session, string tool)
        where T : Element
    {
        ((IEnumerable<Button>)window.Toolbox.ItemsSource!)
            .First(b => (string?)b.Content == tool)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        return (T)session.Canvas.Selection[0];
    }

    /// <summary>The editor row built for one named property.</summary>
    private static Grid Row(MainWindow window, string name) =>
        window.Properties.Children
            .OfType<Grid>()
            .First(g => g.Children.OfType<TextBlock>().Any(t => t.Text == name));

    private static Grid Editor(MainWindow window, string rowName) =>
        Row(window, rowName).Children.OfType<Grid>().Single();

    private static Button BrowseButton(MainWindow window, string rowName) =>
        Editor(window, rowName).Children.OfType<Button>().Single();

    private static TextBox Field(MainWindow window, string rowName) =>
        Editor(window, rowName).Children.OfType<TextBox>().Single();

    /// <summary>Switches an HTML area to a localised string, the way the UI does.</summary>
    private static void MakeLocalized(MainWindow window) =>
        Row(window, "Content").Children.OfType<ComboBox>().Single().SelectedItem =
            nameof(HtmlContentKind.Localized);

    private static void Choose(MainWindow window, int id)
    {
        window.Cliloc.Load(Strings, [], null);
        window.Cliloc.SeedFilter(id);
        window.Cliloc.Apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    /// <summary>
    /// A choice goes to the field whose browse button asked for it.
    /// </summary>
    /// <remarks>
    /// A label is used deliberately: its only cliloc row is the tooltip, so the
    /// localised-area fallback cannot be what applied this.
    /// </remarks>
    [Fact]
    public void AChoiceGoesToTheFieldThatAskedForIt()
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using EditorSession session = SessionIn(directory);
            using MainWindow window = new(session);

            LabelElement label = Add<LabelElement>(window, session, "Label");

            BrowseButton(window, "Tooltip cliloc").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Choose(window, 1062724);

            Assert.Equal(1062724, label.TooltipClilocId);

            // Through a command, like every other edit.
            session.History.Undo();

            Assert.Equal(0, label.TooltipClilocId);
        });
    }

    /// <summary>A localised area is the one field guessed at with no browse click.</summary>
    [Fact]
    public void ALocalisedAreaIsTheOnlyImplicitTarget()
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using EditorSession session = SessionIn(directory);
            using MainWindow window = new(session);

            HtmlElement html = Add<HtmlElement>(window, session, "HTML");

            MakeLocalized(window);

            Assert.True(window.Cliloc.Apply.IsEnabled);

            Choose(window, 1044017);

            Assert.Equal(1044017, html.ClilocId);
        });
    }

    [Fact]
    public void NothingIsGuessedForAnElementThatOnlyHasATooltipCliloc()
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using EditorSession session = SessionIn(directory);
            using MainWindow window = new(session);

            Add<LabelElement>(window, session, "Label");

            Assert.False(window.Cliloc.Apply.IsEnabled);
        });
    }

    /// <summary>Plain markup is not a cliloc, so it is not guessed at either.</summary>
    [Fact]
    public void NothingIsGuessedForAnAreaShowingMarkup()
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using EditorSession session = SessionIn(directory);
            using MainWindow window = new(session);

            Add<HtmlElement>(window, session, "HTML");

            Assert.False(window.Cliloc.Apply.IsEnabled);
        });
    }

    /// <summary>
    /// A pending target does not survive selecting something else.
    /// </summary>
    /// <remarks>
    /// The row a browse button named belongs to one element, and the property
    /// panel it lives in is rebuilt on every selection change.
    /// </remarks>
    [Fact]
    public void ChangingTheSelectionDropsAPendingTarget()
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using EditorSession session = SessionIn(directory);
            using MainWindow window = new(session);

            LabelElement first = Add<LabelElement>(window, session, "Label");

            BrowseButton(window, "Tooltip cliloc").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            LabelElement second = Add<LabelElement>(window, session, "Label");

            Choose(window, 1062724);

            Assert.Equal(0, first.TooltipClilocId);
            Assert.Equal(0, second.TooltipClilocId);
        });
    }

    /// <summary>
    /// The hover card is built as the tooltip opens, not when the row is.
    /// </summary>
    /// <remarks>
    /// This pins framework behaviour rather than ours: Avalonia raises no
    /// opening event for a control with no tip set, and swapping the tip inside
    /// the handler is what makes the card lazy. If an upgrade changes either,
    /// this fails instead of the tooltip quietly reverting to a bare id.
    /// </remarks>
    [Fact]
    public void TheRichCardIsBuiltAsTheTooltipOpens()
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using EditorSession session = SessionIn(directory);
            using MainWindow window = new(session);

            Add<LabelElement>(window, session, "Label");

            TextBox field = Field(window, "Tooltip cliloc");

            Assert.IsType<string>(ToolTip.GetTip(field));

            field.RaiseEvent(new CancelRoutedEventArgs(ToolTip.ToolTipOpeningEvent));

            Assert.IsAssignableFrom<Control>(ToolTip.GetTip(field));
        });
    }

    /// <summary>The browse button has nothing to show without a client.</summary>
    [Fact]
    public void TheBrowseButtonIsDisabledWithNoClient()
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using EditorSession session = SessionIn(directory);
            using MainWindow window = new(session);

            Add<LabelElement>(window, session, "Label");

            Assert.False(BrowseButton(window, "Tooltip cliloc").IsEnabled);
        });
    }
}
