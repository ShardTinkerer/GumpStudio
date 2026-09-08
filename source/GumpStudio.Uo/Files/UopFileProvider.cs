using System.Buffers.Binary;
using System.IO.Compression;

namespace GumpStudio.Uo.Files;

/// <summary>
/// Reads a Mythic <c>.uop</c> package, the container format clients have shipped
/// art in since late 2010.
/// </summary>
/// <remarks>
/// <para>
/// A package is a header followed by a linked list of blocks, each holding a
/// fixed number of entry descriptors. Entries are keyed by a hash of their
/// original build path rather than by index, so this reader hashes the supplied
/// pattern for every index up to <c>maxEntries</c> and builds the index-to-entry
/// mapping once at open time.
/// </para>
/// <para>
/// The old <c>Ultima</c> project had no UOP support whatsoever, which is why the
/// application could not read any client newer than roughly 7.0.24.
/// </para>
/// </remarks>
public sealed class UopFileProvider : IUoFileProvider
{
    /// <summary>"MYP\0" little-endian.</summary>
    private const uint Magic = 0x0050_594D;

    private const int EntryDescriptorSize = 34;

    /// <summary>Width and height, as two little-endian int32s, ahead of gump payloads.</summary>
    private const int DimensionPrefixSize = 8;

    private readonly SafeFileHandleOwner _data;
    private readonly UopEntry[] _entries;

    /// <summary>
    /// Whether this package prefixes payloads with their pixel dimensions, which
    /// only <c>gumpartLegacyMUL.uop</c> does.
    /// </summary>
    private readonly bool _hasDimensionPrefix;

    /// <summary>
    /// How much decoded payload to keep, in bytes.
    /// </summary>
    /// <remarks>
    /// Decoding all 5579 gumps of a retail package eagerly took minutes and
    /// hundreds of megabytes, so this is deliberately a modest window over the
    /// entries in play rather than the whole container.
    /// </remarks>
    private const long PayloadBudget = 32L * 1024 * 1024;

    /// <summary>
    /// Recently decoded payloads, most recently used last.
    /// </summary>
    /// <remarks>
    /// Decoding is expensive — inflate plus, for compression flag 3, a
    /// MegaCliloc pass — and the normal access pattern is
    /// <see cref="GetEntry"/> immediately followed by <see cref="Read"/> for the
    /// same index, so a memo of the single last payload already collapsed that
    /// pair to one decode.
    ///
    /// It only worked while requests arrived in that order and one at a time.
    /// An art browser scrolling through thumbnails interleaves indices, and on
    /// a container that carries its dimensions inside the payload every
    /// <c>GetEntry</c> is itself a full decode — so a one-entry memo turned
    /// each revisited entry back into a fresh inflate. A small window keeps the
    /// entries actually in play. Image-level caching still belongs in the
    /// rendering layer, not here.
    /// </remarks>
    private readonly Dictionary<int, ReadOnlyMemory<byte>> _payloads = [];
    private readonly LinkedList<int> _payloadOrder = new();
    private readonly Dictionary<int, LinkedListNode<int>> _payloadNodes = [];
    private long _payloadBytes;

    /// <summary>
    /// Guards the lazily-populated dimension cache and the payload window.
    /// </summary>
    /// <remarks>
    /// Both are written on first access, so concurrent readers — an art browser
    /// prefetching on a worker while the canvas renders on the UI thread — would
    /// otherwise race. Decoding dominates the cost, so the lock is not the
    /// bottleneck.
    /// </remarks>
    private readonly Lock _sync = new();

    private UopFileProvider(SafeFileHandleOwner data, UopEntry[] entries, bool hasDimensionPrefix)
    {
        _data = data;
        _entries = entries;
        _hasDimensionPrefix = hasDimensionPrefix;
    }

    public int Count => _entries.Length;

    /// <summary>
    /// Opens a package and resolves entries for indices <c>0 .. maxEntries - 1</c>.
    /// </summary>
    /// <param name="path">Path to the <c>.uop</c> file.</param>
    /// <param name="pattern">
    /// Build-path pattern with a <c>{0:D8}</c> placeholder, for example
    /// <c>build/gumpartlegacymul/{0:D8}.tga</c>.
    /// </param>
    /// <param name="maxEntries">Highest index to resolve, exclusive.</param>
    /// <param name="hasExtraDimensions">
    /// True for <c>gumpartLegacyMUL.uop</c>, whose payloads are prefixed with two
    /// little-endian int32s holding width and height. The MUL format carried
    /// those in the index Extra field, which UOP has no equivalent of.
    /// </param>
    public static UopFileProvider Open(
        string path,
        string pattern,
        int maxEntries,
        bool hasExtraDimensions = false)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentOutOfRangeException.ThrowIfNegative(maxEntries);

