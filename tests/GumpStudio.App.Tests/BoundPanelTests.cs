using Avalonia.Controls;

using GumpStudio.App;
using GumpStudio.App.Controls;
using GumpStudio.App.ViewModels;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Primitives;
using GumpStudio.TestSupport;

using Xunit;

namespace GumpStudio.App.Tests;

/// <summary>
/// That the bound panels reach a view model and show what it holds.
/// </summary>
/// <remarks>
/// <para>
/// Worth asserting rather than assuming. Dock builds a tool's content through a
/// deferred content control, outside the window's name scope, so nothing
/// inherits a <c>DataContext</c> down to a panel — the window hands each one
/// over explicitly. A panel whose bindings silently resolved against nothing
/// would show an empty list, which looks exactly like a document with nothing
/// in it.
/// </para>
/// <para>
/// Reached through the window's own accessors rather than the visual tree, for
/// that same reason: a headless window never realises a deferred dockable's
/// content, so the panels are not logical descendants. These are the same
/// objects the running application uses.
/// </para>
/// </remarks>
[Collection("Headless")]
public class BoundPanelTests
{
    private static EditorSession SessionIn(TempDirectory directory) =>
        new(AppSettings.Load(Path.Combine(directory.Path, "settings.json")));

    private static LabelElement Label(string text) =>
        new() { Text = text, Location = new GumpPoint(3, 4) };

    [Fact]
    public void TheElementListIsBoundToTheShell()
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using EditorSession session = SessionIn(directory);
            using MainWindow window = new(session);

            Assert.IsType<MainViewModel>(window.ElementList.DataContext);
            Assert.IsType<MainViewModel>(window.PageTabs.DataContext);
        });
    }

    [Fact]
    public void TheElementListShowsThePagesElements()
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using EditorSession session = SessionIn(directory);
            using MainWindow window = new(session);

            session.ActivePage.Root.Add(Label("one"));
            session.ActivePage.Root.Add(Label("two"));

            // Adding through the root does not go through the history, so the
            // selection is what tells the shell to look again.
            session.Canvas.SelectAll();

            List<ElementRowViewModel> rows =
                [.. window.ElementList.ItemsSource!.OfType<ElementRowViewModel>()];

            Assert.Equal(2, rows.Count);
            Assert.All(rows, row => Assert.True(row.IsSelected));
        });
    }

    /// <summary>
    /// A marquee selects everything it covers, so the list has to be able to
    /// show more than one row selected at once. Its predecessor bound a single
    /// <c>SelectedItem</c> and could not.
    /// </summary>
    [Fact]
    public void TheElementListAllowsAMultipleSelection()
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using EditorSession session = SessionIn(directory);
            using MainWindow window = new(session);

            Assert.Equal(SelectionMode.Multiple, window.ElementList.SelectionMode);
        });
    }

    [Fact]
    public void ThePageStripShowsATabPerPage()
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using EditorSession session = SessionIn(directory);
            using MainWindow window = new(session);

            List<PageTabViewModel> tabs =
                [.. window.PageTabs.ItemsSource!.OfType<PageTabViewModel>()];

            Assert.Single(tabs);
            Assert.Equal("Page 0", tabs[0].Label);
            Assert.True(tabs[0].IsActive);

            window.FindControl<MenuItem>("MenuAddPage")!.Command!.Execute(null);

            tabs = [.. window.PageTabs.ItemsSource!.OfType<PageTabViewModel>()];

            Assert.Equal(2, tabs.Count);
            Assert.False(tabs[0].IsActive);
            Assert.True(tabs[1].IsActive);
        });
    }

    /// <summary>
    /// The list's own selection follows the canvas, through real containers.
    /// </summary>
    /// <remarks>
    /// Hosted in a plain window rather than reached through the shell: a
    /// <c>ListBoxItem</c> only exists once the list has been laid out, and Dock
    /// never lays out a deferred dockable's content headlessly. The binding
    /// under test is the panel's own, so it does not need the rest of the shell -
    /// and this is the one part of it a view-model test cannot reach, because
    /// the row and the container have to agree.
    /// </remarks>
    [Fact]
    public void TheListsOwnSelectionFollowsTheCanvas()
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using EditorSession session = SessionIn(directory);

            MainViewModel viewModel = new(
                session, new FakeEditorDialogs(), new FakeTextClipboard(), new FakeShellView());

            ElementsPanel panel = new() { DataContext = viewModel };
            Window host = new() { Content = panel, Width = 300, Height = 400 };

            host.Show();

            LabelElement one = Label("one");
            LabelElement two = Label("two");

            session.ActivePage.Root.Add(one);
            session.ActivePage.Root.Add(two);
            session.Canvas.SelectAll();

            // Containers are built during layout, so the selection cannot have
            // reached them before one has run.
            host.UpdateLayout();

            Assert.Equal(2, panel.List.SelectedItems!.Count);

            session.Canvas.Select(one);
            host.UpdateLayout();

            ElementRowViewModel selected =
                Assert.Single(panel.List.SelectedItems!.OfType<ElementRowViewModel>());

            Assert.Same(one, selected.Element);

            host.Close();
        });
    }

    /// <summary>
    /// And the other way: picking in the list reaches the canvas.
    /// </summary>
    [Fact]
    public void PickingInTheListReachesTheCanvas()
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using EditorSession session = SessionIn(directory);

            MainViewModel viewModel = new(
                session, new FakeEditorDialogs(), new FakeTextClipboard(), new FakeShellView());

            ElementsPanel panel = new() { DataContext = viewModel };
            Window host = new() { Content = panel, Width = 300, Height = 400 };

            host.Show();

            LabelElement one = Label("one");
            session.ActivePage.Root.Add(one);
            session.ActivePage.Root.Add(Label("two"));
            session.Canvas.SelectAll();

            host.UpdateLayout();

            panel.List.SelectedItems!.Clear();
            panel.List.SelectedIndex = 0;

            host.UpdateLayout();

            Assert.Same(one, Assert.Single(session.Canvas.Selection));

            host.Close();
        });
    }

    /// <summary>
    /// The strip is not rebuilt when only the active page changes.
    /// </summary>
    /// <remarks>
    /// The whole point of the collection: the tabs used to be cleared and made
    /// again — a fresh button per page, with its handler — every time anything
    /// about the document changed.
    /// </remarks>
    [Fact]
    public void SwitchingPageKeepsTheSameTabs()
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using EditorSession session = SessionIn(directory);
            using MainWindow window = new(session);

            window.FindControl<MenuItem>("MenuAddPage")!.Command!.Execute(null);

            List<PageTabViewModel> before =
                [.. window.PageTabs.ItemsSource!.OfType<PageTabViewModel>()];

            session.ActivePageIndex = 0;

            List<PageTabViewModel> after =
                [.. window.PageTabs.ItemsSource!.OfType<PageTabViewModel>()];

            Assert.Same(before[0], after[0]);
            Assert.Same(before[1], after[1]);
            Assert.True(after[0].IsActive);
        });
    }
}
