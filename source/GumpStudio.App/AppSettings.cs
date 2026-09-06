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
    /// <summary>The Ultima Online installation to read art from.</summary>
    public string? ClientPath { get; set; }

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

    /// <summary>Where the settings file lives.</summary>
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GumpStudio",
        "settings.json");

    public static AppSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize(File.ReadAllText(FilePath), SettingsContext.Default.AppSettings)
                    ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Unreadable settings are not worth failing startup over.
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

            File.WriteAllText(
                FilePath,
                JsonSerializer.Serialize(settings, SettingsContext.Default.AppSettings));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing a preference is preferable to losing the session.
        }
    }
}

/// <summary>Source-generated serialization, so settings work under trimming.</summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsContext : JsonSerializerContext;
