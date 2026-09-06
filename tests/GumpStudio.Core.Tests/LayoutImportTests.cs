using GumpStudio.Core.Document;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Layout;
using GumpStudio.Core.Primitives;
using GumpStudio.TestSupport;

using Xunit;

namespace GumpStudio.Core.Tests;

/// <summary>
/// Reading the client's layout text back into a document.
/// </summary>
/// <remarks>
/// The important case is a capture taken off the wire, which is not written by
/// this application and is not obliged to be tidy: irregular spacing, a repeated
/// page, a missing text block, and an argument list whose closing delimiter is
/// absent all appear in the real one checked in beside these tests.
/// </remarks>
public class LayoutImportTests
{
    [Fact]
    public void ParsesABracedCommand()
    {
        LayoutParseResult result = LayoutStringParser.Parse("{ resizepic 10 20 5054 300 200 }");

        Assert.Empty(result.Warnings);

        ResizePicCommand command = Assert.IsType<ResizePicCommand>(Assert.Single(result.Layout.Commands));

        Assert.Equal((10, 20, 5054, 300, 200), (command.X, command.Y, command.GumpId, command.Width, command.Height));
    }

    /// <summary>A capture that has had its braces stripped is still readable.</summary>
    [Fact]
    public void ParsesACommandWithoutBraces()
    {
        LayoutParseResult result = LayoutStringParser.Parse("resizepic 10 20 5054 300 200");

        Assert.IsType<ResizePicCommand>(Assert.Single(result.Layout.Commands));
    }

    [Fact]
    public void ParsesSeveralCommandsOnOneLine()
    {
        LayoutParseResult result = LayoutStringParser.Parse("{ page 0 }{ tilepic 1 2 3 }");

        Assert.Equal(2, result.Layout.Commands.Count);
    }

    /// <summary>The real capture has a double space after the command name.</summary>
    [Fact]
    public void ToleratesRunsOfWhitespace()
    {
        LayoutParseResult result =
            LayoutStringParser.Parse("{ xmfhtmlgumpcolor  10 302 150 25 1044012 0 0 16777215 }");

        XmfHtmlCommand command = Assert.IsType<XmfHtmlCommand>(Assert.Single(result.Layout.Commands));

        Assert.Equal(1044012, command.ClilocId);
        Assert.Equal(16777215, command.Color);
    }

    /// <summary>
    /// Its slots are not the colour form's: the flags precede the colour and the
    /// cliloc id comes last.
    /// </summary>
    [Fact]
    public void ReadsTheTokenFormsSlotsInTheRightOrder()
    {
        LayoutParseResult result = LayoutStringParser.Parse(
            "{ xmfhtmltok 255 63 220 18 0 0 16777215 1114057 @#1027027 }");

        XmfHtmlCommand command = Assert.IsType<XmfHtmlCommand>(Assert.Single(result.Layout.Commands));

        Assert.Equal(1114057, command.ClilocId);
        Assert.Equal(16777215, command.Color);
        Assert.Equal("#1027027", command.Arguments);
    }

    /// <summary>Captures are inconsistent about the closing delimiter.</summary>
    [Theory]
    [InlineData("@0@10", "0@10")]
    [InlineData("@0@10@", "0@10")]
    [InlineData("@#1027027", "#1027027")]
    public void ReadsArgumentsWithOrWithoutAClosingDelimiter(string written, string expected)
    {
        LayoutParseResult result = LayoutStringParser.Parse(
            $"{{ xmfhtmltok 1 2 3 4 0 0 0 1000 {written} }}");

        Assert.Equal(expected, Assert.IsType<XmfHtmlCommand>(result.Layout.Commands[0]).Arguments);
    }

    /// <summary>
    /// Quit decides the kind; a page button carries its target in the page slot
    /// and a reply button its id in the return slot.
    /// </summary>
    [Theory]
    [InlineData("button 15 382 4005 4007 0 10 0", ButtonKind.Page, 10)]
    [InlineData("button 270 442 4005 4007 1 0 1999", ButtonKind.Reply, 1999)]
    public void ReadsBothButtonKinds(string line, ButtonKind kind, int param)
    {
        ButtonCommand command = Assert.IsType<ButtonCommand>(
            LayoutStringParser.Parse(line).Layout.Commands[0]);

        Assert.Equal(kind, command.Kind);
        Assert.Equal(param, command.Param);
    }

