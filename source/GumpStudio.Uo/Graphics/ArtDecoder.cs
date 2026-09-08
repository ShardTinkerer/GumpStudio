using System.Buffers.Binary;

using GumpStudio.Uo.Primitives;

namespace GumpStudio.Uo.Graphics;

/// <summary>
/// Decodes item and land art from <c>art.mul</c> / <c>artLegacyMUL.uop</c>.
/// </summary>
/// <remarks>
/// Two very different encodings share the container. Indices below
/// <see cref="StaticArtOffset"/> are land tiles: a fixed 44x44 diamond with no
/// header. At and above it are static item tiles: a self-describing header
/// followed by run-length encoded scanlines.
/// </remarks>
public static class ArtDecoder
{
    /// <summary>Static item art begins at this index; below it is land art.</summary>
    public const int StaticArtOffset = 0x4000;

    /// <summary>Land tiles are always this wide and tall.</summary>
    public const int LandTileSize = 44;

    /// <summary>Refuses anything larger, as a guard against a corrupt entry.</summary>
    public const int MaxDimension = 4096;

    /// <summary>
    /// Decodes a static item tile.
    /// </summary>
    /// <returns>The image, or <see langword="null"/> if the data is unusable.</returns>
    public static Argb1555Image? DecodeStatic(ReadOnlySpan<byte> data)
    {
        // int32 header (unused), then int16 width and height.
        const int HeaderSize = sizeof(int) + (2 * sizeof(short));

        if (data.Length < HeaderSize)
        {
            return null;
        }

        int width = BinaryPrimitives.ReadInt16LittleEndian(data[4..]);
        int height = BinaryPrimitives.ReadInt16LittleEndian(data[6..]);

        if (width is <= 0 or > MaxDimension || height is <= 0 or > MaxDimension)
        {
            return null;
        }

        // A uint16 lookup per row, in words relative to the end of the table.
        long tableBytes = (long)height * sizeof(ushort);

        if (data.Length < HeaderSize + tableBytes)
        {
            return null;
        }

        int dataStart = HeaderSize + (int)tableBytes;

        Argb1555Image image = new(width, height);

        for (int y = 0; y < height; y++)
        {
            int lookup = BinaryPrimitives.ReadUInt16LittleEndian(
                data[(HeaderSize + (y * sizeof(ushort)))..]);

            long rowOffset = dataStart + ((long)lookup * sizeof(ushort));

            if (rowOffset < dataStart || rowOffset >= data.Length)
            {
                continue;
            }

            DecodeStaticRow(data[(int)rowOffset..], image, y);
        }

        return image;
    }

    /// <summary>
    /// Reads one scanline: a sequence of (x-offset, run-length, pixels...) chunks
    /// terminated when offset and run are both zero.
    /// </summary>
    private static void DecodeStaticRow(ReadOnlySpan<byte> source, Argb1555Image image, int y)
    {
        Span<ushort> row = image.Row(y);

        int x = 0;
        int offset = 0;

        while (true)
        {
            if (offset + 4 > source.Length)
            {
                return;
            }

            int xOffset = BinaryPrimitives.ReadUInt16LittleEndian(source[offset..]);
            int xRun = BinaryPrimitives.ReadUInt16LittleEndian(source[(offset + 2)..]);

            offset += 4;

            if (xOffset + xRun == 0)
            {
                return;
            }

            x += xOffset;

            // The original trusted these straight from the file and wrote through
            // a raw pointer, so a corrupt entry could scribble past the bitmap.
            if (x < 0 || x >= row.Length)
            {
                return;
            }

            int run = Math.Min(xRun, row.Length - x);

            if (offset + (run * sizeof(ushort)) > source.Length)
            {
                run = Math.Max(0, (source.Length - offset) / sizeof(ushort));
            }

            for (int i = 0; i < run; i++)
            {
                row[x + i] = (ushort)(
                    BinaryPrimitives.ReadUInt16LittleEndian(source[(offset + (i * sizeof(ushort)))..])
                    | Color16.AlphaMask);
            }

            offset += xRun * sizeof(ushort);
            x += xRun;
        }
    }

    /// <summary>
    /// Decodes a land tile: a 44x44 diamond stored as two triangular halves with
    /// no header and no run-length encoding.
    /// </summary>
    public static Argb1555Image? DecodeLand(ReadOnlySpan<byte> data)
    {
        // 44 rows whose widths run 2,4,..44,44,..4,2 — 1012 pixels in total.
        const int PixelCount = 1012;

        if (data.Length < PixelCount * sizeof(ushort))
        {
            return null;
        }

        Argb1555Image image = new(LandTileSize, LandTileSize);

        int offset = 0;

        // Upper half: widening from the top point.
        for (int y = 0, xOffset = 21, run = 2; y < 22; y++, xOffset--, run += 2)
        {
            offset = CopyRun(data, offset, image.Row(y), xOffset, run);
        }

        // Lower half: narrowing to the bottom point.
        for (int y = 22, xOffset = 0, run = 44; y < 44; y++, xOffset++, run -= 2)
        {
            offset = CopyRun(data, offset, image.Row(y), xOffset, run);
        }

        return image;
    }

    private static int CopyRun(ReadOnlySpan<byte> source, int offset, Span<ushort> row, int x, int run)
    {
        for (int i = 0; i < run; i++)
        {
            if (offset + sizeof(ushort) > source.Length)
            {
                return offset;
            }

            row[x + i] = (ushort)(
                BinaryPrimitives.ReadUInt16LittleEndian(source[offset..]) | Color16.AlphaMask);

            offset += sizeof(ushort);
        }

        return offset;
    }
}
