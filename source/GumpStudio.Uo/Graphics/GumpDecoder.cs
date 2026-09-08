using System.Buffers.Binary;

using GumpStudio.Uo.Primitives;

namespace GumpStudio.Uo.Graphics;

/// <summary>
/// Decodes gump art: a per-row lookup table followed by run-length encoded
/// ARGB1555 pixels.
/// </summary>
/// <remarks>
/// Gump payloads do not carry their own dimensions. In a MUL container they come
/// from the index's <c>Extra</c> field; in a UOP container they come from an
/// eight-byte prefix the provider strips. Either way the caller supplies them.
/// </remarks>
public static class GumpDecoder
{
    /// <summary>Refuses anything larger, as a guard against a corrupt index.</summary>
    public const int MaxDimension = 4096;

    /// <summary>
    /// Decodes one gump.
    /// </summary>
    /// <returns>The image, or <see langword="null"/> if the data is unusable.</returns>
    public static Argb1555Image? Decode(ReadOnlySpan<byte> data, int width, int height)
    {
        if (width is <= 0 or > MaxDimension || height is <= 0 or > MaxDimension)
        {
            return null;
        }

        // The lookup table is one int32 per row, holding a dword offset from the
        // start of the payload.
        long tableBytes = (long)height * sizeof(int);

        if (data.Length < tableBytes)
        {
            return null;
        }

        Argb1555Image image = new(width, height);

        for (int y = 0; y < height; y++)
        {
            int rowStart = BinaryPrimitives.ReadInt32LittleEndian(data[(y * sizeof(int))..]);

            // Offsets are in dwords. A negative or out-of-range offset means a
            // corrupt entry; skip the row instead of reading arbitrary memory,
            // which is what the original unsafe decoder did.
            long byteOffset = (long)rowStart * sizeof(int);

            if (byteOffset < tableBytes || byteOffset >= data.Length)
            {
                continue;
            }

            DecodeRow(data[(int)byteOffset..], image.Row(y));
        }

        return image;
    }

    /// <summary>
    /// Fills one row from a sequence of (colour, run-length) pairs.
    /// </summary>
    private static void DecodeRow(ReadOnlySpan<byte> source, Span<ushort> row)
    {
        int x = 0;
        int offset = 0;

        while (x < row.Length)
        {
            // Each pair is two little-endian uint16s. A short tail means the row
            // is truncated; leave the remainder transparent.
            if (offset + 4 > source.Length)
            {
                return;
            }

            ushort color = BinaryPrimitives.ReadUInt16LittleEndian(source[offset..]);
            int run = BinaryPrimitives.ReadUInt16LittleEndian(source[(offset + 2)..]);

            offset += 4;

            if (run <= 0)
            {
                // A zero-length run would spin forever. The original decoder had
                // no such guard.
                return;
            }

            if (run > row.Length - x)
            {
                run = row.Length - x;
            }

            if (color != 0)
            {
                // Colour 0 is a transparent run; the buffer is already zeroed.
                // The original XORed the alpha bit in. OR is equivalent for real
                // files (bit 15 is always clear on disk) and cannot accidentally
                // turn an already-opaque colour transparent.
                row.Slice(x, run).Fill((ushort)(color | Color16.AlphaMask));
            }

            x += run;
        }
    }
}
