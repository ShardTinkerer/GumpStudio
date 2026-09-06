using GumpStudio.Core.Elements;
using GumpStudio.Core.Layout;
using GumpStudio.Core.Primitives;

using Xunit;

namespace GumpStudio.Core.Tests;

/// <summary>
/// The importer against a gump genuinely captured off the wire.
/// </summary>
/// <remarks>
/// <c>Captures/wire-capture.txt</c> is a real dump of the classic client's
/// character-profile gump, not something this application produced. It is the
/// only evidence available of what such a file actually looks like, and it
/// carries several shapes a tidy exporter never emits: a page opened five
/// separate times, page numbers that skip from 2 to 9, a doubled space after a
/// command name, an argument list with no closing delimiter, and a text block
/// holding one empty entry while the commands index nothing at all.
/// </remarks>
public class WireCaptureTests
{
    private static string CaptureText =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Captures", "wire-capture.txt"));

    [Fact]
    public void TheCaptureIsRecognisedAsALayout() =>
        Assert.True(LayoutStringParser.LooksLikeLayout(CaptureText));

    [Fact]
    public void EveryCommandInTheCaptureIsUnderstood()
    {
        LayoutParseResult result = LayoutStringParser.Parse(CaptureText);

        Assert.Empty(result.Warnings);
        Assert.False(result.IsEmpty);
    }

    [Fact]
    public void ReadsTheHeaderIdAndPosition()
    {
        LayoutImportResult result = GumpLayoutReader.Import(CaptureText);

        Assert.Equal(0x1CC, result.Document.Properties.TypeId);
        Assert.Equal(new GumpPoint(50, 50), result.Document.Properties.Location);
    }

    /// <summary>
    /// The capture uses pages 0, 1, 2, 9 and 10, and reopens page 0 five times.
    /// </summary>
    /// <remarks>
    /// Pages are a contiguous list here, so 3 to 8 are created empty. That keeps
    /// every page button pointing where the server meant it to.
    /// </remarks>
    [Fact]
    public void MergesTheRepeatedPagesAndFillsTheGaps()
    {
        LayoutImportResult result = GumpLayoutReader.Import(CaptureText);

        Assert.Equal(11, result.Document.PageCount);

        Assert.NotEmpty(result.Document.Pages[0].Leaves());
        Assert.NotEmpty(result.Document.Pages[1].Leaves());
        Assert.NotEmpty(result.Document.Pages[2].Leaves());
        Assert.Empty(result.Document.Pages[5].Leaves());
        Assert.NotEmpty(result.Document.Pages[9].Leaves());
        Assert.NotEmpty(result.Document.Pages[10].Leaves());
    }

    [Fact]
    public void ProducesTheElementsTheCaptureDescribes()
    {
        LayoutImportResult result = GumpLayoutReader.Import(CaptureText);

        List<Element> all = [.. result.Document.Pages.SelectMany(p => p.Leaves())];

        Assert.Single(all.OfType<BackgroundElement>());
        Assert.Single(all.OfType<AlphaElement>());
        Assert.Equal(6, all.OfType<TiledElement>().Count());
        Assert.NotEmpty(all.OfType<ButtonElement>());
        Assert.All(all.OfType<HtmlElement>(), h => Assert.Equal(HtmlContentKind.Localized, h.ContentKind));
    }

    /// <summary>The first background is the window frame, at the size given.</summary>
    [Fact]
    public void ReadsPositionsAndSizes()
    {
        LayoutImportResult result = GumpLayoutReader.Import(CaptureText);

        BackgroundElement frame = result.Document.Pages[0].Leaves().OfType<BackgroundElement>().First();

        Assert.Equal(5054, frame.GumpId);
        Assert.Equal(new GumpPoint(0, 0), frame.Location);
        Assert.Equal(new GumpSize(530, 497), frame.Size);
    }

    /// <summary>
    /// The capture's page buttons switch page; its reply buttons carry an id.
    /// </summary>
    [Fact]
    public void DistinguishesPageButtonsFromReplyButtons()
    {
        LayoutImportResult result = GumpLayoutReader.Import(CaptureText);

        List<ButtonElement> buttons =
            [.. result.Document.Pages.SelectMany(p => p.Leaves()).OfType<ButtonElement>()];

        // { button 15 382 4005 4007 0 10 0 } — quit 0, so it opens page 10.
        Assert.Contains(buttons, b => b.Kind == ButtonKind.Page && b.Param == 10);

        // { button 270 442 4005 4007 1 0 1999 } — quit 1, so 1999 is its reply id.
        Assert.Contains(buttons, b => b.Kind == ButtonKind.Reply && b.Param == 1999);
    }

    /// <summary>
    /// One argument list in the capture is <c>@0@10</c>, with no closing
    /// delimiter and two values.
    /// </summary>
    [Fact]
    public void ReadsTheTokenArgumentsTheCaptureCarries()
    {
        LayoutImportResult result = GumpLayoutReader.Import(CaptureText);

        List<HtmlElement> html =
            [.. result.Document.Pages.SelectMany(p => p.Leaves()).OfType<HtmlElement>()];

        Assert.Contains(html, h => h.ClilocId == 1079443 && h.Arguments == "0@10");
        Assert.Contains(html, h => h.ClilocId == 1114057 && h.Arguments == "#1027027");

        // xmfhtmlgumpcolor carries a colour and no arguments.
        Assert.Contains(html, h => h.ClilocId == 1044002 && h.Color == 16777215 && h.Arguments.Length == 0);
    }

    /// <summary>
    /// The capture's text block holds one empty entry and nothing indexes it.
    /// </summary>
    /// <remarks>
    /// Which is the ordinary case for a gump built entirely from clilocs, and the
    /// reason a missing or truncated text block must not fail an import.
    /// </remarks>
    [Fact]
    public void ImportsCleanlyDespiteAnEmptyTextBlock()
    {
        LayoutImportResult result = GumpLayoutReader.Import(CaptureText);

        Assert.Empty(result.Warnings);
    }

    /// <summary>
    /// Re-exporting an imported capture reproduces the commands it came from.
    /// </summary>
    /// <remarks>
    /// Not the file byte for byte — the capture's own spacing is irregular and
    /// its repeated <c>page</c> commands collapse — but every element command,
    /// in order, with the same values.
    /// </remarks>
    [Fact]
    public void ReExportsTheSameElementCommands()
    {
        LayoutImportResult imported = GumpLayoutReader.Import(CaptureText);

        List<string> original = ElementCommands(LayoutStringParser.Parse(CaptureText).Layout);
        List<string> exported = ElementCommands(GumpLayoutBuilder.Build(imported.Document));

        Assert.Equal(original.Order(StringComparer.Ordinal), exported.Order(StringComparer.Ordinal));
    }

    /// <summary>Everything except the page and group structure, which is rebuilt.</summary>
    private static List<string> ElementCommands(GumpLayout layout) =>
    [
        .. layout.Commands
            .Where(c => c is not (PageCommand or GroupCommand or EndGroupCommand))
            .Select(c => LayoutStringWriter.Format(
                c, new LayoutStringOptions(), LayoutStringWriter.ByIndex))
            .OfType<string>(),
    ];
}
