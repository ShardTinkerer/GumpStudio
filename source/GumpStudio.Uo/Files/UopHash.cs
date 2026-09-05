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
/// The algorithm is the Jenkins lookup2 variant used by the Mythic packaging
/// tool. It is transcribed register-for-register from the original, which is why
/// the locals are named after x86 registers: the mixing order and the exact
/// rotate amounts are load-bearing, and "tidying" them silently produces a hash
/// that matches nothing. <c>UopHashTests</c> pins it against values taken from a
/// real client package.
/// </para>
/// </remarks>
public static class UopHash
{
    /// <summary>Hashes a build path, which must already be lowercase ASCII.</summary>
    public static ulong Compute(ReadOnlySpan<char> path)
    {
        uint eax = 0;
        uint ecx;
        uint edx;

        uint ebx = (uint)path.Length + 0xDEADBEEF;
        uint edi = ebx;
        uint esi = ebx;

        int i = 0;

        for (; i + 12 < path.Length; i += 12)
        {
            edi += Read32(path, i + 4);
            esi += Read32(path, i + 8);
            edx = Read32(path, i) - esi;

            edx = (edx + ebx) ^ Rotate(esi, 4);
            esi += edi;
            edi = (edi - edx) ^ Rotate(edx, 6);
            edx += esi;
            esi = (esi - edi) ^ Rotate(edi, 8);
            edi += edx;
            ebx = (edx - esi) ^ Rotate(esi, 16);
            esi += edi;
            edi = (edi - ebx) ^ Rotate(ebx, 19);
            ebx += esi;
            esi = (esi - edi) ^ Rotate(edi, 4);
            edi += ebx;
        }

        int remaining = path.Length - i;

        if (remaining == 0)
        {
            return ((ulong)esi << 32) | eax;
        }

        // Tail characters are folded in most-significant-first, falling through.
        switch (remaining)
        {
            case 12: esi += (uint)path[i + 11] << 24; goto case 11;
            case 11: esi += (uint)path[i + 10] << 16; goto case 10;
            case 10: esi += (uint)path[i + 9] << 8; goto case 9;
            case 9: esi += path[i + 8]; goto case 8;
            case 8: edi += (uint)path[i + 7] << 24; goto case 7;
            case 7: edi += (uint)path[i + 6] << 16; goto case 6;
            case 6: edi += (uint)path[i + 5] << 8; goto case 5;
            case 5: edi += path[i + 4]; goto case 4;
            case 4: ebx += (uint)path[i + 3] << 24; goto case 3;
            case 3: ebx += (uint)path[i + 2] << 16; goto case 2;
            case 2: ebx += (uint)path[i + 1] << 8; goto case 1;
            case 1: ebx += path[i]; break;
            default: break;
        }

        esi = (esi ^ edi) - Rotate(edi, 14);
        ecx = (esi ^ ebx) - Rotate(esi, 11);
        edi = (edi ^ ecx) - Rotate(ecx, 25);
        esi = (esi ^ edi) - Rotate(edi, 16);
        edx = (esi ^ ecx) - Rotate(esi, 4);
        edi = (edi ^ edx) - Rotate(edx, 14);
        eax = (esi ^ edi) - Rotate(edi, 24);

        return ((ulong)edi << 32) | eax;
    }

    /// <summary>Hashes the path produced by formatting <paramref name="pattern"/> with an index.</summary>
    /// <param name="pattern">
    /// A build path containing a single <c>{0:D8}</c> placeholder, for example
    /// <c>build/gumpartlegacymul/{0:D8}.tga</c>.
    /// </param>
    /// <param name="index">The entry index to substitute.</param>
    public static ulong ComputeForIndex(string pattern, int index)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        return Compute(
            string.Format(CultureInfo.InvariantCulture, pattern, index).ToLowerInvariant());
    }

    private static uint Rotate(uint value, int bits) => (value >> (32 - bits)) | (value << bits);

    private static uint Read32(ReadOnlySpan<char> path, int offset) =>
        (uint)((path[offset + 3] << 24) | (path[offset + 2] << 16) | (path[offset + 1] << 8) | path[offset]);
}
