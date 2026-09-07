using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using GumpStudio.Core.Editing;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Geometry;
using GumpStudio.Core.Primitives;
using GumpStudio.Rendering;

using SkiaSharp;

using GumpRenderOptions = GumpStudio.Rendering.RenderOptions;

namespace GumpStudio.App.Controls;

/// <summary>
/// The design surface: renders the active page through SkiaSharp and forwards
/// pointer input to the interaction controller.
/// </summary>
/// <remarks>
/// Deliberately thin. Everything about what a gesture <em>means</em> lives in
/// <see cref="CanvasInteractionController"/>, which is in Core and tested there;
/// this control only translates Avalonia events into gump coordinates and blits
/// the result.
/// </remarks>
public sealed class GumpCanvas : Control, IDisposable
{
    private WriteableBitmap? _surface;
    private EditorSession? _session;
    private int _surfaceWidth;
    private int _surfaceHeight;
    private double _surfaceScaling;

    // Rebuilt only when the art source changes. It was allocated per frame,
    // along with the options record and the backdrop brush.
    private GumpRenderer? _renderer;
    private IGumpArtSource? _rendererArt;

    private GumpRenderOptions? _options;
    private bool _optionsShowSharedPage;
    private GridSettings? _optionsGrid;

    /// <summary>The empty space around the gump. A constant, so it is shared.</summary>
    private static readonly IBrush Backdrop = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2E));

    public GumpCanvas()
    {
        Focusable = true;
        ClipToBounds = true;

        // Sized here rather than in markup, so the zoom is the only thing that
        // decides how large the surface is.
        Width = DesignWidth;
        Height = DesignHeight;
    }

    /// <summary>The session whose active page is drawn.</summary>
    public EditorSession? Session
    {
        get => _session;
        set
        {
            if (ReferenceEquals(_session, value))
            {
                return;
            }

            if (_session is not null)
            {
                _session.Canvas.Changed -= OnSessionChanged;
                _session.PageChanged -= OnSessionChanged;
                _session.DocumentChanged -= OnSessionChanged;
            }

            _session = value;

            if (_session is not null)
            {
                _session.Canvas.Changed += OnSessionChanged;
                _session.PageChanged += OnSessionChanged;
                _session.DocumentChanged += OnSessionChanged;

                // The session is attached after construction, so it has to be
                // told the handle size the current zoom implies.
                _session.Canvas.HandleSize = HandleSizeForZoom(_zoom);
            }

            InvalidateVisual();
        }
    }

    /// <summary>
    /// Whether page 0 is drawn beneath the active page.
    /// </summary>
    /// <remarks>
    /// Page 0 is always visible in the client, so showing it is the default;
    /// hiding it is occasionally useful when it obscures what you are editing.
    /// </remarks>
    public bool ShowSharedPage { get; set; } = true;

    /// <summary>The design area, in gump units, before zoom.</summary>
    /// <remarks>
    /// A gump has no intrinsic canvas size — the client draws wherever an
    /// element is placed — so the editor offers a fixed area to lay one out in,
    /// as the original did.
    /// </remarks>
    public const int DesignWidth = 1024;

    public const int DesignHeight = 768;

    /// <summary>Smallest and largest zoom the editor offers.</summary>
    public const double MinZoom = 0.25;

    public const double MaxZoom = 4.0;

    private double _zoom = 1.0;

    /// <summary>
    /// How magnified the design surface is.
    /// </summary>
    /// <remarks>
    /// Gump art is pixel art at a fixed size, so the client shows it one art
    /// pixel to one screen pixel and so does this by default. Zoom exists for
    /// the two things that are otherwise awkward: placing something precisely,
    /// and seeing a gump wider than the window.
    ///
    /// Everything below the canvas keeps working in gump units. Only
    /// <see cref="ToGump"/> and the render transform know about the factor, and
    /// the decoration sizes are divided by it so handles stay the same size
    /// under the pointer at every zoom.
    /// </remarks>
    public double Zoom
    {
        get => _zoom;
        set
        {
            double clamped = Math.Clamp(value, MinZoom, MaxZoom);

            if (Math.Abs(clamped - _zoom) < 0.0001)
            {
                return;
            }

            _zoom = clamped;

            ApplyZoom();

            ZoomChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Raised after a gesture changes the selection or geometry.</summary>
    public event EventHandler? InteractionChanged;

    /// <summary>Raised after <see cref="Zoom"/> changes.</summary>
    public event EventHandler? ZoomChanged;

    /// <summary>
    /// Sizes the control for the current zoom and tells the session how big a
    /// handle now has to be.
    /// </summary>
    private void ApplyZoom()
    {
        Width = DesignWidth * _zoom;
        Height = DesignHeight * _zoom;

        if (_session is not null)
        {
            _session.Canvas.HandleSize = HandleSizeForZoom(_zoom);
        }

        _options = null;

        InvalidateVisual();
    }

    /// <summary>
    /// A handle's side in gump units, so that it is
    /// <see cref="HandleGeometry.HandleSize"/> pixels on screen.
    /// </summary>
    /// <remarks>
    /// Rounded up and forced odd, because a handle is centred on its corner by
    /// integer halving: an even side would sit half a pixel off.
    /// </remarks>
    private static int HandleSizeForZoom(double zoom)
    {
        int size = (int)Math.Ceiling(HandleGeometry.HandleSize / zoom);

        return size % 2 == 0 ? size + 1 : size;
    }

    /// <summary>Sets the zoom so the whole design area fits a viewport.</summary>
    public void ZoomToFit(Size viewport)
    {
        if (viewport.Width <= 0 || viewport.Height <= 0)
        {
            return;
        }

        Zoom = Math.Min(viewport.Width / DesignWidth, viewport.Height / DesignHeight);
    }

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        base.Render(context);

        int width = Math.Max(1, (int)Bounds.Width);
        int height = Math.Max(1, (int)Bounds.Height);

        context.FillRectangle(Backdrop, Bounds);

        if (_session?.Art is not { } art)
        {
            return;
        }

        // The surface is allocated in device pixels and the drawing scaled to
        // match, so a gump stays sharp on a scaled display. It used to be
        // allocated at the logical size and stamped 96 dpi regardless, which
        // under-sampled everything at any scaling above 100%.
        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;

        EnsureSurface(width, height, scaling);

        if (_surface is null)
        {
            return;
        }

        using (ILockedFramebuffer buffer = _surface.Lock())
        {
            SKImageInfo info = new(
                buffer.Size.Width, buffer.Size.Height, SKColorType.Bgra8888, SKAlphaType.Premul);

            using SKSurface skia = SKSurface.Create(info, buffer.Address, buffer.RowBytes);

            skia.Canvas.Clear(SKColors.Transparent);

            // Everything below draws in gump units; this is the only place that
            // knows about device pixels or magnification.
            skia.Canvas.Scale((float)(scaling * _zoom));

            RendererFor(art).RenderDocument(
                skia.Canvas,
                _session.Document,
                _session.ActivePageIndex,
                CurrentOptions());

            DrawMarquee(skia.Canvas);

            skia.Canvas.Flush();
        }

        context.DrawImage(_surface, new Rect(0, 0, width, height));
    }

    private GumpRenderer RendererFor(IGumpArtSource art)
    {
        if (_renderer is null || !ReferenceEquals(_rendererArt, art))
        {
            _renderer = new GumpRenderer(art);
            _rendererArt = art;
        }

        return _renderer;
    }

    /// <summary>
    /// The render options for this frame, reused while nothing about them moves.
    /// </summary>
    /// <remarks>
    /// <c>RenderOptions</c> is an immutable record, so holding one is safe; the
    /// grid is compared by reference because the session mutates the same
    /// instance in place, which is also why its values are re-read below.
    /// </remarks>
    private GumpRenderOptions CurrentOptions()
    {
        GridSettings grid = _session!.Canvas.Grid;

        if (_options is null
            || _optionsShowSharedPage != ShowSharedPage
            || !ReferenceEquals(_optionsGrid, grid))
        {
            _options = new GumpRenderOptions
            {
                DrawSelection = true,
                DrawGroupOutlines = true,
                ShowSharedPage = ShowSharedPage,
                Grid = grid,
                HandleSize = HandleSizeForZoom(_zoom),

                // A hairline: one device pixel whatever the transform, which is
                // what an outline around pixel art wants at every zoom.
                OutlineWidth = 0,
            };

            _optionsShowSharedPage = ShowSharedPage;
            _optionsGrid = grid;
        }

        return _options;
    }

    private void DrawMarquee(SKCanvas canvas)
    {
        if (_session?.Canvas.Marquee is not { } marquee || marquee.IsEmpty)
        {
            return;
        }

        SKRect rect = SKRect.Create(marquee.X, marquee.Y, marquee.Width, marquee.Height);

        using SKPaint fill = new() { Color = new SKColor(0x33, 0x99, 0xFF, 0x30) };
        using SKPaint stroke = new()
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            Color = new SKColor(0x33, 0x99, 0xFF),
        };

        canvas.DrawRect(rect, fill);
        canvas.DrawRect(rect, stroke);
    }

    /// <summary>
    /// Allocates the backing bitmap for a logical size at a display scaling.
    /// </summary>
    /// <remarks>
    /// The bitmap declares its dpi as 96 times the scaling, so Avalonia reports
    /// its size in the same logical units the caller passed and the blit stays
    /// one device pixel to one bitmap pixel.
    /// </remarks>
    private void EnsureSurface(int width, int height, double scaling)
    {
        if (_surface is not null
            && _surfaceWidth == width
            && _surfaceHeight == height
            && _surfaceScaling == scaling)
        {
            return;
        }

        _surface?.Dispose();

        _surface = new WriteableBitmap(
            new PixelSize(
                Math.Max(1, (int)Math.Ceiling(width * scaling)),
                Math.Max(1, (int)Math.Ceiling(height * scaling))),
            new Vector(96 * scaling, 96 * scaling),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        _surfaceWidth = width;
        _surfaceHeight = height;
        _surfaceScaling = scaling;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        base.OnPointerPressed(e);

        if (_session is null)
        {
            return;
        }

        Focus();

        GumpPoint at = ToGump(e.GetPosition(this));

        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            SelectForContextMenu(at);

            // Deliberately not handled and not captured: Avalonia opens the
            // context menu itself, and a captured pointer would leave the menu
            // unable to take the release.
            return;
        }

        _session.Canvas.PointerPressed(at, ToModifiers(e.KeyModifiers));

        e.Pointer.Capture(this);
        e.Handled = true;
    }

    /// <summary>
    /// Makes the right-clicked element the one the context menu will act on.
    /// </summary>
    /// <remarks>
    /// Right-clicking inside an existing multiple selection keeps it, which is
    /// what every editor does: otherwise "bring these four to the front" would
    /// collapse to one element the moment you reached for the menu.
    /// </remarks>
    private void SelectForContextMenu(GumpPoint at)
    {
        Element? hit = _session!.Canvas.HitTest(at);

        if (hit is null)
        {
            _session.Canvas.Select(null);
        }
        else if (!_session.Canvas.Selection.Contains(hit))
        {
            _session.Canvas.Select(hit);
        }

        // Whatever was right-clicked is what an alignment lines the rest up on,
        // even when it was already part of the selection.
        _session.Canvas.Anchor = hit;

        InvalidateVisual();
        InteractionChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        base.OnPointerMoved(e);

        if (_session is null)
        {
            return;
        }

        GumpPoint at = ToGump(e.GetPosition(this));

        if (_session.Canvas.IsDragging)
        {
            _session.Canvas.PointerMoved(at);
            e.Handled = true;

            return;
        }

        UpdateCursor(at);
    }

    /// <summary>
    /// Shows what a press here would do.
    /// </summary>
    /// <remarks>
    /// Without this the resize handles are invisible to the hand: there is no way
    /// to tell a grab point from the element body until you have already dragged
    /// something. The mapping asks the same
    /// <c>HandleGeometry</c> the press handler will use, so the cursor cannot
    /// disagree with what actually happens.
    /// </remarks>
    private void UpdateCursor(GumpPoint at)
    {
        DragMode mode = DragMode.None;

        // Handles on the current selection win, matching the press handler.
        foreach (Element selected in _session!.Canvas.Selection)
        {
            if (!selected.IsResizable)
            {
                continue;
            }

            DragMode handle = HandleGeometry.HitTest(
                selected.GetAbsoluteBounds(), at, resizable: true, _session.Canvas.HandleSize);

            if (HandleGeometry.IsResize(handle))
            {
                mode = handle;

                break;
            }
        }

        if (mode == DragMode.None && _session.Canvas.HitTest(at) is not null)
        {
            mode = DragMode.Move;
        }

        if (mode == _cursorMode)
        {
            return;
        }

        _cursorMode = mode;
        Cursor = CursorFor(mode);
    }

    /// <summary>
    /// The pointer shape for a gesture, from a fixed set.
    /// </summary>
    /// <remarks>
    /// Shared instances, and only assigned when the mode actually changes. A
    /// <see cref="Cursor"/> owns a platform handle, and this used to construct
    /// a fresh one on every pointer-move event and never dispose it.
    /// </remarks>
    private static Cursor CursorFor(DragMode mode) => mode switch
    {
        DragMode.ResizeLeft or DragMode.ResizeRight => WestEast,
        DragMode.ResizeTop or DragMode.ResizeBottom => NorthSouth,

        // Avalonia names the diagonals after the corner pair they span.
        DragMode.ResizeTopLeft or DragMode.ResizeBottomRight => TopLeftCorner,
        DragMode.ResizeTopRight or DragMode.ResizeBottomLeft => TopRightCorner,

        DragMode.Move => SizeAll,
        _ => Cursor.Default,
    };

    private static readonly Cursor WestEast = new(StandardCursorType.SizeWestEast);
    private static readonly Cursor NorthSouth = new(StandardCursorType.SizeNorthSouth);
    private static readonly Cursor TopLeftCorner = new(StandardCursorType.TopLeftCorner);
    private static readonly Cursor TopRightCorner = new(StandardCursorType.TopRightCorner);
    private static readonly Cursor SizeAll = new(StandardCursorType.SizeAll);

    // What CursorFor was last asked for, so an unchanged mode costs nothing.
    private DragMode _cursorMode = DragMode.None;

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);

        // Leave the pointer as the caller found it once it is off the canvas.
        _cursorMode = DragMode.None;
        Cursor = Cursor.Default;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        base.OnPointerReleased(e);

        if (_session is null)
        {
            return;
        }

        // A right-press started no gesture and captured nothing. Marking its
        // release handled would swallow the context-menu request Avalonia raises
        // from it, which is why the menu never appeared.
        if (e.InitialPressMouseButton == MouseButton.Right)
        {
            return;
        }

        GumpPoint at = ToGump(e.GetPosition(this));

        _session.Canvas.PointerReleased(at);

        // The selection has just changed, so what a press would do here has too.
        UpdateCursor(at);

        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        base.OnKeyDown(e);

        if (_session is null)
        {
            return;
        }

        // Shift nudges by a larger step, which is the usual convention and
        // replaces the original's unused acceleration setting.
        int step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;

        // Delete and Ctrl+A are deliberately absent: the window binds both, and
        // an Avalonia window KeyBinding fires even over a key an inner control
        // has marked handled, so having them here too ran each twice. What
        // remains is what only the canvas offers.
        switch (e.Key)
        {
            case Key.Left: _session.Canvas.Nudge(-step, 0); break;
            case Key.Right: _session.Canvas.Nudge(step, 0); break;
            case Key.Up: _session.Canvas.Nudge(0, -step); break;
            case Key.Down: _session.Canvas.Nudge(0, step); break;
            case Key.Escape: _session.Canvas.CancelGesture(); break;

            default:
                return;
        }

        e.Handled = true;

        InvalidateVisual();
        InteractionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Releases the off-screen surface.</summary>
    public void Dispose()
    {
        _surface?.Dispose();
        _surface = null;
    }

    /// <summary>
    /// Maps a pointer position to gump coordinates.
    /// </summary>
    /// <remarks>
    /// The floor is deliberate: a gump coordinate names a pixel, and a press
    /// anywhere within that pixel belongs to it.
    /// </remarks>
    private GumpPoint ToGump(Point position) => new(
        (int)Math.Floor(position.X / _zoom),
        (int)Math.Floor(position.Y / _zoom));

    private static InputModifiers ToModifiers(KeyModifiers modifiers) =>
        modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Shift)
            ? InputModifiers.Extend
            : InputModifiers.None;

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        InvalidateVisual();
        InteractionChanged?.Invoke(this, EventArgs.Empty);
    }
}
