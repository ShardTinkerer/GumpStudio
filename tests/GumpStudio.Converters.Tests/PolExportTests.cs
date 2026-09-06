using GumpStudio.Core.Document;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Primitives;
using GumpStudio.Core.Export;
using GumpStudio.Converters;

using Xunit;

namespace GumpStudio.Converters.Tests;

public class PolExportTests
{
    /// <summary>Fixed so output is reproducible and diffable.</summary>
    private static readonly DateTimeOffset Stamp = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private static GumpDocument BuildSample()
    {
        GumpDocument document = new();

        document.Properties.Location = new GumpPoint(50, 60);

        GumpPage page = document.Pages[0];

        page.Root.Add(new BackgroundElement
        {
            Name = "Frame",
            Location = new GumpPoint(0, 0),
            Size = new GumpSize(300, 200),
            GumpId = 5054,
        });

        page.Root.Add(new LabelElement
        {
            Name = "Title",
            Location = new GumpPoint(10, 10),
            Text = "Hello",
            Hue = 88,
        });

        page.Root.Add(new ItemElement { Location = new GumpPoint(20, 40), ItemId = 3821, Hue = 12 });
        page.Root.Add(new ImageElement { Location = new GumpPoint(30, 50), GumpId = 1417 });
        page.Root.Add(new AlphaElement { Location = new GumpPoint(5, 5), Size = new GumpSize(50, 40) });
        page.Root.Add(new TiledElement
        {
            Location = new GumpPoint(60, 60),
            Size = new GumpSize(40, 30),
            GumpId = 4,
        });

        page.Root.Add(new ButtonElement
        {
            Location = new GumpPoint(15, 150),
            NormalId = 247,
            PressedId = 248,
            Kind = ButtonKind.Reply,
            Param = 7,
        });

        page.Root.Add(new CheckboxElement
        {
            Location = new GumpPoint(15, 170),
            CheckedId = 211,
            UncheckedId = 210,
            IsChecked = true,
            GroupId = 2,
        });

        page.Root.Add(new RadioElement
        {
            Location = new GumpPoint(60, 170),
            CheckedId = 208,
            UncheckedId = 209,
            GroupId = 3,
            Value = 4,
        });

        page.Root.Add(new TextEntryElement
        {
            Location = new GumpPoint(100, 100),
            Size = new GumpSize(120, 20),
            InitialText = "name",
            EntryId = 1,
        });

        page.Root.Add(new HtmlElement
        {
            Location = new GumpPoint(100, 130),
            Size = new GumpSize(150, 40),
            Html = "<b>hi</b>",
            ContentKind = HtmlContentKind.Html,
        });

        return document;
    }

    [Fact]
    public void GumpPackageDialectEmitsTheExpectedCalls()
    {
        string script = PolScriptBuilder.Build(BuildSample(), "MyGump", null, Stamp);

        Assert.Contains("var MyGump := GFCreateGump(50,60);", script, StringComparison.Ordinal);
        Assert.Contains("GFPage(MyGump, 0);", script, StringComparison.Ordinal);
        Assert.Contains("GFResizePic(MyGump, 0, 0, 5054, 300, 200);", script, StringComparison.Ordinal);
        Assert.Contains("GFTextLine(MyGump, 10, 10, 88, \"Hello\");", script, StringComparison.Ordinal);
        Assert.Contains("GFTilePic(MyGump, 20, 40, 3821, 12);", script, StringComparison.Ordinal);
        Assert.Contains("GFGumpPic(MyGump, 30, 50, 1417, 0);", script, StringComparison.Ordinal);
        Assert.Contains("GFAddAlphaRegion(MyGump, 5, 5, 50, 40);", script, StringComparison.Ordinal);
        Assert.Contains("GFAddButton(MyGump, 15, 150, 247, 248, GF_CLOSE_BTN, 7);", script, StringComparison.Ordinal);
        Assert.Contains("GFCheckBox(MyGump, 15, 170, 210, 211, 1, 2);", script, StringComparison.Ordinal);
        Assert.Contains("GFSetRadioGroup(MyGump, 3);", script, StringComparison.Ordinal);
        Assert.Contains("GFRadioButton(MyGump, 60, 170, 209, 208, 0, 4);", script, StringComparison.Ordinal);
        Assert.Contains("GFTextEntry(MyGump, 100, 100, 120, 20, 0, \"name\", 1);", script, StringComparison.Ordinal);
        Assert.Contains("GFHTMLArea(MyGump, 100, 130, 150, 40, \"<b>hi</b>\");", script, StringComparison.Ordinal);
        Assert.Contains("GFSendGump(who, MyGump);", script, StringComparison.Ordinal);
        Assert.Contains("endprogram", script, StringComparison.Ordinal);

        // The gump package has no tiled call, so the original commented it out.
        // The name in the note is taken from the command on the line below it, so
        // it is lowercase where the original spelled it "GumpPicTiled".
        Assert.Contains("//Gump package does not support gumppictiled", script, StringComparison.Ordinal);
        Assert.Contains("//gumppictiled 60 60 40 30 4", script, StringComparison.Ordinal);
    }

