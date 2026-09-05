using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using GumpStudio.Core.Editing;
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

            new GumpRenderer(art).Render(
                skia.Canvas,
                _session.ActivePage,
                new GumpRenderOptions { DrawSelection = true, DrawGroupOutlines = true });

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

        _session.Canvas.PointerPressed(ToGump(e.GetPosition(this)), ToModifiers(e.KeyModifiers));

        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        base.OnPointerMoved(e);

        if (_session?.Canvas.IsDragging == true)
        {
            _session.Canvas.PointerMoved(ToGump(e.GetPosition(this)));
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        base.OnPointerReleased(e);

        if (_session is null)
        {
            return;
        }

        _session.Canvas.PointerReleased(ToGump(e.GetPosition(this)));

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
