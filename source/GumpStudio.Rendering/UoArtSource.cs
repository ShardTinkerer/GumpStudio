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
    private readonly UoDataContext _data;
    private readonly Dictionary<ArtKey, SKImage?> _cache = [];
    private readonly LinkedList<ArtKey> _order = [];
    private readonly Lock _sync = new();
    private readonly int _capacity;

    public UoArtSource(UoDataContext data, int capacity = 512)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        _data = data;
        _capacity = capacity;
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
            foreach (SKImage? image in _cache.Values)
            {
                image?.Dispose();
            }

            _cache.Clear();
            _order.Clear();
        }
    }

    private SKImage? Get(ArtKey key)
    {
        lock (_sync)
        {
            if (_cache.TryGetValue(key, out SKImage? cached))
            {
                Touch(key);

                return cached;
            }

            SKImage? image = Decode(key);

            _cache[key] = image;
            _order.AddLast(key);

            Evict();

            return image;
        }
    }

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
        _order.Remove(key);
        _order.AddLast(key);
    }

    private void Evict()
    {
        while (_cache.Count > _capacity && _order.First is { } oldest)
        {
            _order.RemoveFirst();

            if (_cache.Remove(oldest.Value, out SKImage? image))
            {
                image?.Dispose();
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
}
