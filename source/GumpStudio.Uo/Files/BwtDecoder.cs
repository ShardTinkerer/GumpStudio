using System.Buffers.Binary;

namespace GumpStudio.Uo.Files;

/// <summary>
/// Decodes the Burrows-Wheeler stage that modern clients apply to some UOP
/// payloads on top of zlib.
/// </summary>
/// <remarks>
/// <para>
/// A UOP entry's compression flag is 0 for stored, 1 for zlib, and 3 for
/// "zlib then BWT". Retail <c>gumpartLegacyMUL.uop</c> uses flag 3 for every
/// entry, so without this stage gump art is unreadable on any modern client —
/// and modern clients no longer ship <c>gumpart.mul</c> at all.
/// </para>
/// <para>
/// The transform is a move-to-front pass followed by an inverse
/// Burrows-Wheeler using a 1024-byte symbol-count header. The format was
/// determined by reference to the ClassicUO project's implementation
/// (BSD-2-Clause), then verified here: decoding a retail gump yields the exact
/// byte sequence the same gump has in a legacy <c>gumpart.mul</c>.
/// </para>
/// </remarks>
public static class BwtDecoder
{
    /// <summary>Bytes of little-endian int32 symbol counts at the head of the block.</summary>
    private const int SymbolCountTableSize = 256 * sizeof(int);

    /// <summary>Largest output we will allocate, as a guard against a corrupt header.</summary>
    private const int MaxOutputLength = 64 * 1024 * 1024;

    /// <summary>
    /// Reverses the transform.
    /// </summary>
    /// <param name="input">A zlib-inflated payload whose entry had compression flag 3.</param>
    /// <returns>The decoded bytes, or an empty array if the input is malformed.</returns>
    public static byte[] Decompress(ReadOnlySpan<byte> input)
    {
        // 4-byte header, then the first move-to-front symbol.
        if (input.Length < 6)
        {
            return [];
        }

        byte[] moveToFront = ReverseMoveToFront(input);

        return moveToFront.Length == 0 ? [] : InverseBurrowsWheeler(moveToFront);
    }

    /// <summary>
    /// Undoes the move-to-front coding.
    /// </summary>
    /// <remarks>
    /// The original builds a 65536-entry table and sorts it, but every index it
    /// ever uses comes from a byte, and sorting a full permutation of
    /// 0..65535 always yields the identity. So only the first 256 entries are
    /// ever touched and they always start as 0..255 — a plain 256-byte table is
    /// exactly equivalent and far cheaper.
    /// </remarks>
    private static byte[] ReverseMoveToFront(ReadOnlySpan<byte> input)
    {
        Span<byte> table = stackalloc byte[256];

        for (int i = 0; i < table.Length; i++)
        {
            table[i] = (byte)i;
        }

        byte[] output = new byte[input.Length - 4];

        byte current = input[4];
        int position = 5;
        int written = 0;

        while (position < input.Length)
        {
            byte value = table[current];

            // Move the symbol to the front, sliding everything above it down.
            for (int i = current; i > 0; i--)
            {
                table[i] = table[i - 1];
            }

            table[0] = value;
            output[written++] = value;

            current = input[position++];
        }

        return output;
    }

    private static byte[] InverseBurrowsWheeler(ReadOnlySpan<byte> input)
    {
        if (input.Length < SymbolCountTableSize)
        {
            return [];
        }

        // The block opens with one int32 occurrence count per possible byte.
        Span<int> counts = stackalloc int[256];
        Span<int> next = stackalloc int[256];
        Span<int> end = stackalloc int[256];

        long total = 0;

        for (int i = 0; i < 256; i++)
        {
            counts[i] = BinaryPrimitives.ReadInt32LittleEndian(input[(i * sizeof(int))..]);

            if (counts[i] < 0)
            {
                return [];
            }

            total += counts[i];
        }

        if (total is 0 or > MaxOutputLength)
        {
            return [];
        }

        ReadOnlySpan<byte> body = input[SymbolCountTableSize..];
        int length = (int)total;

        Span<byte> symbols = stackalloc byte[256];

        for (int i = 0; i < 256; i++)
        {
            symbols[i] = (byte)i;
        }

        int distinct = 0;

        for (int i = 0; i < 256; i++)
        {
            if (counts[i] != 0)
            {
                distinct++;
            }
        }

        Span<byte> ordered = stackalloc byte[256];
        OrderByDescendingCount(counts, ordered);

        for (int i = 0, cursor = 0; i < distinct; i++)
        {
            byte symbol = ordered[i];

            if (cursor >= body.Length)
            {
                return [];
            }

            symbols[body[cursor]] = symbol;
            next[symbol] = cursor + 1;
            cursor += counts[symbol];
            end[symbol] = cursor;
        }

        byte[] output = new byte[length];
        byte value = symbols[0];
        int remaining = distinct;

        for (int written = 0; written < length; written++)
        {
            output[written] = value;

            if (next[value] >= end[value])
            {
                // This symbol's run is exhausted; drop it from the alphabet.
                remaining--;

                if (remaining >= 0)
                {
                    ShiftLeft(symbols, remaining);
                    value = symbols[0];
                }
            }
            else
            {
                int cursor = next[value];

                if (cursor >= body.Length)
                {
                    return [];
                }

                byte index = body[cursor];
                next[value] = cursor + 1;

                if (index != 0)
                {
                    ShiftLeft(symbols, index);
                    symbols[index] = value;
                    value = symbols[0];
                }
            }
        }

        return output;
    }

    /// <summary>Lists the symbols that occur, most frequent first.</summary>
    private static void OrderByDescendingCount(ReadOnlySpan<int> counts, Span<byte> ordered)
    {
        Span<int> remaining = stackalloc int[256];
        counts.CopyTo(remaining);

        for (int i = 0; i < 256; i++)
        {
            int best = 0;
            byte bestIndex = 0;

            for (int j = 0; j < 256; j++)
            {
                if (remaining[j] > best)
                {
                    bestIndex = (byte)j;
                    best = remaining[j];
                }
            }

            if (best == 0)
            {
                return;
            }

            ordered[i] = bestIndex;
            remaining[bestIndex] = 0;
        }
    }

    private static void ShiftLeft(Span<byte> values, int count)
    {
        for (int i = 0; i < count; i++)
        {
            values[i] = values[i + 1];
        }
    }
}
