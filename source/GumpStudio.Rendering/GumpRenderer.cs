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

    /// <summary>The design grid to draw, or null for none.</summary>
    public Core.Editing.GridSettings? Grid { get; init; }

    /// <summary>
    /// The side of a resize handle, in gump units.
    /// </summary>
    /// <remarks>
    /// Grows as the canvas zooms out, so a handle keeps a constant size on
    /// screen. It has to match what the interaction controller hit tests with.
    /// </remarks>
    public int HandleSize { get; init; } = Core.Geometry.HandleGeometry.HandleSize;

    /// <summary>
    /// Width of a selection outline, in gump units.
    /// </summary>
    /// <remarks>
    /// Zero asks Skia for a hairline, which is one device pixel whatever the
    /// transform — exactly what a selection outline wants when zoomed in.
    /// </remarks>
    public float OutlineWidth { get; init; } = 1;

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

        DrawGrid(canvas, options);

        using (ElementPainter painter = new(canvas, _art, options))
        {
            foreach (Element child in page.Root.Children)
            {
                painter.Paint(child);
            }
        }

        if (options.DrawSelection)
        {
            DrawSelectionOf(canvas, page.Root, options);
        }
    }

    /// <summary>
    /// Draws the decoration for every selected element in a subtree.
    /// </summary>
    /// <remarks>
    /// A plain recursive walk rather than <c>Descendants().Where(...)</c>: this
    /// runs on every repaint, and the LINQ form built an iterator chain and a
    /// closure over the whole tree to find the one or two elements that are
    /// usually selected.
    /// </remarks>
    private static void DrawSelectionOf(SKCanvas canvas, GroupElement group, RenderOptions options)
    {
        foreach (Element child in group.Children)
        {
            if (child.IsSelected)
            {
                DrawSelection(canvas, child, options);
            }

            if (child is GroupElement nested)
            {
                DrawSelectionOf(canvas, nested, options);
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

            // The grid is drawn once, by the page-0 pass above.
            options = options with { Grid = null };
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

    /// <summary>
    /// Draws the design grid behind everything else.
    /// </summary>
    /// <remarks>
    /// Dots at the intersections rather than full lines: a line grid over
    /// low-contrast gump art is hard to see past, while dots stay readable
    /// without competing with the artwork.
    /// </remarks>
    private static void DrawGrid(SKCanvas canvas, RenderOptions options)
    {
        if (options.Grid is not { Visible: true } grid)
        {
            return;
        }

        SKRect bounds = canvas.LocalClipBounds;

        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        // A grid finer than a couple of pixels turns into a solid wash, so stop
        // drawing rather than produce noise.
        const int MinimumVisibleSpacing = 3;

        if (grid.Width < MinimumVisibleSpacing || grid.Height < MinimumVisibleSpacing)
        {
            return;
        }

        // One cell, tiled, rather than a draw call per dot. A 5x5 grid — the
        // default — over the canvas is some thirty thousand one-pixel rectangles
        // per frame, which made the grid by far the most expensive thing on
        // screen and made dragging anything with it on visibly slow. The cell
        // bitmap and its shader are built per frame and thrown away: they cost
        // a hundred-odd bytes and two objects, against the calls they replace.
        using SKBitmap cell = new(grid.Width, grid.Height, SKColorType.Bgra8888, SKAlphaType.Premul);

        cell.Erase(SKColors.Transparent);
        cell.SetPixel(0, 0, new SKColor(0xFF, 0xFF, 0xFF, 0x38));

        using SKShader dots = SKShader.CreateBitmap(cell, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
        using SKPaint paint = new() { Shader = dots };

        canvas.DrawRect(SKRect.Create(0, 0, bounds.Right, bounds.Bottom), paint);
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
                // Gump-backed elements are measured through TryGetGumpSize,
                // which answers from the index where the container allows it and
                // never adds an image to the art cache. Decoding them here used
                // to fill the cache with an unhued copy of art the painter then
                // asked for again, hued — two decodes and two cache slots for
                // one element.
                case ImageElement image when _art.TryGetGumpSize(image.GumpId, out int w, out int h):
                    image.SetContentSize(w, h);
                    break;

                case ItemElement item
                    when _art.GetItem(item.ItemId, item.Hue, partialHue: true) is { } art:
                    item.SetContentSize(art.Width, art.Height);
                    break;

                // A cropped label's rectangle is the user's, not the text's, so
                // measuring it would silently undo every resize.
                //
                // The hue and the font family are passed because the painter
                // passes them: without them an ASCII label was measured with a
                // Unicode face, so its box was the wrong size for the glyphs
                // that would be drawn in it.
                case LabelElement { Cropped: false } label
                    when _art.GetText(label.FontIndex, label.Text, label.Hue, label.FontFamily) is { } art:
                    label.SetContentSize(art.Width, art.Height);
                    break;

                case TileAsGumpElement tile
                    when _art.GetItem(tile.ItemId, hue: 0, partialHue: true) is { } art:
                    tile.SetContentSize(art.Width, art.Height);
                    break;

                // The state the painter will draw, not always the normal face.
                case ButtonElement button
                    when _art.TryGetGumpSize(
                        button.State == ButtonState.Pressed ? button.PressedId : button.NormalId,
                        out int w,
                        out int h):
                    button.SetContentSize(w, h);
                    break;

                case CheckboxElement checkbox
                    when _art.TryGetGumpSize(
                        checkbox.IsChecked ? checkbox.CheckedId : checkbox.UncheckedId,
                        out int w,
                        out int h):
                    checkbox.SetContentSize(w, h);
                    break;

                default:
                    break;
            }
        }
    }

    private static void DrawSelection(SKCanvas canvas, Element element, RenderOptions options)
    {
        GumpRect bounds = element.GetAbsoluteBounds();
        SKRect rect = SKRect.Create(bounds.X, bounds.Y, Math.Max(1, bounds.Width), Math.Max(1, bounds.Height));

        using SKPaint outline = new()
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = options.OutlineWidth,
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
            GumpRect box = HandleGeometry.GetHandleRect(bounds, handle, options.HandleSize);
            SKRect handleRect = SKRect.Create(box.X, box.Y, box.Width, box.Height);

            canvas.DrawRect(handleRect, fill);
            canvas.DrawRect(handleRect, outline);
        }
    }
}
