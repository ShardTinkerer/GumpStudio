using GumpStudio.Core.Document;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Legacy;
using GumpStudio.Core.Primitives;
using GumpStudio.TestSupport;

using Xunit;

namespace GumpStudio.Core.Tests;

/// <summary>
/// Tests for reading GumpStudio 1.8 files.
/// </summary>
/// <remarks>
/// The fixtures are synthesized NRBF payloads whose member names and ordering
/// mirror the original's <c>GetObjectData</c>, recovered by decompiling the
/// shipped binary. They prove the importer handles the format as documented;
/// only a genuine 1.8 file could confirm the recovered layout itself, and none
/// was available.
/// </remarks>
public class LegacyImportTests
{
    private static GumpDocument Import(byte[] payload)
    {
        using MemoryStream stream = new(payload);

        return LegacyGumpImporter.ImportDocument(stream);
    }

    [Fact]
    public void ReadsASinglePageWithOneElement()
    {
        byte[] payload = LegacyGumpFixture.BuildDocument(
            [LegacyGumpFixture.PageRoot(LegacyGumpFixture.Label(10, 20, "Hello"))]);

        GumpDocument document = Import(payload);

        Assert.Equal(1, document.PageCount);

        LabelElement label = Assert.IsType<LabelElement>(document.Pages[0].Root.Children.Single());

        Assert.Equal("Hello", label.Text);
        Assert.Equal(new GumpPoint(10, 20), label.Location);
    }

    [Fact]
    public void ReadsEveryElementType()
    {
        byte[] payload = LegacyGumpFixture.BuildDocument(
        [
            LegacyGumpFixture.PageRoot(
                LegacyGumpFixture.Background(0, 0, 400, 300, 5054),
                LegacyGumpFixture.Alpha(2, 2, 100, 50),
                LegacyGumpFixture.Tiled(4, 4, 60, 60, 4, 3),
                LegacyGumpFixture.Image(6, 6, 1417, 32),
                LegacyGumpFixture.Item(8, 8, 3821, 11),
                LegacyGumpFixture.Label(10, 10, "Text", 87, 3),
                LegacyGumpFixture.Button(12, 12, 247, 248, 0, 5),
                LegacyGumpFixture.Checkbox(14, 14, true, 4),
                LegacyGumpFixture.Radio(16, 16, true, 5, 9),
                LegacyGumpFixture.TextEntry(18, 18, 120, 20, "type", 3),
                LegacyGumpFixture.Html(20, 20, 200, 60, "<b>x</b>", 1049004, 1)),
        ]);

        IReadOnlyList<Element> children = Import(payload).Pages[0].Root.Children;

        Assert.Equal(11, children.Count);

        Assert.Equal(5054, Assert.IsType<BackgroundElement>(children[0]).GumpId);
        Assert.IsType<AlphaElement>(children[1]);

        TiledElement tiled = Assert.IsType<TiledElement>(children[2]);
        Assert.Equal(4, tiled.GumpId);

        Assert.Equal(1417, Assert.IsType<ImageElement>(children[3]).GumpId);
        Assert.Equal(3821, Assert.IsType<ItemElement>(children[4]).ItemId);

        LabelElement label = Assert.IsType<LabelElement>(children[5]);
        Assert.Equal("Text", label.Text);
        Assert.Equal(3, label.FontIndex);

        ButtonElement button = Assert.IsType<ButtonElement>(children[6]);
        Assert.Equal(247, button.NormalId);
        Assert.Equal(248, button.PressedId);
        Assert.Equal(ButtonKind.Page, button.Kind);
        Assert.Equal(5, button.Param);

        CheckboxElement checkbox = Assert.IsType<CheckboxElement>(children[7]);
        Assert.True(checkbox.IsChecked);
        Assert.Equal(4, checkbox.GroupId);

        RadioElement radio = Assert.IsType<RadioElement>(children[8]);
        Assert.Equal(9, radio.Value);
        Assert.Equal(5, radio.GroupId);

        TextEntryElement entry = Assert.IsType<TextEntryElement>(children[9]);
        Assert.Equal("type", entry.InitialText);
        Assert.Equal(3, entry.EntryId);
        Assert.Equal(new GumpSize(120, 20), entry.Size);

        HtmlElement html = Assert.IsType<HtmlElement>(children[10]);
        Assert.Equal("<b>x</b>", html.Html);
        Assert.Equal(1049004, html.ClilocId);
        Assert.Equal(HtmlContentKind.Localized, html.ContentKind);
        Assert.True(html.ShowScrollbar);
    }

