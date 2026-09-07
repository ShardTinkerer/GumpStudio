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
    /// <summary>Cliloc strings this source can resolve, by id.</summary>
    public Dictionary<int, string> Clilocs { get; } = [];

    /// <summary>Every string the renderer asked to draw, in order.</summary>
    public List<string> TextRequests { get; } = [];

    /// <summary>The font each of those was asked for, in the same order.</summary>
    public List<(GumpFontFamily Family, int Index)> FontRequests { get; } = [];

    /// <summary>
    /// Every text lookup in full, so the measure pass and the paint pass can be
    /// compared key for key.
    /// </summary>
    public List<(int Index, string Text, int Hue, GumpFontFamily Family)> TextKeys { get; } = [];

    /// <summary>Every gump lookup, as (id, hue), in order.</summary>
    public List<(int Id, int Hue)> GumpRequests { get; } = [];

    /// <summary>Every dimension-only lookup, which decodes no pixels.</summary>
    public List<int> SizeRequests { get; } = [];

    public SKImage? GetGump(int gumpId, int hue = 0, bool partialHue = false)
    {
        GumpRequests.Add((gumpId, hue));

        return art.Lookup("gump", gumpId);
    }

    public SKImage? GetItem(int itemId, int hue = 0, bool partialHue = false) => art.Lookup("item", itemId);

    public SKImage? GetText(
        int fontIndex, string text, int hue = 0, GumpFontFamily family = GumpFontFamily.Unicode)
    {
        TextRequests.Add(text);
        FontRequests.Add((family, fontIndex));
        TextKeys.Add((fontIndex, text, hue, family));

        return art.MakeText(text);
    }

    public string? GetCliloc(int clilocId) =>
        Clilocs.TryGetValue(clilocId, out string? text) ? text : null;

    public bool TryGetGumpSize(int gumpId, out int width, out int height)
    {
        SizeRequests.Add(gumpId);

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
        using SKBitmap decorated = renderer.RenderToBitmap(page, 64, 64, new RenderOptions { DrawSelection = true });

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

/// <summary>
/// Page 0 is Ultima Online's always-visible layer: whatever it holds stays on
/// screen while the player switches between pages 1, 2 and so on.
/// </summary>
public class SharedPageTests
{
    private static readonly SKColor Shared = new(0xFF, 0x00, 0x00);
    private static readonly SKColor Active = new(0x00, 0xFF, 0x00);

    private static (GumpDocument Document, FakeArtSource Art) Build()
    {
        FakeArtSource art = new();

        art.AddGump(1, 10, 10, Shared);
        art.AddGump(2, 10, 10, Active);

        GumpDocument document = new();

        document.Pages[0].Root.Add(new ImageElement { GumpId = 1, Location = GumpPoint.Origin });

        GumpPage second = document.AddPage();

        second.Root.Add(new ImageElement { GumpId = 2, Location = new GumpPoint(20, 0) });

        return (document, art);
    }

    [Fact]
    public void Page0IsDrawnBeneathAnotherPage()
    {
        (GumpDocument document, FakeArtSource art) = Build();

        using (art)
        {
            using SKBitmap output = Render(document, art, activePage: 1, new RenderOptions());

            // Page 0's element and page 1's element are both on screen.
            Assert.Equal(Shared, output.GetPixel(5, 5));
            Assert.Equal(Active, output.GetPixel(25, 5));
        }
    }

    [Fact]
    public void Page0CanBeHidden()
    {
        (GumpDocument document, FakeArtSource art) = Build();

        using (art)
        {
            using SKBitmap output = Render(
                document, art, activePage: 1, new RenderOptions { ShowSharedPage = false });

            Assert.Equal(0, output.GetPixel(5, 5).Alpha);
            Assert.Equal(Active, output.GetPixel(25, 5));
        }
    }

    [Fact]
    public void Page0IsNotDrawnTwiceWhenItIsTheActivePage()
    {
        (GumpDocument document, FakeArtSource art) = Build();

        using (art)
        {
            using SKBitmap output = Render(document, art, activePage: 0, new RenderOptions());

            Assert.Equal(Shared, output.GetPixel(5, 5));

            // Page 1's content must not leak onto page 0.
            Assert.Equal(0, output.GetPixel(25, 5).Alpha);
        }
    }

    /// <summary>
    /// The backdrop is context, not the edit target, so it must not draw
    /// selection handles for elements the user cannot grab from here.
    /// </summary>
    [Fact]
    public void Page0DrawsWithoutSelectionDecoration()
    {
        using FakeArtSource art = new();

        art.AddGump(1, 10, 10, Shared);

        GumpDocument document = new();

        // Away from the origin, so there is room for a handle to show outside it.
        AlphaElement backdrop = new()
        {
            Location = new GumpPoint(20, 20),
            Size = new GumpSize(10, 10),
            IsSelected = true,
        };

        document.Pages[0].Root.Add(backdrop);
        document.AddPage();

        using SKBitmap onPage0 = Render(document, art, 0, new RenderOptions { DrawSelection = true });
        using SKBitmap asBackdrop = Render(document, art, 1, new RenderOptions { DrawSelection = true });

        // Selected on its own page, the top-left handle is drawn just outside it.
        Assert.NotEqual(0, onPage0.GetPixel(19, 19).Alpha);

        // As a backdrop it is context, not the edit target, so no handles.
        Assert.Equal(0, asBackdrop.GetPixel(19, 19).Alpha);
    }

    [Fact]
    public void MeasuringADocumentSizesBothPage0AndTheActivePage()
    {
        (GumpDocument document, FakeArtSource art) = Build();

        using (art)
        {
            GumpRenderer renderer = new(new TestArtSource(art));

            renderer.MeasureDocument(document, activePageIndex: 1);

            Assert.Equal(new GumpSize(10, 10), document.Pages[0].Root.Children[0].Size);
            Assert.Equal(new GumpSize(10, 10), document.Pages[1].Root.Children[0].Size);
        }
    }

    private static SKBitmap Render(
        GumpDocument document, FakeArtSource art, int activePage, RenderOptions options)
    {
        GumpRenderer renderer = new(new TestArtSource(art));

        renderer.MeasureDocument(document, activePage);

        SKBitmap bitmap = new(new SKImageInfo(64, 64, SKColorType.Bgra8888, SKAlphaType.Premul));

        using SKCanvas canvas = new(bitmap);

        canvas.Clear(SKColors.Transparent);

        renderer.RenderDocument(canvas, document, activePage, options);
        canvas.Flush();

        return bitmap;
    }
}

/// <summary>Rendering for the gump commands added after GumpStudio 1.8.</summary>
public class LateGumpCommandRenderingTests
{
    private static readonly SKColor Red = new(0xFF, 0x00, 0x00);
    private static readonly SKColor Blue = new(0x00, 0x00, 0xFF);

    /// <summary>
    /// The source region is cut out by clipping and shifting, so the proof is
    /// that the part of the source lying past the element's own rectangle is gone
    /// and the part before its origin never appears.
    /// </summary>
    [Fact]
    public void PicInPicDrawsTheSourceRegionAndClipsTheRest()
    {
        using FakeArtSource art = new();

        // A 40x40 source, of which the region starting at (30, 30) is wanted. Only
        // 10x10 of the source is left past that point, so a 20x20 element shows
        // colour in its first ten pixels and nothing after.
        art.AddGump(9000, 40, 40, Red);

        GumpPage page = new();

        page.Root.Add(new PicInPicElement
        {
            GumpId = 9000,
            Location = new GumpPoint(4, 4),
            Size = new GumpSize(20, 20),
            SourceX = 30,
            SourceY = 30,
        });

        GumpRenderer renderer = new(new TestArtSource(art));

        using SKBitmap output = renderer.RenderToBitmap(page, 64, 64, RenderOptions.Plain);

        Assert.Equal(Red, output.GetPixel(4, 4));
        Assert.Equal(Red, output.GetPixel(13, 13));

        // Past the end of the source region.
        Assert.Equal(0, output.GetPixel(14, 14).Alpha);

        // Never outside the element, however big the source is.
        Assert.Equal(0, output.GetPixel(3, 4).Alpha);
        Assert.Equal(0, output.GetPixel(24, 24).Alpha);
    }

    [Fact]
    public void AButtonWithTileArtDrawsTheOverlayAtItsOffset()
    {
        using FakeArtSource art = new();

        art.AddGump(247, 20, 20, Red);
        art.AddItem(3821, 4, 4, Blue);

        GumpPage page = new();

        page.Root.Add(new ButtonElement
        {
            NormalId = 247,
            PressedId = 248,
            Location = GumpPoint.Origin,
            TileId = 3821,
            TileX = 5,
            TileY = 6,
        });

        GumpRenderer renderer = new(new TestArtSource(art));

        using SKBitmap output = renderer.RenderToBitmap(page, 32, 32, RenderOptions.Plain);

        Assert.Equal(Blue, output.GetPixel(5, 6));
        Assert.Equal(Blue, output.GetPixel(8, 9));

        // The button art still shows everywhere the overlay does not cover.
        Assert.Equal(Red, output.GetPixel(0, 0));
        Assert.Equal(Red, output.GetPixel(9, 6));
    }

    [Fact]
    public void AButtonWithoutTileArtDrawsOnlyTheButton()
    {
        using FakeArtSource art = new();

        art.AddGump(247, 20, 20, Red);
        art.AddItem(3821, 4, 4, Blue);

        GumpPage page = new();

        page.Root.Add(new ButtonElement { NormalId = 247, PressedId = 248, Location = GumpPoint.Origin });

        GumpRenderer renderer = new(new TestArtSource(art));

        using SKBitmap output = renderer.RenderToBitmap(page, 32, 32, RenderOptions.Plain);

        Assert.Equal(Red, output.GetPixel(5, 6));
    }

    [Fact]
    public void TileArtInAGumpSlotDrawsItsItemArt()
    {
        using FakeArtSource art = new();

        art.AddItem(3821, 8, 8, Blue);

        GumpPage page = new();

        page.Root.Add(new TileAsGumpElement { ItemId = 3821, Location = new GumpPoint(3, 4) });

        GumpRenderer renderer = new(new TestArtSource(art));

        using SKBitmap output = renderer.RenderToBitmap(page, 32, 32, RenderOptions.Plain);

        Assert.Equal(Blue, output.GetPixel(3, 4));
        Assert.Equal(0, output.GetPixel(2, 4).Alpha);
    }

    /// <summary>
    /// A plain label takes its size from the rendered text, but a cropped one owns
    /// its rectangle. Measuring a cropped label would silently undo every resize
    /// the user made.
    /// </summary>
    [Fact]
    public void MeasuringLeavesACroppedLabelsRectangleAlone()
    {
        using FakeArtSource art = new();

        LabelElement plain = new() { Text = "measure me" };
        LabelElement cropped = new() { Text = "measure me", Cropped = true, Size = new GumpSize(7, 3) };

        GumpPage page = new();

        page.Root.Add(plain);
        page.Root.Add(cropped);

        new GumpRenderer(new TestArtSource(art)).MeasureContentSizes(page);

        Assert.Equal(new GumpSize(7, 3), cropped.Size);
        Assert.NotEqual(default, plain.Size);
    }

    [Fact]
    public void MeasuringSizesTileArtInAGumpSlotFromItsArt()
    {
        using FakeArtSource art = new();

        art.AddItem(3821, 8, 12, Blue);

        TileAsGumpElement tile = new() { ItemId = 3821 };
        GumpPage page = new();

        page.Root.Add(tile);

        new GumpRenderer(new TestArtSource(art)).MeasureContentSizes(page);

        Assert.Equal(new GumpSize(8, 12), tile.Size);
    }
}
