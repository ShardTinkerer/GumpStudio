using SkiaSharp;

namespace GumpStudio.Rendering;

/// <summary>
/// Draws a UO resize-pic: a frame built from nine consecutive gump images.
/// </summary>
/// <remarks>
/// <para>
/// The nine images, in id order from the element's base id, are the top-left,
/// top, top-right, left, centre, right, bottom-left, bottom and bottom-right
/// pieces. Corners are drawn once at fixed positions; the four edges tile along
/// their axis; the centre tiles both ways. Every tiled region is clipped to its
/// band so a piece that does not divide evenly is cut off rather than
/// overhanging.
/// </para>
/// <para>
/// Written from those semantics rather than transcribed from the original, whose
/// decompiled implementation was unreadable sign-XOR loop residue that assigned
/// a clip region and then disposed it while it was still active.
/// </para>
/// </remarks>
public static class NineSlice
{
    /// <summary>Number of images a resize-pic is built from.</summary>
    public const int PieceCount = 9;

    private const int TopLeft = 0;
    private const int Top = 1;
    private const int TopRight = 2;
    private const int Left = 3;
    private const int Center = 4;
    private const int Right = 5;
    private const int BottomLeft = 6;
    private const int Bottom = 7;
    private const int BottomRight = 8;

    /// <summary>
    /// Draws the frame into <paramref name="destination"/>.
    /// </summary>
    /// <param name="canvas">Target canvas.</param>
    /// <param name="pieces">The nine images, in id order. Missing pieces are skipped.</param>
    /// <param name="destination">Where to draw, in canvas coordinates.</param>
    /// <param name="paint">Optional paint, for example to apply opacity.</param>
    public static void Draw(SKCanvas canvas, IReadOnlyList<SKImage?> pieces, SKRect destination, SKPaint? paint = null)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(pieces);

        if (pieces.Count < PieceCount || destination.Width <= 0 || destination.Height <= 0)
        {
            return;
        }

        // Corner sizes set the frame's margins. Taking them from the corner
        // images rather than assuming a square border is what lets asymmetric
        // frames render correctly.
        float leftWidth = pieces[TopLeft]?.Width ?? pieces[Left]?.Width ?? 0;
        float rightWidth = pieces[TopRight]?.Width ?? pieces[Right]?.Width ?? 0;
        float topHeight = pieces[TopLeft]?.Height ?? pieces[Top]?.Height ?? 0;
        float bottomHeight = pieces[BottomLeft]?.Height ?? pieces[Bottom]?.Height ?? 0;

        // A frame smaller than its own borders would produce negative bands.
        leftWidth = Math.Min(leftWidth, destination.Width);
        rightWidth = Math.Min(rightWidth, destination.Width - leftWidth);
        topHeight = Math.Min(topHeight, destination.Height);
        bottomHeight = Math.Min(bottomHeight, destination.Height - topHeight);

        float innerLeft = destination.Left + leftWidth;
        float innerTop = destination.Top + topHeight;
        float innerRight = destination.Right - rightWidth;
        float innerBottom = destination.Bottom - bottomHeight;

        int saved = canvas.Save();
        canvas.ClipRect(destination);

        // Edges and centre first, so corners sit on top of any overhang.
        Tile(canvas, pieces[Top], SKRect.Create(innerLeft, destination.Top, innerRight - innerLeft, topHeight), paint);
        Tile(canvas, pieces[Bottom], SKRect.Create(innerLeft, innerBottom, innerRight - innerLeft, bottomHeight), paint);
        Tile(canvas, pieces[Left], SKRect.Create(destination.Left, innerTop, leftWidth, innerBottom - innerTop), paint);
        Tile(canvas, pieces[Right], SKRect.Create(innerRight, innerTop, rightWidth, innerBottom - innerTop), paint);
        Tile(canvas, pieces[Center], SKRect.Create(innerLeft, innerTop, innerRight - innerLeft, innerBottom - innerTop), paint);

        DrawCorner(canvas, pieces[TopLeft], destination.Left, destination.Top, paint);
        DrawCorner(canvas, pieces[TopRight], innerRight, destination.Top, paint);
        DrawCorner(canvas, pieces[BottomLeft], destination.Left, innerBottom, paint);
        DrawCorner(canvas, pieces[BottomRight], innerRight, innerBottom, paint);

        canvas.RestoreToCount(saved);
    }

    private static void DrawCorner(SKCanvas canvas, SKImage? image, float x, float y, SKPaint? paint)
    {
        if (image is not null)
        {
            canvas.DrawImage(image, x, y, PixelArt.Sampling, paint);
        }
    }

    /// <summary>Repeats an image across a band, clipped to it.</summary>
    private static void Tile(SKCanvas canvas, SKImage? image, SKRect band, SKPaint? paint)
    {
        if (image is null || band.Width <= 0 || band.Height <= 0 || image.Width <= 0 || image.Height <= 0)
        {
            return;
        }

        int saved = canvas.Save();
        canvas.ClipRect(band);

        for (float y = band.Top; y < band.Bottom; y += image.Height)
        {
            for (float x = band.Left; x < band.Right; x += image.Width)
            {
                canvas.DrawImage(image, x, y, PixelArt.Sampling, paint);
            }
        }

        canvas.RestoreToCount(saved);
    }
}
