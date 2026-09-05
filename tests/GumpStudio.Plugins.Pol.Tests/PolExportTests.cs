using GumpStudio.Core.Document;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Primitives;
using GumpStudio.Plugins.Pol;

using Xunit;

namespace GumpStudio.Plugins.Pol.Tests;

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
        Assert.Contains("//Gump package does not support GumpPicTiled", script, StringComparison.Ordinal);
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
    public void TheExporterIsDiscoverableThroughThePluginContract()
    {
        PolExporterPlugin plugin = new();
        RecordingHost host = new();

        plugin.Initialize(host);

        Assert.Equal("gumpstudio.exporters.pol", plugin.Info.Id);

        Core.Export.IGumpExporter exporter = Assert.Single(host.Exporters);

        Assert.Equal("pol", exporter.Id);
        Assert.Equal(".src", exporter.FileExtension);
        Assert.Contains(
            "GFCreateGump",
            exporter.Export(new GumpDocument(), new Core.Export.GumpExportOptions { GumpName = "Test" }),
            StringComparison.Ordinal);
    }

    private sealed class RecordingHost : IPluginHost
    {
        public List<Core.Export.IGumpExporter> Exporters { get; } = [];

        public IGumpDocumentSession Session => throw new NotSupportedException();

        public void RegisterExporter(Core.Export.IGumpExporter exporter) => Exporters.Add(exporter);

        public void RegisterMenuCommand(MenuCommandDescriptor descriptor)
        {
        }

        public void Notify(PluginNotificationLevel level, string message)
        {
        }
    }
}
