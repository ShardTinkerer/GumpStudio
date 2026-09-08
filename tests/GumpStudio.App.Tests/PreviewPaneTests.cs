using Avalonia.Controls;

using GumpStudio.App;
using GumpStudio.App.Controls;
using GumpStudio.TestSupport;
using GumpStudio.Uo;

using Xunit;

namespace GumpStudio.App.Tests;

/// <summary>
/// The art browser's resizable preview pane.
/// </summary>
/// <remarks>
/// The preview was docked at a fixed 260 pixels with the image drawn at native
/// size, so a gump wider than that was cropped and there was no way to give the
/// pane more room. Gump art runs to several hundred pixels across, which makes
/// the split between the list and the preview a per-piece-of-art decision rather
/// than something to hard-code.
/// </remarks>
[Collection("Headless")]
public class PreviewPaneTests
{
    private static string? Client =>
        Environment.GetEnvironmentVariable("GUMPSTUDIO_TEST_CLIENT_UOP");

    private static string PathIn(TempDirectory directory) =>
        Path.Combine(directory.Path, "settings.json");

    [Fact]
    public void TheWidthDefaultsAndSurvivesARoundTrip()
    {
        using TempDirectory directory = new();
        string path = PathIn(directory);

        AppSettings fresh = AppSettings.Load(path);

        Assert.Equal(AppSettings.DefaultPreviewWidth, fresh.ArtBrowserPreviewWidth);

        fresh.ArtBrowserPreviewWidth = 420;
        fresh.Save();

        Assert.Equal(420, AppSettings.Load(path).ArtBrowserPreviewWidth);
    }

    /// <summary>
    /// A settings file written before this existed has no such key, and the
    /// source generator's habit of assigning every member it knows about is
    /// what made an earlier property come back as null.
    /// </summary>
    [Fact]
    public void ASettingsFileFromBeforeThePaneStillGetsAWidth()
    {
        using TempDirectory directory = new();
        string path = PathIn(directory);

        File.WriteAllText(path, "{ \"GridWidth\": 8 }");

        AppSettings settings = AppSettings.Load(path);

        Assert.Equal(AppSettings.DefaultPreviewWidth, settings.ArtBrowserPreviewWidth);
        Assert.Equal(AppSettings.DefaultPreviewWidth, settings.UsablePreviewWidth());
    }

    [Theory]
    [InlineData(0, AppSettings.MinPreviewWidth)]
    [InlineData(-500, AppSettings.MinPreviewWidth)]
    [InlineData(40, AppSettings.MinPreviewWidth)]
    [InlineData(99999, AppSettings.MaxPreviewWidth)]
    public void AnUnusableStoredWidthIsBroughtIntoRange(int stored, int expected)
    {
        using TempDirectory directory = new();

        AppSettings settings = AppSettings.Load(PathIn(directory));

        settings.ArtBrowserPreviewWidth = stored;

        // Clamped rather than rejected: unlike a dock proportion, any width in
        // range is usable, so the nearest one is the right answer.
        Assert.Equal(expected, settings.UsablePreviewWidth());
    }

    [Theory]
    [InlineData(AppSettings.MinPreviewWidth)]
    [InlineData(260)]
    [InlineData(640)]
    [InlineData(AppSettings.MaxPreviewWidth)]
    public void AnOrdinaryWidthIsKept(int stored)
    {
        using TempDirectory directory = new();

        AppSettings settings = AppSettings.Load(PathIn(directory));

        settings.ArtBrowserPreviewWidth = stored;

        Assert.Equal(stored, settings.UsablePreviewWidth());
    }

    [Fact]
    public void TheBrowserHasASplitterBetweenTheListAndThePreview()
    {
        Assert.SkipWhen(string.IsNullOrWhiteSpace(Client), "No UOP client configured.");

        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using UoDataContext data = UoDataContext.Open(Client!);

            using ArtBrowserWindow browser =
                new(data, ArtBrowserKind.Gump, 250, AppSettings.Load(PathIn(directory)));

            Assert.NotNull(browser.FindControl<GridSplitter>("PreviewSplitter"));
            Assert.NotNull(browser.FindControl<Grid>("Panes"));
        });
    }

    [Fact]
    public void TheStoredWidthIsAppliedWhenTheBrowserOpens()
    {
        Assert.SkipWhen(string.IsNullOrWhiteSpace(Client), "No UOP client configured.");

        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            string path = PathIn(directory);

            AppSettings stored = AppSettings.Load(path);
            stored.ArtBrowserPreviewWidth = 500;
            stored.Save();

            using UoDataContext data = UoDataContext.Open(Client!);
            using ArtBrowserWindow browser =
                new(data, ArtBrowserKind.Gump, 250, AppSettings.Load(path));

            Assert.Equal(500, browser.PreviewWidth);
        });
    }

    [Fact]
    public void AnUnusableStoredWidthDoesNotOpenABrokenPane()
    {
        Assert.SkipWhen(string.IsNullOrWhiteSpace(Client), "No UOP client configured.");

        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            string path = PathIn(directory);

            AppSettings stored = AppSettings.Load(path);
            stored.ArtBrowserPreviewWidth = 0;
            stored.Save();

            using UoDataContext data = UoDataContext.Open(Client!);
            using ArtBrowserWindow browser =
                new(data, ArtBrowserKind.Gump, 250, AppSettings.Load(path));

            Assert.Equal(AppSettings.MinPreviewWidth, browser.PreviewWidth);
        });
    }
}
