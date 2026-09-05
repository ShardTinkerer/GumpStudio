using System.Buffers.Binary;
using System.Text;

namespace GumpStudio.Uo.Data;

/// <summary>
/// Tile properties from <c>tiledata.mul</c>.
/// </summary>
/// <remarks>
/// <para>
/// A gump editor only really needs the static item names, which the item-art
/// browser shows beside each tile, but flags and height come along for free.
/// </para>
/// <para>
/// Two layouts exist. Clients from High Seas onward widened the flags field to
/// 64 bits, growing a land record from 26 to 30 bytes and a static record from
/// 37 to 41. The layout is chosen from the file's length rather than hard-coded,
/// which is what lets one build read both a 2005-era and a current client.
/// </para>
/// </remarks>
public sealed class TileDataTable
{
    private const int TilesPerGroup = 32;
    private const int NameLength = 20;

    /// <summary>Exact size of a High Seas <c>tiledata.mul</c>.</summary>
    private const long HighSeasLength = (512L * 964) + (2048L * 1316);

    private readonly TileEntry[] _land;
    private readonly TileEntry[] _statics;

    private TileDataTable(TileEntry[] land, TileEntry[] statics, bool isHighSeas)
    {
        _land = land;
        _statics = statics;
        IsHighSeasFormat = isHighSeas;
    }

    /// <summary>True when the file used the 64-bit flags layout.</summary>
    public bool IsHighSeasFormat { get; }

    public int LandCount => _land.Length;

    public int StaticCount => _statics.Length;

    /// <summary>An empty table, used when the client has no <c>tiledata.mul</c>.</summary>
    public static TileDataTable Empty { get; } = new([], [], false);

    public static TileDataTable Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        byte[] raw = File.ReadAllBytes(path);

        bool highSeas = raw.LongLength >= HighSeasLength;

        int landRecord = highSeas ? 30 : 26;
        int staticRecord = highSeas ? 41 : 37;
        int landGroups = highSeas ? 512 : 512;

        int landGroupSize = sizeof(int) + (TilesPerGroup * landRecord);
        int staticGroupSize = sizeof(int) + (TilesPerGroup * staticRecord);

        long landSection = (long)landGroups * landGroupSize;

        if (raw.LongLength < landSection)
        {
            return Empty;
        }

        TileEntry[] land = ReadSection(raw, 0, landGroups, landGroupSize, landRecord, highSeas, isLand: true);

        int staticGroups = (int)((raw.LongLength - landSection) / staticGroupSize);

        TileEntry[] statics = ReadSection(
            raw, landSection, staticGroups, staticGroupSize, staticRecord, highSeas, isLand: false);

        return new TileDataTable(land, statics, highSeas);
    }

    private static TileEntry[] ReadSection(
        byte[] raw,
        long sectionStart,
        int groups,
        int groupSize,
        int recordSize,
        bool highSeas,
        bool isLand)
    {
        TileEntry[] entries = new TileEntry[groups * TilesPerGroup];

        int flagSize = highSeas ? sizeof(ulong) : sizeof(uint);

        // Within a static record the name sits after flags, weight, quality,
        // an unused dword, animId, an unused word, lightId and height.
        int nameOffset = isLand
            ? flagSize + sizeof(ushort)
            : flagSize + 1 + 1 + 4 + 2 + 2 + 2 + 1;

        for (int group = 0; group < groups; group++)
        {
            // Each group opens with a 4-byte tag the client reads and discards.
            long groupStart = sectionStart + ((long)group * groupSize) + sizeof(int);

            for (int i = 0; i < TilesPerGroup; i++)
            {
                long offset = groupStart + ((long)i * recordSize);
                int index = (group * TilesPerGroup) + i;

                if (offset + recordSize > raw.LongLength)
                {
                    entries[index] = new TileEntry(0, string.Empty);

                    continue;
                }

                ReadOnlySpan<byte> record = raw.AsSpan((int)offset, recordSize);

                ulong flags = highSeas
                    ? BinaryPrimitives.ReadUInt64LittleEndian(record)
                    : BinaryPrimitives.ReadUInt32LittleEndian(record);

                entries[index] = new TileEntry(flags, ReadName(record[nameOffset..]));
            }
        }

        return entries;
    }

    private static string ReadName(ReadOnlySpan<byte> raw)
    {
        ReadOnlySpan<byte> name = raw[..Math.Min(NameLength, raw.Length)];

        int end = name.IndexOf((byte)0);

        if (end >= 0)
        {
            name = name[..end];
        }

        // Tile names are NUL-padded ASCII in every shipped file.
        return Encoding.ASCII.GetString(name).Trim();
    }

    /// <summary>Properties of a static item tile, by item id.</summary>
    public TileEntry GetStatic(int itemId) =>
        (uint)itemId < (uint)_statics.Length ? _statics[itemId] : default;

    /// <summary>Properties of a land tile.</summary>
    public TileEntry GetLand(int landId) =>
        (uint)landId < (uint)_land.Length ? _land[landId] : default;

    /// <summary>The display name for a static item, or an empty string.</summary>
    public string GetStaticName(int itemId) => GetStatic(itemId).Name;
}

/// <summary>One tile's properties.</summary>
/// <param name="Flags">The tile's flag bitfield, widened to 64 bits for both layouts.</param>
/// <param name="Name">The NUL-padded ASCII description, trimmed.</param>
public readonly record struct TileEntry(ulong Flags, string Name)
{
    /// <summary>The name, or a placeholder when the tile is unnamed.</summary>
    public string DisplayName => string.IsNullOrEmpty(Name) ? "(unnamed)" : Name;
}
