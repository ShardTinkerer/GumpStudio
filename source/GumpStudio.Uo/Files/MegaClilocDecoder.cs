using System.Buffers.Binary;

namespace GumpStudio.Uo.Files;

/// <summary>
/// Decodes MegaCliloc, the two-stage codec modern clients apply to cliloc files
/// and to some UOP payloads on top of zlib.
/// </summary>
/// <remarks>
/// <para>
/// A UOP entry's compression flag is 0 for stored, 1 for zlib, and 3 for "zlib
/// then MegaCliloc". Retail <c>gumpartLegacyMUL.uop</c> uses flag 3 for every
/// entry, so without this stage gump art is unreadable on any modern client —
/// and modern clients no longer ship <c>gumpart.mul</c> at all. A
/// <c>cliloc.*</c> file carries no zlib layer: the whole file is MegaCliloc.
/// </para>
/// <para>
/// The name is the client's own, taken from its error string "Error
/// (MegaCliloc) : StringId Not Found : ". It is often described as a
/// Burrows-Wheeler transform, which it is not: stage one is a move-to-front
/// cipher, and stage two a frequency-driven move-to-front expander. The format
/// is documented in <c>docs/uop-format.md</c>, which records the client function
/// each step was read from.
/// </para>
/// </remarks>
public static class MegaClilocDecoder
{
    /// <summary>
    /// Mask over the four-byte header that yields the decoded length.
    /// </summary>
    /// <remarks>
    /// Verified against the client matrix rather than taken on trust: the
    /// compressed <c>Cliloc.deu</c>, <c>.fra</c> and <c>.esp</c> of a 7.0.114.4
    /// client give 706,468, 722,744 and 678,210, which are the exact sizes of
    /// the same three files shipped uncompressed by a 7.0.50.0 client.
    /// </remarks>
    private const uint MagicMask = 0x8E2C9A3D;

    /// <summary>Bytes of little-endian int32 symbol counts at the head of the block.</summary>
    private const int FrequencyTableSize = 256 * sizeof(int);

    /// <summary>Largest output we will allocate, as a guard against a corrupt header.</summary>
    private const int MaxOutputLength = 64 * 1024 * 1024;

    /// <summary>
    /// Reverses the codec.
    /// </summary>
    /// <param name="input">
    /// A whole <c>cliloc.*</c> file, or a zlib-inflated payload whose entry had
    /// compression flag 3.
    /// </param>
    /// <returns>The decoded bytes, or an empty array if the input is malformed.</returns>
    /// <remarks>
    /// Returns empty rather than throwing on a malformed input. Both callers
    /// treat an undecodable payload as absent data, and a reader that threw
    /// part-way would leave them holding a partial buffer they cannot tell from
    /// a good one.
    /// </remarks>
    public static byte[] Decompress(ReadOnlySpan<byte> input)
    {
        if (input.Length < sizeof(uint) + FrequencyTableSize)
        {
            return [];
        }

        // The header is the decoded length, and the only place it is stated
        // exactly. The frequency table restates it, and is checked against this.
        uint declared = BinaryPrimitives.ReadUInt32LittleEndian(input) ^ MagicMask;

        if (declared is 0 or > MaxOutputLength)
        {
            return [];
        }

        int length = (int)declared;

        // Stage two reads a frequency table and then exactly one move code per
        // decoded byte. Shipped files carry several kilobytes beyond that, which
        // is padding from the encoder: neither decoded nor needed.
        int codedLength = FrequencyTableSize + length;

        if (input.Length - sizeof(uint) < codedLength)
        {
            return [];
        }

        byte[] coded = ReverseMoveToFront(input.Slice(sizeof(uint), codedLength));

        return Expand(coded, length);
    }

    /// <summary>
    /// Undoes the stage-one move-to-front cipher.
    /// </summary>
    /// <remarks>
    /// The client holds a 65536-entry table and keeps only the low byte of each
    /// entry, but every index it looks up comes from a byte, so entries above
    /// 255 are never reached and the table starts as the identity either way. A
    /// plain 256-byte table is exactly equivalent and far cheaper.
    /// </remarks>
    private static byte[] ReverseMoveToFront(ReadOnlySpan<byte> input)
    {
        Span<byte> table = stackalloc byte[256];

        for (int i = 0; i < table.Length; i++)
        {
            table[i] = (byte)i;
        }

        byte[] output = new byte[input.Length];

        for (int i = 0; i < input.Length; i++)
        {
            byte index = input[i];
            byte value = table[index];

            // Move the symbol to the front, sliding everything above it down.
            for (int k = index; k > 0; k--)
            {
                table[k] = table[k - 1];
            }

            table[0] = value;
            output[i] = value;
        }

        return output;
    }

    /// <summary>
    /// Runs stage two: a frequency table, then one move code per decoded byte.
    /// </summary>
    private static byte[] Expand(ReadOnlySpan<byte> coded, int length)
    {
        Span<int> counts = stackalloc int[256];
        Span<int> next = stackalloc int[256];
        Span<int> end = stackalloc int[256];

        long total = 0;

        for (int i = 0; i < 256; i++)
        {
            counts[i] = BinaryPrimitives.ReadInt32LittleEndian(coded[(i * sizeof(int))..]);

            if (counts[i] < 0)
            {
                return [];
            }

            total += counts[i];
        }

        // The client asserts this. Checking it is what turns a misread frequency
        // table into a clean failure rather than a plausible-looking buffer of
        // the wrong length.
        if (total != length)
        {
            return [];
        }

        ReadOnlySpan<byte> codes = coded[FrequencyTableSize..];

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

        // Each symbol owns a run of the code stream, laid out most frequent
        // first. The byte at the head of a run seeds the symbol table.
        for (int i = 0, cursor = 0; i < distinct; i++)
        {
            byte symbol = ordered[i];

            symbols[codes[cursor]] = symbol;
            next[symbol] = cursor + 1;
            cursor += counts[symbol];
            end[symbol] = cursor;
        }

        byte[] output = new byte[length];
        byte value = symbols[0];
        int remaining = distinct;

        // Every index below is in range because the counts sum to the declared
        // length and the code stream was cut to exactly that: a symbol's run
        // ends at its own cumulative total, and the last run ends at the end.
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
                byte index = codes[cursor];
                next[value] = cursor + 1;

                // Zero repeats the current symbol, which is how runs are coded.
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
