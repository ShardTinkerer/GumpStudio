using System.Xml.Linq;

using GumpStudio.Core.Document;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Primitives;
using GumpStudio.Core.Serialization;
using GumpStudio.TestSupport;

using Xunit;

namespace GumpStudio.Core.Tests;

public class GumpXmlSerializerTests
{
    /// <summary>A document exercising every element type and a nested group.</summary>
    private static GumpDocument BuildSample()
    {
        GumpDocument document = new();

        document.Properties.Location = new GumpPoint(120, 80);
        document.Properties.Movable = false;
        document.Properties.Closable = false;
        document.Properties.Disposable = false;
        document.Properties.TypeId = 42;
        document.Properties.MasterGumpId = 3000;
        document.Properties.UpperWordCase = true;
        document.Properties.CroppedText = true;
        document.Properties.EnhancedClientInput = true;

        GumpPage page = document.Pages[0];

        page.Root.Add(new BackgroundElement
        {
            Name = "Frame",
            Location = new GumpPoint(0, 0),
            Size = new GumpSize(400, 300),
            GumpId = 5054,
        });

        page.Root.Add(new AlphaElement { Location = new GumpPoint(5, 5), Size = new GumpSize(390, 290) });
        page.Root.Add(new TiledElement { Location = new GumpPoint(8, 8), Size = new GumpSize(50, 50), GumpId = 4, Hue = 7 });
        page.Root.Add(new ImageElement
        {
            Location = new GumpPoint(10, 10),
            GumpId = 1417,
            Hue = 33,
            PartialHue = true,
        });

        page.Root.Add(new PicInPicElement
        {
            Location = new GumpPoint(12, 14),
            Size = new GumpSize(60, 24),
            GumpId = 9000,
            SourceX = 5,
            SourceY = 6,
            Hue = 21,
            PartialHue = true,
        });

        page.Root.Add(new TileAsGumpElement
        {
            Location = new GumpPoint(16, 18),
            ItemId = 3823,
            LinkId = 2,
            ParamB = 3,
            ParamC = 4,
        });
        page.Root.Add(new ItemElement { Location = new GumpPoint(20, 20), ItemId = 3821, Hue = 12 });
        page.Root.Add(new LabelElement
        {
            Location = new GumpPoint(30, 30),
            Text = "Hello <&> \"world\"",
            Hue = 88,
            FontIndex = 3,
            Cropped = true,
            Size = new GumpSize(90, 18),
            Comment = "a comment",
            TooltipClilocId = 1042971,
            TooltipArguments = "Bob@42",
        });

        page.Root.Add(new ButtonElement
        {
            Location = new GumpPoint(40, 40),
            NormalId = 247,
            PressedId = 248,
            Kind = ButtonKind.Page,
            Param = 2,
            CodeBehind = "// handler",
            TileId = 3821,
            TileHue = 33,
            TileX = 4,
            TileY = 5,
            ItemPropertySerial = 0x40001234,
        });

        page.Root.Add(new CheckboxElement
        {
            Location = new GumpPoint(50, 50),
            CheckedId = 211,
            UncheckedId = 210,
            IsChecked = true,
            GroupId = 4,
        });

        page.Root.Add(new RadioElement
        {
            Location = new GumpPoint(60, 60),
            CheckedId = 208,
            UncheckedId = 209,
            GroupId = 5,
            Value = 9,
            IsChecked = true,
        });

        page.Root.Add(new TextEntryElement
        {
            Location = new GumpPoint(70, 70),
            Size = new GumpSize(120, 20),
            InitialText = "type here",
            Hue = 5,
            EntryId = 3,
            MaxLength = 40,
        });

        page.Root.Add(new HtmlElement
        {
            Location = new GumpPoint(80, 80),
            Size = new GumpSize(200, 60),
            Html = "<b>bold</b>",
            ClilocId = 1049004,
            ShowScrollbar = true,
            ShowBackground = true,
            ContentKind = HtmlContentKind.Localized,
            Color = 0x7FFF,
            Arguments = "Bob@42",
        });

        GroupElement group = new() { Name = "Nested", Location = new GumpPoint(100, 100) };

        group.Add(new LabelElement { Location = new GumpPoint(3, 4), Text = "inside" });
        page.Root.Add(group);

        GumpPage second = document.AddPage("Second");
        second.Root.Add(new LabelElement { Text = "page two" });

        return document;
    }

    [Fact]
    public void RoundTripsEveryElementType()
    {
        GumpDocument original = BuildSample();
        GumpDocument reloaded = GumpXmlSerializer.FromXml(GumpXmlSerializer.ToXml(original));

        // Comparing the re-serialised XML catches any property the writer emits
        // and the reader drops, across every type at once.
        Assert.Equal(
            GumpXmlSerializer.ToXml(original).ToString(),
            GumpXmlSerializer.ToXml(reloaded).ToString());
    }

