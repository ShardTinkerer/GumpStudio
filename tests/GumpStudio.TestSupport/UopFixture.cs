using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;

namespace GumpStudio.TestSupport;

/// <summary>
/// Builds synthetic <c>.uop</c> packages for testing the reader.
/// </summary>
/// <remarks>
/// This writer intentionally mirrors the on-disk layout described by the reader:
/// header, then a linked list of blocks of entry descriptors, then payloads. It
/// deliberately does <em>not</em> reuse the production hash function — the caller
/// supplies hashes — so a round-trip test exercises the container plumbing
/// without hiding a wrong hash behind a matching writer.
/// </remarks>
public sealed class UopFixture
{
    private const uint Magic = 0x0050_594D;
    private const int EntryDescriptorSize = 34;

    private readonly List<Entry> _entries = [];

    /// <summary>Number of descriptor slots per block. Small values exercise the block chain.</summary>
    public int BlockSize { get; init; } = 100;

    /// <summary>Adds a payload addressed by <paramref name="hash"/>.</summary>
    public UopFixture Add(ulong hash, byte[] payload, bool compress = false)
    {
        ArgumentNullException.ThrowIfNull(payload);

        _entries.Add(new Entry(hash, payload, compress));

        return this;
    }

    /// <summary>
    /// Adds a gump-style payload, prefixed with the eight-byte width/height
    /// header that <c>gumpartLegacyMUL.uop</c> uses.
    /// </summary>
    public UopFixture AddWithDimensions(
        ulong hash,
        byte[] payload,
        int width,
        int height,
        bool compress = false)
    {
        ArgumentNullException.ThrowIfNull(payload);

        byte[] prefixed = new byte[payload.Length + 8];

        BinaryPrimitives.WriteInt32LittleEndian(prefixed, width);
        BinaryPrimitives.WriteInt32LittleEndian(prefixed.AsSpan(4), height);
        payload.CopyTo(prefixed.AsSpan(8));

        return Add(hash, prefixed, compress);
    }

    /// <summary>Formats the build path a client would use for an indexed entry.</summary>
    public static string BuildPath(string pattern, int index) =>
        string.Format(CultureInfo.InvariantCulture, pattern, index).ToLowerInvariant();

    /// <summary>Writes the package and returns its path.</summary>
    public string Write(string directory, string fileName = "test.uop")
    {
        ArgumentNullException.ThrowIfNull(directory);

        Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, fileName);

        using MemoryStream file = new();

        // Header: magic, version, signature, first block offset, block size, file count.
        const int HeaderSize = 28;

        int blockCount = Math.Max(1, (int)Math.Ceiling(_entries.Count / (double)BlockSize));

        WriteUInt32(file, Magic);
        WriteUInt32(file, 5);
        WriteUInt32(file, 0xFD23EC43);
        WriteInt64(file, HeaderSize);
        WriteInt32(file, BlockSize);
        WriteInt32(file, _entries.Count);

        // Payloads go after every block, so their offsets need the total block
        // region size up front.
        long blockRegion = (long)blockCount * (12 + ((long)BlockSize * EntryDescriptorSize));
        long payloadCursor = HeaderSize + blockRegion;

        using MemoryStream payloads = new();
        List<byte[]> stored = [];

        foreach (Entry entry in _entries)
        {
            stored.Add(entry.Compress ? Deflate(entry.Payload) : entry.Payload);
        }

        for (int block = 0; block < blockCount; block++)
        {
            int first = block * BlockSize;
            int inBlock = Math.Min(BlockSize, _entries.Count - first);

            long nextBlock = block + 1 < blockCount
                ? HeaderSize + ((long)(block + 1) * (12 + ((long)BlockSize * EntryDescriptorSize)))
                : 0;

            WriteInt32(file, inBlock);
            WriteInt64(file, nextBlock);

            for (int slot = 0; slot < BlockSize; slot++)
            {
                if (slot >= inBlock)
                {
                    // Unused slot: an all-zero descriptor, offset 0.
                    file.Write(new byte[EntryDescriptorSize]);

                    continue;
                }

                Entry entry = _entries[first + slot];
                byte[] data = stored[first + slot];

                WriteInt64(file, payloadCursor + payloads.Length);
                WriteInt32(file, 0);                    // header length
                WriteInt32(file, data.Length);          // compressed length
                WriteInt32(file, entry.Payload.Length); // decompressed length
                WriteUInt64(file, entry.Hash);
                WriteUInt32(file, 0);                   // data hash, unchecked by the reader
                WriteInt16(file, (short)(entry.Compress ? 1 : 0));

                payloads.Write(data);
            }
        }

        file.Write(payloads.ToArray());

        File.WriteAllBytes(path, file.ToArray());

        return path;
    }

    private static byte[] Deflate(byte[] input)
    {
        using MemoryStream output = new();

        using (ZLibStream compressor = new(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            compressor.Write(input);
        }

        return output.ToArray();
    }

    private static void WriteInt16(Stream s, short v)
    {
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(b, v);
        s.Write(b);
    }

    private static void WriteInt32(Stream s, int v)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(b, v);
        s.Write(b);
    }

    private static void WriteUInt32(Stream s, uint v)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, v);
        s.Write(b);
    }

    private static void WriteInt64(Stream s, long v)
    {
        Span<byte> b = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(b, v);
        s.Write(b);
    }

    private static void WriteUInt64(Stream s, ulong v)
    {
        Span<byte> b = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(b, v);
        s.Write(b);
    }

    private readonly record struct Entry(ulong Hash, byte[] Payload, bool Compress);
}
