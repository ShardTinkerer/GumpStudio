namespace GumpStudio.Uo.Primitives;

/// <summary>
/// A decoded image in BGRA8888, the byte order SkiaSharp's <c>Bgra8888</c>
/// colour type expects on little-endian platforms.
/// </summary>
/// <remarks>
/// This is deliberately a plain buffer rather than any framework bitmap type.
/// The old code returned <c>System.Drawing.Bitmap</c>, which tied the data layer
/// to Windows, made headless testing impossible, and produced a long tail of
/// undisposed GDI handles. Nothing here owns an OS resource.
/// </remarks>
public sealed class UoImage
{
    /// <summary>Bytes per pixel: blue, green, red, alpha.</summary>
    public const int BytesPerPixel = 4;

    private readonly byte[] _pixels;

    public UoImage(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        Width = width;
        Height = height;
        _pixels = new byte[checked(width * height * BytesPerPixel)];
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Row stride in bytes.</summary>
    public int Stride => Width * BytesPerPixel;

    /// <summary>The whole buffer, row-major.</summary>
    public Span<byte> Pixels => _pixels;

    /// <summary>Reads one pixel as a packed 0xAARRGGBB value. Intended for tests and picking.</summary>
    public uint GetPixel(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Width);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);

        int offset = (y * Stride) + (x * BytesPerPixel);

        return ((uint)_pixels[offset + 3] << 24)
             | ((uint)_pixels[offset + 2] << 16)
             | ((uint)_pixels[offset + 1] << 8)
             | _pixels[offset];
    }
}
