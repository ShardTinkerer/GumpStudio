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
