using Avalonia;

using GumpStudio.App;
using GumpStudio.App.Controls;
using GumpStudio.Core.Geometry;
using GumpStudio.TestSupport;

using Xunit;

namespace GumpStudio.App.Tests;

/// <summary>
/// Canvas magnification.
/// </summary>
/// <remarks>
/// Neither this editor nor the 1.8 original had any zoom: the design surface
/// was a fixed 1024 by 768 at one art pixel to one screen pixel, so a gump
/// wider than the window could not be seen whole and fine placement had to be
/// done by typing coordinates.
/// </remarks>
[Collection("Headless")]
public class CanvasZoomTests
{
    private static EditorSession SessionIn(TempDirectory directory) =>
        new(AppSettings.Load(Path.Combine(directory.Path, "settings.json")));

    [Fact]
    public void TheCanvasStartsAtActualSize()
    {
        HeadlessAppSession.Run(() =>
        {
            GumpCanvas canvas = new();

            Assert.Equal(1.0, canvas.Zoom);
            Assert.Equal(GumpCanvas.DesignWidth, canvas.Width);
            Assert.Equal(GumpCanvas.DesignHeight, canvas.Height);
        });
    }

    [Fact]
    public void ZoomingResizesTheSurfaceItScrollsInside()
    {
        HeadlessAppSession.Run(() =>
        {
            GumpCanvas canvas = new() { Zoom = 2.0 };

            Assert.Equal(GumpCanvas.DesignWidth * 2, canvas.Width);
            Assert.Equal(GumpCanvas.DesignHeight * 2, canvas.Height);

            canvas.Zoom = 0.5;

            Assert.Equal(GumpCanvas.DesignWidth * 0.5, canvas.Width);
            Assert.Equal(GumpCanvas.DesignHeight * 0.5, canvas.Height);
        });
    }

    [Theory]
    [InlineData(0.01, GumpCanvas.MinZoom)]
    [InlineData(-4.0, GumpCanvas.MinZoom)]
    [InlineData(100.0, GumpCanvas.MaxZoom)]
    public void ZoomIsClampedToWhatTheEditorOffers(double requested, double expected)
    {
        HeadlessAppSession.Run(() =>
        {
            GumpCanvas canvas = new() { Zoom = requested };

            Assert.Equal(expected, canvas.Zoom);
        });
    }

    [Fact]
    public void FittingChoosesTheAxisThatConstrainsTheView()
    {
        HeadlessAppSession.Run(() =>
        {
            GumpCanvas canvas = new();

            // Wide but short: the height is what limits it.
            canvas.ZoomToFit(new Size(4096, 384));

            Assert.Equal(0.5, canvas.Zoom, 3);

            // Tall but narrow: now the width does.
            canvas.ZoomToFit(new Size(512, 4096));

            Assert.Equal(0.5, canvas.Zoom, 3);
        });
    }

    [Fact]
    public void FittingAnEmptyViewportChangesNothing()
    {
        HeadlessAppSession.Run(() =>
        {
            GumpCanvas canvas = new() { Zoom = 2.0 };

            canvas.ZoomToFit(new Size(0, 0));

            Assert.Equal(2.0, canvas.Zoom);
        });
    }

    [Fact]
    public void ZoomChangedIsRaisedOnlyWhenItActuallyMoves()
    {
        HeadlessAppSession.Run(() =>
        {
            GumpCanvas canvas = new();

            int raised = 0;
            canvas.ZoomChanged += (_, _) => raised++;

            canvas.Zoom = 2.0;

            Assert.Equal(1, raised);

            canvas.Zoom = 2.0;

            Assert.Equal(1, raised);
        });
    }

    /// <summary>
    /// The handles have to grow in gump units as the view shrinks, or they
    /// would become impossible to grab when zoomed out.
    /// </summary>
    [Fact]
    public void TheSessionIsToldHowLargeAHandleMustBe()
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using EditorSession session = SessionIn(directory);

            GumpCanvas canvas = new() { Session = session };

            Assert.Equal(HandleGeometry.HandleSize, session.Canvas.HandleSize);

            canvas.Zoom = 0.25;

            // Five screen pixels at quarter size is twenty gump units, rounded
            // up to an odd number so the handle centres on its corner.
            Assert.Equal(21, session.Canvas.HandleSize);

            canvas.Zoom = 4.0;

            // Zoomed in, a five-pixel handle is smaller than a gump pixel, so it
            // never shrinks below the drawn minimum.
            Assert.True(session.Canvas.HandleSize >= 1);
            Assert.True(session.Canvas.HandleSize % 2 == 1);
        });
    }

    [Fact]
    public void AttachingASessionAppliesTheZoomAlreadySet()
    {
        HeadlessAppSession.Run(() =>
        {
            using TempDirectory directory = new();
            using EditorSession session = SessionIn(directory);

            // Zoom set before the session is attached, which is the order the
            // window uses when a stored zoom is restored.
            GumpCanvas canvas = new() { Zoom = 0.5 };

            canvas.Session = session;

            Assert.Equal(11, session.Canvas.HandleSize);
        });
    }
}
