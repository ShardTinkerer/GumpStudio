using GumpStudio.Uo.Data;
using GumpStudio.Uo.Files;
using GumpStudio.Uo.Fonts;
using GumpStudio.Uo.Graphics;
using GumpStudio.Uo.Primitives;

namespace GumpStudio.Uo;

/// <summary>
/// Everything loaded from one Ultima Online installation.
/// </summary>
/// <remarks>
/// <para>
/// This is an ordinary disposable object, not a set of static classes. The old
/// SDK initialised every loader in a static constructor keyed off a global
/// directory list, which is why changing the data path made the application tell
/// the user to restart. Here, pointing at a different client is just a matter of
/// disposing one context and opening another.
/// </para>
/// <para>
/// Containers are chosen per file: a <c>.uop</c> package is preferred when
/// present and the legacy <c>.mul</c> pair is the fallback, because modern
/// clients ship no <c>gumpart.mul</c> at all while older ones ship no UOP.
/// </para>
/// </remarks>
public sealed class UoDataContext : IDisposable
{
    private const string GumpUopPattern = "build/gumpartlegacymul/{0:D8}.tga";
    private const string ArtUopPattern = "build/artlegacymul/{0:D8}.tga";

    /// <summary>Highest addressable gump id.</summary>
    private const int MaxGumps = 0x10000;

    /// <summary>Land art occupies 0..0x3FFF, static art 0x4000 upward.</summary>
    private const int MaxArt = 0x14000;

    /// <summary>The cliloc file preferred when the client ships several.</summary>
    private const string DefaultClilocLanguage = "enu";

    private readonly IUoFileProvider? _gumps;
    private readonly IUoFileProvider? _art;
    private readonly VerdataPatchSet _verdata;

    private readonly Lazy<AsciiFonts> _asciiFonts;
    private readonly Lazy<UnicodeFonts> _unicodeFonts;

    /// <summary>Cliloc file per language code, keyed case-insensitively.</summary>
    private readonly IReadOnlyDictionary<string, string> _clilocPaths;

    /// <summary>
    /// The chosen language and its deferred table, swapped together.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One object holding both, so the language and the strings can never
    /// disagree: a reader takes a single reference and sees a matched pair.
    /// </para>
    /// <para>
    /// <c>volatile</c> rather than a lock, because the read path is hot — a
    /// localised element resolves text on every repaint, and background art
    /// decodes resolve it off the UI thread — while a write happens when someone
    /// picks a menu item. The keyword is what guarantees a reader cannot observe
    /// the reference before the <see cref="Lazy{T}"/> it points at is built;
    /// on a weakly ordered processor that is not otherwise assured.
    /// <see cref="Lazy{T}"/> keeps its default <c>ExecutionAndPublication</c>
    /// mode, so concurrent first readers block on one parse rather than starting
    /// two.
    /// </para>
    /// <para>
    /// Only one thread ever writes. Switching is a user action from the UI
    /// thread, so no mutual exclusion between two switchers is provided — if
    /// that ever stops being true, this needs a lock on the write side.
    /// </para>
    /// </remarks>
    private volatile ClilocSelection _clilocs;

    /// <summary>Shared by every installation that ships no cliloc file.</summary>
    private static readonly Lazy<ClilocTable> NoClilocs = new(() => ClilocTable.Empty);

    private UoDataContext(
        string clientPath,
        IUoFileProvider? gumps,
        IUoFileProvider? art,
        VerdataPatchSet verdata,
        HueTable hues,
        TileDataTable tileData,
        IReadOnlyDictionary<string, string> clilocPaths,
        string? clilocLanguage,
        Lazy<AsciiFonts> asciiFonts,
        Lazy<UnicodeFonts> unicodeFonts)
    {
        ClientPath = clientPath;
        _clilocPaths = clilocPaths;
        ClilocLanguages = OrderLanguages(clilocPaths.Keys);
        _clilocs = Select(clilocPaths, ClilocLanguages, clilocLanguage);
        _gumps = gumps;
        _art = art;
        _verdata = verdata;
        Hues = hues;
        TileData = tileData;
        _asciiFonts = asciiFonts;
        _unicodeFonts = unicodeFonts;
    }

    /// <summary>The installation this context reads from.</summary>
    public string ClientPath { get; }

    public HueTable Hues { get; }

