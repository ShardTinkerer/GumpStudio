using System.Buffers.Binary;

namespace GumpStudio.TestSupport;

/// <summary>
/// Builds synthetic <c>.idx</c> / <c>.mul</c> container pairs so the reader can
/// be tested without shipping (non-redistributable) client files.
/// </summary>
public sealed class MulFixture
{
    private readonly List<Record> _records = [];

    /// <summary>Appends a record holding <paramref name="data"/>.</summary>
    public MulFixture Add(byte[] data, int extra = 0)
    {
        ArgumentNullException.ThrowIfNull(data);

        _records.Add(new Record(data, extra, Present: true));

        return this;
    }

    /// <summary>Appends an empty slot, written to the index with a lookup of -1.</summary>
    public MulFixture AddMissing()
    {
        _records.Add(new Record([], 0, Present: false));

        return this;
    }

    /// <summary>
    /// Writes the pair into <paramref name="directory"/>.
    /// </summary>
    /// <param name="directory">Directory to write both files into.</param>
    /// <param name="baseName">Stem of the file names, producing <c>&lt;name&gt;idx.mul</c> and <c>&lt;name&gt;.mul</c>.</param>
    /// <param name="truncateDataBy">
    /// Cuts this many bytes off the end of the <c>.mul</c> without changing the
    /// index, producing the truncated-file case a corrupt install shows.
    /// </param>
    public (string IndexPath, string DataPath) Write(
        string directory,
        string baseName = "test",
        int truncateDataBy = 0)
    {
        ArgumentNullException.ThrowIfNull(directory);

        Directory.CreateDirectory(directory);

        string indexPath = Path.Combine(directory, baseName + "idx.mul");
        string dataPath = Path.Combine(directory, baseName + ".mul");

        using MemoryStream data = new();
        using MemoryStream index = new();

        Span<byte> record = stackalloc byte[12];

        foreach (Record entry in _records)
        {
            if (!entry.Present)
            {
                BinaryPrimitives.WriteInt32LittleEndian(record, -1);
                BinaryPrimitives.WriteInt32LittleEndian(record[4..], -1);
                BinaryPrimitives.WriteInt32LittleEndian(record[8..], -1);
            }
            else
            {
                BinaryPrimitives.WriteInt32LittleEndian(record, (int)data.Length);
                BinaryPrimitives.WriteInt32LittleEndian(record[4..], entry.Data.Length);
                BinaryPrimitives.WriteInt32LittleEndian(record[8..], entry.Extra);

                data.Write(entry.Data);
            }

            index.Write(record);
        }

        byte[] dataBytes = data.ToArray();

        if (truncateDataBy > 0)
        {
            dataBytes = dataBytes[..Math.Max(0, dataBytes.Length - truncateDataBy)];
        }

        File.WriteAllBytes(indexPath, index.ToArray());
        File.WriteAllBytes(dataPath, dataBytes);

        return (indexPath, dataPath);
    }

    /// <summary>Writes a <c>verdata.mul</c> containing the supplied patches.</summary>
    public static string WriteVerdata(string directory, IEnumerable<VerdataRecord> patches)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(patches);

        Directory.CreateDirectory(directory);

        List<VerdataRecord> list = [.. patches];
        string path = Path.Combine(directory, "verdata.mul");

        using MemoryStream body = new();
        using MemoryStream payload = new();

        Span<byte> record = stackalloc byte[20];

        // Patch data follows the record table, so offsets are only known once the
        // table size is fixed: count int + 20 bytes per record.
        int dataStart = 4 + (list.Count * 20);

        foreach (VerdataRecord patch in list)
        {
            BinaryPrimitives.WriteInt32LittleEndian(record, patch.File);
            BinaryPrimitives.WriteInt32LittleEndian(record[4..], patch.Index);
            BinaryPrimitives.WriteInt32LittleEndian(record[8..], dataStart + (int)payload.Length);
            BinaryPrimitives.WriteInt32LittleEndian(record[12..], patch.Data.Length);
            BinaryPrimitives.WriteInt32LittleEndian(record[16..], patch.Extra);

            body.Write(record);
            payload.Write(patch.Data);
        }

        using MemoryStream file = new();

        Span<byte> count = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(count, list.Count);

        file.Write(count);
        file.Write(body.ToArray());
        file.Write(payload.ToArray());

        File.WriteAllBytes(path, file.ToArray());

        return path;
    }

    private readonly record struct Record(byte[] Data, int Extra, bool Present);
}

/// <summary>One <c>verdata.mul</c> patch record.</summary>
public readonly record struct VerdataRecord(int File, int Index, byte[] Data, int Extra);