    /// <summary>
    /// The preview font survives a save and load, for every element that has one.
    /// </summary>
    /// <remarks>
    /// Nothing an exporter writes depends on it, which is exactly why a
    /// round-trip test is worth having: a property that reaches no output is one
    /// that can be dropped by the serializer without any other test noticing.
    /// </remarks>
    [Fact]
    public void RoundTripsThePreviewFont()
    {
        GumpDocument original = new();

        original.Pages[0].Root.Add(new LabelElement
        {
            Text = "label",
            FontFamily = GumpFontFamily.Ascii,
            FontIndex = 3,
        });

        original.Pages[0].Root.Add(new HtmlElement { Html = "html", FontIndex = 6 });

        original.Pages[0].Root.Add(new TextEntryElement
        {
            InitialText = "entry",
            FontFamily = GumpFontFamily.Ascii,
            FontIndex = 9,
        });

        GumpDocument reloaded = GumpXmlSerializer.FromXml(GumpXmlSerializer.ToXml(original));
        List<IFontedElement> fonts = [.. reloaded.Pages[0].Leaves().OfType<IFontedElement>()];

        Assert.Equal(
            [(GumpFontFamily.Ascii, 3), (GumpFontFamily.Unicode, 6), (GumpFontFamily.Ascii, 9)],
            fonts.Select(f => (f.FontFamily, f.FontIndex)));
    }

    /// <summary>
    /// A file written before the font was selectable keeps each element's default.
    /// </summary>
    /// <remarks>
    /// Labels always stored a font, so they keep whatever they had. HTML areas
    /// and text entries never did, and must not be forced to zero — that is the
    /// ornate face the selectable font exists to get away from.
    /// </remarks>
    [Fact]
    public void ReadsAnOlderFileWithoutAFontAttribute()
    {
        GumpDocument document = GumpXmlSerializer.FromXml(XDocument.Parse(
            """
            <gump version="4">
              <properties />
              <page name="Page 0">
                <label hue="0" font="0" cropped="false" />
                <html kind="Html" clilocId="1000000" scrollbar="false" background="false" color="0" />
              </page>
            </gump>
            """));

        List<Element> elements = [.. document.Pages[0].Leaves()];

        Assert.Equal(0, ((LabelElement)elements[0]).FontIndex);
        Assert.Equal(TextElementDefaults.FontIndex, ((HtmlElement)elements[1]).FontIndex);
    }

    [Fact]
    public void RoundTripsThroughAFile()
    {
        using TempDirectory dir = new();

        string path = Path.Combine(dir.Path, "sample.gump");
        GumpDocument original = BuildSample();

        GumpXmlSerializer.Save(original, path);

        GumpDocument reloaded = GumpXmlSerializer.Load(path);

        Assert.Equal(2, reloaded.PageCount);
        Assert.Equal(
            GumpXmlSerializer.ToXml(original).ToString(),
            GumpXmlSerializer.ToXml(reloaded).ToString());
    }

    [Fact]
    public void PreservesGumpProperties()
    {
        GumpDocument reloaded = GumpXmlSerializer.FromXml(GumpXmlSerializer.ToXml(BuildSample()));

        Assert.Equal(new GumpPoint(120, 80), reloaded.Properties.Location);
        Assert.False(reloaded.Properties.Movable);
        Assert.False(reloaded.Properties.Closable);
        Assert.False(reloaded.Properties.Disposable);
        Assert.Equal(42, reloaded.Properties.TypeId);
        Assert.Equal(3000, reloaded.Properties.MasterGumpId);
        Assert.True(reloaded.Properties.UpperWordCase);
        Assert.True(reloaded.Properties.CroppedText);
        Assert.True(reloaded.Properties.EnhancedClientInput);
    }

    [Fact]
    public void PreservesTheCommandsAddedAfterVersion18()
    {
        GumpDocument reloaded = GumpXmlSerializer.FromXml(GumpXmlSerializer.ToXml(BuildSample()));
        IReadOnlyList<Element> children = reloaded.Pages[0].Root.Children;

        PicInPicElement pic = children.OfType<PicInPicElement>().Single();

        Assert.Equal(9000, pic.GumpId);
        Assert.Equal(5, pic.SourceX);
        Assert.Equal(6, pic.SourceY);
        Assert.Equal(new GumpSize(60, 24), pic.Size);
        Assert.True(pic.PartialHue);

        TileAsGumpElement tile = children.OfType<TileAsGumpElement>().Single();

        Assert.Equal(3823, tile.ItemId);
        Assert.Equal(2, tile.LinkId);
        Assert.Equal(3, tile.ParamB);
        Assert.Equal(4, tile.ParamC);

        Assert.True(children.OfType<ImageElement>().Single().PartialHue);

        ButtonElement button = children.OfType<ButtonElement>().Single();

        Assert.Equal(3821, button.TileId);
        Assert.Equal(33, button.TileHue);
        Assert.Equal(4, button.TileX);
        Assert.Equal(5, button.TileY);
        Assert.Equal(0x40001234, button.ItemPropertySerial);

        HtmlElement html = children.OfType<HtmlElement>().Single();

        Assert.Equal(0x7FFF, html.Color);
        Assert.Equal("Bob@42", html.Arguments);
    }