        SafeFileHandleOwner data = SafeFileHandleOwner.OpenRead(path);

        try
        {
            Dictionary<ulong, UopEntry> byHash = ReadDirectory(data, path);
            UopEntry[] entries = new UopEntry[maxEntries];

            for (int i = 0; i < maxEntries; i++)
            {
                entries[i] = byHash.TryGetValue(UopHash.ComputeForIndex(pattern, i), out UopEntry entry)
                    ? entry
                    : UopEntry.Missing;
            }

            return new UopFileProvider(data, entries, hasExtraDimensions);
        }
        catch
        {
            data.Dispose();

            throw;
        }
    }

    private static Dictionary<ulong, UopEntry> ReadDirectory(SafeFileHandleOwner data, string path)
    {
        Span<byte> header = stackalloc byte[28];

        if (data.Read(0, header) != header.Length)
        {
            throw new InvalidDataException($"'{path}' is too short to be a UOP package.");
        }

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(header);

        if (magic != Magic)
        {
            throw new InvalidDataException(
                $"'{path}' is not a UOP package (magic 0x{magic:X8}, expected 0x{Magic:X8}).");
        }

        long nextBlock = BinaryPrimitives.ReadInt64LittleEndian(header[12..]);
        int declaredCount = BinaryPrimitives.ReadInt32LittleEndian(header[24..]);

        Dictionary<ulong, UopEntry> result = new(Math.Max(declaredCount, 0));

        // Guard against a malformed package whose block list loops back on itself.
        HashSet<long> visited = [];

        // Allocated once: the block list can be long, and a stackalloc per
        // iteration would grow the frame without bound.
        Span<byte> blockHeader = stackalloc byte[12];

        while (nextBlock > 0 && nextBlock < data.Length && visited.Add(nextBlock))
        {
            if (data.Read(nextBlock, blockHeader) != blockHeader.Length)
            {
                break;
            }

            int filesInBlock = BinaryPrimitives.ReadInt32LittleEndian(blockHeader);
            long following = BinaryPrimitives.ReadInt64LittleEndian(blockHeader[4..]);

            if (filesInBlock <= 0)
            {
                nextBlock = following;

                continue;
            }

            byte[] descriptors = new byte[filesInBlock * EntryDescriptorSize];
            int read = data.Read(nextBlock + 12, descriptors);
            int usable = read / EntryDescriptorSize;

            for (int i = 0; i < usable; i++)
            {
                ReadOnlySpan<byte> record = descriptors.AsSpan(i * EntryDescriptorSize, EntryDescriptorSize);

                long offset = BinaryPrimitives.ReadInt64LittleEndian(record);

                // A zero offset marks an unused slot in the block.
                if (offset <= 0)
                {
                    continue;
                }

                int headerLength = BinaryPrimitives.ReadInt32LittleEndian(record[8..]);
                int compressedLength = BinaryPrimitives.ReadInt32LittleEndian(record[12..]);
                int decompressedLength = BinaryPrimitives.ReadInt32LittleEndian(record[16..]);
                ulong hash = BinaryPrimitives.ReadUInt64LittleEndian(record[20..]);
                short compression = BinaryPrimitives.ReadInt16LittleEndian(record[32..]);

                if (headerLength < 0 || compressedLength <= 0 || decompressedLength < 0)
                {
                    continue;
                }

                result[hash] = new UopEntry(
                    offset + headerLength,
                    compressedLength,
                    decompressedLength,
                    (UopCompression)compression,
                    Extra: 0);
            }

            nextBlock = following;
        }

        return result;
    }

    /// <summary>
    /// Resolves an entry's pixel dimensions, decoding it if necessary.
    /// </summary>
    /// <remarks>
    /// Gump payloads carry width and height as two little-endian int32s at the
    /// front of the fully decoded buffer, because UOP has no equivalent of the
    /// MUL index's <c>Extra</c> field. The dimensions are cached permanently
    /// (four bytes per entry) while the payload itself is not.
    /// </remarks>
    private void ResolveDimensions(int index)
    {
        ref UopEntry entry = ref _entries[index];

        // Checked again under the lock by the caller; this is the fast path.

        if (entry.DimensionsResolved || !entry.Exists)
        {
            return;
        }

        ReadOnlyMemory<byte> decoded = Decode(index);

        if (decoded.Length < DimensionPrefixSize)
        {
            entry = UopEntry.Missing;

            return;
        }

        ReadOnlySpan<byte> prefix = decoded.Span;

        int width = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        int height = BinaryPrimitives.ReadInt32LittleEndian(prefix[4..]);

        entry = entry with
        {
            Extra = ((width & 0xFFFF) << 16) | (height & 0xFFFF),
            HasDimensionPrefix = true,
            DimensionsResolved = true,
        };
    }

    public bool Exists(int index) =>
        (uint)index < (uint)_entries.Length && _entries[index].Exists;

    public UoFileEntry GetEntry(int index)
    {
        if ((uint)index >= (uint)_entries.Length)
        {
            return UoFileEntry.Missing;
        }

        UopEntry entry;

        lock (_sync)
        {
            if (_hasDimensionPrefix)
            {
                ResolveDimensions(index);
            }

            entry = _entries[index];
        }

        return entry.Exists
            ? new UoFileEntry(entry.Offset, entry.PayloadLength, entry.Extra, UoEntrySource.Primary)
            : UoFileEntry.Missing;
    }

    public ReadOnlyMemory<byte> Read(int index)
    {
        if ((uint)index >= (uint)_entries.Length)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        ReadOnlyMemory<byte> decoded;

        lock (_sync)
        {
            decoded = Decode(index);
        }

        if (!_hasDimensionPrefix)
        {
            return decoded;
        }

        return decoded.Length >= DimensionPrefixSize
            ? decoded[DimensionPrefixSize..]
            : ReadOnlyMemory<byte>.Empty;
    }

    /// <summary>Reads and fully decodes an entry, without stripping any prefix.</summary>
    /// <remarks>Callers must hold <see cref="_sync"/>.</remarks>
    private ReadOnlyMemory<byte> Decode(int index)
    {
        UopEntry entry = _entries[index];

        if (!entry.Exists)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        if (_payloads.TryGetValue(index, out ReadOnlyMemory<byte> cached))
        {
            TouchPayload(index);

            return cached;
        }

        byte[] raw = new byte[entry.CompressedLength];

        if (_data.Read(entry.Offset, raw) != raw.Length)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        ReadOnlyMemory<byte> result = entry.Compression switch
        {
            UopCompression.None => raw,
            UopCompression.Zlib => Inflate(raw, entry.DecompressedLength),
            UopCompression.ZlibMegaCliloc => MegaClilocDecoder.Decompress(
                Inflate(raw, entry.DecompressedLength).Span),
            _ => ReadOnlyMemory<byte>.Empty,
        };

        Remember(index, result);

        return result;
    }

    /// <summary>Adds a decoded payload to the window, evicting the oldest.</summary>
    private void Remember(int index, ReadOnlyMemory<byte> payload)
    {
        _payloads[index] = payload;
        _payloadNodes[index] = _payloadOrder.AddLast(index);
        _payloadBytes += payload.Length;

        // One entry always stays, so a payload larger than the whole budget is
        // still usable rather than being decoded and dropped every time.
        while (_payloadBytes > PayloadBudget && _payloads.Count > 1 && _payloadOrder.First is { } oldest)
        {
            int evicted = oldest.Value;

            _payloadOrder.RemoveFirst();
            _payloadNodes.Remove(evicted);

            if (_payloads.Remove(evicted, out ReadOnlyMemory<byte> dropped))
            {
                _payloadBytes -= dropped.Length;
            }
        }
    }

    private void TouchPayload(int index)
    {
        if (_payloadNodes.TryGetValue(index, out LinkedListNode<int>? node))
        {
            _payloadOrder.Remove(node);
            _payloadOrder.AddLast(node);
        }
    }

    private static ReadOnlyMemory<byte> Inflate(byte[] compressed, int decompressedLength)
    {
        byte[] output = new byte[decompressedLength];

        using MemoryStream source = new(compressed, writable: false);
        using ZLibStream inflater = new(source, CompressionMode.Decompress);

        int total = 0;

        while (total < output.Length)
        {
            int read = inflater.Read(output, total, output.Length - total);

            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total == output.Length ? output : output.AsMemory(0, total);
    }

    public void Dispose() => _data.Dispose();

    private readonly record struct UopEntry(
        long Offset,
        int CompressedLength,
        int DecompressedLength,
        UopCompression Compression,
        int Extra)
    {
        public static UopEntry Missing { get; } = new(-1, 0, 0, UopCompression.None, 0);

        public bool IsCompressed => Compression != UopCompression.None;

        public bool Exists => Offset >= 0 && CompressedLength > 0;

        /// <summary>
        /// True when the decoded payload begins with the eight-byte width/height
        /// prefix, which <see cref="Read"/> must strip.
        /// </summary>
        public bool HasDimensionPrefix { get; init; }

        /// <summary>True once the dimensions have been decoded and cached.</summary>
        public bool DimensionsResolved { get; init; }

        /// <summary>Length of the actual data, excluding any dimension prefix.</summary>
        public int PayloadLength => HasDimensionPrefix
            ? Math.Max(0, DecompressedLength - DimensionPrefixSize)
            : DecompressedLength;
    }
}
