namespace GumpStudio.Uo.Files;

/// <summary>How a UOP entry's payload is encoded.</summary>
public enum UopCompression : ushort
{
    /// <summary>Stored verbatim.</summary>
    None = 0,

    /// <summary>Deflate with a zlib wrapper.</summary>
    Zlib = 1,

    /// <summary>
    /// Zlib, then a Burrows-Wheeler stage. Retail <c>gumpartLegacyMUL.uop</c>
    /// uses this for every entry.
    /// </summary>
    ZlibBwt = 3,
}
