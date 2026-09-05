using System.Buffers.Binary;
using System.Text;

using GumpStudio.Uo.Files;

namespace GumpStudio.Uo.Data;

/// <summary>
/// The localised string database from <c>cliloc.&lt;lang&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// Gump elements reference these strings by id, so the editor needs them to show
/// a localised label for an <c>AddHtmlLocalized</c>-style element.
/// </para>
/// <para>
/// Two on-disk forms exist. Classic clients store the record stream verbatim.
/// Modern clients wrap the whole file in the "MegaCliloc" codec, which turns out
/// to be the same Burrows-Wheeler transform UOP uses for compression flag 3 —
/// so <see cref="BwtDecoder"/> handles both. The old SDK knew nothing about the
/// wrapper and would read a modern file as garbage.
/// </para>
/// <para>
/// Clients older than roughly 2002 instead ship numbered <c>clilocNN.enu</c>
/// chunks in an IFF <c>FORM</c>/<c>DATA</c> container. That format is not
/// supported; such a client simply yields an empty table.
/// </para>
/// </remarks>
public sealed class ClilocTable
{
    /// <summary>u32 version, then a u16.</summary>
    private const int HeaderSize = 6;

    /// <summary>u32 id, u8 flag, u16 length.</summary>
    private const int EntryHeaderSize = 7;

    /// <summary>
    /// Bytes of unparsed tail tolerated before a buffer is judged not to be a
    /// plain record stream.
    /// </summary>
    private const int MaxTrailingSlack = 16;

    private readonly Dictionary<int, ClilocEntry> _entries;

    private ClilocTable(Dictionary<int, ClilocEntry> entries) => _entries = entries;

    /// <summary>An empty table, used when the client ships no readable cliloc file.</summary>
    public static ClilocTable Empty { get; } = new([]);

    public int Count => _entries.Count;

    /// <summary>All entries, ordered by id.</summary>
    public IEnumerable<ClilocEntry> Entries => _entries.Values.OrderBy(e => e.Id);

    /// <summary>Loads and, if necessary, unwraps a cliloc file.</summary>
    public static ClilocTable Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return Parse(File.ReadAllBytes(path));
    }

    /// <summary>Parses cliloc bytes, unwrapping the MegaCliloc codec when present.</summary>
    /// <remarks>
    /// Detection is by trial rather than by sniffing the header. Shipped files
    /// carry at least version 0 and version 2, so "is the version small?" is not
    /// a safe test — one real client's English file starts with version 0 and was
    /// mistaken for a wrapped file. Parsing and then checking the result is
    /// unambiguous.
    /// </remarks>
    public static ClilocTable Parse(ReadOnlySpan<byte> raw)
    {
        if (raw.Length < HeaderSize)
        {
            return Empty;
        }

        if (TryParsePlain(raw, out ClilocTable direct))
        {
            return direct;
        }

        byte[] decoded = BwtDecoder.Decompress(raw);

        return decoded.Length >= HeaderSize && TryParsePlain(decoded, out ClilocTable unwrapped)
            ? unwrapped
            : Empty;
    }

    /// <summary>
    /// Parses the record stream and reports whether the result actually looks
    /// like a cliloc file rather than misread noise.
    /// </summary>
    private static bool TryParsePlain(ReadOnlySpan<byte> data, out ClilocTable table)
    {
        table = Empty;

        Dictionary<int, ClilocEntry> entries = Read(data, out int consumed);

        // Noise tends to parse into a few entries and then desynchronise well
        // before the end. A genuine file consumes essentially all of itself.
        if (entries.Count == 0 || consumed < data.Length - MaxTrailingSlack)
        {
            return false;
        }

        table = new ClilocTable(entries);

        return true;
    }

    private static Dictionary<int, ClilocEntry> Read(ReadOnlySpan<byte> data, out int consumed)
    {
        Dictionary<int, ClilocEntry> entries = [];

        int cursor = HeaderSize;

        while (cursor + EntryHeaderSize <= data.Length)
        {
            int id = BinaryPrimitives.ReadInt32LittleEndian(data[cursor..]);
            byte flag = data[cursor + 4];
            int length = BinaryPrimitives.ReadUInt16LittleEndian(data[(cursor + 5)..]);

            if (cursor + EntryHeaderSize + length > data.Length)
            {
                // Truncated tail: keep what parsed rather than losing the file.
                break;
            }

            cursor += EntryHeaderSize;

            // Cliloc text is UTF-8. Worth stating, because it looks like a classic
            // ANSI-versus-UTF-8 bug and is not one.
            string text = Encoding.UTF8.GetString(data.Slice(cursor, length));

            cursor += length;

            entries[id] = new ClilocEntry(id, (ClilocEntryKind)flag, text);
        }

        consumed = cursor;

        return entries;
    }

    /// <summary>Looks up one string.</summary>
    public bool TryGet(int id, out ClilocEntry entry) => _entries.TryGetValue(id, out entry);

    /// <summary>Looks up one string, or returns <see langword="null"/>.</summary>
    public string? GetText(int id) => _entries.TryGetValue(id, out ClilocEntry entry) ? entry.Text : null;
}

/// <summary>One localised string.</summary>
/// <param name="Id">The id servers and gump scripts reference.</param>
/// <param name="Flag">Whether the string is original, patched, or newly added.</param>
/// <param name="Text">
/// The text, which may contain <c>~1_NAME~</c> placeholders the server fills in.
/// </param>
public readonly record struct ClilocEntry(int Id, ClilocEntryKind Flag, string Text)
{
    public override string ToString() => $"{Id}: {Text}";
}

/// <summary>Provenance of a cliloc entry.</summary>
public enum ClilocEntryKind : byte
{
    Original = 0,
    Patched = 1,
    Added = 2,
}
