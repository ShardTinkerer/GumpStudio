using GumpStudio.Core.Document;
using GumpStudio.Core.Editing;
using GumpStudio.Rendering;
using GumpStudio.TestSupport;

using SkiaSharp;

using Xunit;

namespace GumpStudio.Rendering.Tests;

/// <summary>
/// Where the design grid puts its dots.
/// </summary>
/// <remarks>
/// The grid used to be a draw call per dot — some thirty thousand per frame at
/// the default spacing. It is now one tiled fill, so these pin the placement
/// that rewrite had to preserve.
/// </remarks>
public class GridRenderingTests
{
    private static GumpRenderer Renderer() => new(new TestArtSource(new FakeArtSource()));

    private static SKBitmap Render(GridSettings? grid, int width = 32, int height = 24)
    {
        GumpDocument document = new();

        return Renderer().RenderToBitmap(
            document.Pages[0],
            width,
            height,
            new RenderOptions { DrawSelection = false, DrawGroupOutlines = false, Grid = grid });
    }

    private static bool HasDot(SKBitmap bitmap, int x, int y) => bitmap.GetPixel(x, y).Alpha != 0;

    [Fact]
    public void NoGridMeansNoDots()
    {
        using SKBitmap bitmap = Render(null);

        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                Assert.False(HasDot(bitmap, x, y), $"unexpected dot at {x},{y}");
            }
        }
    }

    [Fact]
    public void AnInvisibleGridDrawsNothing()
    {
        using SKBitmap bitmap = Render(new GridSettings { Width = 5, Height = 5, Visible = false });

        Assert.False(HasDot(bitmap, 0, 0));
        Assert.False(HasDot(bitmap, 5, 5));
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(8, 4)]
    [InlineData(3, 3)]
    [InlineData(16, 12)]
    public void DotsLandOnEveryIntersectionAndNowhereElse(int spacingX, int spacingY)
    {
        using SKBitmap bitmap = Render(new GridSettings { Width = spacingX, Height = spacingY, Visible = true });

        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                bool expected = x % spacingX == 0 && y % spacingY == 0;

                Assert.Equal(expected, HasDot(bitmap, x, y));
            }
        }
    }

    /// <summary>
    /// The guard that predates the tiled fill: below three pixels the dots merge
    /// into a wash, so nothing is drawn at all.
    /// </summary>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(2, 8)]
    [InlineData(8, 2)]
    public void ASpacingTooFineToReadDrawsNothing(int spacingX, int spacingY)
    {
        using SKBitmap bitmap = Render(new GridSettings { Width = spacingX, Height = spacingY, Visible = true });

        Assert.False(HasDot(bitmap, 0, 0));
    }

    [Fact]
    public void TheDotIsTheSameFaintWhiteItAlwaysWas()
    {
        using SKBitmap bitmap = Render(new GridSettings { Width = 5, Height = 5, Visible = true });

        SKColor dot = bitmap.GetPixel(0, 0);

        // Premultiplied, so the components scale with the 0x38 alpha.
        Assert.Equal(0x38, dot.Alpha);
        Assert.Equal(dot.Red, dot.Green);
        Assert.Equal(dot.Green, dot.Blue);
    }

    [Fact]
    public void TheGridCoversTheWholeSurfaceIncludingTheFarEdge()
    {
        using SKBitmap bitmap = Render(new GridSettings { Width = 10, Height = 10, Visible = true }, 41, 31);

        Assert.True(HasDot(bitmap, 40, 30));
        Assert.True(HasDot(bitmap, 0, 30));
        Assert.True(HasDot(bitmap, 40, 0));
    }
}