    [Fact]
    public void ReadsTheTextBlock()
    {
        LayoutImportResult result = GumpLayoutReader.Import(
            """
            [layout]
            { text 10 20 88 0 }
            { text 10 40 88 1 }

            [text]
            { Hello }
            { World }
            """);

        Assert.Empty(result.Warnings);
        Assert.Equal(
            ["Hello", "World"],
            result.Document.Pages[0].Leaves().OfType<LabelElement>().Select(l => l.Text));
    }

    /// <summary>A truncated capture is common, and is not a reason to refuse it.</summary>
    [Fact]
    public void ReportsATextIndexWithNoTextBlock()
    {
        LayoutImportResult result = GumpLayoutReader.Import("{ text 10 20 88 3 }");

        Assert.Equal(string.Empty, Assert.Single(result.Document.Pages[0].Leaves().OfType<LabelElement>()).Text);
        Assert.Contains(result.Warnings, w => w.Contains("Text 3", StringComparison.Ordinal));
    }

    [Fact]
    public void AttachesATooltipToTheElementBeforeIt()
    {
        LayoutImportResult result = GumpLayoutReader.Import(
            "{ tilepic 5 6 3821 }{ tooltip 1011036 @Bob@42 }{ itemproperty 12345 }");

        Element element = Assert.Single(result.Document.Pages[0].Leaves());

        Assert.Equal(1011036, element.TooltipClilocId);
        Assert.Equal("Bob@42", element.TooltipArguments);
        Assert.Equal(12345, element.ItemPropertySerial);
    }

    [Fact]
    public void ReportsATooltipWithNothingToAttachTo()
    {
        LayoutImportResult result = GumpLayoutReader.Import("{ tooltip 1011036 }");

        Assert.Contains(result.Warnings, w => w.Contains("no element before it", StringComparison.Ordinal));
    }

    /// <summary>A group applies to the radios after it until the page or an endgroup.</summary>
    [Fact]
    public void AppliesTheOpenGroupToRadios()
    {
        LayoutImportResult result = GumpLayoutReader.Import(
            """
            { group 3 }
            { radio 10 10 208 209 0 1 }
            { radio 10 30 208 209 1 2 }
            { endgroup }
            { radio 10 50 208 209 0 3 }
            """);

        Assert.Equal(
            [3, 3, 0],
            result.Document.Pages[0].Leaves().OfType<RadioElement>().Select(r => r.GroupId));
    }

    /// <summary>
    /// The same page is opened more than once in real captures, and the numbers
    /// used are sparse.
    /// </summary>
    [Fact]
    public void MergesRepeatedPagesAndFillsTheGaps()
    {
        LayoutImportResult result = GumpLayoutReader.Import(
            """
            { page 0 }
            { tilepic 1 1 100 }
            { page 3 }
            { tilepic 2 2 200 }
            { page 0 }
            { tilepic 3 3 300 }
            """);

        Assert.Equal(4, result.Document.PageCount);
        Assert.Equal(2, result.Document.Pages[0].Leaves().Count());
        Assert.Single(result.Document.Pages[3].Leaves());
        Assert.Empty(result.Document.Pages[1].Leaves());
    }

    [Fact]
    public void RefusesAnAbsurdPageNumber()
    {
        LayoutImportResult result = GumpLayoutReader.Import(
            $"{{ page {GumpLayoutReader.MaxPageNumber + 1} }}{{ tilepic 1 1 100 }}");

        Assert.Single(result.Document.Pages[0].Leaves());
        Assert.Contains(result.Warnings, w => w.Contains("outside", StringComparison.Ordinal));
    }

    [Fact]
    public void ReadsTheGumpPropertyCommands()
    {
        LayoutImportResult result = GumpLayoutReader.Import(
            "{ nomove }{ noclose }{ nodispose }{ mastergump 7 }{ toggleupperwordcase }{ echandleinput }");

        GumpProperties properties = result.Document.Properties;

        Assert.False(properties.Movable);
        Assert.False(properties.Closable);
        Assert.False(properties.Disposable);
        Assert.Equal(7, properties.MasterGumpId);
        Assert.True(properties.UpperWordCase);
        Assert.True(properties.EnhancedClientInput);
    }

