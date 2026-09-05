using GumpStudio.Core.Document;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Primitives;
using GumpStudio.Rendering;
using GumpStudio.TestSupport;

using SkiaSharp;

using Xunit;

namespace GumpStudio.Rendering.Tests;

/// <summary>Adapts <see cref="FakeArtSource"/> to the renderer's contract.</summary>
internal sealed class TestArtSource(FakeArtSource art) : IGumpArtSource
{
    public SKImage? GetGump(int gumpId, int hue = 0, bool partialHue = false) => art.Lookup("gump", gumpId);

    public SKImage? GetItem(int itemId, int hue = 0, bool partialHue = false) => art.Lookup("item", itemId);

    public SKImage? GetText(int fontIndex, string text, int hue = 0) => art.MakeText(text);

    public bool TryGetGumpSize(int gumpId, out int width, out int height)
    {
        SKImage? image = art.Lookup("gump", gumpId);

        width = image?.Width ?? 0;
        height = image?.Height ?? 0;

        return image is not null;
    }
}

public class GumpRendererTests
{
    private static readonly SKColor Red = new(0xFF, 0x00, 0x00);
    private static readonly SKColor Green = new(0x00, 0xFF, 0x00);
    private static readonly SKColor Blue = new(0x00, 0x00, 0xFF);

    private static SKColor PixelAt(SKBitmap bitmap, int x, int y) => bitmap.GetPixel(x, y);

    [Fact]
    public void DrawsAnImageAtItsPosition()
    {
        using FakeArtSource art = new();

        art.AddGump(100, 10, 10, Red);

        GumpPage page = new();
        page.Root.Add(new ImageElement { GumpId = 100, Location = new GumpPoint(20, 30) });

        GumpRenderer renderer = new(new TestArtSource(art));

        using SKBitmap output = renderer.RenderToBitmap(page, 64, 64, RenderOptions.Plain);

        Assert.Equal(Red, PixelAt(output, 20, 30));
        Assert.Equal(Red, PixelAt(output, 29, 39));

        // Outside the image the canvas stays clear.
        Assert.Equal(0, PixelAt(output, 19, 30).Alpha);
        Assert.Equal(0, PixelAt(output, 30, 40).Alpha);
    }

    /// <summary>
    /// The defect this guards: the original never applied parent offsets when
    /// exporting, and its renderer tracked them separately from its geometry.
    /// </summary>
    [Fact]
    public void DrawsNestedElementsAtTheirAbsolutePosition()
    {
        using FakeArtSource art = new();

        art.AddGump(100, 4, 4, Green);

        GroupElement group = new() { Location = new GumpPoint(10, 20) };

        group.Add(new ImageElement { GumpId = 100, Location = new GumpPoint(5, 5) });

        GumpPage page = new();
        page.Root.Add(group);

        GumpRenderer renderer = new(new TestArtSource(art));

        using SKBitmap output = renderer.RenderToBitmap(page, 64, 64, RenderOptions.Plain);

        Assert.Equal(Green, PixelAt(output, 15, 25));
        Assert.Equal(0, PixelAt(output, 5, 5).Alpha);
    }

    [Fact]
    public void ZOrderPutsLaterChildrenInFront()
    {
        using FakeArtSource art = new();

        art.AddGump(1, 20, 20, Red);
        art.AddGump(2, 20, 20, Blue);

        GumpPage page = new();

        page.Root.Add(new ImageElement { GumpId = 1, Location = GumpPoint.Origin });
        page.Root.Add(new ImageElement { GumpId = 2, Location = GumpPoint.Origin });

        GumpRenderer renderer = new(new TestArtSource(art));

        using SKBitmap output = renderer.RenderToBitmap(page, 32, 32, RenderOptions.Plain);

        Assert.Equal(Blue, PixelAt(output, 5, 5));
    }

    [Fact]
    public void TilesATiledElementAcrossItsBoundsAndClipsAtTheEdge()
    {
        using FakeArtSource art = new();

        art.AddGump(50, 4, 4, Red);

        GumpPage page = new();

        page.Root.Add(new TiledElement
        {
            GumpId = 50,
            Location = GumpPoint.Origin,
            Size = new GumpSize(10, 10),
        });

        GumpRenderer renderer = new(new TestArtSource(art));

        using SKBitmap output = renderer.RenderToBitmap(page, 32, 32, RenderOptions.Plain);

        // Filled right up to the bound...
        Assert.Equal(Red, PixelAt(output, 0, 0));
        Assert.Equal(Red, PixelAt(output, 9, 9));

        // ...and clipped, even though 4 does not divide 10 evenly.
        Assert.Equal(0, PixelAt(output, 10, 0).Alpha);
        Assert.Equal(0, PixelAt(output, 0, 10).Alpha);
    }

    [Fact]
    public void MissingArtIsMarkedRatherThanSilentlySkipped()
    {
        using FakeArtSource art = new();

        GumpPage page = new();

        page.Root.Add(new TiledElement
        {
            GumpId = 999,
            Location = GumpPoint.Origin,
            Size = new GumpSize(20, 20),
        });

        GumpRenderer renderer = new(new TestArtSource(art));

        using SKBitmap output = renderer.RenderToBitmap(page, 32, 32, RenderOptions.Plain);

        Assert.Contains(999, art.MissingRequests);

        // The marker must actually put something on the canvas.
        bool anythingDrawn = false;

        for (int y = 0; y < 20 && !anythingDrawn; y++)
        {
            for (int x = 0; x < 20; x++)
            {
                if (PixelAt(output, x, y).Alpha != 0)
                {
                    anythingDrawn = true;

                    break;
                }
            }
        }

        Assert.True(anythingDrawn, "Missing art left the canvas completely blank.");
    }

