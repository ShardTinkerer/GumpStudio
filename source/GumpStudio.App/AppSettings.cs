using System.Text.Json;
using System.Text.Json.Serialization;

namespace GumpStudio.App;

/// <summary>
/// User settings, stored as JSON in the platform's application-data folder.
/// </summary>
/// <remarks>
/// Replaces the old <c>ApplicationSettingsBase</c>, which wrote a
/// version-stamped <c>user.config</c> under a hashed directory name that was
/// effectively impossible to find or edit by hand.
/// </remarks>
public sealed class AppSettings
{
    /// <summary>
    /// Where these settings were loaded from, and where <see cref="Save"/>
    /// writes them back.
    /// </summary>
    /// <remarks>
    /// Carried on the instance rather than read from <see cref="FilePath"/> at
    /// write time so that a test — or a second installation — can work against
    /// its own file without touching the real one.
    /// </remarks>
    [JsonIgnore]
    public string Location { get; set; } = FilePath;

    private WindowLayout _layout = new();
    private Dictionary<string, string> _exportDialects = [];

    /// <summary>The Ultima Online installation to read art from.</summary>
    public string? ClientPath { get; set; }

    /// <summary>
    /// Which cliloc language the editor reads, as a file extension code.
    /// </summary>
    /// <remarks>
    /// Null means whatever the client offers first, which is English wherever it
    /// is present. Remembered because it changes what every localised area on
    /// the canvas says, and someone building gumps for a German shard would
    /// otherwise re-pick it on every launch. A code the next client happens not
    /// to ship is ignored rather than honoured, so this can never be what stops
    /// an installation's strings from appearing.
    /// </remarks>
    public string? ClilocLanguage { get; set; }

    /// <summary>Design-grid spacing in gump pixels.</summary>
    public int GridWidth { get; set; } = 5;

    /// <summary>Design-grid spacing in gump pixels.</summary>
    public int GridHeight { get; set; } = 5;

    /// <summary>Whether the grid is drawn.</summary>
    public bool GridVisible { get; set; }

    /// <summary>Whether moving and resizing snap to the grid.</summary>
    public bool GridSnap { get; set; }

    /// <summary>Whether the art browsers open as a tile grid rather than a list.</summary>
    /// <remarks>
    /// A grid by default: picking art is a visual task, and a list of one
    /// thumbnail per row shows a fraction of what the same space can.
    /// </remarks>
    public bool ArtBrowserGallery { get; set; } = true;

    /// <summary>Side of an art-browser thumbnail, in pixels.</summary>
    /// <remarks>
    /// Applies to both views. Large art is scaled down to fit, so a bigger tile
    /// is the difference between recognising a gump and guessing at it.
    /// </remarks>
    public int ArtBrowserTileSize { get; set; } = 144;

    /// <summary>
    /// Width of the art browser's preview pane, in pixels.
    /// </summary>
    /// <remarks>
    /// Remembered like the tile size and the view mode. A gump wider than the
    /// pane is the case the pane needs widening for, so having to drag it again
    /// on every browse would defeat the point.
    /// </remarks>
    public int ArtBrowserPreviewWidth { get; set; } = DefaultPreviewWidth;

    /// <summary>The preview pane's width when nothing has been chosen.</summary>
    public const int DefaultPreviewWidth = 260;

    /// <summary>
    /// Narrowest and widest the preview pane may be.
    /// </summary>
    /// <remarks>
    /// A floor because a narrower pane shows nothing useful and reads as broken
    /// rather than collapsed; a ceiling so a width stored on a wide screen
    /// cannot leave the art list with no room on a smaller one.
    /// </remarks>
    public const int MinPreviewWidth = 160;

    public const int MaxPreviewWidth = 1200;

    /// <summary>The stored preview width, brought within the usable range.</summary>
    public int UsablePreviewWidth() =>
        Math.Clamp(ArtBrowserPreviewWidth, MinPreviewWidth, MaxPreviewWidth);

    /// <summary>
    /// Window size, position and panel arrangement.
    /// </summary>
    /// <remarks>
    /// The setter refuses null, and so does <see cref="ExportDialects"/>. A
    /// property initialiser is not enough: the JSON source generator assigns
    /// every member it knows about while constructing the object, so a settings
    /// file written before this property existed — every file already on disk —
    /// deserialises it as null and the initialiser never survives.
    /// </remarks>
    public WindowLayout Layout
    {
        get => _layout;
        set => _layout = value ?? new WindowLayout();
    }

