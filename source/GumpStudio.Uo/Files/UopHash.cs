using System.Globalization;

namespace GumpStudio.Uo.Files;

/// <summary>
/// The 64-bit path hash used to address entries inside a <c>.uop</c> package.
/// </summary>
/// <remarks>
/// <para>
/// UOP packages do not store file names. Each entry is keyed by this hash of its
/// original build path, for example <c>build/gumpartlegacymul/00000123.tga</c>,
/// so a reader must hash the expected path to find an entry.
/// </para>
/// <para>
/// The algorithm is Bob Jenkins' <c>lookup3</c>, in its <c>hashlittle2</c>
/// two-word form; the client's copy lives at <c>0x0042C9B2</c>. Each character
/// contributes its full 16-bit value, the seed is <c>length + 0xDEADBEEF</c>,
/// input is consumed in blocks of twelve, and the 64-bit result packs the two
/// output words as <c>(b &lt;&lt; 32) | c</c>.
/// </para>
/// <para>
/// The mixing order and the exact rotate amounts are load-bearing: "tidying"
/// them silently produces a hash that matches nothing. <c>UopHashTests</c> pins
/// this against values taken from a real client package, and is the only thing
/// that makes changing this file safe.
/// </para>
/// </remarks>
public static class UopHash
{
    /// <summary>Hashes a build path, which must already be lowercase ASCII.</summary>
    public static ulong Compute(ReadOnlySpan<char> path)
    {
        uint a = (uint)path.Length + 0xDEADBEEF;
        uint b = a;
        uint c = a;

        int i = 0;
        int remaining = path.Length;

        while (remaining > 12)
        {
            a += Read32(path, i);
            b += Read32(path, i + 4);
            c += Read32(path, i + 8);

            Mix(ref a, ref b, ref c);

            i += 12;
            remaining -= 12;
        }

        // The tail is folded in most-significant-first, falling through.
        switch (remaining)
        {
            case 12: c += (uint)path[i + 11] << 24; goto case 11;
            case 11: c += (uint)path[i + 10] << 16; goto case 10;
            case 10: c += (uint)path[i + 9] << 8; goto case 9;
            case 9: c += path[i + 8]; goto case 8;
            case 8: b += (uint)path[i + 7] << 24; goto case 7;
            case 7: b += (uint)path[i + 6] << 16; goto case 6;
            case 6: b += (uint)path[i + 5] << 8; goto case 5;
            case 5: b += path[i + 4]; goto case 4;
            case 4: a += (uint)path[i + 3] << 24; goto case 3;
            case 3: a += (uint)path[i + 2] << 16; goto case 2;
            case 2: a += (uint)path[i + 1] << 8; goto case 1;
            case 1: a += path[i]; break;

            // An empty path is never mixed at all, so the low word stays zero.
            case 0: return (ulong)c << 32;
        }

        Final(ref a, ref b, ref c);

        return ((ulong)b << 32) | c;
    }

    /// <summary>The block mix, run once per twelve characters consumed.</summary>
    private static void Mix(ref uint a, ref uint b, ref uint c)
    {
        a -= c; a ^= Rotate(c, 4); c += b;
        b -= a; b ^= Rotate(a, 6); a += c;
        c -= b; c ^= Rotate(b, 8); b += a;
        a -= c; a ^= Rotate(c, 16); c += b;
        b -= a; b ^= Rotate(a, 19); a += c;
        c -= b; c ^= Rotate(b, 4); b += a;
    }

    /// <summary>The avalanche applied once, after the tail has been folded in.</summary>
    private static void Final(ref uint a, ref uint b, ref uint c)
    {
        c ^= b; c -= Rotate(b, 14);
        a ^= c; a -= Rotate(c, 11);
        b ^= a; b -= Rotate(a, 25);
        c ^= b; c -= Rotate(b, 16);
        a ^= c; a -= Rotate(c, 4);
        b ^= a; b -= Rotate(a, 14);
        c ^= b; c -= Rotate(b, 24);
    }

    /// <summary>Hashes the path produced by formatting <paramref name="pattern"/> with an index.</summary>
    /// <param name="pattern">
    /// A build path containing a single <c>{0:D8}</c> placeholder, for example
    /// <c>build/gumpartlegacymul/{0:D8}.tga</c>.
    /// </param>
    /// <param name="index">The entry index to substitute.</param>
    /// <remarks>
    /// Formatted into a stack buffer rather than through
    /// <see cref="string.Format(IFormatProvider, string, object?)"/> and
    /// <c>ToLowerInvariant</c>. Opening a client hashes every possible index of
    /// every container — over a hundred and forty thousand of them — and the two
    /// strings per call were the largest single source of allocation in a client
    /// open. <see cref="Compute(ReadOnlySpan{char})"/> already took a span.
    ///
    /// The pattern is lowercased as it is copied, so a caller need not hold a
    /// pre-lowered copy: these paths are ASCII, and only the placeholder's
    /// digits are added.
    /// </remarks>
    public static ulong ComputeForIndex(string pattern, int index)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        const string Placeholder = "{0:D8}";

        int at = pattern.IndexOf(Placeholder, StringComparison.Ordinal);

        if (at < 0)
        {
            // Not a pattern this fast path understands; fall back rather than
            // guess at where the index belongs.
            return Compute(
                string.Format(CultureInfo.InvariantCulture, pattern, index).ToLowerInvariant());
        }

        // The placeholder yields exactly eight digits.
        const int Digits = 8;

        int length = pattern.Length - Placeholder.Length + Digits;

        Span<char> buffer = length <= 256 ? stackalloc char[256] : new char[length];
        Span<char> path = buffer[..length];

        ReadOnlySpan<char> prefix = pattern.AsSpan(0, at);
        ReadOnlySpan<char> suffix = pattern.AsSpan(at + Placeholder.Length);

        prefix.ToLowerInvariant(path[..prefix.Length]);

        if (!index.TryFormat(
                path.Slice(prefix.Length, Digits), out _, "D8", CultureInfo.InvariantCulture))
        {
            // An index needing more than eight digits cannot appear in a
            // container this format can address.
            return Compute(
                string.Format(CultureInfo.InvariantCulture, pattern, index).ToLowerInvariant());
        }

        suffix.ToLowerInvariant(path[(prefix.Length + Digits)..]);

        return Compute(path);
    }

    private static uint Rotate(uint value, int bits) => (value >> (32 - bits)) | (value << bits);

    private static uint Read32(ReadOnlySpan<char> path, int offset) =>
        (uint)((path[offset + 3] << 24) | (path[offset + 2] << 16) | (path[offset + 1] << 8) | path[offset]);
}
