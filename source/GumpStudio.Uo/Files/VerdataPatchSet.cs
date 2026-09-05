using System.Buffers.Binary;

namespace GumpStudio.Uo.Files;

/// <summary>
/// The contents of <c>verdata.mul</c>: a set of records that override entries in
/// the primary containers.
/// </summary>
/// <remarks>
/// Classic-era clients shipped incremental content patches this way. Modern
/// clients generally do not have the file at all, in which case
/// <see cref="Empty"/> is used and nothing is patched.
/// </remarks>
public sealed class VerdataPatchSet : IDisposable
{
    /// <summary>20 bytes: file, index, lookup, length, extra.</summary>
    private const int RecordSize = 20;

    private readonly SafeFileHandleOwner? _data;
    private readonly Dictionary<(UoFileKind Kind, int Index), UoFileEntry> _patches;

    private VerdataPatchSet(
        SafeFileHandleOwner? data,
        Dictionary<(UoFileKind, int), UoFileEntry> patches)
    {
        _data = data;
        _patches = patches;
    }

    /// <summary>A patch set that patches nothing.</summary>
    public static VerdataPatchSet Empty { get; } = new(null, []);

    /// <summary>Total number of patch records.</summary>
    public int Count => _patches.Count;

    /// <summary>
    /// Loads <c>verdata.mul</c> from <paramref name="path"/>, or returns
    /// <see cref="Empty"/> when the file is absent.
    /// </summary>
    public static VerdataPatchSet Load(string? path)
    {
        if (path is null || !File.Exists(path))
        {
            return Empty;
        }

        SafeFileHandleOwner data = SafeFileHandleOwner.OpenRead(path);

        try
        {
            Span<byte> countBuffer = stackalloc byte[sizeof(int)];

            if (data.Read(0, countBuffer) != countBuffer.Length)
            {
                // A file too short to hold even a count is not a usable patch set.
                data.Dispose();

                return Empty;
            }

            int count = BinaryPrimitives.ReadInt32LittleEndian(countBuffer);

            // Trust the file's length over its self-declared count: a truncated
            // verdata.mul otherwise walks off the end.
            long available = (data.Length - sizeof(int)) / RecordSize;

            if (count < 0 || count > available)
            {
                count = (int)Math.Max(0, available);
            }

            byte[] records = new byte[count * RecordSize];
            data.Read(sizeof(int), records);

            Dictionary<(UoFileKind, int), UoFileEntry> patches = new(count);

            for (int i = 0; i < count; i++)
            {
                ReadOnlySpan<byte> record = records.AsSpan(i * RecordSize, RecordSize);

                int file = BinaryPrimitives.ReadInt32LittleEndian(record);
                int index = BinaryPrimitives.ReadInt32LittleEndian(record[4..]);
                int lookup = BinaryPrimitives.ReadInt32LittleEndian(record[8..]);
                int length = BinaryPrimitives.ReadInt32LittleEndian(record[12..]);
                int extra = BinaryPrimitives.ReadInt32LittleEndian(record[16..]);

                if (index < 0 || lookup < 0 || length <= 0)
                {
                    continue;
                }

                // Later records for the same slot win, matching the original loader.
                patches[((UoFileKind)file, index)] =
                    new UoFileEntry(lookup, length, extra, UoEntrySource.Verdata);
            }

            return new VerdataPatchSet(data, patches);
        }
        catch (IOException)
        {
            data.Dispose();

            return Empty;
        }
    }

    /// <summary>Finds the patch overriding one slot, if any.</summary>
    public bool TryGetPatch(UoFileKind kind, int index, out UoFileEntry entry) =>
        _patches.TryGetValue((kind, index), out entry);

    /// <summary>Reads patch data. Only valid for entries returned by <see cref="TryGetPatch"/>.</summary>
    public int Read(long offset, Span<byte> destination) =>
        _data?.Read(offset, destination) ?? 0;

    public void Dispose() => _data?.Dispose();
}
