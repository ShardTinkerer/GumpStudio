using GumpStudio.App;
using GumpStudio.TestSupport;

using Xunit;

namespace GumpStudio.App.Tests;

/// <summary>
/// Settings persistence, including the regression that made this project
/// necessary: a save that reset every field it did not set.
/// </summary>
public class AppSettingsTests
{
    private static string PathIn(TempDirectory directory) =>
        Path.Combine(directory.Path, "settings.json");

    [Fact]
    public void LoadReturnsDefaultsWhenNoFileExists()
    {
        using TempDirectory directory = new();

        AppSettings settings = AppSettings.Load(PathIn(directory));

        Assert.Null(settings.ClientPath);
        Assert.Equal(5, settings.GridWidth);
        Assert.Equal(5, settings.GridHeight);
        Assert.True(settings.ArtBrowserGallery);
        Assert.Equal(144, settings.ArtBrowserTileSize);
        Assert.Empty(settings.ExportDialects);
    }

    [Fact]
    public void LoadRemembersWhereItReadFromSoSaveGoesBackToTheSameFile()
    {
        using TempDirectory directory = new();
        string path = PathIn(directory);

        AppSettings settings = AppSettings.Load(path);

        Assert.Equal(path, settings.Location);

        settings.GridWidth = 12;
        settings.Save();

        Assert.True(File.Exists(path));
        Assert.Equal(12, AppSettings.Load(path).GridWidth);
    }

    [Fact]
    public void EverySettingSurvivesARoundTrip()
    {
        using TempDirectory directory = new();
        string path = PathIn(directory);

        AppSettings written = AppSettings.Load(path);

        written.ClientPath = @"C:\Games\UO";
        written.GridWidth = 8;
        written.GridHeight = 16;
        written.GridVisible = true;
        written.GridSnap = true;
        written.ArtBrowserGallery = false;
        written.ArtBrowserTileSize = 240;
        written.SetExportDialect("pol", "layout-strings");
        written.Save();

        AppSettings read = AppSettings.Load(path);

        Assert.Equal(@"C:\Games\UO", read.ClientPath);
        Assert.Equal(8, read.GridWidth);
        Assert.Equal(16, read.GridHeight);
        Assert.True(read.GridVisible);
        Assert.True(read.GridSnap);
        Assert.False(read.ArtBrowserGallery);
        Assert.Equal(240, read.ArtBrowserTileSize);
        Assert.Equal("layout-strings", read.ExportDialectFor("pol"));
    }

    /// <summary>
    /// The defect this guards: choosing a client folder wrote
    /// <c>new AppSettings { ClientPath = path }</c> through a whole-file
    /// overwrite, so the grid, art-browser and export preferences all reverted
    /// to their defaults.
    /// </summary>
    [Fact]
    public void SettingTheClientPathKeepsEveryOtherPreference()
    {
        using TempDirectory directory = new();
        string path = PathIn(directory);

        AppSettings first = AppSettings.Load(path);

        first.GridWidth = 20;
        first.GridVisible = true;
        first.GridSnap = true;
        first.ArtBrowserGallery = false;
        first.ArtBrowserTileSize = 96;
        first.SetExportDialect("sphere", "099");
        first.Save();

        // What the editor now does when a client folder is chosen.
        AppSettings session = AppSettings.Load(path);
        session.ClientPath = @"C:\Games\UO";
        session.Save();

        AppSettings reopened = AppSettings.Load(path);

        Assert.Equal(@"C:\Games\UO", reopened.ClientPath);
        Assert.Equal(20, reopened.GridWidth);
        Assert.True(reopened.GridVisible);
        Assert.True(reopened.GridSnap);
        Assert.False(reopened.ArtBrowserGallery);
        Assert.Equal(96, reopened.ArtBrowserTileSize);
        Assert.Equal("099", reopened.ExportDialectFor("sphere"));
    }

    [Fact]
    public void UnreadableSettingsFallBackToDefaultsRatherThanThrowing()
    {
        using TempDirectory directory = new();
        string path = PathIn(directory);

        File.WriteAllText(path, "{ this is not json");

        AppSettings settings = AppSettings.Load(path);

        Assert.Equal(5, settings.GridWidth);
        Assert.Equal(path, settings.Location);
    }

    [Fact]
    public void SavingCreatesTheDirectoryItNeeds()
    {
        using TempDirectory directory = new();
        string path = Path.Combine(directory.Path, "nested", "deeper", "settings.json");

        AppSettings settings = AppSettings.Load(path);
        settings.GridHeight = 7;
        settings.Save();

        Assert.Equal(7, AppSettings.Load(path).GridHeight);
    }

    /// <summary>
    /// The remembered cliloc language survives a round trip.
    /// </summary>
    /// <remarks>
    /// Null by default, meaning "whatever the client offers first" — a stored
    /// <c>enu</c> would otherwise be indistinguishable from a deliberate choice
    /// on a client that ships no English file.
    /// </remarks>
    [Fact]
    public void TheClilocLanguageDefaultsToNoneAndSurvivesARoundTrip()
    {
        using TempDirectory directory = new();
        string path = PathIn(directory);

        AppSettings fresh = AppSettings.Load(path);

        Assert.Null(fresh.ClilocLanguage);

        fresh.ClilocLanguage = "deu";
        fresh.Save();

        Assert.Equal("deu", AppSettings.Load(path).ClilocLanguage);
    }

    /// <summary>A settings file written before this setting existed still loads.</summary>
    [Fact]
    public void ASettingsFileFromBeforeTheLanguageStillLoads()
    {
        using TempDirectory directory = new();
        string path = PathIn(directory);

        File.WriteAllText(path, "{ \"GridWidth\": 8 }");

        AppSettings settings = AppSettings.Load(path);

        Assert.Equal(8, settings.GridWidth);
        Assert.Null(settings.ClilocLanguage);
    }

    [Fact]
    public void TheSessionSharesOneSettingsInstance()
    {
        using TempDirectory directory = new();

        AppSettings settings = AppSettings.Load(PathIn(directory));

        using EditorSession session = new(settings);

        Assert.Same(settings, session.Settings);
    }
}
