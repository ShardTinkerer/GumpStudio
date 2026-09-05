using System.Buffers.Binary;

namespace GumpStudio.Uo.Files;

/// <summary>
/// Reads a classic <c>.idx</c> / <c>.mul</c> container pair, applying any
/// <c>verdata.mul</c> patches on top.
/// </summary>
public sealed class MulFileProvider : IUoFileProvider
{
    /// <summary>An index record is three little-endian int32s.</summary>
    private const int IndexRecordSize = 12;

    private readonly SafeFileHandleOwner _data;
    private readonly VerdataPatchSet _verdata;
    private readonly UoFileEntry[] _entries;

    private MulFileProvider(SafeFileHandleOwner data, VerdataPatchSet verdata, UoFileEntry[] entries)
    {
        _data = data;
        _verdata = verdata;
        _entries = entries;
    }

    public int Count => _entries.Length;

    /// <summary>
    /// Opens an index/data pair.
    /// </summary>
    /// <param name="indexPath">Path to the <c>.idx</c> file.</param>
    /// <param name="dataPath">Path to the <c>.mul</c> file.</param>
    /// <param name="kind">Which container this is, for verdata patch matching.</param>
    /// <param name="verdata">Patch set to overlay, or <see cref="VerdataPatchSet.Empty"/>.</param>
    /// <param name="maxEntries">
    /// Optional cap on the index size. Omit it to size the index from the
    /// <c>.idx</c> file itself, which is what lets High Seas-era clients with
    /// larger tables work without a code change.
    /// </param>
    public static MulFileProvider Open(
        string indexPath,
        string dataPath,
        UoFileKind kind,
        VerdataPatchSet? verdata = null,
        int? maxEntries = null)
    {
        ArgumentNullException.ThrowIfNull(indexPath);
        ArgumentNullException.ThrowIfNull(dataPath);

        verdata ??= VerdataPatchSet.Empty;

        SafeFileHandleOwner index = SafeFileHandleOwner.OpenRead(indexPath);
        SafeFileHandleOwner data;

        try
        {
            data = SafeFileHandleOwner.OpenRead(dataPath);
        }
        catch
        {
            index.Dispose();

            throw;
        }

        try
        {
            UoFileEntry[] entries = ReadIndex(index, data.Length, maxEntries);

            ApplyPatches(entries, kind, verdata);

            return new MulFileProvider(data, verdata, entries);
        }
        catch
        {
            data.Dispose();

            throw;
        }
        finally
        {
            index.Dispose();
        }
    }

    private static UoFileEntry[] ReadIndex(SafeFileHandleOwner index, long dataLength, int? maxEntries)
    {
        // Cap the record count so a malformed or absurdly large .idx cannot ask
        // for an allocation bigger than an array can hold.
        const int MaxRecords = 1 << 22;

        int recordCount = (int)Math.Min(index.Length / IndexRecordSize, MaxRecords);

        if (maxEntries is { } cap && cap < recordCount)
        {
            recordCount = cap;
        }

        // The index is small (a few hundred KB at most), so reading it whole is
        // cheaper than thousands of individual reads.
        byte[] raw = new byte[recordCount * IndexRecordSize];

        int read = index.Read(0, raw);
        int usable = read / IndexRecordSize;

        int size = maxEntries ?? usable;
        UoFileEntry[] entries = new UoFileEntry[size];

        for (int i = 0; i < entries.Length; i++)
        {
            if (i >= usable)
            {
                entries[i] = UoFileEntry.Missing;

                continue;
            }

            ReadOnlySpan<byte> record = raw.AsSpan(i * IndexRecordSize, IndexRecordSize);

            int lookup = BinaryPrimitives.ReadInt32LittleEndian(record);
            int length = BinaryPrimitives.ReadInt32LittleEndian(record[4..]);
            int extra = BinaryPrimitives.ReadInt32LittleEndian(record[8..]);

            // A negative lookup is the format's "no entry" marker. A record that
            // points outside the data file is corrupt; treat it as absent rather
            // than letting a decoder read whatever happens to be there.
            entries[i] = lookup < 0 || length <= 0 || lookup + (long)length > dataLength
                ? UoFileEntry.Missing
                : new UoFileEntry(lookup, length, extra, UoEntrySource.Primary);
        }

        return entries;
    }

    private static void ApplyPatches(UoFileEntry[] entries, UoFileKind kind, VerdataPatchSet verdata)
    {
        if (verdata.Count == 0)
        {
            return;
        }

        for (int i = 0; i < entries.Length; i++)
        {
            if (verdata.TryGetPatch(kind, i, out UoFileEntry patch))
            {
                entries[i] = patch;
            }
        }
    }

    public UoFileEntry GetEntry(int index) =>
        (uint)index < (uint)_entries.Length ? _entries[index] : UoFileEntry.Missing;

    public ReadOnlyMemory<byte> Read(int index)
    {
        UoFileEntry entry = GetEntry(index);

        if (!entry.Exists)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        byte[] buffer = new byte[entry.Length];

        int read = entry.Source == UoEntrySource.Verdata
            ? _verdata.Read(entry.Offset, buffer)
            : _data.Read(entry.Offset, buffer);

        // A short read means the file is truncated relative to its index.
        // Hand back only what genuinely exists so decoders see a short buffer
        // rather than a tail of zeroes that looks like real pixel data.
        return read == buffer.Length
            ? buffer
            : buffer.AsMemory(0, read);
    }

    public void Dispose() => _data.Dispose();
}
