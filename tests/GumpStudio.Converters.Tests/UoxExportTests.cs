using GumpStudio.Core.Document;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Export;
using GumpStudio.Core.Primitives;
using GumpStudio.Converters;

using Xunit;

namespace GumpStudio.Converters.Tests;

/// <summary>
/// The UOX3 exporter. Every method name and parameter order asserted here is
/// read off <c>CGump_Methods</c> in <c>UOXJSMethods.h</c> and the matching
/// <c>CGump_Add*</c> body in <c>UOXJSMethods.cpp</c>.
/// </summary>
public class UoxExportTests
{
    /// <summary>Fixed so output is reproducible and diffable.</summary>
    private static readonly DateTimeOffset Stamp = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private static GumpDocument WithElement(Element element)
    {
        GumpDocument document = new();

        document.Pages[0].Root.Add(element);

        return document;
    }

    private static string Build(GumpDocument document, UoxExportOptions? options = null) =>
        UoxScriptBuilder.Build(document, options, Stamp);

    [Fact]
    public void TheConverterIsRegisteredWithNoDialects()
    {
        UoxConverter converter = new();

        Assert.Equal("uox3", converter.Id);
        Assert.Equal(".js", converter.FileExtension);
        Assert.Empty(converter.Dialects);

        Assert.Contains(GumpConverters.All, c => c.Id == "uox3");
        Assert.Same(converter.Id, GumpConverters.Find("UOX3")!.Id);
    }

    [Fact]
    public void TheScriptBuildsSendsAndFreesTheGump()
    {
        string script = Build(new GumpDocument(), new UoxExportOptions { FunctionName = "Test" });

        Assert.Contains("function DisplayTest( pUser )", script, StringComparison.Ordinal);
        Assert.Contains("var pSock = pUser.socket;", script, StringComparison.Ordinal);
        Assert.Contains("var test = new Gump();", script, StringComparison.Ordinal);
        Assert.Contains("test.Send( pSock );", script, StringComparison.Ordinal);

        // Free() releases the native gump; UOX3's own scripts always pair it.
        Assert.Contains("test.Free();", script, StringComparison.Ordinal);
    }

