using GumpStudio.Core.Elements;
using GumpStudio.Core.Primitives;
using GumpStudio.Uo.Data;

using SkiaSharp;

namespace GumpStudio.Rendering;

/// <summary>
/// Paints one element onto a canvas.
/// </summary>
/// <remarks>
/// A visitor, so adding an element type breaks the build here rather than
/// silently rendering nothing — which is what a <c>switch</c> with a default arm
/// would do, and what the original's per-exporter switches did.
/// </remarks>
internal sealed class ElementPainter(SKCanvas canvas, IGumpArtSource art, RenderOptions options)
    : IElementVisitor, IDisposable
{
    // Held for the life of one page render rather than allocated per element
    // per frame. An SKPaint wraps a native object, and a page of fifty elements
    // was creating and destroying one for each of them on every repaint.
    private SKPaint? _alphaWash;
    private SKPaint? _textEntryWash;
    private SKPaint? _missing;
    private SKPaint? _outline;

    // Reused across every nine-sliced element on the page.
    private readonly SKImage?[] _pieces = new SKImage?[NineSlice.PieceCount];

    private SKPaint AlphaWash => _alphaWash ??= new SKPaint
    {
        Style = SKPaintStyle.Fill,
        Color = new SKColor(0, 0, 0, 0x80),
    };

    private SKPaint TextEntryWash => _textEntryWash ??= new SKPaint
    {
        Style = SKPaintStyle.Fill,
        Color = new SKColor(0xFF, 0xFF, 0x00, 50),
    };

    private SKPaint Missing => _missing ??= new SKPaint
    {
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 1,
        Color = new SKColor(0xC0, 0x40, 0x40, 0xC0),
    };

    /// <summary>
    /// The outline stroke, whose colour the caller sets.
    /// </summary>
    /// <remarks>
    /// Shared and mutated, which is safe because a painter belongs to a single
    /// page render on one thread and every use draws before the next changes it.
    /// </remarks>
    private SKPaint Outline => _outline ??= new SKPaint
    {
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 1,
    };

    public void Dispose()
    {
        _alphaWash?.Dispose();
        _textEntryWash?.Dispose();
        _missing?.Dispose();
        _outline?.Dispose();
    }

    /// <summary>Draws an element and, for a group, everything inside it.</summary>
    public void Paint(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);

        element.Accept(this);
    }

    public void Visit(GroupElement element)
    {
        // Children are positioned relative to the group, and a throwing child
        // must not leave the transform shifted for everything after it.
        int saved = canvas.Save();

        canvas.Translate(element.X, element.Y);

        try
        {
            foreach (Element child in element.Children)
            {
                child.Accept(this);
            }
        }
        finally
        {
            canvas.RestoreToCount(saved);
        }

        if (options.DrawGroupOutlines && !element.IsPageRoot)
        {
            DrawOutline(element.Bounds, new SKColor(0x80, 0x80, 0x80, 0x60));
        }
    }

    public void Visit(AlphaElement element)
    {
        // An alpha region darkens what is behind it; the client renders a
        // checkerboard stipple, but a flat wash reads better while editing.
        canvas.DrawRect(ToRect(element.Bounds), AlphaWash);
    }

    public void Visit(BackgroundElement element)
    {
        for (int i = 0; i < _pieces.Length; i++)
        {
            _pieces[i] = art.GetGump(element.GumpId + i);
        }

        NineSlice.Draw(canvas, _pieces, ToRect(element.Bounds));
    }

    public void Visit(TiledElement element)
    {
        if (art.GetGump(element.GumpId, element.Hue) is not { } image)
        {
            DrawMissing(element.Bounds);

            return;
        }

        SKRect bounds = ToRect(element.Bounds);
        int saved = canvas.Save();

        canvas.ClipRect(bounds);

        for (float y = bounds.Top; y < bounds.Bottom; y += image.Height)
        {
            for (float x = bounds.Left; x < bounds.Right; x += image.Width)
            {
                canvas.DrawImage(image, x, y, PixelArt.Sampling);
            }
        }

        canvas.RestoreToCount(saved);
    }

    public void Visit(ImageElement element) =>
        DrawArt(art.GetGump(element.GumpId, element.Hue, element.PartialHue), element);

    public void Visit(ItemElement element) =>
        DrawArt(art.GetItem(element.ItemId, element.Hue, partialHue: true), element);

    public void Visit(PicInPicElement element)
    {
        if (art.GetGump(element.GumpId, element.Hue, element.PartialHue) is not { } image)
        {
            DrawMissing(element.Bounds);

            return;
        }

        // The source region is cut out by clipping to the element and drawing the
        // whole image shifted back by the region's origin, which needs no
        // intermediate surface and keeps the nearest-neighbour sampling.
        int saved = canvas.Save();

        canvas.ClipRect(ToRect(element.Bounds));
        canvas.DrawImage(
            image,
            element.X - element.SourceX,
            element.Y - element.SourceY,
            PixelArt.Sampling);

        canvas.RestoreToCount(saved);
    }

    public void Visit(TileAsGumpElement element) =>
        DrawArt(art.GetItem(element.ItemId, hue: 0, partialHue: true), element);

    public void Visit(ButtonElement element)
    {
        DrawArt(
            art.GetGump(element.State == ButtonState.Pressed ? element.PressedId : element.NormalId),
            element);

        if (element.TileId == 0
            || art.GetItem(element.TileId, element.TileHue, partialHue: true) is not { } tile)
        {
            return;
        }

        // The overlay's offset is relative to the button's own origin, and it is
        // not clipped to the button: oversized art deliberately spills out.
        canvas.DrawImage(
            tile,
            element.X + element.TileX,
            element.Y + element.TileY,
            PixelArt.Sampling);
    }

    public void Visit(CheckboxElement element) =>
        DrawArt(art.GetGump(element.IsChecked ? element.CheckedId : element.UncheckedId), element);

    public void Visit(RadioElement element) =>
        DrawArt(art.GetGump(element.IsChecked ? element.CheckedId : element.UncheckedId), element);

    public void Visit(LabelElement element)
    {
        if (art.GetText(element.FontIndex, element.Text, element.Hue, element.FontFamily)
            is not { } text)
        {
            return;
        }

        int saved = canvas.Save();

        if (element.Cropped)
        {
            canvas.ClipRect(ToRect(element.Bounds));
        }

        canvas.DrawImage(text, element.X, element.Y, PixelArt.Sampling);
        canvas.RestoreToCount(saved);
    }

    public void Visit(TextEntryElement element)
    {
        // The client draws no frame for a text entry, so the editor has to show
        // the field's extent some other way or an empty one is invisible. A
        // translucent yellow wash over the bounds is what the original used, and
        // it reads as "the player can type here" at a glance.
        {
            canvas.DrawRect(ToRect(element.Bounds), TextEntryWash);
        }

        if (art.GetText(element.FontIndex, element.InitialText, element.Hue, element.FontFamily)
            is not { } text)
        {
            return;
        }

        int saved = canvas.Save();

        canvas.ClipRect(ToRect(element.Bounds));
        canvas.DrawImage(text, element.X, element.Y, PixelArt.Sampling);
        canvas.RestoreToCount(saved);
    }

    public void Visit(HtmlElement element)
    {
        if (element.ShowBackground)
        {
            // The client frames an HTML area with gump 3000 and its neighbours.
            for (int i = 0; i < _pieces.Length; i++)
            {
                _pieces[i] = art.GetGump(3000 + i);
            }

            NineSlice.Draw(canvas, _pieces, ToRect(element.Bounds));
        }
        else
        {
            DrawOutline(element.Bounds, new SKColor(0x60, 0x60, 0x60, 0xC0));
        }

        // Markup is not interpreted; the editor shows the source text so the
        // author can see what will be sent. A cliloc is resolved, because its id
        // says nothing at all about what the player will read — and a gump
        // captured off the wire is often built from nothing else.
        string preview = element.ContentKind == HtmlContentKind.Localized
            ? Localized(element)
            : element.Html;

        if (art.GetText(element.FontIndex, preview, hue: 0, element.FontFamily) is not { } text)
        {
            return;
        }

        // The colour slot is an RGB value, not a hue, so it is applied here
        // rather than by the font renderer. Glyphs are a white mask, so replacing
        // their colour while keeping their coverage tints them exactly.
        SKColor? colour = element.ContentKind == HtmlContentKind.Localized
            ? GumpColor.ToSkColor(element.Color)
            : null;

        using SKPaint? tint = colour is { } rgb
            ? new SKPaint { ColorFilter = SKColorFilter.CreateBlendMode(rgb, SKBlendMode.SrcIn) }
            : null;

        int saved = canvas.Save();

        canvas.ClipRect(ToRect(element.Bounds));
        canvas.DrawImage(text, new SKPoint(element.X, element.Y), PixelArt.Sampling, tint);
        canvas.RestoreToCount(saved);
    }

    /// <summary>
    /// The text a localised area will actually show.
    /// </summary>
    /// <remarks>
    /// The substitution rules live in <see cref="ClilocFormatter"/> rather than
    /// here, because the properties editor previews the same string on hover and
    /// the two must not be able to disagree.
    /// </remarks>
    private string Localized(HtmlElement element) =>
        ClilocFormatter.Format(element.ClilocId, element.Arguments, art.GetCliloc);

    private void DrawArt(SKImage? image, Element element)
    {
        if (image is null)
        {
            DrawMissing(element.Bounds);

            return;
        }

        canvas.DrawImage(image, element.X, element.Y, PixelArt.Sampling);
    }

    /// <summary>Marks art the client does not have, rather than drawing nothing.</summary>
    private void DrawMissing(GumpRect bounds)
    {
        if (bounds.IsEmpty)
        {
            return;
        }

        SKPaint paint = Missing;
        SKRect rect = ToRect(bounds);

        canvas.DrawRect(rect, paint);
        canvas.DrawLine(rect.Left, rect.Top, rect.Right, rect.Bottom, paint);
        canvas.DrawLine(rect.Right, rect.Top, rect.Left, rect.Bottom, paint);
    }

    private void DrawOutline(GumpRect bounds, SKColor color)
    {
        if (bounds.IsEmpty)
        {
            return;
        }

        SKPaint paint = Outline;
        paint.Color = color;

        canvas.DrawRect(ToRect(bounds), paint);
    }

    private static SKRect ToRect(GumpRect bounds) =>
        SKRect.Create(bounds.X, bounds.Y, bounds.Width, bounds.Height);
}