    public TileDataTable TileData { get; }

    /// <summary>
    /// Localised client strings, read on first use.
    /// </summary>
    /// <remarks>
    /// Deferred along with the two font tables because none of the three is
    /// needed to draw a gump's first frame, and together they were the bulk of
    /// opening a client: the cliloc table alone is around 124,000 strings and is
    /// parsed twice on a modern client, since a plain parse is tried before
    /// decompressing.
    ///
    /// The paths are still resolved eagerly in <see cref="Open"/>, so which
    /// files an installation is missing is decided at open time exactly as
    /// before — only the reading moved.
    ///
    /// Thread safety is load-bearing rather than incidental:
    /// <see cref="Lazy{T}"/> defaults to <c>ExecutionAndPublication</c>, and text
    /// is rendered from background art decodes as well as from the UI thread.
    /// </remarks>
    public ClilocTable Clilocs => _clilocs.Table.Value;

    /// <summary>
    /// The cliloc file extension currently read, such as <c>enu</c>, or null
    /// when the installation ships none.
    /// </summary>
    public string? ClilocLanguage => _clilocs.Language;

    /// <summary>
    /// The cliloc files this installation ships, as their file extensions.
    /// </summary>
    /// <remarks>
    /// Extensions, deliberately, rather than language names. Shard clients
    /// rewrite these files freely and the extension is not a reliable claim
    /// about the contents — one client in the test matrix ships Italian text
    /// under <c>.enu</c> — so presenting <c>enu</c> and letting the author read
    /// the strings beats asserting "English" and being wrong.
    /// </remarks>
    public IReadOnlyList<string> ClilocLanguages { get; }

    /// <summary>
    /// True when the installation ships a cliloc file.
    /// </summary>
    /// <remarks>
    /// Answered from the directory listing, never by reading the file: asking
    /// whether strings exist must not be what parses 124,000 of them.
    /// </remarks>
    public bool HasClilocs => _clilocs.Language is not null;

    /// <summary>
    /// Switches to another of the installation's cliloc files.
    /// </summary>
    /// <remarks>
    /// The table is replaced rather than the context reopened, because a
    /// language is a display choice and reopening would throw away every decoded
    /// gump alongside it. Loading stays deferred: switching costs nothing until
    /// something asks for a string.
    /// </remarks>
    /// <returns>
    /// False, with nothing changed, when the installation has no cliloc file
    /// with that extension. A refusal rather than a throw because the usual
    /// caller is a remembered setting: an author who last used <c>deu</c> and
    /// then opens an English-only client is doing nothing wrong.
    /// </returns>
    public bool UseClilocLanguage(string language)
    {
        ArgumentNullException.ThrowIfNull(language);

        if (!_clilocPaths.TryGetValue(language, out string? path))
        {
            return false;
        }

        string normalised = language.ToLowerInvariant();

        // Re-selecting what is loaded must not discard a parsed table.
        if (string.Equals(_clilocs.Language, normalised, StringComparison.Ordinal))
        {
            return true;
        }

        _clilocs = new ClilocSelection(
            normalised, new Lazy<ClilocTable>(() => ClilocTable.Load(path)));

        return true;
    }

    /// <summary>
    /// Reads the cliloc table now, on the calling thread.
    /// </summary>
    /// <remarks>
    /// Exists so the application can pay the parse on a thread pool thread just
    /// after opening a client, instead of the first hover over a cliloc id
    /// paying it on the UI thread. Scheduling is the caller's business: a
    /// background task started down here would have no cancellation and could
    /// outlive <see cref="Dispose"/>.
    /// </remarks>
    public void PreloadClilocs() => _ = Clilocs;

    public AsciiFonts AsciiFonts => _asciiFonts.Value;

    public UnicodeFonts UnicodeFonts => _unicodeFonts.Value;

    /// <summary>True when gump art was found, in either container format.</summary>
    public bool HasGumps => _gumps is not null;

    /// <summary>True when item art was found, in either container format.</summary>
    public bool HasArt => _art is not null;

    /// <summary>Number of addressable gump slots.</summary>
    public int GumpCount => _gumps?.Count ?? 0;

