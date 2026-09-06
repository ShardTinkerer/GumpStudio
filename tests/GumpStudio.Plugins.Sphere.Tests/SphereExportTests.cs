using GumpStudio.Core.Document;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Export;
using GumpStudio.Core.Primitives;
using GumpStudio.Plugins.Sphere;

using Xunit;

namespace GumpStudio.Plugins.Sphere.Tests;

public class SphereExportTests
{
    /// <summary>Fixed so output is reproducible and diffable.</summary>
    private static readonly DateTimeOffset Stamp = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private static GumpDocument WithElement(Element element)
    {
        GumpDocument document = new();

        document.Pages[0].Root.Add(element);

        return document;
    }

    private static string Revision(GumpDocument document) =>
        SphereScriptBuilder.Build(
            document, new SphereExportOptions { Dialect = SphereDialect.Revision }, Stamp);

    private static string Modern(GumpDocument document) =>
        SphereScriptBuilder.Build(
            document, new SphereExportOptions { Dialect = SphereDialect.Modern }, Stamp);

    private static GumpDocument BuildSample()
    {
        GumpDocument document = new();

        document.Properties.Location = new GumpPoint(50, 60);
        document.Properties.Movable = false;
        document.Properties.Closable = false;
        document.Properties.Disposable = false;

        GumpPage page = document.Pages[0];

        page.Root.Add(new BackgroundElement
        {
            Location = GumpPoint.Origin,
            Size = new GumpSize(300, 200),
            GumpId = 5054,
        });

        page.Root.Add(new LabelElement { Location = new GumpPoint(10, 10), Text = "Hello", Hue = 88 });
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
            Name = "Okay",
            Location = new GumpPoint(15, 150),
            NormalId = 247,
            PressedId = 248,
            Kind = ButtonKind.Reply,
            Param = 7,
        });

        return document;
    }

    [Fact]
    public void TheRevisionDialectEmitsBareLayoutCommands()
    {
        string script = SphereScriptBuilder.Build(
            BuildSample(),
            new SphereExportOptions { Dialect = SphereDialect.Revision, DialogName = "d_shop" },
            Stamp);

        Assert.Contains("[DIALOG d_shop]", script, StringComparison.Ordinal);
        Assert.Contains("50,60", script, StringComparison.Ordinal);
        Assert.Contains("NOCLOSE", script, StringComparison.Ordinal);
        Assert.Contains("NOMOVE", script, StringComparison.Ordinal);
        Assert.Contains("NODISPOSE", script, StringComparison.Ordinal);
        Assert.Contains("page 0", script, StringComparison.Ordinal);
        Assert.Contains("resizepic 0 0 5054 300 200", script, StringComparison.Ordinal);
        Assert.Contains("tilepichue 20 40 3821 12", script, StringComparison.Ordinal);
        Assert.Contains("gumppic 30 50 1417", script, StringComparison.Ordinal);
        Assert.Contains("checkertrans 5 5 50 40", script, StringComparison.Ordinal);
        Assert.Contains("gumppictiled 60 60 40 30 4", script, StringComparison.Ordinal);

        // Strings live in their own block and are referenced by index.
        Assert.Contains("text 10 10 88 0", script, StringComparison.Ordinal);
        Assert.Contains("[DIALOG d_shop TEXT]", script, StringComparison.Ordinal);
        Assert.Contains("Hello", script, StringComparison.Ordinal);

        Assert.Contains("[DIALOG d_shop BUTTON]", script, StringComparison.Ordinal);
        Assert.Contains("ON=7", script, StringComparison.Ordinal);
        Assert.Contains("[EOF]", script, StringComparison.Ordinal);
    }

    [Fact]
    public void TheModernDialectEmitsFunctionCallsWithInlineText()
    {
        string script = SphereScriptBuilder.Build(
            BuildSample(),
            new SphereExportOptions { Dialect = SphereDialect.Modern, DialogName = "d_shop" },
            Stamp);

        Assert.Contains("SetLocation=50,60", script, StringComparison.Ordinal);
        Assert.Contains("NoClose", script, StringComparison.Ordinal);
        Assert.Contains("Page(0)", script, StringComparison.Ordinal);
        Assert.Contains("ResizePic(0,0,5054,300,200)", script, StringComparison.Ordinal);
        Assert.Contains("TilePicHue(20,40,3821,12)", script, StringComparison.Ordinal);
        Assert.Contains("GumpPic(30,50,1417)", script, StringComparison.Ordinal);
        Assert.Contains("CheckerTrans(5,5,50,40)", script, StringComparison.Ordinal);
        Assert.Contains("GumpPicTiled(60,60,40,30,4)", script, StringComparison.Ordinal);
        Assert.Contains("TextA(10,10,88,\"Hello\")", script, StringComparison.Ordinal);

        // Text is inline, so there is no text block.
        Assert.DoesNotContain("TEXT]", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// The layout command takes the released id first. The original emitted the
    /// checked id first, so every checkbox and radio rendered inverted.
    /// </summary>
    [Fact]
    public void ACheckboxPutsItsReleasedGraphicFirst()
    {
        string script = Revision(WithElement(new CheckboxElement
        {
            UncheckedId = 210,
            CheckedId = 211,
            IsChecked = true,
            GroupId = 2,
        }));

        Assert.Contains("checkbox 0 0 210 211 1 2", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ARadioPutsItsReleasedGraphicFirst()
    {
        string script = Revision(WithElement(new RadioElement
        {
            UncheckedId = 209,
            CheckedId = 208,
            GroupId = 3,
            Value = 4,
        }));

        Assert.Contains("radio 0 0 209 208 0 4", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// The original hard-coded the quit flag to 1, so a page button dismissed the
    /// dialog instead of switching page.
    /// </summary>
    [Fact]
    public void APageButtonDoesNotQuit()
    {
        string script = Revision(WithElement(new ButtonElement
        {
            NormalId = 1,
            PressedId = 2,
            Kind = ButtonKind.Page,
            Param = 3,
        }));

        Assert.Contains("button 0 0 1 2 0 3 0", script, StringComparison.Ordinal);
    }

    [Fact]
    public void AReplyButtonQuitsAndCarriesItsReturnValue()
    {
        string script = Revision(WithElement(new ButtonElement
        {
            NormalId = 1,
            PressedId = 2,
            Kind = ButtonKind.Reply,
            Param = 7,
        }));

        Assert.Contains("button 0 0 1 2 1 0 7", script, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyReplyButtonsGetAHandlerBlock()
    {
        GumpDocument document = new();

        document.Pages[0].Root.Add(new ButtonElement { Kind = ButtonKind.Page, Param = 1 });
        document.Pages[0].Root.Add(new ButtonElement
        {
            Name = "Buy",
            Comment = "charges the player",
            Kind = ButtonKind.Reply,
            Param = 9,
        });

        string script = Revision(document);

        Assert.Contains("ON=9", script, StringComparison.Ordinal);
        Assert.Contains("// Buy", script, StringComparison.Ordinal);
        Assert.Contains("// charges the player", script, StringComparison.Ordinal);
        Assert.DoesNotContain("ON=1", script, StringComparison.Ordinal);
    }

    [Fact]
    public void AButtonsCodeBehindGoesIntoItsHandler()
    {
        string script = Revision(WithElement(new ButtonElement
        {
            Name = "Buy",
            Kind = ButtonKind.Reply,
            Param = 9,
            CodeBehind = "SRC.SYSMESSAGE Thanks",
        }));

        Assert.Contains("SRC.SYSMESSAGE Thanks", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>page</c> resets the client's current group, so the same id on a later
    /// page has to be declared again. The original tracked it across the whole
    /// document.
    /// </summary>
    [Fact]
    public void TheSameGroupIdOnTheNextPageIsDeclaredAgain()
    {
        GumpDocument document = new();

        document.Pages[0].Root.Add(new RadioElement { GroupId = 3, Value = 1 });
        document.AddPage().Root.Add(new RadioElement { GroupId = 3, Value = 2 });

        Assert.Equal(2, Occurrences(Revision(document), "Group 3"));
    }

    [Fact]
    public void NestedElementsExportAtTheirAbsolutePosition()
    {
        GumpDocument document = new();

        GroupElement outer = new() { Location = new GumpPoint(10, 20) };
        GroupElement inner = new() { Location = new GumpPoint(3, 4) };

        inner.Add(new ItemElement { Location = new GumpPoint(1, 2), ItemId = 9 });
        outer.Add(inner);
        document.Pages[0].Root.Add(outer);

        Assert.Contains("tilepic 14 26 9", Revision(document), StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyTextGetsAnIndexedPlaceholder()
    {
        GumpDocument document = new();

        document.Pages[0].Root.Add(new LabelElement { Text = string.Empty });

        Assert.Contains("Text id.0", Revision(document), StringComparison.Ordinal);
    }

    [Fact]
    public void TextIsEscapedForTheModernDialect()
    {
        string script = Modern(WithElement(new LabelElement { Text = "say \"hi\"" }));

        Assert.Contains("TextA(0,0,0,\"say \\\"hi\\\"\")", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// Sphere's script format is line-based, so a newline inside a text value
    /// would be read as the start of the next command.
    /// </summary>
    [Fact]
    public void NewlinesNeverReachTheTextBlock()
    {
        string script = Revision(WithElement(new LabelElement { Text = "first\nsecond" }));

        Assert.Contains("first second", script, StringComparison.Ordinal);
        Assert.DoesNotContain("first\nsecond", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ADialogNameIsReducedToOneToken()
    {
        string script = SphereScriptBuilder.Build(
            new GumpDocument(), new SphereExportOptions { DialogName = "my shop" }, Stamp);

        Assert.Contains("[DIALOG my_shop]", script, StringComparison.Ordinal);
    }

    [Fact]
    public void OutputIsReproducibleForAFixedTimestamp()
    {
        GumpDocument document = BuildSample();

        Assert.Equal(Revision(document), Revision(document));
    }

    // -- Commands the client gained after 1.8 -------------------------------

    [Theory]
    [InlineData(0, false, "gumppic 0 0 55")]
    [InlineData(33, false, "gumppic 0 0 55 33")]
    [InlineData(33, true, "gumppicphued 0 0 55 33")]
    public void AGumpImagePicksTheCommandThatMatchesItsHueMode(int hue, bool partial, string expected)
    {
        string script = Revision(WithElement(new ImageElement
        {
            GumpId = 55,
            Hue = hue,
            PartialHue = partial,
        }));

        Assert.Contains(expected, script, StringComparison.Ordinal);
    }

    [Fact]
    public void APicInPicIsEmittedInTheRevisionDialect()
    {
        string script = Revision(WithElement(new PicInPicElement
        {
            GumpId = 9000,
            Size = new GumpSize(20, 30),
            SourceX = 4,
            SourceY = 5,
            Hue = 7,
        }));

        Assert.Contains("picinpichued 0 0 9000 4 5 20 30 7", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ALimitedTextEntryUsesTheLimitedCommand()
    {
        string script = Revision(WithElement(new TextEntryElement
        {
            Size = new GumpSize(120, 20),
            EntryId = 2,
            MaxLength = 40,
        }));

        Assert.Contains("textentrylimited 0 0 120 20 0 2 0 40", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ACroppedLabelUsesCroppedText()
    {
        string script = Revision(WithElement(new LabelElement
        {
            Text = "clip",
            Hue = 5,
            Cropped = true,
            Size = new GumpSize(90, 18),
        }));

        Assert.Contains("croppedtext 0 0 90 18 5 0", script, StringComparison.Ordinal);
    }

    [Fact]
    public void AButtonWithTileArtAppendsTheOverlayParameters()
    {
        string script = Revision(WithElement(new ButtonElement
        {
            NormalId = 1,
            PressedId = 2,
            Kind = ButtonKind.Reply,
            Param = 3,
            TileId = 3821,
            TileHue = 33,
            TileX = 4,
            TileY = 5,
        }));

        Assert.Contains("buttontileart 0 0 1 2 1 0 3 3821 33 4 5", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ALocalizedHtmlAreaWithArgumentsUsesTok()
    {
        string script = Revision(WithElement(new HtmlElement
        {
            Size = new GumpSize(200, 60),
            ContentKind = HtmlContentKind.Localized,
            ClilocId = 1049004,
            ShowBackground = true,
            Color = 32767,
            Arguments = "Bob@42",
        }));

        Assert.Contains("xmfhtmltok 0 0 200 60 1 0 32767 1049004 @Bob@42@", script, StringComparison.Ordinal);
    }

    [Fact]
    public void TooltipsFollowTheElementTheyAttachTo()
    {
        string script = Revision(WithElement(new ImageElement
        {
            GumpId = 55,
            TooltipClilocId = 1042971,
        }));

        int image = script.IndexOf("gumppic 0 0 55", StringComparison.Ordinal);
        int tooltip = script.IndexOf("tooltip 1042971", StringComparison.Ordinal);

        Assert.True(image >= 0 && tooltip > image, script);
    }

    /// <summary>
    /// The 0.99 dialect is a fixed set of script functions, so commands with no
    /// function are written as comments rather than as calls that would not run.
    /// </summary>
    [Fact]
    public void TheModernDialectCommentsOutWhatItCannotExpress()
    {
        string script = Modern(WithElement(new PicInPicElement
        {
            GumpId = 9000,
            Size = new GumpSize(20, 30),
            SourceX = 4,
            SourceY = 5,
        }));

        Assert.Contains("// picinpic 0 0 9000 4 5 20 30", script, StringComparison.Ordinal);
        Assert.Contains("no 0.99 function", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// Only the overlay is unavailable, not the button. Commenting the whole
    /// command out would take the button with it and leave a dialog the player
    /// cannot dismiss.
    /// </summary>
    [Fact]
    public void TheModernDialectKeepsTheButtonWhenOnlyItsTileArtIsUnavailable()
    {
        string script = Modern(WithElement(new ButtonElement
        {
            NormalId = 1,
            PressedId = 2,
            Kind = ButtonKind.Reply,
            Param = 3,
            TileId = 3821,
        }));

        Assert.Contains("Button(0,0,1,2,1,0,3)", script, StringComparison.Ordinal);
        Assert.Contains("// buttontileart 0 0 1 2 1 0 3 3821 0 0 0", script, StringComparison.Ordinal);
        Assert.Contains("the line above is the closest it has", script, StringComparison.Ordinal);
    }

    [Fact]
    public void TheModernDialectKeepsACroppedLabelAsPlainText()
    {
        string script = Modern(WithElement(new LabelElement
        {
            Text = "clip",
            Hue = 5,
            Cropped = true,
            Size = new GumpSize(90, 18),
        }));

        Assert.Contains("TextA(0,0,5,\"clip\")", script, StringComparison.Ordinal);
        Assert.Contains("// croppedtext 0 0 90 18 5 0", script, StringComparison.Ordinal);
    }

    [Fact]
    public void TheModernDialectFallsBackToAFullTintForAPartialHue()
    {
        string script = Modern(WithElement(new ImageElement
        {
            GumpId = 55,
            Hue = 33,
            PartialHue = true,
        }));

        Assert.Contains("GumpPic(0,0,55,33)", script, StringComparison.Ordinal);
        Assert.Contains("// gumppicphued 0 0 55 33", script, StringComparison.Ordinal);
    }

    [Fact]
    public void TheModernDialectKeepsALocalizedHtmlAreaWithoutItsColourOrArguments()
    {
        string script = Modern(WithElement(new HtmlElement
        {
            Size = new GumpSize(200, 60),
            ContentKind = HtmlContentKind.Localized,
            ClilocId = 1049004,
            Color = 32767,
            Arguments = "Bob",
        }));

        Assert.Contains("XmfHtmlGump(0,0,200,60,1049004,0,0)", script, StringComparison.Ordinal);
        Assert.Contains("// xmfhtmltok 0 0 200 60 0 0 32767 1049004 @Bob@", script, StringComparison.Ordinal);
    }

    [Fact]
    public void GumpLevelTokensAreEmittedOnlyWhenSet()
    {
        GumpDocument document = new();

        string off = Revision(document);

        Assert.DoesNotContain("mastergump", off, StringComparison.Ordinal);
        Assert.DoesNotContain("echandleinput", off, StringComparison.Ordinal);

        document.Properties.MasterGumpId = 3000;
        document.Properties.UpperWordCase = true;
        document.Properties.CroppedText = true;
        document.Properties.EnhancedClientInput = true;

        string on = Revision(document);

        Assert.Contains("mastergump 3000", on, StringComparison.Ordinal);
        Assert.Contains("toggleupperwordcase", on, StringComparison.Ordinal);
        Assert.Contains("togglecroppedtext", on, StringComparison.Ordinal);
        Assert.Contains("echandleinput", on, StringComparison.Ordinal);
    }

    [Fact]
    public void TheExporterIsDiscoverableThroughThePluginContract()
    {
        SphereExporterPlugin plugin = new();
        RecordingHost host = new();

        plugin.Initialize(host);

        Assert.Equal("gumpstudio.exporters.sphere", plugin.Info.Id);
        Assert.Equal(["sphere-056", "sphere-099"], host.Exporters.Select(e => e.Id));

        IGumpExporter exporter = host.Exporters[0];

        Assert.Equal(".scp", exporter.FileExtension);
        Assert.Contains(
            "[DIALOG Test]",
            exporter.Export(new GumpDocument(), new GumpExportOptions { GumpName = "Test" }),
            StringComparison.Ordinal);
    }

    private static int Occurrences(string text, string value) => text.Split(value).Length - 1;

    private sealed class RecordingHost : IPluginHost
    {
        public List<IGumpExporter> Exporters { get; } = [];

        public IGumpDocumentSession Session => throw new NotSupportedException();

        public void RegisterExporter(IGumpExporter exporter) => Exporters.Add(exporter);

        public void RegisterMenuCommand(MenuCommandDescriptor descriptor)
        {
        }

        public void Notify(PluginNotificationLevel level, string message)
        {
        }
    }
}
