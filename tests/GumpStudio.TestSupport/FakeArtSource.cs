using SkiaSharp;

namespace GumpStudio.TestSupport;

/// <summary>
/// Synthetic art for renderer tests, so they run with no UO installation.
/// </summary>
/// <remarks>
/// Every image is a flat colour derived from its id, which makes it possible to
/// assert exactly which art landed at which pixel.
/// </remarks>
public sealed class FakeArtSource : IDisposable
{
    private readonly Dictionary<(string Kind, int Id), SKImage> _images = [];
    private readonly List<SKImage> _owned = [];

    /// <summary>Ids requested that had no art, in request order.</summary>
    public List<int> MissingRequests { get; } = [];

    /// <summary>Registers a flat-coloured gump image.</summary>
    public FakeArtSource AddGump(int id, int width, int height, SKColor color) =>
        Add("gump", id, width, height, color);

    /// <summary>Registers a flat-coloured item image.</summary>
    public FakeArtSource AddItem(int id, int width, int height, SKColor color) =>
        Add("item", id, width, height, color);

    /// <summary>Registers the nine consecutive images a resize-pic needs.</summary>
    /// <remarks>
    /// Corners are <paramref name="corner"/> square; edges are that thick and one
    /// pixel along their axis; the centre is a single pixel. Distinct colours make
    /// it possible to prove each piece landed in the right band.
    /// </remarks>
    public FakeArtSource AddNineSlice(int baseId, int corner, SKColor[] colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        ArgumentOutOfRangeException.ThrowIfNotEqual(colors.Length, 9);

        AddGump(baseId + 0, corner, corner, colors[0]);
        AddGump(baseId + 1, 1, corner, colors[1]);
        AddGump(baseId + 2, corner, corner, colors[2]);
        AddGump(baseId + 3, corner, 1, colors[3]);
        AddGump(baseId + 4, 1, 1, colors[4]);
        AddGump(baseId + 5, corner, 1, colors[5]);
        AddGump(baseId + 6, corner, corner, colors[6]);
        AddGump(baseId + 7, 1, corner, colors[7]);
        AddGump(baseId + 8, corner, corner, colors[8]);

        return this;
    }

    private FakeArtSource Add(string kind, int id, int width, int height, SKColor color)
    {
        SKBitmap bitmap = new(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul));

        using (SKCanvas canvas = new(bitmap))
        {
            canvas.Clear(color);
            canvas.Flush();
        }

        SKImage image = SKImage.FromBitmap(bitmap);
        bitmap.Dispose();

        _images[(kind, id)] = image;
        _owned.Add(image);

        return this;
    }

    /// <summary>Looks up registered art, recording a miss when absent.</summary>
    public SKImage? Lookup(string kind, int id)
    {
        if (_images.TryGetValue((kind, id), out SKImage? image))
        {
            return image;
        }

        MissingRequests.Add(id);

        return null;
    }

    /// <summary>Text is rendered as a solid block, eight pixels per character.</summary>
    public SKImage? MakeText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        (string, int) key = ("text", text.GetHashCode(StringComparison.Ordinal));

        if (_images.TryGetValue(key, out SKImage? existing))
        {
            return existing;
        }

        Add("text", key.Item2, text.Length * 8, 10, SKColors.White);

        return _images[key];
    }

    public void Dispose()
    {
        foreach (SKImage image in _owned)
        {
            image.Dispose();
        }

        _owned.Clear();
        _images.Clear();
    }
}
