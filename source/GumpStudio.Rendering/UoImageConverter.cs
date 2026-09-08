using GumpStudio.Uo.Primitives;

using SkiaSharp;

namespace GumpStudio.Rendering;

/// <summary>
/// Bridges the data layer's plain pixel buffers into SkiaSharp.
/// </summary>
/// <remarks>
/// <see cref="UoImage"/> is deliberately BGRA8888, which is exactly
/// <see cref="SKColorType.Bgra8888"/> on little-endian platforms, so the
/// conversion is a straight copy with no per-pixel work.
/// </remarks>
public static class UoImageConverter
{
    /// <summary>Wraps a decoded image in an <see cref="SKBitmap"/>.</summary>
    /// <remarks>The caller owns the returned bitmap.</remarks>
    public static SKBitmap ToSkBitmap(this UoImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        SKImageInfo info = new(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        SKBitmap bitmap = new(info);

        try
        {
            // A straight memory copy: the layouts already match, so there is no
            // per-pixel conversion here.
            image.Pixels.CopyTo(bitmap.GetPixelSpan());

            return bitmap;
        }
        catch
        {
            bitmap.Dispose();

            throw;
        }
    }

    /// <summary>Encodes a decoded image as a PNG.</summary>
    public static byte[] EncodePng(this UoImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        using SKBitmap bitmap = image.ToSkBitmap();
        using SKData data = bitmap.Encode(SKEncodedImageFormat.Png, 100);

        return data.ToArray();
    }

    /// <summary>Writes a decoded image to a PNG file.</summary>
    public static void SavePng(this UoImage image, string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        File.WriteAllBytes(path, image.EncodePng());
    }
}
