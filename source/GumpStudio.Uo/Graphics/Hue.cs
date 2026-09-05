using System.Buffers.Binary;
using System.Text;

using GumpStudio.Uo.Primitives;

namespace GumpStudio.Uo.Graphics;

/// <summary>
/// One colour-shift palette from <c>hues.mul</c>: 32 replacement colours plus a
/// name.
/// </summary>
/// <remarks>
/// Hueing replaces a pixel's colour by looking up its 5-bit red channel in this
/// table. That is why hue application has to happen while the image is still in
/// ARGB1555 — once widened to 8-bit channels the index is gone.
/// </remarks>
public sealed class Hue
{
    /// <summary>Colours per hue.</summary>
    public const int ColorCount = 32;

    /// <summary>32 colours, then table start and end, then a 20-byte name.</summary>
    internal const int RecordSize = (ColorCount * sizeof(ushort)) + (2 * sizeof(ushort)) + NameLength;

    private const int NameLength = 20;

    private readonly ushort[] _colors;

    private Hue(int index, ushort[] colors, ushort tableStart, ushort tableEnd, string name)
    {
        Index = index;
        _colors = colors;
        TableStart = tableStart;
        TableEnd = tableEnd;
        Name = name;
    }

    /// <summary>Zero-based hue index. The wire representation is this plus one.</summary>
    public int Index { get; }

    public string Name { get; }

    public ushort TableStart { get; }

    public ushort TableEnd { get; }

    /// <summary>The 32 replacement colours, in ARGB1555 with the alpha bit set.</summary>
    public ReadOnlySpan<ushort> Colors => _colors;

    /// <summary>An identity hue used for out-of-range lookups.</summary>
    internal static Hue CreateEmpty(int index)
    {
        ushort[] colors = new ushort[ColorCount];

        // A pass-through table: shade n maps to grey level n, so applying this
        // hue leaves a greyscale source unchanged instead of blanking it.
        for (int i = 0; i < colors.Length; i++)
        {
            colors[i] = (ushort)(Color16.AlphaMask | (i << 10) | (i << 5) | i);
        }

        return new Hue(index, colors, 0, 0, string.Empty);
    }

    internal static Hue Read(int index, ReadOnlySpan<byte> record)
    {
        ushort[] colors = new ushort[ColorCount];

        for (int i = 0; i < ColorCount; i++)
        {
            // Bit 15 is reserved in the file; the client forces it on so the
            // colour reads as opaque.
            colors[i] = (ushort)(
                BinaryPrimitives.ReadUInt16LittleEndian(record[(i * sizeof(ushort))..])
                | Color16.AlphaMask);
        }

        int offset = ColorCount * sizeof(ushort);

        ushort tableStart = BinaryPrimitives.ReadUInt16LittleEndian(record[offset..]);
        ushort tableEnd = BinaryPrimitives.ReadUInt16LittleEndian(record[(offset + 2)..]);

        return new Hue(index, colors, tableStart, tableEnd, ReadName(record[(offset + 4)..]));
    }

    private static string ReadName(ReadOnlySpan<byte> raw)
    {
        ReadOnlySpan<byte> name = raw[..Math.Min(NameLength, raw.Length)];

        int end = name.IndexOf((byte)0);

        if (end >= 0)
        {
            name = name[..end];
        }

        // Hue names are plain ASCII in every shipped file; anything else is
        // corruption and is better shown as a replacement char than throwing.
        return Encoding.ASCII.GetString(name).Trim();
    }

    /// <summary>Gets one palette entry as a packed 0xAARRGGBB value.</summary>
    public uint GetColor(int shade)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(shade);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(shade, ColorCount);

        ushort color = _colors[shade];

        return 0xFF000000u
            | ((uint)Color16.Expand5To8(Color16.Red5(color)) << 16)
            | ((uint)Color16.Expand5To8(Color16.Green5(color)) << 8)
            | Color16.Expand5To8(Color16.Blue5(color));
    }

    /// <summary>
    /// Recolours an image in place.
    /// </summary>
    /// <param name="pixels">ARGB1555 pixels, mutated in place.</param>
    /// <param name="onlyGreyPixels">
    /// True for a "partial hue", which recolours only greyscale pixels and
    /// leaves already-coloured detail alone.
    /// </param>
    public void ApplyTo(Span<ushort> pixels, bool onlyGreyPixels)
    {
        for (int i = 0; i < pixels.Length; i++)
        {
            ushort pixel = pixels[i];

            if ((pixel & Color16.AlphaMask) == 0)
            {
                // Transparent stays transparent. Zeroing rather than keeping the
                // stale colour bits avoids fringing when the image is blended.
                pixels[i] = Color16.Transparent;

                continue;
            }

            if (onlyGreyPixels && !Color16.IsGrey(pixel))
            {
                continue;
            }

            pixels[i] = _colors[Color16.Red5(pixel)];
        }
    }
}