    [Fact]
    public void LayoutStringDialectEmitsTheExpectedCommands()
    {
        PolExportOptions options = new() { Style = PolScriptStyle.LayoutStrings };

        string script = PolScriptBuilder.Build(BuildSample(), "MyGump", options, Stamp);

        Assert.Contains("\"page 0\"", script, StringComparison.Ordinal);
        Assert.Contains("\"resizepic 0 0 5054 300 200\"", script, StringComparison.Ordinal);
        Assert.Contains("\"tilepichue 20 40 3821 12\"", script, StringComparison.Ordinal);
        Assert.Contains("\"gumppic 30 50 1417\"", script, StringComparison.Ordinal);
        Assert.Contains("\"checkertrans 5 5 50 40\"", script, StringComparison.Ordinal);
        Assert.Contains("\"gumppictiled 60 60 40 30 4\"", script, StringComparison.Ordinal);
        Assert.Contains("\"checkbox 15 170 210 211 1 2\"", script, StringComparison.Ordinal);
        Assert.Contains("\"group 3\"", script, StringComparison.Ordinal);
        Assert.Contains("\"radio 60 170 209 208 0 4\"", script, StringComparison.Ordinal);
        Assert.Contains("SendDialogGump(who, gump, data, 50, 60);", script, StringComparison.Ordinal);

        // Text lives in the data array and is referenced by index.
        Assert.Contains("\"text 10 10 88 0\"", script, StringComparison.Ordinal);
        Assert.Contains("\"Hello\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void UnhuedArtOmitsTheHueArgumentInLayoutStrings()
    {
        GumpDocument document = new();

        document.Pages[0].Root.Add(new ItemElement { Location = GumpPoint.Origin, ItemId = 100 });

        string script = PolScriptBuilder.Build(
            document, "g", new PolExportOptions { Style = PolScriptStyle.LayoutStrings }, Stamp);

        Assert.Contains("\"tilepic 0 0 100\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("tilepichue", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// The headline defect. The original flattened groups and then emitted
    /// parent-relative coordinates, so nested elements exported to the wrong
    /// place. Confirmed by decompilation to be present in 1.8 itself.
    /// </summary>
    [Fact]
    public void NestedElementsExportAtTheirAbsolutePosition()
    {
        GumpDocument document = new();

        GroupElement group = new() { Location = new GumpPoint(100, 200) };

        group.Add(new ImageElement { Location = new GumpPoint(7, 9), GumpId = 55 });
        document.Pages[0].Root.Add(group);

        string script = PolScriptBuilder.Build(document, "g", null, Stamp);

        Assert.Contains("GFGumpPic(g, 107, 209, 55, 0);", script, StringComparison.Ordinal);
        Assert.DoesNotContain("GFGumpPic(g, 7, 9", script, StringComparison.Ordinal);
    }

    [Fact]
    public void NestedElementsExportAtTheirAbsolutePositionInLayoutStrings()
    {
        GumpDocument document = new();

        GroupElement outer = new() { Location = new GumpPoint(10, 20) };
        GroupElement inner = new() { Location = new GumpPoint(3, 4) };

        inner.Add(new ItemElement { Location = new GumpPoint(1, 2), ItemId = 9 });
        outer.Add(inner);
        document.Pages[0].Root.Add(outer);

        string script = PolScriptBuilder.Build(
            document, "g", new PolExportOptions { Style = PolScriptStyle.LayoutStrings }, Stamp);

        Assert.Contains("\"tilepic 14 26 9\"", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// The original interpolated text straight into a quoted POL string, so any
    /// quote produced a script that would not compile.
    /// </summary>
    [Fact]
    public void TextIsEscapedForTheTargetLanguage()
    {
        GumpDocument document = new();

        document.Pages[0].Root.Add(new LabelElement
        {
            Location = GumpPoint.Origin,
            Text = "say \"hi\"\\there\nand more",
        });

        string script = PolScriptBuilder.Build(document, "g", null, Stamp);

        Assert.Contains("\\\"hi\\\"", script, StringComparison.Ordinal);
        Assert.Contains("\\\\there", script, StringComparison.Ordinal);

        // A newline inside a quoted string would terminate it.
        Assert.DoesNotContain("and more\n\";", script, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyTextGetsAPlaceholderOnlyWhenAsked()
    {
        GumpDocument document = new();

        document.Pages[0].Root.Add(new LabelElement { Location = GumpPoint.Origin, Text = string.Empty });

        Assert.Contains(
            "\"TextLine\"",
            PolScriptBuilder.Build(document, "g", new PolExportOptions { PlaceholderText = true }, Stamp),
            StringComparison.Ordinal);

        Assert.Contains(
            "\"\"",
            PolScriptBuilder.Build(document, "g", new PolExportOptions { PlaceholderText = false }, Stamp),
            StringComparison.Ordinal);
    }

    [Fact]
    public void GumpFlagsAreEmittedOnlyWhenTurnedOff()
    {
        GumpDocument document = new();

        Assert.DoesNotContain("GFMovable", PolScriptBuilder.Build(document, "g", null, Stamp), StringComparison.Ordinal);

        document.Properties.Movable = false;
        document.Properties.Closable = false;
        document.Properties.Disposable = false;

        string script = PolScriptBuilder.Build(document, "g", null, Stamp);

        Assert.Contains("GFMovable(g, 0);", script, StringComparison.Ordinal);
        Assert.Contains("GFClosable(g, 0);", script, StringComparison.Ordinal);
        Assert.Contains("GFDisposable(g, 0);", script, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryPageIsEmitted()
    {
        GumpDocument document = new();

        document.AddPage();
        document.AddPage();

        string script = PolScriptBuilder.Build(document, "g", null, Stamp);

        Assert.Contains("GFPage(g, 0);", script, StringComparison.Ordinal);
        Assert.Contains("GFPage(g, 1);", script, StringComparison.Ordinal);
        Assert.Contains("GFPage(g, 2);", script, StringComparison.Ordinal);
    }

    [Fact]
    public void CommentsAndNamesAreOptional()
    {
        GumpDocument document = new();

        document.Pages[0].Root.Add(new LabelElement
        {
            Name = "Title",
            Comment = "the heading",
            Location = GumpPoint.Origin,
            Text = "x",
        });

        string with = PolScriptBuilder.Build(document, "g", new PolExportOptions(), Stamp);
        string without = PolScriptBuilder.Build(
            document, "g", new PolExportOptions { IncludeComments = false, IncludeNames = false }, Stamp);

        Assert.Contains("//Title: the heading", with, StringComparison.Ordinal);
        Assert.DoesNotContain("//Title", without, StringComparison.Ordinal);
    }

    [Fact]
    public void OutputIsReproducibleForAFixedTimestamp()
    {
        GumpDocument document = BuildSample();

        // The original stamped DateTime.Now into every export, so two exports of
        // the same gump never matched and could not be diffed.
        Assert.Equal(
            PolScriptBuilder.Build(document, "MyGump", null, Stamp),
            PolScriptBuilder.Build(document, "MyGump", null, Stamp));
    }

    [Fact]
    public void AMultiWordNameIsReducedToOneIdentifier()
    {
        string script = PolScriptBuilder.Build(new GumpDocument(), "My Fancy Gump", null, Stamp);

        Assert.Contains("program gump_My(who)", script, StringComparison.Ordinal);
    }

    [Fact]
    public void TheConverterOffersBothDialects()
    {
        PolConverter converter = new();

        Assert.Equal("pol", converter.Id);
        Assert.Equal(".src", converter.FileExtension);

        // Both used to be separate top-level entries. Until they were, the
        // layout-string form could not be reached from the application at all.
        Assert.Equal(
            [PolConverter.GumpPackage, PolConverter.LayoutStrings],
            converter.Dialects.Select(d => d.Id));

        Assert.Contains(
            "GFCreateGump",
            converter.Export(new GumpDocument(), new GumpExportOptions { GumpName = "Test" }),
            StringComparison.Ordinal);

        Assert.Contains(
            "SendDialogGump",
            converter.Export(
                new GumpDocument(),
                new GumpExportOptions { GumpName = "Test", Dialect = PolConverter.LayoutStrings }),
            StringComparison.Ordinal);
    }

    private static string Layout(GumpDocument document) =>
        PolScriptBuilder.Build(
            document, "g", new PolExportOptions { Style = PolScriptStyle.LayoutStrings }, Stamp);

    private static GumpDocument WithElement(Element element)
    {
        GumpDocument document = new();

        document.Pages[0].Root.Add(element);

        return document;
    }

    /// <summary>
    /// The slots are quit, page-id, return-value. The original got all three
    /// wrong and its own source admits it, carrying a
    /// <c>// TODO: Page or Reply???</c> comment: it inverted the quit flag, put a
    /// page button's target page in the return-value slot, and a reply button's
    /// return value in the page slot. The layout asserted here is what both the
    /// POL command reference and the client's parser describe.
    /// </summary>
    [Fact]
    public void APageButtonPutsItsPageInThePageSlotAndDoesNotQuit()
    {
        string script = Layout(WithElement(new ButtonElement
        {
            Location = GumpPoint.Origin,
            NormalId = 1,
            PressedId = 2,
            Kind = ButtonKind.Page,
            Param = 3,
        }));

        Assert.Contains("\"button 0 0 1 2 0 3 0\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void AReplyButtonPutsItsValueInTheReturnSlotAndQuits()
    {
        string script = Layout(WithElement(new ButtonElement
        {
            Location = GumpPoint.Origin,
            NormalId = 1,
            PressedId = 2,
            Kind = ButtonKind.Reply,
            Param = 3,
        }));

        Assert.Contains("\"button 0 0 1 2 1 0 3\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ButtonTileArtAppendsTheOverlayParameters()
    {
        string script = Layout(WithElement(new ButtonElement
        {
            Location = GumpPoint.Origin,
            NormalId = 1,
            PressedId = 2,
            Kind = ButtonKind.Reply,
            Param = 3,
            TileId = 3821,
            TileHue = 33,
            TileX = 4,
            TileY = 5,
        }));

        Assert.Contains("\"buttontileart 0 0 1 2 1 0 3 3821 33 4 5\"", script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, false, "gumppic 0 0 55")]
    [InlineData(33, false, "gumppic 0 0 55 33")]
    [InlineData(33, true, "gumppicphued 0 0 55 33")]
    public void AGumpImagePicksTheCommandThatMatchesItsHueMode(int hue, bool partial, string expected)
    {
        string script = Layout(WithElement(new ImageElement
        {
            Location = GumpPoint.Origin,
            GumpId = 55,
            Hue = hue,
            PartialHue = partial,
        }));

        Assert.Contains($"\"{expected}\"", script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, false, "picinpic 0 0 9000 4 5 20 30")]
    [InlineData(7, false, "picinpichued 0 0 9000 4 5 20 30 7")]
    [InlineData(7, true, "picinpicphued 0 0 9000 4 5 20 30 7")]
    public void APicInPicPicksTheCommandThatMatchesItsHueMode(int hue, bool partial, string expected)
    {
        string script = Layout(WithElement(new PicInPicElement
        {
            Location = GumpPoint.Origin,
            Size = new GumpSize(20, 30),
            GumpId = 9000,
            SourceX = 4,
            SourceY = 5,
            Hue = hue,
            PartialHue = partial,
        }));

        Assert.Contains($"\"{expected}\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void TileArtInAGumpSlotCarriesItsThreeTrailingParameters()
    {
        string script = Layout(WithElement(new TileAsGumpElement
        {
            Location = GumpPoint.Origin,
            ItemId = 3821,
            LinkId = 1,
            ParamB = 2,
            ParamC = 3,
        }));

        Assert.Contains("\"tilepicasgumppic 0 0 3821 1 2 3\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ATextEntryWithALimitUsesTheLimitedCommand()
    {
        GumpDocument document = new();

        document.Pages[0].Root.Add(new TextEntryElement
        {
            Location = GumpPoint.Origin,
            Size = new GumpSize(120, 20),
            EntryId = 2,
            MaxLength = 40,
        });

        string script = Layout(document);

        Assert.Contains("\"textentrylimited 0 0 120 20 0 2 0 40\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("\"textentry 0 0", script, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnlimitedTextEntryStillUsesThePlainCommand()
    {
        GumpDocument document = new();

        document.Pages[0].Root.Add(new TextEntryElement
        {
            Location = GumpPoint.Origin,
            Size = new GumpSize(120, 20),
            EntryId = 2,
        });

        Assert.Contains("\"textentry 0 0 120 20 0 2 0\"", Layout(document), StringComparison.Ordinal);
    }

    [Fact]
    public void ACroppedLabelUsesCroppedText()
    {
        string script = Layout(WithElement(new LabelElement
        {
            Location = GumpPoint.Origin,
            Text = "clip",
            Hue = 5,
            Cropped = true,
            Size = new GumpSize(90, 18),
        }));

        Assert.Contains("\"croppedtext 0 0 90 18 5 0\"", script, StringComparison.Ordinal);
        Assert.Contains("\"clip\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ALocalizedHtmlAreaWithAColourUsesTheColourCommand()
    {
        string script = Layout(WithElement(new HtmlElement
        {
            Location = GumpPoint.Origin,
            Size = new GumpSize(200, 60),
            ContentKind = HtmlContentKind.Localized,
            ClilocId = 1049004,
            ShowBackground = true,
            Color = 32767,
        }));

        Assert.Contains(
            "\"xmfhtmlgumpcolor 0 0 200 60 1049004 1 0 32767\"", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>xmfhtmltok</c> is not <c>xmfhtmlgumpcolor</c> with arguments appended:
    /// the flags come before the colour and the cliloc id comes last.
    /// </summary>
    [Fact]
    public void ALocalizedHtmlAreaWithArgumentsUsesTokAndItsOwnParameterOrder()
    {
        string script = Layout(WithElement(new HtmlElement
        {
            Location = GumpPoint.Origin,
            Size = new GumpSize(200, 60),
            ContentKind = HtmlContentKind.Localized,
            ClilocId = 1049004,
            ShowBackground = true,
            ShowScrollbar = true,
            Color = 32767,
            Arguments = "Bob@42",
        }));

        Assert.Contains(
            "\"xmfhtmltok 0 0 200 60 1 1 32767 1049004 @Bob@42@\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void APlainLocalizedHtmlAreaStillUsesXmfHtmlGump()
    {
        string script = Layout(WithElement(new HtmlElement
        {
            Location = GumpPoint.Origin,
            Size = new GumpSize(200, 60),
            ContentKind = HtmlContentKind.Localized,
            ClilocId = 1049004,
        }));

        Assert.Contains("\"xmfhtmlgump 0 0 200 60 1049004 0 0\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void TooltipsFollowTheElementTheyAttachTo()
    {
        string script = Layout(WithElement(new ImageElement
        {
            Location = GumpPoint.Origin,
            GumpId = 55,
            TooltipClilocId = 1042971,
            TooltipArguments = "Bob@42",
        }));

        int image = script.IndexOf("gumppic 0 0 55", StringComparison.Ordinal);
        int tooltip = script.IndexOf("tooltip 1042971 @Bob@42@", StringComparison.Ordinal);

        // The client attaches a tooltip to whichever element it created last, so
        // order is the whole meaning of the command.
        Assert.True(image >= 0 && tooltip > image, script);
    }

    [Fact]
    public void AnItemPropertyTooltipEmitsItsSerial()
    {
        string script = Layout(WithElement(new ImageElement
        {
            Location = GumpPoint.Origin,
            GumpId = 55,
            ItemPropertySerial = 1073741825,
        }));

        Assert.Contains("\"itemproperty 1073741825\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void GumpLevelTokensAreEmittedOnlyWhenSet()
    {
        GumpDocument document = new();

        string off = Layout(document);

        Assert.DoesNotContain("mastergump", off, StringComparison.Ordinal);
        Assert.DoesNotContain("toggleupperwordcase", off, StringComparison.Ordinal);
        Assert.DoesNotContain("togglecroppedtext", off, StringComparison.Ordinal);
        Assert.DoesNotContain("echandleinput", off, StringComparison.Ordinal);

        document.Properties.MasterGumpId = 3000;
        document.Properties.UpperWordCase = true;
        document.Properties.CroppedText = true;
        document.Properties.EnhancedClientInput = true;

        string on = Layout(document);

        Assert.Contains("\"mastergump 3000\"", on, StringComparison.Ordinal);
        Assert.Contains("\"toggleupperwordcase\"", on, StringComparison.Ordinal);
        Assert.Contains("\"togglecroppedtext\"", on, StringComparison.Ordinal);
        Assert.Contains("\"echandleinput\"", on, StringComparison.Ordinal);
    }

    [Fact]
    public void ARadioGroupIsClosedBeforeThePageEnds()
    {
        GumpDocument document = new();

        document.Pages[0].Root.Add(new RadioElement { GroupId = 3, Value = 1 });

        string script = Layout(document);

        int group = script.IndexOf("\"group 3\"", StringComparison.Ordinal);
        int end = script.IndexOf("\"endgroup\"", StringComparison.Ordinal);

        Assert.True(group >= 0 && end > group, script);
    }

    [Fact]
    public void NoGroupMeansNoEndGroup()
    {
        GumpDocument document = new();

        document.Pages[0].Root.Add(new RadioElement { GroupId = 0, Value = 1 });

        Assert.DoesNotContain("endgroup", Layout(document), StringComparison.Ordinal);
    }

    /// <summary>
    /// The client's <c>page</c> command resets the current group, so the same
    /// group id on a later page has to be declared again. Tracking the group
    /// across pages meant the exporter skipped the declaration and every radio on
    /// the second page silently fell into group 0.
    /// </summary>
    [Fact]
    public void TheSameGroupIdOnTheNextPageIsDeclaredAgain()
    {
        GumpDocument document = new();

        document.Pages[0].Root.Add(new RadioElement { GroupId = 3, Value = 1 });
        document.AddPage().Root.Add(new RadioElement { GroupId = 3, Value = 2 });

        string script = Layout(document);
        int occurrences = script.Split("\"group 3\"").Length - 1;

        Assert.Equal(2, occurrences);
    }

    [Fact]
    public void TheGumpPackageCommentsOutWhatItHasNoFunctionFor()
    {
        GumpDocument document = new();

        document.Pages[0].Root.Add(new PicInPicElement
        {
            Location = GumpPoint.Origin,
            Size = new GumpSize(20, 30),
            GumpId = 9000,
            SourceX = 4,
            SourceY = 5,
        });

        string script = PolScriptBuilder.Build(document, "g", null, Stamp);

        Assert.Contains("//Gump package does not support picinpic", script, StringComparison.Ordinal);
        Assert.Contains("//picinpic 0 0 9000 4 5 20 30", script, StringComparison.Ordinal);
    }
}
