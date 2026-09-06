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

    public GumpCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
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

    /// <summary>Raised after a gesture changes the selection or geometry.</summary>
    public event EventHandler? InteractionChanged;

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        base.Render(context);

        int width = Math.Max(1, (int)Bounds.Width);
        int height = Math.Max(1, (int)Bounds.Height);

        context.FillRectangle(new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2E)), Bounds);

        if (_session?.Art is not { } art)
        {
            return;
        }

        EnsureSurface(width, height);

        if (_surface is null)
        {
            return;
        }

        using (ILockedFramebuffer buffer = _surface.Lock())
        {
            SKImageInfo info = new(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);

            using SKSurface skia = SKSurface.Create(info, buffer.Address, buffer.RowBytes);

            skia.Canvas.Clear(SKColors.Transparent);

            new GumpRenderer(art).RenderDocument(
                skia.Canvas,
                _session.Document,
                _session.ActivePageIndex,
                new GumpRenderOptions
                {
                    DrawSelection = true,
                    DrawGroupOutlines = true,
                    ShowSharedPage = ShowSharedPage,
                    Grid = _session.Canvas.Grid,
                });

            DrawMarquee(skia.Canvas);

            skia.Canvas.Flush();
        }

        context.DrawImage(_surface, new Rect(0, 0, width, height));
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

    private void EnsureSurface(int width, int height)
    {
        if (_surface is not null && _surfaceWidth == width && _surfaceHeight == height)
        {
            return;
        }

        _surface?.Dispose();

        _surface = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        _surfaceWidth = width;
        _surfaceHeight = height;
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

            DragMode handle = HandleGeometry.HitTest(selected.GetAbsoluteBounds(), at, resizable: true);

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

        Cursor = CursorFor(mode);
    }

    private static Cursor CursorFor(DragMode mode) => new(mode switch
    {
        DragMode.ResizeLeft or DragMode.ResizeRight => StandardCursorType.SizeWestEast,
        DragMode.ResizeTop or DragMode.ResizeBottom => StandardCursorType.SizeNorthSouth,

        // Avalonia names the diagonals after the corner pair they span.
        DragMode.ResizeTopLeft or DragMode.ResizeBottomRight => StandardCursorType.TopLeftCorner,
        DragMode.ResizeTopRight or DragMode.ResizeBottomLeft => StandardCursorType.TopRightCorner,

        DragMode.Move => StandardCursorType.SizeAll,
        _ => StandardCursorType.Arrow,
    });

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);

        // Leave the pointer as the caller found it once it is off the canvas.
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

        switch (e.Key)
        {
            case Key.Left: _session.Canvas.Nudge(-step, 0); break;
            case Key.Right: _session.Canvas.Nudge(step, 0); break;
            case Key.Up: _session.Canvas.Nudge(0, -step); break;
            case Key.Down: _session.Canvas.Nudge(0, step); break;
            case Key.Delete: _session.Canvas.DeleteSelection(); break;
            case Key.Escape: _session.Canvas.CancelGesture(); break;

            case Key.A when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                _session.Canvas.SelectAll();
                break;

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

    private static GumpPoint ToGump(Point position) => new((int)position.X, (int)position.Y);

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
