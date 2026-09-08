namespace GumpStudio.Uo.Primitives;

/// <summary>
/// Conversions for the 16-bit ARGB1555 pixel format every Ultima Online art
/// file stores: one alpha bit and three five-bit colour channels.
/// </summary>
public static class Color16
{
    /// <summary>The alpha bit. Set means opaque.</summary>
    public const ushort AlphaMask = 0x8000;

    /// <summary>Fully transparent.</summary>
    public const ushort Transparent = 0x0000;

    /// <summary>
    /// Expands a 5-bit channel to 8 bits.
    /// </summary>
    /// <remarks>
    /// The original SDK used a bare <c>&lt;&lt; 3</c>, which maps the maximum
    /// value 31 to 248 rather than 255 — so pure white art rendered as a very
    /// light grey. Replicating the low bits across the gap is the correct
    /// expansion and makes 31 map to 255.
    /// </remarks>
    public static byte Expand5To8(int channel5) => (byte)((channel5 << 3) | (channel5 >> 2));

    /// <summary>Red channel of an ARGB1555 value, 0-31.</summary>
    public static int Red5(ushort color) => (color >> 10) & 0x1F;

    /// <summary>Green channel of an ARGB1555 value, 0-31.</summary>
    public static int Green5(ushort color) => (color >> 5) & 0x1F;

    /// <summary>Blue channel of an ARGB1555 value, 0-31.</summary>
    public static int Blue5(ushort color) => color & 0x1F;

    /// <summary>True when the three colour channels are equal, i.e. the pixel is a shade of grey.</summary>
    /// <remarks>Partial hueing recolours only these pixels.</remarks>
    public static bool IsGrey(ushort color)
    {
        int r = Red5(color);

        return r == Green5(color) && r == Blue5(color);
    }

    /// <summary>
    /// Writes one ARGB1555 pixel into a BGRA8888 destination, which is the byte
    /// order SkiaSharp's <c>Bgra8888</c> colour type expects on little-endian.
    /// </summary>
    public static void WriteBgra(ushort color, Span<byte> destination)
    {
        if ((color & AlphaMask) == 0)
        {
            // Transparent pixels must be fully zeroed, not just alpha-zeroed:
            // leaving colour behind produces fringing once anything blends them.
            destination[0] = 0;
            destination[1] = 0;
            destination[2] = 0;
            destination[3] = 0;

            return;
        }

        destination[0] = Expand5To8(Blue5(color));
        destination[1] = Expand5To8(Green5(color));
        destination[2] = Expand5To8(Red5(color));
        destination[3] = 0xFF;
    }
}