    [Fact]
    public void ReadsNestedGroupsAndKeepsRelativePositions()
    {
        byte[] payload = LegacyGumpFixture.BuildDocument(
        [
            LegacyGumpFixture.PageRoot(
                LegacyGumpFixture.Group(100, 50, LegacyGumpFixture.Label(3, 4, "inner"))),
        ]);

        GroupElement group = Assert.IsType<GroupElement>(
            Import(payload).Pages[0].Root.Children.Single());

        Assert.Equal(new GumpPoint(100, 50), group.Location);

        Element inner = group.Children.Single();

        Assert.Equal(new GumpPoint(3, 4), inner.Location);
        Assert.Equal(new GumpPoint(103, 54), inner.GetAbsolutePosition());
    }

    [Fact]
    public void ReadsMultiplePages()
    {
        byte[] payload = LegacyGumpFixture.BuildDocument(
        [
            LegacyGumpFixture.PageRoot(LegacyGumpFixture.Label(0, 0, "one")),
            LegacyGumpFixture.PageRoot(LegacyGumpFixture.Label(0, 0, "two")),
            LegacyGumpFixture.PageRoot(),
        ]);

        GumpDocument document = Import(payload);

        Assert.Equal(3, document.PageCount);
        Assert.Equal("one", ((LabelElement)document.Pages[0].Root.Children[0]).Text);
        Assert.Equal("two", ((LabelElement)document.Pages[1].Root.Children[0]).Text);
        Assert.Empty(document.Pages[2].Root.Children);
    }

    [Fact]
    public void ReadsTheTrailingPropertiesPayload()
    {
        byte[] payload = LegacyGumpFixture.BuildDocument(
            [LegacyGumpFixture.PageRoot()],
            LegacyGumpFixture.Properties(120, 80, movable: false, closable: false, disposable: false, type: 7));

        GumpProperties properties = Import(payload).Properties;

        Assert.Equal(new GumpPoint(120, 80), properties.Location);
        Assert.False(properties.Movable);
        Assert.False(properties.Closable);
        Assert.False(properties.Disposable);
        Assert.Equal(7, properties.TypeId);
    }

    /// <summary>
    /// Early builds wrote only the page list. The original's own loader caught
    /// the resulting failure; the importer must simply carry on.
    /// </summary>
    [Fact]
    public void AMissingPropertiesPayloadIsNotAnError()
    {
        byte[] payload = LegacyGumpFixture.BuildDocument([LegacyGumpFixture.PageRoot()]);

        GumpDocument document = Import(payload);

        Assert.Equal(1, document.PageCount);
        Assert.True(document.Properties.Movable);
    }

    /// <summary>
    /// The old format stored the zero-based <c>Hue.Index</c>, while the new model
    /// and every gump script use one-based hues with 0 meaning "no hue".
    /// </summary>
    [Fact]
    public void ConvertsHueIndicesToTheOneBasedConvention()
    {
        byte[] payload = LegacyGumpFixture.BuildDocument(
        [
            LegacyGumpFixture.PageRoot(
                LegacyGumpFixture.Image(0, 0, 100, hueIndex: 0),
                LegacyGumpFixture.Image(0, 0, 100, hueIndex: 32)),
        ]);

        IReadOnlyList<Element> children = Import(payload).Pages[0].Root.Children;

        Assert.Equal(0, ((ImageElement)children[0]).Hue);
        Assert.Equal(33, ((ImageElement)children[1]).Hue);
    }

    [Fact]
    public void RejectsAFileThatIsNotNrbf()
    {
        using MemoryStream stream = new(new byte[256]);

        InvalidDataException error = Assert.Throws<InvalidDataException>(
            () => LegacyGumpImporter.ImportDocument(stream));

        Assert.Contains("GumpStudio 1.8", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnImportedDocumentSurvivesASaveAndReloadInTheNewFormat()
    {
        byte[] payload = LegacyGumpFixture.BuildDocument(
        [
            LegacyGumpFixture.PageRoot(
                LegacyGumpFixture.Group(100, 50, LegacyGumpFixture.Label(3, 4, "inner")),
                LegacyGumpFixture.Button(12, 12, 247, 248, 1, 5)),
        ],
        LegacyGumpFixture.Properties(10, 20));

        GumpDocument imported = Import(payload);

        // The whole point of the importer is migration, so the result has to be
        // expressible in the new format without loss.
        GumpDocument reloaded = Serialization.GumpXmlSerializer.FromXml(
            Serialization.GumpXmlSerializer.ToXml(imported));

        Assert.Equal(
            Serialization.GumpXmlSerializer.ToXml(imported).ToString(),
            Serialization.GumpXmlSerializer.ToXml(reloaded).ToString());

        Assert.Equal(new GumpPoint(10, 20), reloaded.Properties.Location);
    }
}
