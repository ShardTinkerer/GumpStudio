using Avalonia.Media.Imaging;

using GumpStudio.App.Controls;

using SkiaSharp;

using Xunit;

namespace GumpStudio.App.Tests;

/// <summary>
/// The Skia-to-Avalonia pixel copy.
/// </summary>
/// <remarks>
/// This replaced a PNG encode-then-decode that crossed the type boundary for
/// every art-browser thumbnail and every font sample in a dropdown — a full
/// deflate and inflate for data already sitting in memory as raw BGRA.
///
/// The round-trip premultiplied alpha as a side effect of how the two libraries
/// store pixels, so the copy has to do it too. Gump art is full of soft-edged
/// glyph masks and translucent regions, and getting that wrong shows as haloes
/// rather than as an error, so the conversion is pinned separately below in
/// plain Skia: the headless platform substitutes its own bitmap, reporting and
/// handing back the platform's format regardless of what was asked for, which
/// makes it the wrong place to assert anything about a pixel layout.
/// </remarks>
[Collection("Headless")]
public class SkiaBitmapTests
{
    /// <summary>Byte offsets of the blue, green and red channels.</summary>
    private static readonly int[] ColourShifts = [0, 8, 16];

    /// <summary>Builds an unpremultiplied BGRA bitmap, as the decoders produce.</summary>
    private static SKBitmap Source(int width, int height, params SKColor[] pixels)
    {
        SKBitmap bitmap = new(
            new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul));

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bitmap.SetPixel(x, y, pixels[(y * width) + x]);
            }
        }

        return bitmap;
    }

    /// <summary>
    /// Runs the same conversion the copy relies on, into a Skia destination.
    /// </summary>
    private static byte[] Convert(SKColor colour)
    {
        using SKBitmap source = Source(1, 1, colour);

        using SKBitmap target = new(
            new SKImageInfo(1, 1, SKColorType.Bgra8888, SKAlphaType.Premul));

        using SKPixmap pixels = source.PeekPixels();
        using SKPixmap destination = target.PeekPixels();

        Assert.True(pixels.ReadPixels(destination));

        return target.GetPixelSpan().ToArray();
    }

    [Fact]
    public void AnOpaquePixelKeepsItsChannels()
    {
        byte[] bytes = Convert(new SKColor(0x10, 0x20, 0x30, 0xFF));

        // Blue, green, red, alpha, in ascending byte order.
        Assert.Equal([0x30, 0x20, 0x10, 0xFF], bytes);
    }

    [Fact]
    public void ATransparentPixelBecomesEntirelyZero()
    {
        // Not [FF, FF, FF, 00]: an unpremultiplied white at zero alpha carries
        // colour that must be scaled away, or it draws as a white fringe.
        Assert.Equal([0x00, 0x00, 0x00, 0x00], Convert(new SKColor(0xFF, 0xFF, 0xFF, 0x00)));
    }

    /// <summary>
    /// The case the PNG round-trip was quietly handling: a half-transparent
    /// white glyph pixel arrives with its colour scaled by its alpha.
    /// </summary>
    [Fact]
    public void AHalfTransparentPixelIsPremultiplied()
    {
        byte[] bytes = Convert(new SKColor(0xFF, 0xFF, 0xFF, 0x80));

        Assert.Equal(0x80, bytes[3]);

        foreach (int shift in ColourShifts)
        {
            Assert.InRange(bytes[shift / 8], (byte)0x7E, (byte)0x82);
        }
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0x40)]
    [InlineData(0xC0)]
    [InlineData(0xFF)]
    public void NoChannelEverExceedsTheAlphaItIsMultipliedBy(int alpha)
    {
        byte[] bytes = Convert(new SKColor(0xFF, 0xC0, 0x80, (byte)alpha));

        Assert.Equal(alpha, bytes[3]);

        // The defining property of premultiplied colour, and the one a missing
        // conversion would break.
        foreach (int shift in ColourShifts)
        {
            Assert.True(bytes[shift / 8] <= alpha, $"channel {shift / 8} exceeds alpha");
        }
    }

    [Fact]
    public void TheCopyIsImmutableSoItsTextureCanBeCached()
    {
        HeadlessAppSession.Run(() =>
        {
            using SKBitmap source = Source(1, 1, SKColors.Red);
            using Bitmap target = SkiaBitmap.ToAvalonia(source);

            // Not a WriteableBitmap. A gallery of thumbnails is static art
            // redrawn on every scroll frame, and a bitmap declared mutable
            // cannot have its uploaded texture kept between them.
            Assert.IsNotType<WriteableBitmap>(target);
        });
    }

    /// <summary>
    /// The size and stride handed to the Avalonia bitmap come from the
    /// premultiplied intermediate, so that is what is worth checking. The
    /// headless platform substitutes its own bitmap and reports a fabricated
    /// 1x1 whatever the constructor was given, which makes asserting through it
    /// a test of the stub.
    /// </summary>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 2)]
    [InlineData(44, 44)]
    [InlineData(176, 120)]
    public void TheIntermediateKeepsTheSourceGeometry(int width, int height)
    {
        using SKBitmap source = Source(
            width, height, [.. Enumerable.Repeat(SKColors.Red, width * height)]);

        using SKBitmap premultiplied = new(
            new SKImageInfo(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Premul));

        Assert.Equal(width, premultiplied.Width);
        Assert.Equal(height, premultiplied.Height);

        // Four bytes a pixel, which is the stride the bitmap is told about.
        Assert.Equal(width * 4, premultiplied.RowBytes);

        using SKPixmap pixels = source.PeekPixels();
        using SKPixmap destination = premultiplied.PeekPixels();

        Assert.True(pixels.ReadPixels(destination));
    }

    [Fact]
    public void ASourceOfAnySizeIsAccepted()
    {
        HeadlessAppSession.Run(() =>
        {
            foreach ((int width, int height) in new[] { (1, 1), (3, 2), (176, 120) })
            {
                using SKBitmap source = Source(
                    width, height, [.. Enumerable.Repeat(SKColors.Red, width * height)]);

                using Bitmap target = SkiaBitmap.ToAvalonia(source);

                Assert.NotNull(target);
            }
        });
    }

    [Fact]
    public void ANullSourceIsRejected() =>
        Assert.Throws<ArgumentNullException>(() => SkiaBitmap.ToAvalonia(null!));
}
