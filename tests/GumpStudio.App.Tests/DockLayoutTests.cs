using Avalonia.Controls;

using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;

using GumpStudio.App;
using GumpStudio.TestSupport;

using Xunit;

namespace GumpStudio.App.Tests;

/// <summary>
/// The panel arrangement, driven through the real window and the real dock
/// model.
/// </summary>
/// <remarks>
/// These exist because the first version of this feature crashed on startup and
/// nothing could have caught it: everything interesting happens inside a
/// constructed <see cref="MainWindow"/>.
/// </remarks>
[Collection("Headless")]
public class DockLayoutTests
{
    private static readonly string[] HideablePanels =
        ["ToolboxTool", "ElementsTool", "PropertiesTool", "ClilocTool"];

    private static readonly string[] PanelMenuItems =
        ["MenuPanelToolbox", "MenuPanelElements", "MenuPanelProperties", "MenuPanelCliloc"];

    private static EditorSession SessionIn(TempDirectory directory) =>
        new(AppSettings.Load(Path.Combine(directory.Path, "settings.json")));

    private static IDockable? Dockable(MainWindow window, string id) =>
        window.FindNameScope()?.Find(id) as IDockable;

    private static bool IsHidden(MainWindow window, string id)
    {
        DockControl layout = window.FindControl<DockControl>("Layout")!;

        return layout.Layout is IRootDock { HiddenDockables: { } hidden }
            && hidden.Any(d => d.Id == id);
    }

    [Fact]
    public void TheWindowOpensWithEveryPanelPresent()
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using EditorSession session = SessionIn(directory);
            using MainWindow window = new(session);

            Assert.NotNull(Dockable(window, "ToolboxTool"));
            Assert.NotNull(Dockable(window, "ElementsTool"));
            Assert.NotNull(Dockable(window, "PropertiesTool"));
            Assert.NotNull(Dockable(window, "ClilocTool"));
            Assert.NotNull(Dockable(window, "GumpDocument"));
        });
    }

    /// <summary>
    /// Why the tabs are still not closable: Dock's close removes a dockable from
    /// its owner, while hide parks it on the root where RestoreDockable can find
    /// it again.
    /// </summary>
    [Fact]
    public void APanelCanBeHiddenAndBroughtBack()
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using EditorSession session = SessionIn(directory);
            using MainWindow window = new(session);

            IFactory factory = window.FindControl<DockControl>("Layout")!.Factory!;

            factory.HideDockable("ElementsTool");

            Assert.True(IsHidden(window, "ElementsTool"));

            factory.RestoreDockable("ElementsTool");

            Assert.False(IsHidden(window, "ElementsTool"));
        });
    }

    [Fact]
    public void NoPanelCanBeClosedFromItsTab()
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using EditorSession session = SessionIn(directory);
            using MainWindow window = new(session);

            foreach (string id in HideablePanels)
            {
                Assert.False(Dockable(window, id)!.CanClose, id);
            }

            Assert.False(Dockable(window, "GumpDocument")!.CanClose);
        });
    }

    [Fact]
    public void TheViewMenuOffersOneItemPerHideablePanel()
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using EditorSession session = SessionIn(directory);
            using MainWindow window = new(session);

            foreach (string name in PanelMenuItems)
            {
                MenuItem? item = window.FindControl<MenuItem>(name);

                Assert.NotNull(item);
                Assert.True(item!.IsChecked, name);
            }

            Assert.NotNull(window.FindControl<MenuItem>("MenuResetLayout"));
        });
    }

    [Fact]
    public void ThePanesDeclareTheProportionsAResetRestores()
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using EditorSession session = SessionIn(directory);
            using MainWindow window = new(session);

            Assert.Equal(0.13, ((IDock)Dockable(window, "ToolboxPane")!).Proportion, 3);
            Assert.Equal(0.27, ((IDock)Dockable(window, "RightPane")!).Proportion, 3);

            // The canvas shares the middle column with the cliloc browser, so
            // the 0.6 that used to be the canvas's is now the column's.
            Assert.Equal(0.6, ((IDock)Dockable(window, "CenterPane")!).Proportion, 3);
            Assert.Equal(0.75, ((IDock)Dockable(window, "CanvasPane")!).Proportion, 3);
            Assert.Equal(0.25, ((IDock)Dockable(window, "ClilocPane")!).Proportion, 3);
        });
    }

    [Fact]
    public void AStoredProportionIsAppliedWhenTheWindowOpens()
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            string path = Path.Combine(directory.Path, "settings.json");

            AppSettings stored = AppSettings.Load(path);
            stored.Layout.Proportions["ToolboxPane"] = 0.25;
            stored.Save();

            using EditorSession session = new(AppSettings.Load(path));
            using MainWindow window = new(session);

            // The restore runs on Opened, which is where the dock model is
            // reliably built.
            window.Show();

            Assert.Equal(0.25, ((IDock)Dockable(window, "ToolboxPane")!).Proportion, 3);

            window.Close();
        });
    }

    [Fact]
    public void AStoredHiddenPanelIsHiddenWhenTheWindowOpens()
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            string path = Path.Combine(directory.Path, "settings.json");

            AppSettings stored = AppSettings.Load(path);
            stored.Layout.HiddenPanels.Add("PropertiesTool");
            stored.Save();

            using EditorSession session = new(AppSettings.Load(path));
            using MainWindow window = new(session);

            window.Show();

            Assert.True(IsHidden(window, "PropertiesTool"));
            Assert.False(window.FindControl<MenuItem>("MenuPanelProperties")!.IsChecked);

            window.Close();
        });
    }

    /// <summary>
    /// A stored id that no longer names a panel is ignored rather than throwing,
    /// so a layout written by another version still opens.
    /// </summary>
    [Fact]
    public void AnUnknownStoredPanelIsIgnored()
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            string path = Path.Combine(directory.Path, "settings.json");

            AppSettings stored = AppSettings.Load(path);
            stored.Layout.HiddenPanels.Add("APanelFromAnotherVersion");
            stored.Layout.Proportions["NoSuchPane"] = 0.4;
            stored.Save();

            using EditorSession session = new(AppSettings.Load(path));
            using MainWindow window = new(session);

            window.Show();

            Assert.NotNull(Dockable(window, "ToolboxTool"));

            window.Close();
        });
    }
}

/// <summary>
/// Serialises the headless tests: Avalonia allows one application per process
/// and its dispatcher is a single thread.
/// </summary>
[CollectionDefinition("Headless", DisableParallelization = true)]
public class HeadlessTests;
