using System.Buffers.Binary;
using System.Text;

namespace GumpStudio.TestSupport;

/// <summary>
/// Builds synthetic <c>cliloc.&lt;lang&gt;</c> files so localised strings can be
/// tested without shipping (non-redistributable) client files.
/// </summary>
/// <remarks>
/// <para>
/// Only the plain record stream is produced. A MegaCliloc-wrapped file would
/// need a Burrows-Wheeler <em>encoder</em>, which does not exist here, so that
/// path stays covered only by the real-client tests.
/// </para>
/// <para>
/// Entries are written in the order they were added, not sorted, so a test can
/// add them out of order and assert that reading sorts them.
/// </para>
/// </remarks>
public sealed class ClilocFixture
{
    /// <summary>u32 version, then a u16.</summary>
    private const int HeaderSize = 6;

    private readonly List<Entry> _entries = [];

    /// <summary>Appends one string.</summary>
    /// <param name="id">The id gump scripts reference.</param>
    /// <param name="text">The string, which may hold <c>~1_X~</c> placeholders.</param>
    /// <param name="flag">0 original, 1 patched, 2 added.</param>
    public ClilocFixture Add(int id, string text, byte flag = 0)
    {
        ArgumentNullException.ThrowIfNull(text);

        _entries.Add(new Entry(id, text, flag));

        return this;
    }

    /// <summary>
    /// The file's bytes.
    /// </summary>
    /// <param name="version">
    /// The u32 the header opens with. Both 0 and 2 ship, and 0 is why the reader
    /// cannot detect the wrapper by sniffing this.
    /// </param>
    /// <param name="unused">The u16 that follows the version.</param>
    /// <param name="trailingSlack">
    /// Junk bytes appended after the last record. Filled with <c>0xFF</c>, which
    /// reads as a length larger than the bytes that remain and so stops the walk
    /// exactly where the junk starts — which is what makes this a precise test
    /// of how much unparsed tail the reader tolerates.
    /// </param>
    /// <param name="truncateBy">
    /// Cuts this many bytes off the end, producing the half-written file a
    /// failed patch leaves behind.
    /// </param>
    public byte[] ToBytes(
        uint version = 2,
        ushort unused = 0,
        int trailingSlack = 0,
        int truncateBy = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(trailingSlack);
        ArgumentOutOfRangeException.ThrowIfNegative(truncateBy);

        List<byte> bytes = new(HeaderSize + (_entries.Count * 16));

        Span<byte> header = stackalloc byte[HeaderSize];

        BinaryPrimitives.WriteUInt32LittleEndian(header, version);
        BinaryPrimitives.WriteUInt16LittleEndian(header[4..], unused);

        bytes.AddRange(header);

        // One buffer for every record: a stackalloc inside the loop would grow
        // the frame per iteration.
        Span<byte> record = stackalloc byte[7];

        foreach (Entry entry in _entries)
        {
            // The length is a count of UTF-8 bytes, not of characters. Writing
            // text.Length instead desynchronises the whole file the moment a
            // string stops being ASCII.
            byte[] text = Encoding.UTF8.GetBytes(entry.Text);

            BinaryPrimitives.WriteInt32LittleEndian(record, entry.Id);
            record[4] = entry.Flag;
            BinaryPrimitives.WriteUInt16LittleEndian(record[5..], (ushort)text.Length);

            bytes.AddRange(record);
            bytes.AddRange(text);
        }

        for (int i = 0; i < trailingSlack; i++)
        {
            bytes.Add(0xFF);
        }

        byte[] built = [.. bytes];

        return truncateBy == 0 ? built : built[..Math.Max(0, built.Length - truncateBy)];
    }

    /// <summary>Writes <c>cliloc.&lt;language&gt;</c> into a directory.</summary>
    public string Write(string directory, string language = "enu") =>
        WriteAs(directory, $"cliloc.{language}");

    /// <summary>Writes under an exact file name, for the odd-casing cases.</summary>
    public string WriteAs(string directory, string fileName)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(fileName);

        string path = Path.Combine(directory, fileName);

        File.WriteAllBytes(path, ToBytes());

        return path;
    }

    private readonly record struct Entry(int Id, string Text, byte Flag);
}
