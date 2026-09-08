using System.Buffers.Binary;

namespace GumpStudio.TestSupport;

/// <summary>
/// Encodes MegaCliloc, so the decoder can be tested without a client.
/// </summary>
/// <remarks>
/// <para>
/// Client files are not redistributable, which left the decoder covered only by
/// tests that skip on a machine without a UO installation — CI never ran it.
/// This is the encoder that closes that gap.
/// </para>
/// <para>
/// It is derived from the format rather than ported from a client routine, and
/// it does not try to reproduce the byte stream EA's encoder emits: any stream
/// the decoder turns back into the original is a valid encoding, and there are
/// many. What makes it a real test is that nothing here shares code with the
/// decoder, so a mistake in one does not cancel out in the other.
/// </para>
/// <para>
/// The construction falls out of one observation. Let <c>T(p)</c> be the
/// decoder's symbol table before it emits byte <c>p</c>; the format guarantees
/// <c>T(p)</c> holds the symbols still to come, ordered by when each next
/// appears. So the move code for position <c>p</c> is simply the rank of that
/// symbol in what remains after it, which one backward pass over the input
/// yields directly.
/// </para>
/// </remarks>
public static class MegaClilocFixture
{
    /// <summary>Mask over the four-byte header that yields the decoded length.</summary>
    private const uint MagicMask = 0x8E2C9A3D;

    /// <summary>Bytes of little-endian int32 symbol counts at the head of the block.</summary>
    private const int FrequencyTableSize = 256 * sizeof(int);

    /// <summary>
    /// Encodes <paramref name="plain"/> into a MegaCliloc payload.
    /// </summary>
    /// <param name="plain">The bytes a decoder should get back.</param>
    /// <param name="trailingPadding">
    /// Junk bytes appended after the code stream. Shipped files carry several
    /// kilobytes of it, so a decoder that sizes its work from the header rather
    /// than from the file length needs a test that it is ignored.
    /// </param>
    public static byte[] Encode(ReadOnlySpan<byte> plain, int trailingPadding = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(trailingPadding);

        Span<int> counts = stackalloc int[256];

        foreach (byte value in plain)
        {
            counts[value]++;
        }

        // Runs are laid out most frequent first, and ties break towards the
        // lower byte value — the order the decoder rebuilds from the same table.
        Span<byte> ordered = stackalloc byte[256];
        int distinct = OrderByDescendingCount(counts, ordered);

        Span<int> start = stackalloc int[256];
        Span<int> cursor = stackalloc int[256];

        for (int i = 0, at = 0; i < distinct; i++)
        {
            byte symbol = ordered[i];

            start[symbol] = at;
            at += counts[symbol];

            // Each symbol's codes are filled from the back, because the backward
            // pass below meets its occurrences in reverse.
            cursor[symbol] = at - 1;
        }

        byte[] codes = new byte[plain.Length];

        // Symbols still to come, soonest first. Walking backwards means the
        // position of a symbol in this list is exactly the rank the decoder will
        // reinsert it at.
        List<byte> upcoming = new(distinct);

        for (int p = plain.Length - 1; p >= 0; p--)
        {
            byte symbol = plain[p];
            int rank = upcoming.IndexOf(symbol);

            if (rank >= 0)
            {
                // Not the symbol's last occurrence, so it carries a move code.
                // Its final occurrence carries none: the decoder notices the run
                // is spent and drops the symbol instead of reading a code.
                codes[cursor[symbol]--] = (byte)rank;
                upcoming.RemoveAt(rank);
            }

            upcoming.Insert(0, symbol);
        }

        // The pass ends holding the symbols in order of first appearance, which
        // is the seed the decoder needs: the byte at the head of a run says where
        // in the initial table that run's symbol belongs.
        for (int rank = 0; rank < upcoming.Count; rank++)
        {
            codes[start[upcoming[rank]]] = (byte)rank;
        }

        byte[] block = new byte[FrequencyTableSize + codes.Length];

        for (int i = 0; i < 256; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(block.AsSpan(i * sizeof(int)), counts[i]);
        }

        codes.CopyTo(block.AsSpan(FrequencyTableSize));

        byte[] output = new byte[sizeof(uint) + block.Length + trailingPadding];

        BinaryPrimitives.WriteUInt32LittleEndian(output, (uint)plain.Length ^ MagicMask);
        MoveToFront(block, output.AsSpan(sizeof(uint)));

        for (int i = output.Length - trailingPadding; i < output.Length; i++)
        {
            output[i] = 0xFF;
        }

        return output;
    }

    /// <summary>
    /// Applies the stage-one move-to-front cipher, emitting the index of each
    /// byte in the table and promoting it to the front.
    /// </summary>
    private static void MoveToFront(ReadOnlySpan<byte> input, Span<byte> output)
    {
        Span<byte> table = stackalloc byte[256];

        for (int i = 0; i < table.Length; i++)
        {
            table[i] = (byte)i;
        }

        for (int i = 0; i < input.Length; i++)
        {
            byte value = input[i];
            int index = table.IndexOf(value);

            output[i] = (byte)index;

            for (int k = index; k > 0; k--)
            {
                table[k] = table[k - 1];
            }

            table[0] = value;
        }
    }

    /// <summary>Lists the symbols that occur, most frequent first.</summary>
    /// <returns>How many symbols occur at all.</returns>
    private static int OrderByDescendingCount(ReadOnlySpan<int> counts, Span<byte> ordered)
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
                return i;
            }

            ordered[i] = bestIndex;
            remaining[bestIndex] = 0;
        }

        return 256;
    }
}
