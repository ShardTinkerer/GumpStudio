using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using SkiaSharp;

namespace GumpStudio.App.Controls;

/// <summary>
/// Moves decoded art from Skia into something Avalonia can draw.
/// </summary>
/// <remarks>
/// A pixel copy. Both callers used to PNG-encode at quality 100 and immediately
/// PNG-decode the result purely to cross the type boundary — a full deflate and
/// inflate, per art-browser thumbnail and per font sample in a dropdown, for
/// data already sitting in memory as raw BGRA.
/// </remarks>
internal static class SkiaBitmap
{
    /// <summary>
    /// Copies a Skia bitmap into a new immutable Avalonia one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Immutable deliberately, and this matters more than the copy does. The
    /// first version of this handed back a <see cref="WriteableBitmap"/>, which
    /// is for content that changes: the render backend has to assume its pixels
    /// may differ from one frame to the next, so it cannot keep the uploaded
    /// texture the way it does for a bitmap that is declared never to change.
    /// A gallery of thumbnails is static art being redrawn on every scroll
    /// frame, which is the worst possible case for that — and the PNG round-trip
    /// this replaced happened to produce an immutable bitmap, so the type went
    /// backwards while the decoding went forwards.
    /// </para>
    /// <para>
    /// The conversion to premultiplied BGRA is done by Skia on the way through.
    /// The decoders produce unpremultiplied BGRA and Avalonia wants it
    /// premultiplied, which is the same conversion the PNG round-trip was
    /// performing incidentally: gump art is full of soft-edged glyph masks and
    /// translucent regions, and getting it wrong shows as haloes rather than as
    /// an error.
    /// </para>
    /// </remarks>
    public static Bitmap ToAvalonia(SKBitmap source)
    {
        ArgumentNullException.ThrowIfNull(source);

        int width = Math.Max(1, source.Width);
        int height = Math.Max(1, source.Height);

        SKImageInfo info = new(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);

        using SKBitmap premultiplied = new(info);
        using SKPixmap destination = premultiplied.PeekPixels();
        using SKPixmap pixels = source.PeekPixels();

        pixels.ReadPixels(destination);

        // Copies out of the pointer, so the Skia bitmap can be released here.
        return new Bitmap(
            PixelFormat.Bgra8888,
            AlphaFormat.Premul,
            premultiplied.GetPixels(),
            new PixelSize(width, height),
            new Vector(96, 96),
            premultiplied.RowBytes);
    }
}
