namespace GumpStudio.Uo.Primitives;

/// <summary>
/// A decoded art image still in the client's native ARGB1555 format.
/// </summary>
/// <remarks>
/// Hueing is defined in terms of the 5-bit red channel, so hues must be applied
/// before the image is widened to 8-bit channels. Decoders produce this type,
/// hue application mutates it, and <see cref="ToUoImage"/> is the last step.
/// </remarks>
public sealed class Argb1555Image
{
    private readonly ushort[] _pixels;

    public Argb1555Image(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        Width = width;
        Height = height;
        _pixels = new ushort[checked(width * height)];
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>The whole buffer, row-major, <see cref="Width"/> * <see cref="Height"/> entries.</summary>
    public Span<ushort> Pixels => _pixels;

    /// <summary>One row of the image.</summary>
    public Span<ushort> Row(int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);

        return _pixels.AsSpan(y * Width, Width);
    }

    /// <summary>Widens to BGRA8888 for handing to the renderer.</summary>
    public UoImage ToUoImage()
    {
        UoImage image = new(Width, Height);
        Span<byte> destination = image.Pixels;

        for (int i = 0; i < _pixels.Length; i++)
        {
            Color16.WriteBgra(_pixels[i], destination.Slice(i * 4, 4));
        }

        return image;
    }
}
