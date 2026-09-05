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
        page.Root.Add(new ImageElement { Location = new GumpPoint(10, 10), GumpId = 1417, Hue = 33 });
        page.Root.Add(new ItemElement { Location = new GumpPoint(20, 20), ItemId = 3821, Hue = 12 });
        page.Root.Add(new LabelElement
        {
            Location = new GumpPoint(30, 30),
            Text = "Hello <&> \"world\"",
            Hue = 88,
            FontIndex = 3,
            Cropped = true,
            Comment = "a comment",
        });

        page.Root.Add(new ButtonElement
        {
            Location = new GumpPoint(40, 40),
            NormalId = 247,
            PressedId = 248,
            Kind = ButtonKind.Page,
            Param = 2,
            CodeBehind = "// handler",
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
        XDocument xml = GumpXmlSerializer.ToXml(BuildSample());

        xml.Root!.Elements("page").First().Add(new XElement("hologram", new XAttribute("x", 1)));

        GumpDocument reloaded = GumpXmlSerializer.FromXml(xml);

        // A file from a newer build must still open, minus what we cannot model.
        Assert.Equal(12, reloaded.Pages[0].Root.Children.Count);
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