    [Fact]
    public void MeasureContentSizesGivesArtBackedElementsTheirExtent()
    {
        using FakeArtSource art = new();

        art.AddGump(100, 12, 7, Red);
        art.AddItem(200, 5, 9, Blue);

        GumpPage page = new();

        ImageElement image = new() { GumpId = 100 };
        ItemElement item = new() { ItemId = 200 };

        page.Root.Add(image);
        page.Root.Add(item);

        Assert.True(image.Size.IsEmpty);

        new GumpRenderer(new TestArtSource(art)).MeasureContentSizes(page);

        Assert.Equal(new GumpSize(12, 7), image.Size);
        Assert.Equal(new GumpSize(5, 9), item.Size);
    }

    [Fact]
    public void SelectionDecorationIsOptional()
    {
        using FakeArtSource art = new();

        art.AddGump(100, 8, 8, Red);

        GumpPage page = new();
        AlphaElement element = new()
        {
            Location = new GumpPoint(20, 20),
            Size = new GumpSize(10, 10),
            IsSelected = true,
        };

        page.Root.Add(element);

        GumpRenderer renderer = new(new TestArtSource(art));

        using SKBitmap plain = renderer.RenderToBitmap(page, 64, 64, RenderOptions.Plain);
        using SKBitmap decorated = renderer.RenderToBitmap(page, 64, 64, new RenderOptions(DrawSelection: true));

        // A handle sits at the top-left corner, outside the element itself.
        Assert.Equal(0, PixelAt(plain, 18, 18).Alpha);
        Assert.NotEqual(0, PixelAt(decorated, 18, 18).Alpha);
    }

    [Fact]
    public void RenderingIsRepeatable()
    {
        using FakeArtSource art = new();

        art.AddGump(100, 10, 10, Red);
        art.AddNineSlice(200, 4, [Red, Green, Blue, Red, Green, Blue, Red, Green, Blue]);

        GumpPage page = new();

        page.Root.Add(new BackgroundElement
        {
            GumpId = 200,
            Location = GumpPoint.Origin,
            Size = new GumpSize(40, 30),
        });

        page.Root.Add(new ImageElement { GumpId = 100, Location = new GumpPoint(12, 8) });

        GumpRenderer renderer = new(new TestArtSource(art));

        using SKBitmap first = renderer.RenderToBitmap(page, 64, 64, RenderOptions.Plain);
        using SKBitmap second = renderer.RenderToBitmap(page, 64, 64, RenderOptions.Plain);

        // Byte-for-byte determinism is what makes golden-image tests viable.
        Assert.Equal(first.Bytes, second.Bytes);
    }
}

public class NineSliceTests
{
    private static readonly SKColor[] Pieces =
    [
        new(1, 0, 0),   // top-left
        new(2, 0, 0),   // top
        new(3, 0, 0),   // top-right
        new(4, 0, 0),   // left
        new(5, 0, 0),   // centre
        new(6, 0, 0),   // right
        new(7, 0, 0),   // bottom-left
        new(8, 0, 0),   // bottom
        new(9, 0, 0),   // bottom-right
    ];

    private static SKBitmap RenderFrame(int width, int height, int corner = 4)
    {
        using FakeArtSource art = new();

        art.AddNineSlice(500, corner, Pieces);

        GumpPage page = new();

        page.Root.Add(new BackgroundElement
        {
            GumpId = 500,
            Location = GumpPoint.Origin,
            Size = new GumpSize(width, height),
        });

        return new GumpRenderer(new TestArtSource(art))
            .RenderToBitmap(page, width + 10, height + 10, RenderOptions.Plain);
    }

    [Fact]
    public void PlacesEveryPieceInItsOwnBand()
    {
        using SKBitmap output = RenderFrame(40, 30);

        // Corners.
        Assert.Equal(Pieces[0].Red, output.GetPixel(0, 0).Red);
        Assert.Equal(Pieces[2].Red, output.GetPixel(39, 0).Red);
        Assert.Equal(Pieces[6].Red, output.GetPixel(0, 29).Red);
        Assert.Equal(Pieces[8].Red, output.GetPixel(39, 29).Red);

        // Edges, sampled between the corners.
        Assert.Equal(Pieces[1].Red, output.GetPixel(20, 0).Red);
        Assert.Equal(Pieces[7].Red, output.GetPixel(20, 29).Red);
        Assert.Equal(Pieces[3].Red, output.GetPixel(0, 15).Red);
        Assert.Equal(Pieces[5].Red, output.GetPixel(39, 15).Red);

        // Centre.
        Assert.Equal(Pieces[4].Red, output.GetPixel(20, 15).Red);
    }

    [Fact]
    public void ClipsToTheRequestedBoundsRatherThanOverhanging()
    {
        using SKBitmap output = RenderFrame(40, 30);

        Assert.Equal(0, output.GetPixel(40, 0).Alpha);
        Assert.Equal(0, output.GetPixel(0, 30).Alpha);
    }

    /// <summary>
    /// A frame dragged smaller than its own corners must still render without
    /// producing negative-width bands.
    /// </summary>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 20)]
    [InlineData(20, 3)]
    [InlineData(8, 8)]
    public void SurvivesBoundsSmallerThanItsBorders(int width, int height)
    {
        using SKBitmap output = RenderFrame(width, height);

        Assert.NotEqual(0, output.GetPixel(0, 0).Alpha);
    }
}
