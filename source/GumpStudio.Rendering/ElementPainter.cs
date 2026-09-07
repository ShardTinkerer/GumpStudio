using System.Globalization;
using System.Text;

using GumpStudio.Core.Elements;
using GumpStudio.Core.Primitives;

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
    /// Falls back to <c>#id</c> when no client is loaded or the id is not in the
    /// cliloc table, which is what the editor showed for every localised area
    /// before this: readable only to someone who had the id memorised.
    /// </remarks>
    private string Localized(HtmlElement element)
    {
        if (art.GetCliloc(element.ClilocId) is not { } text)
        {
            return $"#{element.ClilocId}";
        }

        return element.Arguments.Length > 0
            ? Substitute(text, element.Arguments)
            : text;
    }

    /// <summary>
    /// Fills a cliloc's <c>~1_THING~</c> placeholders from the argument list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client numbers them from one and separates the values with <c>@</c>,
    /// running consecutive delimiters together. A placeholder with no argument is
    /// left as it stands rather than blanked, so a missing value is visible
    /// instead of silently disappearing.
    /// </para>
    /// <para>
    /// An argument of the form <c>#1234</c> is itself a cliloc id — the
    /// <c>xmfhtmltok</c> form uses that to nest one localised string inside
    /// another — so it is resolved in turn, once, without recursing.
    /// </para>
    /// </remarks>
    private string Substitute(string text, string arguments)
    {
        // Empty tokens are dropped, because the client runs consecutive
        // delimiters together the way strtok does: `@@#1072325` names one
        // argument, not an empty one followed by a real one.
        string[] values = arguments.Split('@', StringSplitOptions.RemoveEmptyEntries);
        StringBuilder built = new(text.Length);

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '~')
            {
                built.Append(text[i]);

                continue;
            }

            int close = text.IndexOf('~', i + 1);

            if (close < 0)
            {
                built.Append(text[i..]);

                break;
            }

            string placeholder = text[(i + 1)..close];
            int underscore = placeholder.IndexOf('_', StringComparison.Ordinal);
            string ordinal = underscore < 0 ? placeholder : placeholder[..underscore];

            built.Append(
                int.TryParse(ordinal, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
                && index >= 1
                && index <= values.Length
                    ? Value(values[index - 1])
                    : text[i..(close + 1)]);

            i = close;
        }

        return built.ToString();
    }

    /// <summary>One substitution value, resolving a nested cliloc reference.</summary>
    private string Value(string argument) =>
        argument.StartsWith('#')
        && int.TryParse(
            argument[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int nested)
            ? art.GetCliloc(nested) ?? argument
            : argument;

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
