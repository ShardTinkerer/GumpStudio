using Avalonia.Controls;
using Avalonia.Interactivity;

using GumpStudio.App.Controls;
using GumpStudio.Uo.Data;

using Xunit;

namespace GumpStudio.App.Tests;

/// <summary>
/// The cliloc browser panel.
/// </summary>
/// <remarks>
/// Driven through synthetic strings rather than a client, which is what the
/// panel's <c>Load</c> seam exists for: the art browser's own tests all skip
/// without an Ultima installation, and the filter is the part most worth
/// covering everywhere.
/// </remarks>
[Collection("Headless")]
public class ClilocPanelTests
{
    private static ClilocEntry Entry(int id, string text) =>
        new(id, ClilocEntryKind.Original, text);

    private static readonly ClilocEntry[] Strings =
    [
        Entry(1044017, "a sturdy pickaxe"),
        Entry(1062724, "the vendor price"),
        Entry(1080000, "a vendor sign"),
        Entry(1114057, "~1_VAL~ stones"),
    ];

    private static ClilocPanel Loaded(params string[] languages)
    {
        ClilocPanel panel = new();

        panel.Load(Strings, languages, languages.Length > 0 ? languages[0] : null);

        return panel;
    }

    [Fact]
    public void TheFilterNarrowsByNumber()
    {
        HeadlessAppSession.Run(() =>
        {
            ClilocPanel panel = Loaded();

            panel.ApplyQuery("1062");

            Assert.Equal(1, panel.MatchCount);
        });
    }

    [Fact]
    public void TheFilterNarrowsByText()
    {
        HeadlessAppSession.Run(() =>
        {
            ClilocPanel panel = Loaded();

            panel.ApplyQuery("vendor");

            Assert.Equal(2, panel.MatchCount);
        });
    }

    [Fact]
    public void TheCountReportsTheMatchesAndTheWhole()
    {
        HeadlessAppSession.Run(() =>
        {
            ClilocPanel panel = Loaded();

            panel.ApplyQuery("vendor");

            Assert.Equal("2 of 4", panel.CountText);
        });
    }

    [Fact]
    public void AnEmptyQueryShowsEverything()
    {
        HeadlessAppSession.Run(() =>
        {
            ClilocPanel panel = Loaded();

            panel.ApplyQuery("vendor");
            panel.ApplyQuery(string.Empty);

            Assert.Equal(4, panel.MatchCount);
            Assert.Equal("4 of 4", panel.CountText);
        });
    }

    [Fact]
    public void SeedingFromABrowseButtonFiltersToThatIdAndSelectsIt()
    {
        HeadlessAppSession.Run(() =>
        {
            ClilocPanel panel = Loaded();

            panel.SeedFilter(1044017);

            Assert.Equal("1044017", panel.Filter.Text);
            Assert.Equal(1044017, panel.SelectedId);
        });
    }

    /// <summary>An id of zero means "none", so it seeds an empty filter.</summary>
    [Fact]
    public void SeedingWithNoIdClearsTheFilter()
    {
        HeadlessAppSession.Run(() =>
        {
            ClilocPanel panel = Loaded();

            panel.ApplyQuery("vendor");
            panel.SeedFilter(0);

            Assert.Equal(string.Empty, panel.Filter.Text);
            Assert.Equal(4, panel.MatchCount);
        });
    }

    [Fact]
    public void TakingARowReportsItOnce()
    {
        HeadlessAppSession.Run(() =>
        {
            ClilocPanel panel = Loaded();

            List<int> chosen = [];

            panel.EntryChosen += (_, id) => chosen.Add(id);

            panel.ShowTarget("Cliloc id");
            panel.SeedFilter(1062724);
            panel.Apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal([1062724], chosen);
        });
    }

    /// <summary>Nothing is taken when there is nowhere to put it.</summary>
    [Fact]
    public void NothingIsTakenWithoutATarget()
    {
        HeadlessAppSession.Run(() =>
        {
            ClilocPanel panel = Loaded();

            List<int> chosen = [];

            panel.EntryChosen += (_, id) => chosen.Add(id);

            panel.ShowTarget(null);
            panel.SeedFilter(1062724);
            panel.Apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.False(panel.Apply.IsEnabled);
            Assert.Empty(chosen);
        });
    }

    /// <summary>
    /// Filling the language list must not read as a user choice.
    /// </summary>
    /// <remarks>
    /// Assigning <c>SelectedItem</c> raises the same event a click does, so
    /// without the guard, loading a client would save its own language back to
    /// settings and start a redundant re-read of 124,000 strings.
    /// </remarks>
    [Fact]
    public void TheLanguageComboReportsOnlyTheUsersChoice()
    {
        HeadlessAppSession.Run(() =>
        {
            List<string> picked = [];

            ClilocPanel panel = new();

            panel.LanguageChosen += (_, code) => picked.Add(code);
            panel.Load(Strings, ["enu", "deu"], "enu");

            Assert.Empty(picked);
            Assert.Equal("ENU", panel.Languages.SelectedItem);

            panel.Languages.SelectedItem = "DEU";

            Assert.Equal(["DEU"], picked);
        });
    }

    /// <summary>One language is not a choice, so the combo stays out of the way.</summary>
    [Fact]
    public void TheLanguageComboIsDisabledUnlessThereIsAChoice()
    {
        HeadlessAppSession.Run(() =>
        {
            Assert.False(Loaded("enu").Languages.IsEnabled);
            Assert.True(Loaded("enu", "deu").Languages.IsEnabled);
        });
    }

    [Fact]
    public void WithNoClientTheListIsEmptyAndTheStatusSaysSo()
    {
        HeadlessAppSession.Run(() =>
        {
            ClilocPanel panel = new();

            panel.Load([], [], null);
            panel.ShowStatus("No client loaded.");

            Assert.Equal(0, panel.MatchCount);
            Assert.False(panel.Languages.IsEnabled);
            Assert.Equal("No client loaded.", panel.CountText);
        });
    }

    /// <summary>A filter that still contains the selected row keeps it.</summary>
    [Fact]
    public void FilteringKeepsASelectionThatSurvivesIt()
    {
        HeadlessAppSession.Run(() =>
        {
            ClilocPanel panel = Loaded();

            panel.SeedFilter(1062724);
            panel.ApplyQuery("vendor");

            Assert.Equal(1062724, panel.SelectedId);

            panel.ApplyQuery("pickaxe");

            Assert.Null(panel.SelectedId);
        });
    }
}