    /// <summary>
    /// Checks an installation before opening it.
    /// </summary>
    /// <returns>
    /// The files that are required but missing. Empty means the directory is
    /// usable.
    /// </returns>
    /// <remarks>
    /// The old application checked only for <c>art.mul</c>, so a partial data
    /// folder crashed later inside a static constructor with no clue as to why.
    /// </remarks>
    public static IReadOnlyList<string> Validate(string clientPath)
    {
        ArgumentNullException.ThrowIfNull(clientPath);

        List<string> missing = [];

        if (!Directory.Exists(clientPath))
        {
            return [clientPath];
        }

        if (!HasGumpContainer(clientPath))
        {
            missing.Add("gumpartLegacyMUL.uop or gumpart.mul + gumpidx.mul");
        }

        if (!HasArtContainer(clientPath))
        {
            missing.Add("artLegacyMUL.uop or art.mul + artidx.mul");
        }

        if (Find(clientPath, "hues.mul") is null)
        {
            missing.Add("hues.mul");
        }

        return missing;
    }

    /// <summary>Opens an installation.</summary>
    /// <param name="clientPath">The installation directory.</param>
    /// <param name="clilocLanguage">
    /// Which cliloc file to read, as its extension. Null or an extension this
    /// installation does not ship falls back to <c>enu</c>, then to whatever it
    /// does ship — an author's remembered choice must not stop a different
    /// client from opening.
    /// </param>
    /// <exception cref="DirectoryNotFoundException">The path does not exist.</exception>
    public static UoDataContext Open(string clientPath, string? clilocLanguage = null)
    {
        ArgumentNullException.ThrowIfNull(clientPath);

        if (!Directory.Exists(clientPath))
        {
            throw new DirectoryNotFoundException($"'{clientPath}' is not an existing directory.");
        }

        VerdataPatchSet verdata = VerdataPatchSet.Load(Find(clientPath, "verdata.mul"));

        IUoFileProvider? gumps = null;
        IUoFileProvider? art = null;

        try
        {
            gumps = OpenGumps(clientPath, verdata);
            art = OpenArt(clientPath, verdata);

            HueTable hues = Find(clientPath, "hues.mul") is { } huePath
                ? HueTable.Load(huePath)
                : HueTable.Empty;

            TileDataTable tileData = Find(clientPath, "tiledata.mul") is { } tilePath
                ? TileDataTable.Load(tilePath)
                : TileDataTable.Empty;

            // Paths resolved now, contents read on first use. One directory
            // pass builds the whole code-to-file map, which subsumes what a
            // per-name Find would do and costs less than the single lookup it
            // replaces.
            Dictionary<string, string> clilocPaths = FindClilocs(clientPath);

            string? fontPath = Find(clientPath, "fonts.mul");

            Lazy<AsciiFonts> asciiFonts = new(() =>
                fontPath is not null ? AsciiFonts.Load(fontPath) : AsciiFonts.Empty);

            Lazy<UnicodeFonts> unicodeFonts = new(() => UnicodeFonts.Load(clientPath));

            return new UoDataContext(
                clientPath, gumps, art, verdata, hues, tileData,
                clilocPaths, clilocLanguage, asciiFonts, unicodeFonts);
        }
        catch
        {
            gumps?.Dispose();
            art?.Dispose();
            verdata.Dispose();

            throw;
        }
    }

    private static IUoFileProvider? OpenGumps(string clientPath, VerdataPatchSet verdata)
    {
        if (Find(clientPath, "gumpartLegacyMUL.uop") is { } uop)
        {
            return UopFileProvider.Open(uop, GumpUopPattern, MaxGumps, hasExtraDimensions: true);
        }

        string? index = Find(clientPath, "gumpidx.mul");
        string? data = Find(clientPath, "gumpart.mul");

        return index is not null && data is not null
            ? MulFileProvider.Open(index, data, UoFileKind.Gumps, verdata)
            : null;
    }

    private static IUoFileProvider? OpenArt(string clientPath, VerdataPatchSet verdata)
    {
        if (Find(clientPath, "artLegacyMUL.uop") is { } uop)
        {
            return UopFileProvider.Open(uop, ArtUopPattern, MaxArt);
        }

        string? index = Find(clientPath, "artidx.mul");
        string? data = Find(clientPath, "art.mul");

        return index is not null && data is not null
            ? MulFileProvider.Open(index, data, UoFileKind.Art, verdata)
            : null;
    }