    /// <summary>
    /// A label is only resizable once it is cropped, so the reader has to set
    /// <c>Cropped</c> before it assigns the size. Reading them the other way round
    /// silently discards the crop rectangle, because
    /// <see cref="Element.Size"/> ignores writes to a non-resizable element.
    /// </summary>
    [Fact]
    public void ACroppedLabelKeepsItsRectangle()
    {
        LabelElement label = new()
        {
            Text = "clip me",
            Cropped = true,
            Size = new GumpSize(90, 18),
        };

        GumpDocument document = new();

        document.Pages[0].Root.Add(label);

        LabelElement reloaded = GumpXmlSerializer
            .FromXml(GumpXmlSerializer.ToXml(document))
            .Pages[0].Root.Children.OfType<LabelElement>().Single();

        Assert.True(reloaded.Cropped);
        Assert.Equal(new GumpSize(90, 18), reloaded.Size);
    }

    [Fact]
    public void AnUncroppedLabelWritesNoRectangle()
    {
        GumpDocument document = new();

        document.Pages[0].Root.Add(new LabelElement { Text = "measure me" });

        XElement node = GumpXmlSerializer.ToXml(document).Root!.Element("page")!.Element("label")!;

        // Its extent comes from the rendered text, so persisting one would just
        // go stale the next time the font or the string changed.
        Assert.Null(node.Attribute("w"));
        Assert.Null(node.Attribute("h"));
    }

    [Fact]
    public void TooltipAttributesAreWrittenOnlyWhenSet()
    {
        GumpDocument document = new();

        document.Pages[0].Root.Add(new ImageElement { GumpId = 5 });

        XElement plain = GumpXmlSerializer.ToXml(document).Root!.Element("page")!.Element("image")!;

        Assert.Null(plain.Attribute("tooltip"));
        Assert.Null(plain.Attribute("tooltipArgs"));
        Assert.Null(plain.Attribute("itemProperty"));
    }

    [Fact]
    public void PreservesNestingAndAbsolutePositions()
    {
        GumpDocument reloaded = GumpXmlSerializer.FromXml(GumpXmlSerializer.ToXml(BuildSample()));

        GroupElement group = reloaded.Pages[0].Root.Children.OfType<GroupElement>().Single();
        Element inner = group.Children.Single();

        Assert.Equal(new GumpPoint(3, 4), inner.Location);
        Assert.Equal(new GumpPoint(103, 104), inner.GetAbsolutePosition());
    }

    [Fact]
    public void EscapesTextThatWouldOtherwiseBreakTheMarkup()
    {
        GumpDocument reloaded = GumpXmlSerializer.FromXml(GumpXmlSerializer.ToXml(BuildSample()));

        LabelElement label = reloaded.Pages[0].Root.Children.OfType<LabelElement>().First();

        Assert.Equal("Hello <&> \"world\"", label.Text);
        Assert.Equal("a comment", label.Comment);
    }

    [Fact]
    public void UnknownElementTypesAreSkippedNotFatal()
    {
        GumpDocument original = BuildSample();
        XDocument xml = GumpXmlSerializer.ToXml(original);

        xml.Root!.Elements("page").First().Add(new XElement("hologram", new XAttribute("x", 1)));

        GumpDocument reloaded = GumpXmlSerializer.FromXml(xml);

        // A file from a newer build must still open, minus what we cannot model.
        // Counted against the sample rather than a literal, so growing the sample
        // does not break this.
        Assert.Equal(
            original.Pages[0].Root.Children.Count,
            reloaded.Pages[0].Root.Children.Count);
    }

    [Fact]
    public void RefusesAFileFromANewerFormatVersion()
    {
        XDocument xml = GumpXmlSerializer.ToXml(new GumpDocument());

        xml.Root!.SetAttributeValue("version", GumpXmlSerializer.CurrentVersion + 1);

        InvalidDataException error = Assert.Throws<InvalidDataException>(
            () => GumpXmlSerializer.FromXml(xml));

        Assert.Contains("newer version", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesSomethingThatIsNotAGumpFile()
    {
        Assert.Throws<InvalidDataException>(
            () => GumpXmlSerializer.FromXml(new XDocument(new XElement("html"))));
    }

    /// <summary>
    /// The format must not be tied to CLR type identity the way the old
    /// BinaryFormatter one was: type names on disk are the stable
    /// <see cref="Element.TypeName"/> values, not namespace-qualified class names.
    /// </summary>
    [Fact]
    public void UsesStableTypeNamesRatherThanClrTypeNames()
    {
        string xml = GumpXmlSerializer.ToXml(BuildSample()).ToString();

        Assert.DoesNotContain("GumpStudio.Core", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("Element,", xml, StringComparison.Ordinal);
        Assert.Contains("<label", xml, StringComparison.Ordinal);
        Assert.Contains("<textentry", xml, StringComparison.Ordinal);
    }
}