    /// <summary>The header a capture tool writes carries the id and the position.</summary>
    [Fact]
    public void ReadsTheHeaderComment()
    {
        LayoutImportResult result = GumpLayoutReader.Import(
            "// Gump 0x1CC at (50, 60) — serial 043CAD798\n{ tilepic 1 1 100 }");

        Assert.Equal(new GumpPoint(50, 60), result.Document.Properties.Location);
        Assert.Equal(0x1CC, result.Document.Properties.TypeId);
    }

    [Fact]
    public void ReportsAnUnknownCommandAndKeepsGoing()
    {
        LayoutImportResult result = GumpLayoutReader.Import("{ notacommand 1 2 }{ tilepic 1 1 100 }");

        Assert.Single(result.Document.Pages[0].Leaves());
        Assert.Contains(result.Warnings, w => w.Contains("notacommand", StringComparison.Ordinal));
    }

    [Fact]
    public void ReportsACommandWithTooFewArguments()
    {
        LayoutImportResult result = GumpLayoutReader.Import("{ resizepic 1 2 }");

        Assert.Empty(result.Document.Pages[0].Leaves());
        Assert.Contains(result.Warnings, w => w.Contains("too few", StringComparison.Ordinal));
    }

    /// <summary>
    /// The Enhanced Client has its own commands, which the classic one ignores.
    /// </summary>
    /// <remarks>
    /// A capture taken from an EC session carries several, all with a cliloc of
    /// -1 and no geometry. Naming them beats calling them unknown, because there
    /// is nothing wrong with the capture.
    /// </remarks>
    [Fact]
    public void NamesTheEnhancedClientCommandsItIgnores()
    {
        LayoutImportResult result = GumpLayoutReader.Import(
            "{ kr_xmfhtmlgump 0 0 0 0 -1 0 0 }{ tilepic 1 1 100 }");

        Assert.Single(result.Document.Pages[0].Leaves());
        Assert.Contains(result.Warnings, w => w.Contains("Enhanced Client", StringComparison.Ordinal));
    }

    /// <summary>
    /// One warning per distinct problem, however many lines had it.
    /// </summary>
    /// <remarks>
    /// A real capture carries the same unusable command dozens of times, and a
    /// list of forty near-identical lines buries the one that matters.
    /// </remarks>
    [Fact]
    public void CollapsesRepeatsOfTheSameProblem()
    {
        LayoutImportResult result = GumpLayoutReader.Import(
            string.Join(
                Environment.NewLine,
                Enumerable.Repeat("{ kr_xmfhtmlgump 0 0 0 0 -1 0 0 }", 40)));

        string warning = Assert.Single(result.Warnings);

        Assert.Contains("39 more", warning, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{ resizepic 0 0 5054 530 497 }\n{ tilepic 1 1 2 }", true)]
    [InlineData("[layout]", true)]
    [InlineData("public class Foo { public int X { get; set; } }", false)]
    [InlineData("", false)]
    [InlineData("just some prose about gumps", false)]
    public void DetectsWhetherTextIsALayout(string text, bool expected) =>
        Assert.Equal(expected, LayoutStringParser.LooksLikeLayout(text));

    /// <summary>
    /// Importing and exporting are inverses.
    /// </summary>
    /// <remarks>
    /// The strongest statement available about the pair: writing a document as
    /// layout text, reading it back and writing it again must produce the same
    /// text. Group elements are the one thing that cannot survive — the client
    /// has no notion of one — but they only affect an element's absolute
    /// position, which the first write has already resolved.
    /// </remarks>
    [Fact]
    public void RoundTripsThroughLayoutText()
    {
        GumpLayout original = GumpLayoutBuilder.Build(SampleDocuments.Full());
        string written = string.Join('\n', Write(original));

        LayoutImportResult imported = GumpLayoutReader.Import(WithTextBlock(original, written));

        Assert.Empty(imported.Warnings);
        Assert.Equal(written, string.Join('\n', Write(GumpLayoutBuilder.Build(imported.Document))));
    }

    private static IEnumerable<string> Write(GumpLayout layout) =>
        LayoutStringWriter.GumpLevelTokens(layout.Properties)
            .Concat(LayoutStringWriter.Write(layout, new LayoutStringOptions(), LayoutStringWriter.ByIndex));

    /// <summary>Appends the text block the commands index into.</summary>
    private static string WithTextBlock(GumpLayout layout, string commands) =>
        commands
        + "\n\n[text]\n"
        + string.Join('\n', layout.Texts.Select(t => "{ " + t.Value + " }"));
}