    private static bool HasGumpContainer(string clientPath) =>
        Find(clientPath, "gumpartLegacyMUL.uop") is not null
        || (Find(clientPath, "gumpidx.mul") is not null && Find(clientPath, "gumpart.mul") is not null);

    private static bool HasArtContainer(string clientPath) =>
        Find(clientPath, "artLegacyMUL.uop") is not null
        || (Find(clientPath, "artidx.mul") is not null && Find(clientPath, "art.mul") is not null);

    /// <summary>
    /// Finds a client file case-insensitively.
    /// </summary>
    /// <remarks>
    /// Clients ship inconsistent casing (<c>Gumpart.mul</c> vs <c>gumpart.mul</c>),
    /// which does not matter on Windows but breaks the Linux and macOS builds.
    /// </remarks>
    /// <summary>
    /// Every <c>cliloc.&lt;code&gt;</c> the installation ships, keyed by code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The stem has to be exactly <c>cliloc</c>. That is what excludes the
    /// numbered <c>clilocNN.enu</c> chunks pre-2002 clients ship, which
    /// <see cref="ClilocTable"/> deliberately cannot read, and equally a
    /// <c>cliloc.enu.bak</c> someone left beside the real one.
    /// </para>
    /// <para>
    /// Nothing is validated here beyond the name. Confirming a candidate really
    /// holds strings would mean parsing every one of them at open time, which is
    /// the cost the deferred load exists to avoid; a stray <c>cliloc.old</c> is
    /// listed and, if chosen, reads as an empty table.
    /// </para>
    /// <para>
    /// First name wins, so a case-sensitive filesystem holding both
    /// <c>cliloc.enu</c> and <c>CLILOC.ENU</c> yields one entry rather than
    /// throwing.
    /// </para>
    /// </remarks>
    private static Dictionary<string, string> FindClilocs(string directory)
    {
        Dictionary<string, string> found = new(StringComparer.OrdinalIgnoreCase);

        foreach (string candidate in Directory.EnumerateFiles(directory))
        {
            if (!string.Equals(
                    Path.GetFileNameWithoutExtension(candidate),
                    "cliloc",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string extension = Path.GetExtension(candidate);

            // ".enu" -> "enu". A bare "cliloc" with no extension is not one.
            if (extension.Length > 1)
            {
                found.TryAdd(extension[1..].ToLowerInvariant(), candidate);
            }
        }

        return found;
    }

    /// <summary>The discovered codes, <c>enu</c> first then alphabetical.</summary>
    /// <remarks>
    /// English first because practically every client ships it and it is what
    /// most gump scripts were written against; the rest ordinally, so the list
    /// does not depend on directory order or on the current culture.
    /// </remarks>
    private static IReadOnlyList<string> OrderLanguages(IEnumerable<string> codes) =>
        [.. codes
            .Select(static code => code.ToLowerInvariant())
            .OrderByDescending(static code => code == DefaultClilocLanguage)
            .ThenBy(static code => code, StringComparer.Ordinal)];

    /// <summary>
    /// Picks the cliloc file to read, honouring a remembered choice.
    /// </summary>
    /// <remarks>
    /// A remembered code the installation does not have falls back to the
    /// preferred one rather than to nothing: an author's last choice must not
    /// stop a different client's strings from appearing. The path is captured
    /// here so the deferred load closes over a string rather than the map.
    /// </remarks>
    private static ClilocSelection Select(
        IReadOnlyDictionary<string, string> paths,
        IReadOnlyList<string> ordered,
        string? requested)
    {
        string? code = requested is not null && paths.ContainsKey(requested)
            ? requested.ToLowerInvariant()
            : ordered.Count > 0 ? ordered[0] : null;

        if (code is null)
        {
            return new ClilocSelection(null, NoClilocs);
        }

        string path = paths[code];

        return new ClilocSelection(code, new Lazy<ClilocTable>(() => ClilocTable.Load(path)));
    }

    /// <summary>A cliloc language and the table read for it, as one value.</summary>
    private sealed record ClilocSelection(string? Language, Lazy<ClilocTable> Table);

    private static string? Find(string directory, string fileName)
    {
        string direct = Path.Combine(directory, fileName);

        if (File.Exists(direct))
        {
            return direct;
        }

        foreach (string candidate in Directory.EnumerateFiles(directory))
        {
            if (string.Equals(Path.GetFileName(candidate), fileName, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Reports a gump's dimensions without decoding its pixels.</summary>
    /// <remarks>The art browser relies on this to list thousands of entries quickly.</remarks>
    public bool TryGetGumpSize(int gumpId, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (_gumps is null)
        {
            return false;
        }

        UoFileEntry entry = _gumps.GetEntry(gumpId);

        if (!entry.Exists)
        {
            return false;
        }

        width = entry.ExtraWidth;
        height = entry.ExtraHeight;

        return width is > 0 and <= GumpDecoder.MaxDimension
            && height is > 0 and <= GumpDecoder.MaxDimension;
    }

    /// <summary>True when a gump id has art behind it.</summary>
    /// <remarks>
    /// Cheap: does not decode. Use <see cref="TryGetGumpSize"/> when the
    /// dimensions are actually needed.
    /// </remarks>
    public bool IsValidGump(int gumpId) => _gumps?.Exists(gumpId) == true;

    /// <summary>Every gump id that has art, in ascending order.</summary>
    public IEnumerable<int> EnumerateGumpIds()
    {
        if (_gumps is null)
        {
            yield break;
        }

        for (int id = 0; id < _gumps.Count; id++)
        {
            if (_gumps.Exists(id))
            {
                yield return id;
            }
        }
    }

    /// <summary>Every static item id that has art, in ascending order.</summary>
    public IEnumerable<int> EnumerateItemIds()
    {
        if (_art is null)
        {
            yield break;
        }

        int max = _art.Count - ArtDecoder.StaticArtOffset;

        for (int id = 0; id < max; id++)
        {
            if (_art.Exists(id + ArtDecoder.StaticArtOffset))
            {
                yield return id;
            }
        }
    }

    /// <summary>Decodes a gump, optionally recoloured.</summary>
    /// <param name="gumpId">The gump id.</param>
    /// <param name="hue">A one-based hue as stored on elements, or 0 for none.</param>
    /// <param name="partialHue">True to recolour only greyscale pixels.</param>
    public UoImage? GetGump(int gumpId, int hue = 0, bool partialHue = false)
    {
        if (_gumps is null || !TryGetGumpSize(gumpId, out int width, out int height))
        {
            return null;
        }

        Argb1555Image? image = GumpDecoder.Decode(_gumps.Read(gumpId).Span, width, height);

        return image is null ? null : Finish(image, hue, partialHue);
    }

    /// <summary>True when an item id has static art behind it.</summary>
    public bool IsValidStatic(int itemId) =>
        _art?.Exists(itemId + ArtDecoder.StaticArtOffset) == true;

    /// <summary>Decodes a static item tile, optionally recoloured.</summary>
    /// <param name="itemId">The item id, as gump scripts use it (not offset by 0x4000).</param>
    /// <param name="hue">A one-based hue as stored on elements, or 0 for none.</param>
    /// <param name="partialHue">True to recolour only greyscale pixels.</param>
    public UoImage? GetStatic(int itemId, int hue = 0, bool partialHue = false)
    {
        if (_art is null)
        {
            return null;
        }

        ReadOnlyMemory<byte> data = _art.Read(itemId + ArtDecoder.StaticArtOffset);

        if (data.IsEmpty)
        {
            return null;
        }

        Argb1555Image? image = ArtDecoder.DecodeStatic(data.Span);

        return image is null ? null : Finish(image, hue, partialHue);
    }

    /// <summary>Decodes a land tile.</summary>
    public UoImage? GetLand(int landId)
    {
        if (_art is null)
        {
            return null;
        }

        ReadOnlyMemory<byte> data = _art.Read(landId);

        if (data.IsEmpty)
        {
            return null;
        }

        Argb1555Image? image = ArtDecoder.DecodeLand(data.Span);

        return image?.ToUoImage();
    }

    private UoImage Finish(Argb1555Image image, int hue, bool partialHue)
    {
        if (Hues.Get(hue) is { } palette)
        {
            palette.ApplyTo(image.Pixels, partialHue);
        }

        return image.ToUoImage();
    }

    public void Dispose()
    {
        _gumps?.Dispose();
        _art?.Dispose();
        _verdata.Dispose();

        // Only if something asked for it. Reading the property would open up to
        // thirteen font files purely in order to release them again.
        if (_unicodeFonts.IsValueCreated)
        {
            _unicodeFonts.Value.Dispose();
        }
    }
}
