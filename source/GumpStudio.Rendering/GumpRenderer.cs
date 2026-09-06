using GumpStudio.Core.Document;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Geometry;
using GumpStudio.Core.Primitives;

using SkiaSharp;

namespace GumpStudio.Rendering;

/// <summary>
/// How much decoration the renderer draws around the gump itself.
/// </summary>
/// <remarks>
/// A record class, not a struct: defaulted primary-constructor parameters on a
/// struct are skipped by <c>default</c>, so an omitted argument would silently
/// mean "draw nothing extra" rather than the stated defaults.
/// </remarks>
public sealed record RenderOptions
{
    /// <summary>Draw selection outlines and resize handles.</summary>
    public bool DrawSelection { get; init; } = true;

    /// <summary>Outline groups, which have no art of their own.</summary>
    public bool DrawGroupOutlines { get; init; } = true;

    /// <summary>Canvas fill, or null to leave it transparent.</summary>
    public SKColor? BackgroundColor { get; init; }

    /// <summary>
    /// Draw page 0 beneath the active page.
    /// </summary>
    /// <remarks>
    /// In Ultima Online page 0 is the always-visible layer: whatever it contains
    /// stays on screen while the player switches between pages 1, 2 and so on.
    /// Showing it while editing another page is the only way to see what the
    /// finished gump will actually look like. The original defaulted this on and
    /// offered a menu toggle; so does this.
    /// </remarks>
    public bool ShowSharedPage { get; init; } = true;

    /// <summary>Plain output with no editor decoration, for export and golden images.</summary>
    public static RenderOptions Plain { get; } = new() { DrawSelection = false, DrawGroupOutlines = false };
}

/// <summary>
/// Draws a gump page onto a Skia canvas.
/// </summary>
/// <remarks>
/// Headless by construction: it takes a canvas and an art source and touches no
/// UI type, which is what makes golden-image tests possible. The old renderer
/// lived inside the designer form and could only run inside a running WinForms
/// application.
/// </remarks>
public sealed class GumpRenderer(IGumpArtSource art)
{
    private readonly IGumpArtSource _art = art ?? throw new ArgumentNullException(nameof(art));

    /// <summary>Draws a whole page.</summary>
    public void Render(SKCanvas canvas, GumpPage page, RenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(page);

        options ??= new RenderOptions();

        if (options.BackgroundColor is { } background)
        {
            canvas.Clear(background);
        }

        ElementPainter painter = new(canvas, _art, options);

        foreach (Element child in page.Root.Children)
        {
            painter.Paint(child);
        }

        if (options.DrawSelection)
        {
            foreach (Element element in page.Descendants().Where(e => e.IsSelected))
            {
                DrawSelection(canvas, element);
            }
        }
    }

    /// <summary>
    /// Draws a document's active page, with page 0 beneath it.
    /// </summary>
    /// <remarks>
    /// Prefer this over <see cref="Render(SKCanvas, GumpPage, RenderOptions)"/>
    /// whenever a document is in hand: page 0 being always-visible is a rule of
    /// the format, not an editor preference, and encoding it once here keeps the
    /// canvas, the CLI and any future preview honest about it.
    /// </remarks>
    public void RenderDocument(
        SKCanvas canvas,
        GumpDocument document,
        int activePageIndex,
        RenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(document);

        options ??= new RenderOptions();

        int active = Math.Clamp(activePageIndex, 0, document.PageCount - 1);

        if (options.ShowSharedPage && active != 0)
        {
            // Drawn without selection decoration: it is context, not what the
            // user is editing, and its elements are not selectable from here.
            Render(canvas, document.Pages[0], options with { DrawSelection = false });
        }

        Render(canvas, document.Pages[active], options);
    }

    /// <summary>Measures art-derived sizes on every page the canvas will draw.</summary>
    public void MeasureDocument(GumpDocument document, int activePageIndex)
    {
        ArgumentNullException.ThrowIfNull(document);

        int active = Math.Clamp(activePageIndex, 0, document.PageCount - 1);

        MeasureContentSizes(document.Pages[0]);

        if (active != 0)
        {
            MeasureContentSizes(document.Pages[active]);
        }
    }

    /// <summary>Renders a page into a new bitmap of the given size.</summary>
    public SKBitmap RenderToBitmap(GumpPage page, int width, int height, RenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        SKBitmap? bitmap = new(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));

        try
        {
            using (SKCanvas canvas = new(bitmap))
            {
                canvas.Clear(SKColors.Transparent);

                Render(canvas, page, options);
                canvas.Flush();
            }

            // Ownership passes to the caller, so the finally must not dispose it.
            SKBitmap result = bitmap;
            bitmap = null;

            return result;
        }
        finally
        {
            bitmap?.Dispose();
        }
    }

    /// <summary>
    /// Updates the sizes of elements whose extent comes from their art.
    /// </summary>
    /// <remarks>
    /// Images, items and labels have no intrinsic size until their art is
    /// decoded. The editor calls this after loading a document so hit testing and
    /// group bounds are correct before anything is drawn.
    /// </remarks>
    public void MeasureContentSizes(GumpPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        foreach (Element element in page.Descendants())
        {
            switch (element)
            {
                case ImageElement image when _art.GetGump(image.GumpId) is { } art:
                    image.SetContentSize(art.Width, art.Height);
                    break;

                case ItemElement item when _art.GetItem(item.ItemId) is { } art:
                    item.SetContentSize(art.Width, art.Height);
                    break;

                case LabelElement label when _art.GetText(label.FontIndex, label.Text) is { } art:
                    label.SetContentSize(art.Width, art.Height);
                    break;

                case ButtonElement button when _art.GetGump(button.NormalId) is { } art:
                    button.SetContentSize(art.Width, art.Height);
                    break;

                case CheckboxElement checkbox
                    when _art.GetGump(checkbox.IsChecked ? checkbox.CheckedId : checkbox.UncheckedId) is { } art:
                    checkbox.SetContentSize(art.Width, art.Height);
                    break;

                default:
                    break;
            }
        }
    }

    private static void DrawSelection(SKCanvas canvas, Element element)
    {
        GumpRect bounds = element.GetAbsoluteBounds();
        SKRect rect = SKRect.Create(bounds.X, bounds.Y, Math.Max(1, bounds.Width), Math.Max(1, bounds.Height));

        using SKPaint outline = new()
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            Color = new SKColor(0x33, 0x99, 0xFF),
            IsAntialias = false,
        };

        canvas.DrawRect(rect, outline);

        if (!element.IsResizable)
        {
            return;
        }

        using SKPaint fill = new() { Style = SKPaintStyle.Fill, Color = SKColors.White };

        foreach (DragMode handle in HandleGeometry.ResizeHandles)
        {
            GumpRect box = HandleGeometry.GetHandleRect(bounds, handle);
            SKRect handleRect = SKRect.Create(box.X, box.Y, box.Width, box.Height);

            canvas.DrawRect(handleRect, fill);
            canvas.DrawRect(handleRect, outline);
        }
    }
}
