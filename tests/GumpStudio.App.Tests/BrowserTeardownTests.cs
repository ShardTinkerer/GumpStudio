using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

using GumpStudio.App;
using GumpStudio.App.Controls;
using GumpStudio.TestSupport;
using GumpStudio.Uo;

using Xunit;

namespace GumpStudio.App.Tests;

/// <summary>
/// Closing the art browser while it is still decoding.
/// </summary>
/// <remarks>
/// This is the regression that mattered most. Scrolling the gallery and then
/// closing it left the whole application unable to close: the browser disposed a
/// <c>SemaphoreSlim</c> that queued decodes were still using, their pending
/// <c>Release</c> calls threw <see cref="ObjectDisposedException"/> from inside a
/// <c>finally</c>, the faulted task's awaiter resumed on the dispatcher and
/// rethrew there — and an unhandled exception in a dispatcher continuation takes
/// the dispatcher with it.
///
/// A headless dispatcher is pumped by hand, so a continuation that throws
/// surfaces out of <c>RunJobs</c> and fails these outright. That is the whole
/// detector: they need a real client, because the fault only arises when there
/// is art actually being decoded.
/// </remarks>
[Collection("Headless")]
public class BrowserTeardownTests
{
    private static string? Client =>
        Environment.GetEnvironmentVariable("GUMPSTUDIO_TEST_CLIENT_UOP");

    private static AppSettings ScratchSettings(TempDirectory directory, bool gallery)
    {
        AppSettings settings = AppSettings.Load(Path.Combine(directory.Path, "settings.json"));

        settings.ArtBrowserGallery = gallery;
        settings.ArtBrowserTileSize = 144;

        return settings;
    }

    /// <summary>Runs the dispatcher, which is where a broken continuation shows.</summary>
    private static void Pump(int turns)
    {
        for (int i = 0; i < turns; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static int Filled(ArtBrowserWindow browser) =>
        browser.GetVisualDescendants().OfType<Image>().Count(i => i.Source is Bitmap);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ClosingWhileDecodingLeavesTheDispatcherWorking(bool gallery)
    {
        Assert.SkipWhen(string.IsNullOrWhiteSpace(Client), "No UOP client configured.");

        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using UoDataContext data = UoDataContext.Open(Client!);

            using (ArtBrowserWindow browser =
                new(data, ArtBrowserKind.Gump, 250, ScratchSettings(directory, gallery)))
            {
                browser.Width = 900;
                browser.Height = 700;
                browser.Show();

                // Let a screenful start decoding, then close on top of it.
                Pump(40);

                browser.Close();
            }

            // Anything the close left behind runs here. Before the fix this threw
            // ObjectDisposedException out of RunJobs.
            Pump(200);

            // And the dispatcher still does work afterwards.
            bool ran = false;

            Dispatcher.UIThread.Post(() => ran = true);
            Pump(10);

            Assert.True(ran, "the dispatcher stopped running jobs after the browser closed");
        });
    }

    [Fact]
    public void ScrollingThenClosingLeavesTheDispatcherWorking()
    {
        Assert.SkipWhen(string.IsNullOrWhiteSpace(Client), "No UOP client configured.");

        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using UoDataContext data = UoDataContext.Open(Client!);

            using (ArtBrowserWindow browser =
                new(data, ArtBrowserKind.Gump, 250, ScratchSettings(directory, gallery: true)))
            {
                browser.Width = 900;
                browser.Height = 700;
                browser.Show();

                Pump(40);

                // Scroll, which queues decodes for rows that are then abandoned.
                if (browser.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()
                    is { } scroller)
                {
                    for (int step = 1; step <= 12; step++)
                    {
                        scroller.Offset = new Avalonia.Vector(0, step * 400);

                        Pump(3);
                    }
                }

                browser.Close();
            }

            Pump(300);

            bool ran = false;

            Dispatcher.UIThread.Post(() => ran = true);
            Pump(10);

            Assert.True(ran, "the dispatcher stopped running jobs after a scrolled browser closed");
        });
    }

    /// <summary>
    /// The browser is a modal opened from the editor, so the editor has to be
    /// able to close afterwards — which is the symptom that was actually
    /// reported.
    /// </summary>
    [Fact]
    public void TheEditorStillClosesAfterBrowsingArt()
    {
        Assert.SkipWhen(string.IsNullOrWhiteSpace(Client), "No UOP client configured.");

        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using UoDataContext data = UoDataContext.Open(Client!);

            AppSettings settings = ScratchSettings(directory, gallery: true);

            using EditorSession session = new(settings);
            using MainWindow editor = new(session);

            editor.Show();
            Pump(20);

            using (ArtBrowserWindow browser = new(data, ArtBrowserKind.Gump, 250, settings))
            {
                browser.Width = 900;
                browser.Height = 700;
                browser.Show();

                Pump(40);

                browser.Close();
            }

            Pump(200);

            editor.Close();
            Pump(20);

            Assert.False(editor.IsVisible, "the editor would not close after browsing art");
        });
    }

    /// <summary>
    /// The decodes still have to arrive when nothing interrupts them, or the
    /// tests above would pass on a browser that simply never loads anything.
    /// </summary>
    [Fact]
    public void ThumbnailsDoArriveWhenLeftAlone()
    {
        Assert.SkipWhen(string.IsNullOrWhiteSpace(Client), "No UOP client configured.");

        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using UoDataContext data = UoDataContext.Open(Client!);

            using ArtBrowserWindow browser =
                new(data, ArtBrowserKind.Gump, 250, ScratchSettings(directory, gallery: true));

            browser.Width = 900;
            browser.Height = 700;
            browser.Show();

            Pump(400);

            Assert.True(Filled(browser) > 0, "no thumbnail ever acquired a source");

            browser.Close();
            Pump(50);
        });
    }
}
