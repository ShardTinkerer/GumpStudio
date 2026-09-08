namespace GumpStudio.Uo.Graphics;

/// <summary>
/// The contents of <c>hues.mul</c>: colour-shift palettes, stored as 375 groups
/// of 8.
/// </summary>
public sealed class HueTable
{
    /// <summary>Group header (4 bytes) plus eight hue records.</summary>
    private const int GroupSize = sizeof(int) + (8 * Hue.RecordSize);

    private const int HuesPerGroup = 8;

    private readonly Hue[] _hues;

    private HueTable(Hue[] hues) => _hues = hues;

    /// <summary>Number of hues, normally 3000.</summary>
    public int Count => _hues.Length;

    /// <summary>All hues, in file order.</summary>
    public ReadOnlySpan<Hue> Hues => _hues;

    /// <summary>
    /// Loads <c>hues.mul</c>.
    /// </summary>
    /// <remarks>
    /// The count is taken from the file's own length rather than the historical
    /// 375-group cap, so a client that ships a larger table is read in full.
    /// </remarks>
    public static HueTable Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        byte[] raw = File.ReadAllBytes(path);
        int groups = raw.Length / GroupSize;

        Hue[] hues = new Hue[groups * HuesPerGroup];

        for (int group = 0; group < groups; group++)
        {
            // The 4-byte group header is a tag the client reads and discards.
            int offset = (group * GroupSize) + sizeof(int);

            for (int i = 0; i < HuesPerGroup; i++)
            {
                int index = (group * HuesPerGroup) + i;

                hues[index] = Hue.Read(index, raw.AsSpan(offset + (i * Hue.RecordSize), Hue.RecordSize));
            }
        }

        return new HueTable(hues);
    }

    /// <summary>An empty table, used when the client has no <c>hues.mul</c>.</summary>
    public static HueTable Empty { get; } = new([Hue.CreateEmpty(0)]);

    /// <summary>
    /// Looks up a hue by the value stored on an element.
    /// </summary>
    /// <param name="hue">
    /// A hue as elements and gump scripts store it: one-based, with the two high
    /// bits used as flags. Zero means "no hue".
    /// </param>
    /// <returns>The hue, or <see langword="null"/> when <paramref name="hue"/> is 0.</returns>
    /// <remarks>
    /// The one-based convention trips people up constantly: hue 1 in a script is
    /// <see cref="Hues"/>[0]. Masking off the flag bits matches the client.
    /// </remarks>
    public Hue? Get(int hue)
    {
        int index = (hue & 0x3FFF) - 1;

        if (index < 0)
        {
            return null;
        }

        return index < _hues.Length ? _hues[index] : _hues[0];
    }

    /// <summary>Looks up by raw zero-based index, clamping out-of-range values.</summary>
    public Hue GetByIndex(int index) =>
        (uint)index < (uint)_hues.Length ? _hues[index] : _hues[0];
}
