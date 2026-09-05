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
    : IElementVisitor
{
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
        using SKPaint paint = new()
        {
            Style = SKPaintStyle.Fill,
            Color = new SKColor(0, 0, 0, 0x80),
        };

        canvas.DrawRect(ToRect(element.Bounds), paint);
    }

    public void Visit(BackgroundElement element)
    {
        SKImage?[] pieces = new SKImage?[NineSlice.PieceCount];

        for (int i = 0; i < pieces.Length; i++)
        {
            pieces[i] = art.GetGump(element.GumpId + i);
        }

        NineSlice.Draw(canvas, pieces, ToRect(element.Bounds));
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
        DrawArt(art.GetGump(element.GumpId, element.Hue), element);

    public void Visit(ItemElement element) =>
        DrawArt(art.GetItem(element.ItemId, element.Hue, partialHue: true), element);

    public void Visit(ButtonElement element) =>
        DrawArt(
            art.GetGump(element.State == ButtonState.Pressed ? element.PressedId : element.NormalId),
            element);

    public void Visit(CheckboxElement element) =>
        DrawArt(art.GetGump(element.IsChecked ? element.CheckedId : element.UncheckedId), element);

    public void Visit(RadioElement element) =>
        DrawArt(art.GetGump(element.IsChecked ? element.CheckedId : element.UncheckedId), element);

    public void Visit(LabelElement element)
    {
        if (art.GetText(element.FontIndex, element.Text, element.Hue) is not { } text)
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
        // The client draws no frame for a text entry; the editor needs to show
        // one or the field would be invisible until it has text.
        DrawOutline(element.Bounds, new SKColor(0x60, 0x60, 0x60, 0xC0));

        if (art.GetText(0, element.InitialText, element.Hue) is not { } text)
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
            SKImage?[] pieces = new SKImage?[NineSlice.PieceCount];

            for (int i = 0; i < pieces.Length; i++)
            {
                pieces[i] = art.GetGump(3000 + i);
            }

            NineSlice.Draw(canvas, pieces, ToRect(element.Bounds));
        }
        else
        {
            DrawOutline(element.Bounds, new SKColor(0x60, 0x60, 0x60, 0xC0));
        }

        // Markup and cliloc substitution are not interpreted; the editor shows
        // the source text so the author can see what will be sent.
        string preview = element.ContentKind == HtmlContentKind.Localized
            ? $"#{element.ClilocId}"
            : element.Html;

        if (art.GetText(0, preview) is not { } text)
        {
            return;
        }

        int saved = canvas.Save();

        canvas.ClipRect(ToRect(element.Bounds));
        canvas.DrawImage(text, element.X, element.Y, PixelArt.Sampling);
        canvas.RestoreToCount(saved);
    }

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

        using SKPaint paint = new()
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            Color = new SKColor(0xC0, 0x40, 0x40, 0xC0),
        };

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

        using SKPaint paint = new()
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            Color = color,
        };

        canvas.DrawRect(ToRect(bounds), paint);
    }

    private static SKRect ToRect(GumpRect bounds) =>
        SKRect.Create(bounds.X, bounds.Y, bounds.Width, bounds.Height);
}
