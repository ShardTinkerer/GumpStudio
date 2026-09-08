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

    /// <summary>
    /// Largest mean record size a plain record stream may have.
    /// </summary>
    /// <remarks>
    /// Cliloc strings are short pieces of interface text: a real file averages
    /// well under 150 bytes per record, including the seven-byte header. The
    /// wrapped files that fool the walk average thousands — a two-megabyte
    /// <c>Cliloc.cht</c> reads as 345 records of about 5.8 KB each — because a
    /// random u16 length is on average enormous. The gap between the two is more
    /// than an order of magnitude, so this separates them without decoding a
    /// single string.
    /// </remarks>
    private const int MaxMeanRecordSize = 512;

    private readonly Dictionary<int, ClilocEntry> _entries;

    private readonly Func<int, string?> _lookup;

    private IReadOnlyList<ClilocEntry>? _ordered;

    private ClilocTable(Dictionary<int, ClilocEntry> entries)
    {
        _entries = entries;
        _lookup = GetText;
    }

    /// <summary>An empty table, used when the client ships no readable cliloc file.</summary>
    public static ClilocTable Empty { get; } = new([]);

    public int Count => _entries.Count;

    /// <summary>
    /// All entries, ordered by id.
    /// </summary>
    /// <remarks>
    /// Sorted once and kept, rather than ordered per enumeration. A browser
    /// binds this and re-reads it on every keystroke, and sorting 124,000
    /// entries each time is felt. The table is immutable, so the snapshot cannot
    /// go stale; a benign race just sorts twice and keeps one result.
    /// </remarks>
    public IReadOnlyList<ClilocEntry> Entries =>
        _ordered ??= [.. _entries.Values.OrderBy(e => e.Id)];

    /// <summary>Loads and, if necessary, unwraps a cliloc file.</summary>
    public static ClilocTable Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return Parse(File.ReadAllBytes(path));
    }

    /// <summary>Parses cliloc bytes, unwrapping the MegaCliloc codec when present.</summary>
    /// <remarks>
    /// <para>
    /// Detection is by trial rather than by sniffing the header. Shipped files
    /// carry at least version 0 and version 2, so "is the version small?" is not
    /// a safe test — one real client's English file starts with version 0 and was
    /// mistaken for a wrapped file.
    /// </para>
    /// <para>
    /// The trial can be fooled, though, which is why the result is judged as
    /// well as the walk. A wrapped <c>Cliloc.cht</c> in the test matrix — two
    /// megabytes of it — happens to walk as a valid record stream and lands
    /// within the tolerated tail, yielding 345 entries of mostly replacement
    /// characters. Real cliloc text is UTF-8, so a table that decodes to
    /// nonsense is offered the wrapper's interpretation instead. A file that
    /// already read as text is unaffected, and neither reading can turn a
    /// readable file into an empty one.
    /// </para>
    /// </remarks>
    public static ClilocTable Parse(ReadOnlySpan<byte> raw)
    {
        if (raw.Length < HeaderSize)
        {
            return Empty;
        }

        if (TryParsePlain(raw, out ClilocTable direct) && direct.LooksLikeText())
        {
            return direct;
        }

        byte[] decoded = BwtDecoder.Decompress(raw);

        if (decoded.Length >= HeaderSize
            && TryParsePlain(decoded, out ClilocTable unwrapped)
            && unwrapped.LooksLikeText())
        {
            return unwrapped;
        }

        // Neither reading is text. Report nothing rather than nonsense: a
        // caller can render "#1044017" for a string it does not have, but it
        // cannot tell that a string it was handed is garbage. One real
        // Cliloc.deu decompresses to this, so it is a reachable state rather
        // than a theoretical one.
        return Empty;
    }

    /// <summary>
    /// Whether the parsed strings actually read as text.
    /// </summary>
    /// <remarks>
    /// Misread bytes run through a UTF-8 decoder produce U+FFFD in droves, so a
    /// sample is enough to tell a real string table from a coincidence. Sampled
    /// rather than exhaustive because this decides between two readings of every
    /// cliloc file that is opened.
    /// </remarks>
    private bool LooksLikeText()
    {
        // A correctly decoded file has essentially none of these, so the bar is
        // low rather than generous. It is not zero because a shard can rewrite
        // these files by hand and get one string wrong.
        const int sampleSize = 256;
        const int mangledLimit = sampleSize / 50;

        int sampled = 0;
        int mangled = 0;

        foreach (ClilocEntry entry in _entries.Values)
        {
            if (entry.Text.Contains('�'))
            {
                mangled++;
            }

            if (++sampled == sampleSize)
            {
                break;
            }
        }

        return sampled > 0 && mangled * sampleSize <= mangledLimit * sampled;
    }

    /// <summary>
    /// Parses the record stream and reports whether the result actually looks
    /// like a cliloc file rather than misread noise.
    /// </summary>
    private static bool TryParsePlain(ReadOnlySpan<byte> data, out ClilocTable table)
    {
        table = Empty;

        if (!LooksLikeRecordStream(data))
        {
            return false;
        }

        table = new ClilocTable(Read(data));

        return true;
    }

    /// <summary>
    /// Walks the record headers, deciding whether this is a record stream
    /// without decoding a single string.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Noise tends to parse into a few entries and then desynchronise well
    /// before the end. A genuine file consumes essentially all of itself.
    /// </para>
    /// <para>
    /// Separate from <see cref="Read"/> because a wrapped file is tried as a
    /// plain one first, and building the dictionary to answer the question meant
    /// allocating — and UTF-8 decoding — megabytes of garbage strings before
    /// throwing them away. Switching cliloc language pays this trial parse
    /// again, so it is a recurring cost rather than a one-off at startup.
    /// </para>
    /// <para>
    /// The verdict is identical to building first: the cursor advances by the
    /// same arithmetic in both loops, and every record writes a key, so
    /// "any record" and "any entry" agree even where duplicate ids make the
    /// dictionary smaller than the record count.
    /// </para>
    /// </remarks>
    private static bool LooksLikeRecordStream(ReadOnlySpan<byte> data)
    {
        int cursor = HeaderSize;
        int records = 0;

        while (cursor + EntryHeaderSize <= data.Length)
        {
            int length = BinaryPrimitives.ReadUInt16LittleEndian(data[(cursor + 5)..]);

            if (cursor + EntryHeaderSize + length > data.Length)
            {
                break;
            }

            cursor += EntryHeaderSize + length;
            records++;
        }

        return records > 0
            && cursor >= data.Length - MaxTrailingSlack
            && cursor / records <= MaxMeanRecordSize;
    }

    private static Dictionary<int, ClilocEntry> Read(ReadOnlySpan<byte> data)
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

        return entries;
    }

    /// <summary>Looks up one string.</summary>
    public bool TryGet(int id, out ClilocEntry entry) => _entries.TryGetValue(id, out entry);

    /// <summary>Looks up one string, or returns <see langword="null"/>.</summary>
    public string? GetText(int id) => _entries.TryGetValue(id, out ClilocEntry entry) ? entry.Text : null;

    /// <summary>
    /// The text a cliloc reference will show, arguments substituted.
    /// </summary>
    /// <remarks>
    /// Saves every caller holding a table from building the lookup delegate
    /// itself. The delegate is cached per table, so this allocates nothing.
    /// </remarks>
    public string Format(int id, string? arguments = null) =>
        ClilocFormatter.Format(id, arguments, _lookup);
}

/// <summary>One localised string.</summary>
/// <param name="Id">The id servers and gump scripts reference.</param>
/// <param name="Flag">Whether the string is original, patched, or newly added.</param>
/// <param name="RawText">
/// The text, or <see langword="null"/> for a defaulted entry. Read
/// <see cref="Text"/> instead.
/// </param>
/// <remarks>
/// <see cref="ClilocTable.TryGet"/> writes <c>default</c> on a miss, so a
/// positional non-nullable <c>Text</c> would hand callers a null string whenever
/// a lookup failed. The computed property keeps that from being possible.
/// </remarks>
public readonly record struct ClilocEntry(int Id, ClilocEntryKind Flag, string? RawText)
{
    /// <summary>
    /// The text, which may contain <c>~1_NAME~</c> placeholders the server fills
    /// in. Empty when the entry is absent, never null.
    /// </summary>
    public string Text => RawText ?? string.Empty;

    public override string ToString() => $"{Id}: {Text}";
}

/// <summary>Provenance of a cliloc entry.</summary>
public enum ClilocEntryKind : byte
{
    Original = 0,
    Patched = 1,
    Added = 2,
}
