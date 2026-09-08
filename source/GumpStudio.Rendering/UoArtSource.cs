using GumpStudio.Core.Elements;
using GumpStudio.Uo;
using GumpStudio.Uo.Fonts;
using GumpStudio.Uo.Graphics;
using GumpStudio.Uo.Primitives;

using SkiaSharp;

namespace GumpStudio.Rendering;

/// <summary>
/// Art from a real Ultima Online installation, decoded on demand and cached.
/// </summary>
/// <remarks>
/// One bounded cache lives here, in the rendering layer, rather than the eleven
/// ad-hoc per-element bitmap fields the old element classes each carried — none
/// of which were bounded, and several of which leaked.
/// </remarks>
public sealed class UoArtSource : IGumpArtSource, IDisposable
{
    /// <summary>
    /// How much decoded art to keep, in bytes.
    /// </summary>
    /// <remarks>
    /// A byte budget rather than a count. Entries here differ enormously in
    /// size — a nine-slice corner is a few hundred bytes and a full-window
    /// background is megabytes — so a fixed number of entries either wasted the
    /// cache on small art or held hundreds of megabytes of large art.
    /// </remarks>
    public const long DefaultBudget = 64L * 1024 * 1024;

    private readonly UoDataContext _data;
    private readonly Dictionary<ArtKey, Entry> _cache = [];

    // The recency order, plus each key's node in it, so a cache hit is O(1).
    // Looking the node up by value made every hit a linear scan of the list,
    // under the lock, on every art draw of every frame.
    private readonly LinkedList<ArtKey> _order = [];
    private readonly Dictionary<ArtKey, LinkedListNode<ArtKey>> _nodes = [];

    private readonly Lock _sync = new();
    private readonly long _budget;
    private long _bytes;

    public UoArtSource(UoDataContext data, long budget = DefaultBudget)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(budget);

        _data = data;
        _budget = budget;
    }

    /// <summary>Bytes of decoded art currently held.</summary>
    public long CachedBytes
    {
        get
        {
            lock (_sync)
            {
                return _bytes;
            }
        }
    }

    /// <inheritdoc />
    public string? GetCliloc(int clilocId) => _data.Clilocs.GetText(clilocId);

    /// <summary>Images currently held.</summary>
    public int CachedImageCount
    {
        get
        {
            lock (_sync)
            {
                return _cache.Count;
            }
        }
    }

    public SKImage? GetGump(int gumpId, int hue = 0, bool partialHue = false) =>
        Get(new ArtKey(ArtKind.Gump, gumpId, hue, partialHue, null));

    public SKImage? GetItem(int itemId, int hue = 0, bool partialHue = false) =>
        Get(new ArtKey(ArtKind.Item, itemId, hue, partialHue, null));

    public SKImage? GetText(
        int fontIndex, string text, int hue = 0, GumpFontFamily family = GumpFontFamily.Unicode) =>
        string.IsNullOrEmpty(text)
            ? null
            : Get(new ArtKey(
                family == GumpFontFamily.Ascii ? ArtKind.AsciiText : ArtKind.Text,
                fontIndex,
                hue,
                false,
                text));

    public bool TryGetGumpSize(int gumpId, out int width, out int height) =>
        _data.TryGetGumpSize(gumpId, out width, out height);

    /// <summary>Drops every cached image, for example after the client path changes.</summary>
    public void Clear()
    {
        lock (_sync)
        {
            foreach (Entry entry in _cache.Values)
            {
                entry.Image?.Dispose();
            }

            _cache.Clear();
            _order.Clear();
            _nodes.Clear();
            _bytes = 0;
        }
    }

    private SKImage? Get(ArtKey key)
    {
        lock (_sync)
        {
            if (_cache.TryGetValue(key, out Entry cached))
            {
                Touch(key);

                return cached.Image;
            }

            SKImage? image = Decode(key);
            long size = SizeOf(image);

            _cache[key] = new Entry(image, size);
            _nodes[key] = _order.AddLast(key);
            _bytes += size;

            Evict();

            return image;
        }
    }

    /// <summary>
    /// What an image costs the budget.
    /// </summary>
    /// <remarks>
    /// Four bytes a pixel, which is what the decoders produce. A miss is still
    /// charged a nominal amount, so that a page referencing thousands of absent
    /// ids cannot fill the dictionary for free.
    /// </remarks>
    private static long SizeOf(SKImage? image) =>
        image is null ? 64 : (long)image.Width * image.Height * 4;

    private SKImage? Decode(ArtKey key)
    {
        UoImage? decoded = key.Kind switch
        {
            ArtKind.Gump => _data.GetGump(key.Id, key.Hue, key.PartialHue),
            ArtKind.Item => _data.GetStatic(key.Id, key.Hue, key.PartialHue),
            ArtKind.Text => RenderText(key),
            ArtKind.AsciiText => RenderAsciiText(key),
            _ => null,
        };

        if (decoded is null)
        {
            return null;
        }

        // FromBitmap copies the pixels, so the intermediate bitmap is temporary.
        using SKBitmap bitmap = decoded.ToSkBitmap();

        return SKImage.FromBitmap(bitmap);
    }

    private UoImage? RenderText(ArtKey key)
    {
        if (key.Text is null)
        {
            return null;
        }

        IReadOnlyList<UnicodeFont> fonts = _data.UnicodeFonts.Fonts;

        UnicodeFont? font = _data.UnicodeFonts.Get(key.Id)
            ?? (fonts.Count > 0 ? fonts[0] : null);

        if (font?.Render(key.Text) is not { } glyphs)
        {
            return null;
        }

        // Unicode glyphs are a solid white mask, so a hue recolours them wholesale.
        _data.Hues.Get(key.Hue)?.ApplyTo(glyphs.Pixels, onlyGreyPixels: false);

        return glyphs.ToUoImage();
    }

    /// <summary>
    /// Renders text in one of the ten <c>fonts.mul</c> faces.
    /// </summary>
    /// <remarks>
    /// These carry their own colours, so a hue recolours only the grey pixels —
    /// the opposite of the Unicode faces, which are a solid white mask.
    /// </remarks>
    private UoImage? RenderAsciiText(ArtKey key)
    {
        if (key.Text is null || _data.AsciiFonts.Get(key.Id) is not { } font)
        {
            return null;
        }

        if (font.Render(key.Text) is not { } glyphs)
        {
            return null;
        }

        _data.Hues.Get(key.Hue)?.ApplyTo(glyphs.Pixels, onlyGreyPixels: true);

        return glyphs.ToUoImage();
    }

    private void Touch(ArtKey key)
    {
        if (!_nodes.TryGetValue(key, out LinkedListNode<ArtKey>? node))
        {
            return;
        }

        _order.Remove(node);
        _order.AddLast(node);
    }

    private void Evict()
    {
        // One entry always stays, so an image larger than the whole budget is
        // still usable rather than being decoded and dropped on every draw.
        while (_bytes > _budget && _cache.Count > 1 && _order.First is { } oldest)
        {
            ArtKey key = oldest.Value;

            _order.RemoveFirst();
            _nodes.Remove(key);

            if (_cache.Remove(key, out Entry entry))
            {
                _bytes -= entry.Bytes;
                entry.Image?.Dispose();
            }
        }
    }

    public void Dispose() => Clear();

    private enum ArtKind
    {
        Gump,
        Item,
        Text,
        AsciiText,
    }

    private readonly record struct ArtKey(ArtKind Kind, int Id, int Hue, bool PartialHue, string? Text);

    /// <summary>A cached image and what it costs the budget.</summary>
    private readonly record struct Entry(SKImage? Image, long Bytes);
}
