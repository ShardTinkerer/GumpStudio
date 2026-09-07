using GumpStudio.App;
using GumpStudio.TestSupport;

using Xunit;

namespace GumpStudio.App.Tests;

/// <summary>
/// The stored window and panel arrangement.
/// </summary>
/// <remarks>
/// The editor used to open on the layout declared in markup every time, so
/// moving or resizing a panel lasted only as long as the session.
/// </remarks>
public class WindowLayoutTests
{
    private static string PathIn(TempDirectory directory) =>
        Path.Combine(directory.Path, "settings.json");

    /// <summary>
    /// A settings file written before layout persistence existed has no
    /// <c>Layout</c> object at all, and must still load with one.
    /// </summary>
    [Fact]
    public void ASettingsFileFromBeforeLayoutsStillHasOne()
    {
        using TempDirectory directory = new();
        string path = PathIn(directory);

        File.WriteAllText(path, "{ \"ClientPath\": \"C:/Games/UO\", \"GridWidth\": 8 }");

        AppSettings settings = AppSettings.Load(path);

        Assert.NotNull(settings.Layout);
        Assert.Empty(settings.Layout.HiddenPanels);
        Assert.Equal(8, settings.GridWidth);

        // The same trap: a collection property the file omits.
        Assert.NotNull(settings.ExportDialects);
        Assert.Null(settings.ExportDialectFor("pol"));
    }

    [Fact]
    public void AFreshLayoutAsksForNothing()
    {
        using TempDirectory directory = new();

        WindowLayout layout = AppSettings.Load(PathIn(directory)).Layout;

        Assert.Null(layout.Width);
        Assert.Null(layout.Height);
        Assert.Null(layout.X);
        Assert.Null(layout.Y);
        Assert.False(layout.Maximized);
        Assert.Empty(layout.Proportions);
        Assert.Empty(layout.HiddenPanels);
    }

    [Fact]
    public void TheLayoutSurvivesARoundTrip()
    {
        using TempDirectory directory = new();
        string path = PathIn(directory);

        AppSettings written = AppSettings.Load(path);

        written.Layout.Width = 1440;
        written.Layout.Height = 900;
        written.Layout.X = 120;
        written.Layout.Y = 60;
        written.Layout.Maximized = true;
        written.Layout.Proportions["ToolboxPane"] = 0.2;
        written.Layout.Proportions["CanvasPane"] = 0.55;
        written.Layout.HiddenPanels.Add("ElementsTool");
        written.Save();

        WindowLayout read = AppSettings.Load(path).Layout;

        Assert.Equal(1440, read.Width);
        Assert.Equal(900, read.Height);
        Assert.Equal(120, read.X);
        Assert.Equal(60, read.Y);
        Assert.True(read.Maximized);
        Assert.Equal(0.2, read.Proportions["ToolboxPane"]);
        Assert.Equal(0.55, read.Proportions["CanvasPane"]);
        Assert.Equal(["ElementsTool"], read.HiddenPanels);
    }

    [Fact]
    public void TheLayoutIsStoredBesideTheOtherPreferences()
    {
        using TempDirectory directory = new();
        string path = PathIn(directory);

        AppSettings settings = AppSettings.Load(path);

        settings.GridWidth = 9;
        settings.Layout.HiddenPanels.Add("PropertiesTool");
        settings.Save();

        AppSettings reopened = AppSettings.Load(path);

        Assert.Equal(9, reopened.GridWidth);
        Assert.Equal(["PropertiesTool"], reopened.Layout.HiddenPanels);
    }

    /// <summary>
    /// A layout file written by a future version, or corrupted, must not stop
    /// the editor opening.
    /// </summary>
    [Fact]
    public void AnUnreadableLayoutFallsBackToDefaults()
    {
        using TempDirectory directory = new();
        string path = PathIn(directory);

        File.WriteAllText(path, """{ "Layout": { "Width": "not a number" } }""");

        WindowLayout layout = AppSettings.Load(path).Layout;

        Assert.Null(layout.Width);
        Assert.Empty(layout.HiddenPanels);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(-0.5)]
    [InlineData(42.0)]
    [InlineData(double.NaN)]
    public void AProportionOutsideTheUsableRangeIsRejected(double proportion)
    {
        // Rejected rather than clamped, and either extreme would collapse a
        // panel to nothing, which reads as the panel having disappeared.
        Assert.False(WindowLayout.IsUsableProportion(proportion));
    }

    [Theory]
    [InlineData(0.03)]
    [InlineData(0.13)]
    [InlineData(0.6)]
    [InlineData(0.97)]
    public void AnOrdinaryProportionIsAccepted(double proportion)
    {
        Assert.True(WindowLayout.IsUsableProportion(proportion));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(1280.0, null)]
    [InlineData(10.0, 900.0)]
    [InlineData(1280.0, 10.0)]
    [InlineData(99999.0, 900.0)]
    [InlineData(double.NaN, 900.0)]
    public void AnUnusableWindowSizeIsRejected(double? width, double? height)
    {
        Assert.False(WindowLayout.IsUsableSize(width, height));
    }

    [Fact]
    public void AnOrdinaryWindowSizeIsAccepted()
    {
        Assert.True(WindowLayout.IsUsableSize(1280, 860));
    }
}
