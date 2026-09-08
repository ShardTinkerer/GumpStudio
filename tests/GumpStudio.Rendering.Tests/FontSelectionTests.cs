using GumpStudio.Core.Document;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Primitives;
using GumpStudio.TestSupport;

using SkiaSharp;

using Xunit;

namespace GumpStudio.Rendering.Tests;

/// <summary>
/// Which face each text element is previewed in.
/// </summary>
/// <remarks>
/// The renderer used to draw every localised area and text entry in Unicode font
/// 0 — the ornate blackletter face — with no way to change it, which is
/// unreadable at gump sizes and is not what the client uses for body text. Only
/// labels honoured a font at all.
/// </remarks>
public class FontSelectionTests
{
    [Fact]
    public void DrawsAnHtmlAreaInItsChosenFont()
    {
        Assert.Equal(
            (GumpFontFamily.Unicode, 4),
            Font(new HtmlElement { Html = "hello", FontIndex = 4 }));
    }

    [Fact]
    public void DrawsATextEntryInItsChosenFont()
    {
        Assert.Equal(
            (GumpFontFamily.Unicode, 6),
            Font(new TextEntryElement { InitialText = "hello", FontIndex = 6 }));
    }

    [Fact]
    public void DrawsALabelInItsChosenFont()
    {
        Assert.Equal(
            (GumpFontFamily.Unicode, 2),
            Font(new LabelElement { Text = "hello", FontIndex = 2 }));
    }

    /// <summary>
    /// The ten <c>fonts.mul</c> faces were loaded but nothing could reach them.
    /// </summary>
    [Fact]
    public void DrawsInTheAsciiFamilyWhenAsked()
    {
        Assert.Equal(
            (GumpFontFamily.Ascii, 3),
            Font(new LabelElement
            {
                Text = "hello",
                FontFamily = GumpFontFamily.Ascii,
                FontIndex = 3,
            }));
    }

    /// <summary>
    /// New text elements default to the plain face, not the ornate one.
    /// </summary>
    /// <remarks>
    /// Nothing an exporter writes changes with it — the protocol's text commands
    /// carry no font — so this is purely about the preview being readable.
    /// </remarks>
    [Theory]
    [InlineData("html")]
    [InlineData("entry")]
    [InlineData("label")]
    public void DefaultsToThePlainFace(string kind)
    {
        Element element = kind switch
        {
            "html" => new HtmlElement { Html = "hello" },
            "entry" => new TextEntryElement { InitialText = "hello" },
            _ => new LabelElement { Text = "hello" },
        };

        Assert.Equal((GumpFontFamily.Unicode, TextElementDefaults.FontIndex), Font(element));
    }

    /// <summary>The font the renderer asked for when drawing one element.</summary>
    private static (GumpFontFamily Family, int Index) Font(Element element)
    {
        using FakeArtSource art = new();

        element.Location = new GumpPoint(0, 0);

        if (element.IsResizable)
        {
            element.Size = new GumpSize(200, 40);
        }

        GumpPage page = new();
        page.Root.Add(element);

        TestArtSource source = new(art);

        using SKBitmap output =
            new GumpRenderer(source).RenderToBitmap(page, 220, 60, RenderOptions.Plain);

        return Assert.Single(source.FontRequests);
    }
}
