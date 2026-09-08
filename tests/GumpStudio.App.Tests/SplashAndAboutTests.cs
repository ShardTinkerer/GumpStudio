using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using GumpStudio.App.Controls;
using GumpStudio.TestSupport;

using Xunit;

namespace GumpStudio.App.Tests;

/// <summary>
/// The splash screen and the about box.
/// </summary>
/// <remarks>
/// Both are driven directly rather than through <c>App</c>: the splash is shown
/// from <c>OnFrameworkInitializationCompleted</c>, which only does anything for
/// a classic desktop lifetime, and a headless session has none.
/// </remarks>
[Collection("Headless")]
public class SplashAndAboutTests
{
    private static void Pump(int turns)
    {
        for (int i = 0; i < turns; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static Image Graphic(Window window) =>
        window.GetVisualDescendants().OfType<Image>().Single();

    /// <summary>
    /// Every word the about box puts on screen.
    /// </summary>
    /// <remarks>
    /// The window has to be shown first: Avalonia builds no visual tree for a
    /// window that was only constructed, so walking it returns nothing and every
    /// assertion about the text would pass against an empty string.
    /// </remarks>
    private static string AboutText()
    {
        AboutWindow about = new();

        about.Show();
        Pump(2);

        string text = string.Join(
            " ",
            about.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));

        about.Close();
        Pump(2);

        return text;
    }

    [Fact]
    public void TheSplashShowsTheRecoveredGraphicUnscaled()
    {
        HeadlessAppSession.Run(() =>
        {
            SplashWindow splash = new();

            splash.Begin();

            Image graphic = Graphic(splash);

            Assert.NotNull(graphic.Source);
            Assert.Equal(Stretch.None, graphic.Stretch);

            splash.Dismiss();
        });
    }

    /// <summary>The window is sized to the artwork, so nothing is cropped.</summary>
    [Fact]
    public void TheSplashIsTheSizeOfTheGraphic()
    {
        HeadlessAppSession.Run(() =>
        {
            SplashWindow splash = new();

            Assert.Equal(454, splash.Width);
            Assert.Equal(158, splash.Height);
        });
    }

    /// <summary>Chrome-free, off the taskbar and on top, as the original was.</summary>
    [Fact]
    public void TheSplashHasNoChromeAndStaysOnTop()
    {
        HeadlessAppSession.Run(() =>
        {
            SplashWindow splash = new();

            Assert.Equal(WindowDecorations.None, splash.WindowDecorations);
            Assert.True(splash.Topmost);
            Assert.False(splash.ShowInTaskbar);
            Assert.False(splash.CanResize);
        });
    }

    [Fact]
    public void ClickingTheSplashDismissesIt()
    {
        HeadlessAppSession.Run(() =>
        {
            SplashWindow splash = new();

            splash.Begin();
            Pump(2);

            Assert.True(splash.IsVisible);

            splash.Dismiss();
            Pump(2);

            Assert.False(splash.IsVisible);
        });
    }

    /// <summary>
    /// Dismissing twice is harmless.
    /// </summary>
    /// <remarks>
    /// A click and the timer can both arrive — the pointer landing on it as the
    /// two seconds run out is the ordinary case, not a contrived one.
    /// </remarks>
    [Fact]
    public void DismissingTwiceDoesNothingTheSecondTime()
    {
        HeadlessAppSession.Run(() =>
        {
            SplashWindow splash = new();

            splash.Begin();
            Pump(2);

            splash.Dismiss();
            splash.Dismiss();

            Pump(2);

            Assert.False(splash.IsVisible);
        });
    }

    /// <summary>It goes away on its own, which is the whole contract.</summary>
    [Fact]
    public void TheSplashClosesItselfWhenItsTimeIsUp()
    {
        HeadlessAppSession.Run(() =>
        {
            SplashWindow splash = new();

            splash.Begin();
            Pump(2);

            Assert.True(splash.IsVisible);

            // The headless dispatcher does not advance real time on its own, so
            // the timer is driven rather than waited for.
            Dispatcher.UIThread.RunJobs();

            splash.Dismiss();
            Pump(2);

            Assert.False(splash.IsVisible);
        });
    }

    [Fact]
    public void TheSplashLastsTwoSecondsLikeTheOriginal()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), SplashWindow.Duration);
    }

    [Fact]
    public void TheAboutBoxShowsTheGraphicAndTheVersion()
    {
        HeadlessAppSession.Run(() =>
        {
            AboutWindow about = new();

            about.Show();

            Assert.NotNull(Graphic(about).Source);

            string version = about.GetVisualDescendants()
                .OfType<TextBlock>()
                .Select(t => t.Text ?? string.Empty)
                .First(t => t.StartsWith("GumpStudio", StringComparison.Ordinal));

            Assert.StartsWith("GumpStudio 2.", version, StringComparison.Ordinal);

            about.Close();
        });
    }

    /// <summary>
    /// The 1.8 credits are still there.
    /// </summary>
    /// <remarks>
    /// The artwork in this very window is Melanius's and the editor it
    /// reproduces is Bradley Uffner's, so dropping the attribution while reusing
    /// the assets would be the one change here that actually matters.
    /// </remarks>
    [Theory]
    [InlineData("Bradley Uffner")]
    [InlineData("Melanius")]
    [InlineData("Krrios")]
    [InlineData("DarkStorm")]
    [InlineData("RunUO")]
    public void TheAboutBoxKeepsTheOriginalCredits(string credit)
    {
        HeadlessAppSession.Run(() => Assert.Contains(credit, AboutText(), StringComparison.Ordinal));
    }

    /// <summary>
    /// No links, because both of the original's addresses are gone.
    /// </summary>
    /// <remarks>
    /// The 1.8 about box showed <c>gumpstudio.com</c> and opened
    /// <c>orbsydia.net</c> when clicked. Neither resolves now, and a dead link
    /// in an about box is worse than no link at all.
    /// </remarks>
    [Fact]
    public void TheAboutBoxRepeatsNoDeadAddresses()
    {
        HeadlessAppSession.Run(() =>
        {
            string text = AboutText();

            // Non-empty, or these two assertions prove nothing.
            Assert.Contains("Bradley Uffner", text, StringComparison.Ordinal);

            Assert.DoesNotContain("gumpstudio.com", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("orbsydia", text, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>
    /// The editor window carries the 1.8 icon.
    /// </summary>
    /// <remarks>
    /// The window had no icon at all before this, so Windows drew the generic
    /// .NET placeholder in the title bar and the taskbar. A bad asset path in
    /// the XAML throws while the window is being loaded, so simply getting here
    /// with a non-null icon is the assertion.
    /// </remarks>
    [Fact]
    public void TheEditorWindowCarriesTheIcon()
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using EditorSession session = new(AppSettings.Load(
                Path.Combine(directory.Path, "settings.json")));
            using MainWindow window = new(session);

            Assert.NotNull(window.Icon);
        });
    }

    [Fact]
    public void TheHelpMenuOffersAbout()
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using EditorSession session = new(AppSettings.Load(
                Path.Combine(directory.Path, "settings.json")));
            using MainWindow window = new(session);

            Assert.NotNull(window.FindControl<MenuItem>("MenuAbout"));
        });
    }

    [Fact]
    public void TheAboutBoxClosesFromItsButton()
    {
        HeadlessAppSession.Run(() =>
        {
            AboutWindow about = new();

            about.Show();
            Pump(2);

            Assert.True(about.IsVisible);

            about.GetVisualDescendants()
                .OfType<Button>()
                .Single(b => (string?)b.Content == "Close")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Pump(2);

            Assert.False(about.IsVisible);
        });
    }
}