    /// <summary>
    /// The dialect last chosen for each converter, keyed by converter id.
    /// </summary>
    /// <remarks>
    /// Per converter rather than one global setting: choosing POL's
    /// layout-string form says nothing about whether Sphere scripts should be
    /// 0.56 or 0.99.
    /// </remarks>
    public Dictionary<string, string> ExportDialects
    {
        get => _exportDialects;
        set => _exportDialects = value ?? [];
    }

    /// <summary>The remembered dialect for a converter, or null for its default.</summary>
    public string? ExportDialectFor(string converterId) =>
        ExportDialects.TryGetValue(converterId, out string? dialect) ? dialect : null;

    /// <summary>Remembers the dialect chosen for a converter.</summary>
    public void SetExportDialect(string converterId, string? dialect)
    {
        if (dialect is null)
        {
            ExportDialects.Remove(converterId);

            return;
        }

        ExportDialects[converterId] = dialect;
    }

    /// <summary>Where the settings file lives by default.</summary>
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GumpStudio",
        "settings.json");

    /// <summary>Reads the settings, or returns defaults when there are none.</summary>
    /// <param name="path">The file to read; defaults to <see cref="FilePath"/>.</param>
    /// <remarks>
    /// The editor loads once and keeps the instance — see
    /// <c>EditorSession.Settings</c>. Reloading before each write was how a
    /// half-populated instance came to overwrite the whole file.
    /// </remarks>
    public static AppSettings Load(string? path = null)
    {
        string location = path ?? FilePath;

        try
        {
            AppSettings settings = File.Exists(location)
                ? JsonSerializer.Deserialize(File.ReadAllText(location), SettingsContext.Default.AppSettings)
                    ?? new AppSettings()
                : new AppSettings();

            settings.Location = location;

            return settings;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Unreadable settings are not worth failing startup over.
            return new AppSettings { Location = location };
        }
    }

    /// <summary>Writes these settings back to <see cref="Location"/>.</summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Location)!);

            File.WriteAllText(
                Location,
                JsonSerializer.Serialize(this, SettingsContext.Default.AppSettings));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing a preference is preferable to losing the session.
        }
    }
}

/// <summary>
/// Where the window and its panels were left.
/// </summary>
/// <remarks>
/// A snapshot of what actually varies — the size of each pane, which panels
/// are hidden, and the window's own placement — rather than Dock's serialised
/// model. Dock's <c>IDockSerializer</c> is a contract only; its implementations
/// live in separate packages that reflect over the dock model, and this
/// application is published with NativeAOT and already suppresses Dock's trim
/// warnings. Reintroducing reflective serialisation over the same model would
/// widen a suppression the CI publish job exists to police.
///
/// The cost is that tearing a panel off into its own window, or re-tabbing one
/// beside another, is not remembered: those fall back to the layout declared in
/// <c>MainWindow.axaml</c>.
/// </remarks>
public sealed class WindowLayout
{
    /// <summary>Logical window size, or null to use the declared default.</summary>
    public double? Width { get; set; }

    public double? Height { get; set; }

    /// <summary>Window position, or null to let the platform place it.</summary>
    public int? X { get; set; }

    public int? Y { get; set; }

    /// <summary>Whether the window was maximised.</summary>
    public bool Maximized { get; set; }

    /// <summary>Each pane's share of its parent, by dockable id.</summary>
    public Dictionary<string, double> Proportions { get; init; } = [];

    /// <summary>Ids of the panels that were hidden.</summary>
    public List<string> HiddenPanels { get; init; } = [];

    /// <summary>
    /// Whether a stored pane proportion is worth applying.
    /// </summary>
    /// <remarks>
    /// Rejected rather than clamped. A value outside this range is not a
    /// preference anyone expressed — it is a corrupt or hand-edited file — and
    /// clamping it would silently accept a layout nobody chose. Either extreme
    /// also collapses a panel to nothing, which looks like the panel is gone.
    /// </remarks>
    public static bool IsUsableProportion(double proportion) => proportion is > 0.02 and < 0.98;

    /// <summary>Whether a stored window size is worth applying.</summary>
    public static bool IsUsableSize(double? width, double? height) =>
        width is > 200 and < 20000 && height is > 150 and < 20000;
}

/// <summary>Source-generated serialization, so settings work under trimming.</summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsContext : JsonSerializerContext;