    [Fact]
    public void GumpFlagsBecomeTheirOwnCalls()
    {
        GumpDocument document = new();

        Assert.DoesNotContain("NoMove", Build(document), StringComparison.Ordinal);

        document.Properties.Movable = false;
        document.Properties.Closable = false;
        document.Properties.Disposable = false;

        string script = Build(document);

        Assert.Contains("myGump.NoMove();", script, StringComparison.Ordinal);
        Assert.Contains("myGump.NoClose();", script, StringComparison.Ordinal);
        Assert.Contains("myGump.NoDispose();", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// A UOX3 gump has no screen position: <c>new Gump()</c> takes none and
    /// <c>Send()</c> takes only a socket. The editor's position is reported so it
    /// is not silently lost.
    /// </summary>
    [Fact]
    public void TheScreenPositionIsReportedAsInexpressible()
    {
        GumpDocument document = new();

        Assert.DoesNotContain("Opens at", Build(document), StringComparison.Ordinal);

        document.Properties.Location = new GumpPoint(50, 60);

        Assert.Contains("// Opens at 50,60 in the editor.", Build(document), StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>CGump_MasterGump</c> formats five values from a single argument, so the
    /// command it appends is garbage. Calling it would be worse than not.
    /// </summary>
    [Fact]
    public void MasterGumpIsReportedRatherThanCalled()
    {
        GumpDocument document = new();

        document.Properties.MasterGumpId = 3000;

        string script = Build(document);

        Assert.Contains("// No usable call for mastergump 3000", script, StringComparison.Ordinal);
        Assert.DoesNotContain(".MasterGump(", script, StringComparison.Ordinal);
    }

    [Fact]
    public void TheParserTogglesAreReported()
    {
        GumpDocument document = new();

        document.Properties.UpperWordCase = true;
        document.Properties.CroppedText = true;
        document.Properties.EnhancedClientInput = true;

        string script = Build(document);

        Assert.Contains("// No UOX3 call for toggleupperwordcase.", script, StringComparison.Ordinal);
        Assert.Contains("// No UOX3 call for togglecroppedtext.", script, StringComparison.Ordinal);
        Assert.Contains("// No UOX3 call for echandleinput.", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ABackgroundTakesItsSizeAfterTheGumpId()
    {
        string script = Build(WithElement(new BackgroundElement
        {
            Location = GumpPoint.Origin,
            Size = new GumpSize(300, 200),
            GumpId = 5054,
        }));

        Assert.Contains("myGump.AddBackground( 0, 0, 5054, 300, 200 );", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// The trap in this API: <c>AddCroppedText</c> takes its hue <em>third</em>,
    /// before the width and height, and reorders it into the layout command
    /// itself — where the hue comes last.
    /// </summary>
    [Fact]
    public void ACroppedLabelTakesItsHueThird()
    {
        string script = Build(WithElement(new LabelElement
        {
            Location = GumpPoint.Origin,
            Text = "clip",
            Hue = 5,
            Cropped = true,
            Size = new GumpSize(90, 18),
        }));

        Assert.Contains(
            "myGump.AddCroppedText( 0, 0, 5, 90, 18, \"clip\" );", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>AddPicInPic</c> takes the source offset before the size, matching the
    /// client. The RunUO-family cores transpose those two pairs.
    /// </summary>
    [Fact]
    public void APicInPicTakesItsSourceOffsetBeforeItsSize()
    {
        string script = Build(WithElement(new PicInPicElement
        {
            Location = GumpPoint.Origin,
            Size = new GumpSize(60, 24),
            GumpId = 1417,
            SourceX = 10,
            SourceY = 20,
        }));

        Assert.Contains(
            "myGump.AddPicInPic( 0, 0, 1417, 10, 20, 60, 24 );", script, StringComparison.Ordinal);
    }

    [Fact]
    public void AHuedPicInPicIsReportedAsUntinted()
    {
        string script = Build(WithElement(new PicInPicElement
        {
            Location = GumpPoint.Origin,
            Size = new GumpSize(60, 24),
            GumpId = 1417,
            Hue = 15,
        }));

        Assert.Contains("// AddPicInPic takes no hue", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>AddGumpColor</c> writes the <c>hue=</c> keyword form, which is the one
    /// the client's <c>gumppic</c> handler actually reads.
    /// </summary>
    [Theory]
    [InlineData(0, false, "myGump.AddGump( 0, 0, 1417 );")]
    [InlineData(22, false, "myGump.AddGumpColor( 0, 0, 1417, 22 );")]
    [InlineData(22, true, "myGump.AddGumpColor( 0, 0, 1417, 22 );")]
    public void ArtPicksTheCallThatMatchesItsHue(int hue, bool partial, string expected)
    {
        string script = Build(WithElement(new ImageElement
        {
            Location = GumpPoint.Origin,
            GumpId = 1417,
            Hue = hue,
            PartialHue = partial,
        }));

        Assert.Contains(expected, script, StringComparison.Ordinal);
    }

    [Fact]
    public void APartialHueIsReportedAsAFullTint()
    {
        string script = Build(WithElement(new ImageElement
        {
            Location = GumpPoint.Origin,
            GumpId = 1417,
            Hue = 22,
            PartialHue = true,
        }));

        Assert.Contains("// No UOX3 call for a partial hue", script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, "myGump.AddPicture( 0, 0, 3821 );")]
    [InlineData(12, "myGump.AddPictureColor( 0, 0, 3821, 12 );")]
    public void ItemArtPicksTheCallThatMatchesItsHue(int hue, string expected)
    {
        string script = Build(WithElement(new ItemElement
        {
            Location = GumpPoint.Origin,
            ItemId = 3821,
            Hue = hue,
        }));

        Assert.Contains(expected, script, StringComparison.Ordinal);
    }

    [Fact]
    public void TileArtInAGumpSlotIsReportedRatherThanFaked()
    {
        string script = Build(WithElement(new TileAsGumpElement { ItemId = 3821 }));

        Assert.Contains(
            "// No UOX3 call for tilepicasgumppic: item 3821", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// The slots are quit, page-id and return-value. <c>AddButton</c> writes all
    /// seven, where <c>AddPageButton</c> writes only six.
    /// </summary>
    [Theory]
    [InlineData(ButtonKind.Page, 3, "myGump.AddButton( 0, 0, 1, 2, 0, 3, 0 );")]
    [InlineData(ButtonKind.Reply, 3, "myGump.AddButton( 0, 0, 1, 2, 1, 0, 3 );")]
    public void ButtonsWriteAllSevenSlots(ButtonKind kind, int param, string expected)
    {
        string script = Build(WithElement(new ButtonElement
        {
            Location = GumpPoint.Origin,
            NormalId = 1,
            PressedId = 2,
            Kind = kind,
            Param = param,
        }));

        Assert.Contains(expected, script, StringComparison.Ordinal);
        Assert.DoesNotContain("AddPageButton", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ATileArtButtonUsesAddButtonTileArt()
    {
        string script = Build(WithElement(new ButtonElement
        {
            Location = GumpPoint.Origin,
            NormalId = 1,
            PressedId = 2,
            Kind = ButtonKind.Reply,
            Param = 3,
            TileId = 3823,
            TileHue = 8,
            TileX = 4,
            TileY = 2,
        }));

        Assert.Contains(
            "myGump.AddButtonTileArt( 0, 0, 1, 2, 1, 0, 3, 3823, 8, 4, 2 );",
            script,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// UOX3 is the only core that exposes the client's <c>endgroup</c>, without
    /// which a group does not work on pages above the first.
    /// </summary>
    [Fact]
    public void ARadioGroupIsOpenedAndClosed()
    {
        GumpDocument document = new();

        document.Pages[0].Root.Add(new RadioElement { GroupId = 3, Value = 1 });

        string script = Build(document);

        int group = script.IndexOf("myGump.AddGroup( 3 );", StringComparison.Ordinal);
        int end = script.IndexOf("myGump.EndGroup();", StringComparison.Ordinal);

        Assert.True(group >= 0 && end > group, script);
    }

    /// <summary>
    /// Unlike the other text calls, <c>AddTextEntry</c> is handed the index of
    /// its own string rather than being assigned one.
    /// </summary>
    [Fact]
    public void ATextEntryIsHandedItsOwnTextIndex()
    {
        GumpDocument document = new();

        document.Pages[0].Root.Add(new LabelElement { Location = GumpPoint.Origin, Text = "a" });
        document.Pages[0].Root.Add(new TextEntryElement
        {
            Location = new GumpPoint(0, 20),
            Size = new GumpSize(120, 20),
            EntryId = 1,
            InitialText = "b",
        });

        // The label takes slot 0, so the entry's string is at slot 1.
        Assert.Contains(
            "myGump.AddTextEntry( 0, 20, 120, 20, 0, 1, 1, \"b\" );",
            Build(document),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ACappedTextEntryUsesTheLimitedCall()
    {
        string script = Build(WithElement(new TextEntryElement
        {
            Location = GumpPoint.Origin,
            Size = new GumpSize(120, 20),
            EntryId = 2,
            MaxLength = 16,
        }));

        Assert.Contains(
            "myGump.AddTextEntryLimited( 0, 0, 120, 20, 0, 2, 0, \"\", 16 );",
            script,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>AddTextEntry</c> pushes a string onto UOX3's list without advancing the
    /// counter the auto-assigning calls read, so every later index is short by
    /// the number of entries above it. Nothing in the API can fix that from the
    /// script side, so the output says where it happens.
    /// </summary>
    [Fact]
    public void ATextElementAfterAnEntryIsWarnedAbout()
    {
        GumpDocument document = new();

        document.Pages[0].Root.Add(new TextEntryElement
        {
            Location = GumpPoint.Origin,
            Size = new GumpSize(120, 20),
            EntryId = 1,
        });

        document.Pages[0].Root.Add(new LabelElement
        {
            Location = new GumpPoint(0, 40),
            Text = "after",
        });

        string script = Build(document);

        Assert.Contains(
            "// UOX3 will index this string as 0, but it is at 1", script, StringComparison.Ordinal);
    }

    /// <summary>A gump with no text entry never drifts, so it says nothing.</summary>
    [Fact]
    public void TextElementsWithNoEntryAboveThemAreSilent()
    {
        GumpDocument document = new();

        document.Pages[0].Root.Add(new LabelElement { Location = GumpPoint.Origin, Text = "a" });
        document.Pages[0].Root.Add(new LabelElement { Location = new GumpPoint(0, 20), Text = "b" });

        Assert.DoesNotContain("will index this string", Build(document), StringComparison.Ordinal);
    }

    [Fact]
    public void AnHtmlAreaPassesItsFlagsAsBooleans()
    {
        string script = Build(WithElement(new HtmlElement
        {
            Location = GumpPoint.Origin,
            Size = new GumpSize(200, 60),
            ContentKind = HtmlContentKind.Html,
            Html = "<b>hi</b>",
            ShowBackground = true,
        }));

        Assert.Contains(
            "myGump.AddHTMLGump( 0, 0, 200, 60, true, false, \"<b>hi</b>\" );",
            script,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EachLocalizedHtmlFormIsReachable()
    {
        GumpDocument document = new();

        document.Pages[0].Root.Add(new HtmlElement
        {
            Location = GumpPoint.Origin,
            Size = new GumpSize(200, 40),
            ContentKind = HtmlContentKind.Localized,
            ClilocId = 1049004,
        });

        document.Pages[0].Root.Add(new HtmlElement
        {
            Location = new GumpPoint(0, 50),
            Size = new GumpSize(200, 40),
            ContentKind = HtmlContentKind.Localized,
            ClilocId = 1049005,
            ShowBackground = true,
            Color = 65280,
        });

        string script = Build(document);

        Assert.Contains(
            "myGump.AddXMFHTMLGump( 0, 0, 200, 40, 1049004, false, false );",
            script,
            StringComparison.Ordinal);

        Assert.Contains(
            "myGump.AddXMFHTMLGumpColor( 0, 50, 200, 40, 1049005, true, false, 65280 );",
            script,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>AddXMFHTMLTok</c> declares an arity of eight but reads eleven arguments
    /// unconditionally, and emits exactly three tab-separated ones, so the three
    /// are always passed and padded.
    /// </summary>
    [Fact]
    public void TokenisedHtmlAlwaysPassesThreeArguments()
    {
        string script = Build(WithElement(new HtmlElement
        {
            Location = GumpPoint.Origin,
            Size = new GumpSize(200, 40),
            ContentKind = HtmlContentKind.Localized,
            ClilocId = 1049006,
            ShowScrollbar = true,
            Color = 65280,
            Arguments = "alpha\tbeta",
        }));

        Assert.Contains(
            "myGump.AddXMFHTMLTok( 0, 0, 200, 40, false, true, 65280, 1049006, \"alpha\", \"beta\", \"\" );",
            script,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>CGump_AddToolTip</c> starts its argument loop at index two rather than
    /// one, so the first argument after the cliloc is skipped and a
    /// single-argument call emits an empty block. Passing a placeholder to work
    /// around that would break the day it is fixed.
    /// </summary>
    [Fact]
    public void TooltipArgumentsAreReportedRatherThanPassed()
    {
        string script = Build(WithElement(new ImageElement
        {
            Location = GumpPoint.Origin,
            GumpId = 55,
            TooltipClilocId = 1042971,
            TooltipArguments = "Bob",
        }));

        Assert.Contains("// Tooltip arguments dropped", script, StringComparison.Ordinal);
        Assert.Contains("myGump.AddToolTip( 1042971 );", script, StringComparison.Ordinal);
    }

    [Fact]
    public void AnItemPropertyPassesItsSerial()
    {
        string script = Build(WithElement(new ImageElement
        {
            Location = GumpPoint.Origin,
            GumpId = 55,
            ItemPropertySerial = 1073741825,
        }));

        Assert.Contains("myGump.AddItemProperty( 1073741825 );", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// Entries come back through <c>gumpData</c>, matched on the id rather than
    /// the position: the client sends only the entries it has.
    /// </summary>
    [Fact]
    public void TheHandlerReadsEntriesByIdAndSwitchesOnReplyButtons()
    {
        GumpDocument document = new();

        document.Pages[0].Root.Add(new TextEntryElement
        {
            Location = GumpPoint.Origin,
            Size = new GumpSize(120, 20),
            EntryId = 1,
            Name = "Name",
        });

        document.Pages[0].Root.Add(new ButtonElement
        {
            Location = new GumpPoint(0, 40),
            NormalId = 1,
            PressedId = 2,
            Kind = ButtonKind.Reply,
            Param = 5,
            Name = "Accept",
        });

        // A page button switches page in the client and never reaches the handler.
        document.Pages[0].Root.Add(new ButtonElement
        {
            Location = new GumpPoint(0, 60),
            NormalId = 1,
            PressedId = 2,
            Kind = ButtonKind.Page,
            Param = 1,
        });

        string script = Build(document);

        Assert.Contains(
            "function onGumpPress( pSock, pButton, gumpData )", script, StringComparison.Ordinal);

        Assert.Contains("for( var i = 0; i < gumpData.IDs; i++ )", script, StringComparison.Ordinal);
        Assert.Contains(
            "case 1: text1 = gumpData.GetEdit( i ); break;", script, StringComparison.Ordinal);

        Assert.Contains("case 5: // Accept", script, StringComparison.Ordinal);
        Assert.DoesNotContain("case 1: // ", script, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHandlerIsOptional()
    {
        string script = Build(
            new GumpDocument(), new UoxExportOptions { IncludeHandlers = false });

        Assert.DoesNotContain("onGumpPress", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>onGumpInput</c> is deliberately absent: it carries a socket, an index
    /// and a reply string for the client's separate text-prompt packet, not for a
    /// gump's text entries.
    /// </summary>
    [Fact]
    public void TheInputHookIsNotGenerated()
    {
        GumpDocument document = new();

        document.Pages[0].Root.Add(new TextEntryElement
        {
            Location = GumpPoint.Origin,
            Size = new GumpSize(120, 20),
            EntryId = 1,
        });

        Assert.DoesNotContain("onGumpInput", Build(document), StringComparison.Ordinal);
    }

    [Fact]
    public void TextIsEscapedForAJavaScriptString()
    {
        string script = Build(WithElement(new LabelElement
        {
            Location = GumpPoint.Origin,
            Text = "say \"hi\"\\there",
        }));

        Assert.Contains("\\\"hi\\\"", script, StringComparison.Ordinal);
        Assert.Contains("\\\\there", script, StringComparison.Ordinal);
    }

    [Fact]
    public void NestedElementsExportAtTheirAbsolutePosition()
    {
        GumpDocument document = new();

        GroupElement group = new() { Location = new GumpPoint(100, 200) };

        group.Add(new ImageElement { Location = new GumpPoint(7, 9), GumpId = 55 });
        document.Pages[0].Root.Add(group);

        Assert.Contains(
            "myGump.AddGump( 107, 209, 55 );", Build(document), StringComparison.Ordinal);
    }

    [Fact]
    public void EveryPageIsEmitted()
    {
        GumpDocument document = new();

        document.AddPage();

        string script = Build(document);

        Assert.Contains("myGump.AddPage( 0 );", script, StringComparison.Ordinal);
        Assert.Contains("myGump.AddPage( 1 );", script, StringComparison.Ordinal);
    }

    [Fact]
    public void CommentsAreOptional()
    {
        GumpDocument document = WithElement(new LabelElement
        {
            Name = "Title",
            Comment = "the heading",
            Location = GumpPoint.Origin,
            Text = "x",
        });

        Assert.Contains("// Title: the heading", Build(document), StringComparison.Ordinal);

        Assert.DoesNotContain(
            "// Title",
            Build(document, new UoxExportOptions { IncludeComments = false }),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AMultiWordNameIsReducedToOneIdentifier()
    {
        string script = Build(
            new GumpDocument(), new UoxExportOptions { FunctionName = "My Fancy Gump" });

        Assert.Contains("function DisplayMyFancyGump( pUser )", script, StringComparison.Ordinal);
    }

    [Fact]
    public void OutputIsReproducibleForAFixedTimestamp() =>
        Assert.Equal(Build(new GumpDocument()), Build(new GumpDocument()));

    [Fact]
    public void TheConverterHonoursTheSharedOptions()
    {
        UoxConverter converter = new();

        string script = converter.Export(
            WithElement(new LabelElement { Name = "Title", Location = GumpPoint.Origin, Text = "x" }),
            new GumpExportOptions { GumpName = "Test", IncludeComments = false });

        Assert.Contains("function DisplayTest( pUser )", script, StringComparison.Ordinal);
        Assert.DoesNotContain("// Title", script, StringComparison.Ordinal);
    }
}
