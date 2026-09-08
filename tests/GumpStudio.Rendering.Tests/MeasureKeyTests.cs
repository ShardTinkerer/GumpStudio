using GumpStudio.Core.Document;
using GumpStudio.Core.Elements;
using GumpStudio.Rendering;
using GumpStudio.TestSupport;

using Xunit;

namespace GumpStudio.Rendering.Tests;

/// <summary>
/// That measuring an element asks the art source for exactly what painting it
/// will ask for.
/// </summary>
/// <remarks>
/// They disagreed: the measure pass dropped the hue and the font family, so a
/// hued or ASCII element was decoded once for its size and again for its
/// pixels, under two different cache keys. For an ASCII label it was also
/// wrong, not just wasteful — it was measured with a Unicode face and drawn
/// with an ASCII one.
/// </remarks>
public class MeasureKeyTests
{
    private static (TestArtSource Art, GumpRenderer Renderer) NewRenderer()
    {
        TestArtSource art = new(new FakeArtSource());

        return (art, new GumpRenderer(art));
    }

    private static GumpPage PageWith(Element element)
    {
        GumpDocument document = new();

        document.Pages[0].Root.Add(element);

        return document.Pages[0];
    }

    [Fact]
    public void AnAsciiLabelIsMeasuredWithTheFaceItWillBeDrawnIn()
    {
        (TestArtSource art, GumpRenderer renderer) = NewRenderer();

        GumpPage page = PageWith(new LabelElement
        {
            Text = "hello",
            FontFamily = GumpFontFamily.Ascii,
            FontIndex = 3,
        });

        renderer.MeasureContentSizes(page);

        Assert.Contains((GumpFontFamily.Ascii, 3), art.FontRequests);
        Assert.DoesNotContain((GumpFontFamily.Unicode, 3), art.FontRequests);
    }

    [Fact]
    public void AHuedLabelIsMeasuredAtItsOwnHue()
    {
        (TestArtSource art, GumpRenderer renderer) = NewRenderer();

        GumpPage page = PageWith(new LabelElement { Text = "hello", Hue = 42, FontIndex = 4 });

        renderer.MeasureContentSizes(page);

        Assert.Contains((4, "hello", 42, GumpFontFamily.Unicode), art.TextKeys);
    }

    [Fact]
    public void MeasuringAndPaintingALabelUseTheSameKey()
    {
        (TestArtSource art, GumpRenderer renderer) = NewRenderer();

        GumpPage page = PageWith(new LabelElement
        {
            Text = "hello",
            Hue = 42,
            FontFamily = GumpFontFamily.Ascii,
            FontIndex = 2,
        });

        renderer.MeasureContentSizes(page);

        int afterMeasure = art.TextKeys.Count;

        Assert.Equal(1, afterMeasure);

        using (SKBitmapHolder holder = new(renderer.RenderToBitmap(page, 64, 32, RenderOptions.Plain)))
        {
            // The paint pass asked for something; whatever it was must match.
            Assert.True(art.TextKeys.Count > afterMeasure);
        }

        Assert.All(art.TextKeys, key => Assert.Equal(art.TextKeys[0], key));
    }

    [Fact]
    public void AHuedImageIsNotDecodedTwiceUnderTwoKeys()
    {
        (TestArtSource art, GumpRenderer renderer) = NewRenderer();

        GumpPage page = PageWith(new ImageElement { GumpId = 100, Hue = 7 });

        renderer.MeasureContentSizes(page);

        // Measured through TryGetGumpSize, which is not an image lookup at all,
        // so nothing unhued is pulled into the cache.
        Assert.DoesNotContain((100, 0), art.GumpRequests);
    }

    [Fact]
    public void APressedButtonIsMeasuredFromThePressedFace()
    {
        (TestArtSource art, GumpRenderer renderer) = NewRenderer();

        GumpPage page = PageWith(new ButtonElement
        {
            NormalId = 200,
            PressedId = 201,
            State = ButtonState.Pressed,
        });

        renderer.MeasureContentSizes(page);

        Assert.Contains(201, art.SizeRequests);
        Assert.DoesNotContain(200, art.SizeRequests);
    }
}

/// <summary>Disposes a bitmap the renderer handed over.</summary>
internal sealed class SKBitmapHolder(SkiaSharp.SKBitmap bitmap) : IDisposable
{
    public void Dispose() => bitmap.Dispose();
}
