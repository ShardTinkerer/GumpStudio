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

    private readonly IUoFileProvider? _gumps;
    private readonly IUoFileProvider? _art;
    private readonly VerdataPatchSet _verdata;

    private UoDataContext(
        string clientPath,
        IUoFileProvider? gumps,
        IUoFileProvider? art,
        VerdataPatchSet verdata,
        HueTable hues,
        TileDataTable tileData,
        ClilocTable clilocs,
        AsciiFonts asciiFonts,
        UnicodeFonts unicodeFonts)
    {
        ClientPath = clientPath;
        _gumps = gumps;
        _art = art;
        _verdata = verdata;
        Hues = hues;
        TileData = tileData;
        Clilocs = clilocs;
        AsciiFonts = asciiFonts;
        UnicodeFonts = unicodeFonts;
    }

    /// <summary>The installation this context reads from.</summary>
    public string ClientPath { get; }

    public HueTable Hues { get; }

    public TileDataTable TileData { get; }

    public ClilocTable Clilocs { get; }

    public AsciiFonts AsciiFonts { get; }

    public UnicodeFonts UnicodeFonts { get; }

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
    /// <exception cref="DirectoryNotFoundException">The path does not exist.</exception>
    public static UoDataContext Open(string clientPath)
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

            ClilocTable clilocs = Find(clientPath, "cliloc.enu") is { } clilocPath
                ? ClilocTable.Load(clilocPath)
                : ClilocTable.Empty;

            AsciiFonts asciiFonts = Find(clientPath, "fonts.mul") is { } fontPath
                ? AsciiFonts.Load(fontPath)
                : AsciiFonts.Empty;

            UnicodeFonts unicodeFonts = UnicodeFonts.Load(clientPath);

            return new UoDataContext(
                clientPath, gumps, art, verdata, hues, tileData, clilocs, asciiFonts, unicodeFonts);
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
    public bool IsValidGump(int gumpId) => TryGetGumpSize(gumpId, out _, out _);

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
        _art is not null && _art.GetEntry(itemId + ArtDecoder.StaticArtOffset).Exists;

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
        UnicodeFonts.Dispose();
    }
}
