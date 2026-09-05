using System.Buffers.Binary;

using GumpStudio.Uo.Primitives;

namespace GumpStudio.Uo.Fonts;

/// <summary>
/// The bitmap fonts in <c>fonts.mul</c>: colour glyphs for classic UO text.
/// </summary>
/// <remarks>
/// Each font holds 224 glyphs covering characters 0x20 to 0xFF, so a character
/// maps directly to <c>glyphs[ch - 0x20]</c>. The old implementation instead ran
/// each character through code page 1251, silently mangling anything outside
/// Cyrillic; the mapping is a plain 8-bit one and needs no encoding at all.
/// </remarks>
public sealed class AsciiFonts
{
    /// <summary>Characters 0x20 through 0xFF.</summary>
    public const int GlyphsPerFont = 224;

    /// <summary>First character a font has a glyph for.</summary>
    public const char FirstCharacter = ' ';

    /// <summary>The client reads at most this many font slots.</summary>
    private const int MaxFonts = 25;

    private readonly AsciiFont[] _fonts;

    private AsciiFonts(AsciiFont[] fonts) => _fonts = fonts;

    public int Count => _fonts.Length;

    /// <summary>An empty collection, used when the client has no <c>fonts.mul</c>.</summary>
    public static AsciiFonts Empty { get; } = new([]);

    public static AsciiFonts Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        byte[] raw = File.ReadAllBytes(path);

        List<AsciiFont> fonts = [];
        int offset = 0;

        // Fonts are concatenated with no index, so each one has to be walked to
        // find where the next begins.
        while (fonts.Count < MaxFonts && offset < raw.Length)
        {
            // A leading flag byte; the glyphs follow immediately.
            offset++;

            AsciiGlyph[] glyphs = new AsciiGlyph[GlyphsPerFont];
            bool truncated = false;

            for (int i = 0; i < GlyphsPerFont; i++)
            {
                if (offset + 3 > raw.Length)
                {
                    truncated = true;

                    break;
                }

                int width = raw[offset];
                int height = raw[offset + 1];
                sbyte unknown = (sbyte)raw[offset + 2];

                offset += 3;

                int pixelBytes = width * height * sizeof(ushort);

                if (width <= 0 || height <= 0)
                {
                    // A zero-sized glyph is legal; it just renders nothing.
                    glyphs[i] = AsciiGlyph.Empty;

                    continue;
                }

                if (offset + pixelBytes > raw.Length)
                {
                    truncated = true;

                    break;
                }

                glyphs[i] = new AsciiGlyph(width, height, unknown, raw.AsSpan(offset, pixelBytes).ToArray());

                offset += pixelBytes;
            }

            if (truncated)
            {
                break;
            }

            fonts.Add(new AsciiFont(fonts.Count, glyphs));
        }

        return new AsciiFonts([.. fonts]);
    }

    /// <summary>Gets a font by index, or <see langword="null"/> when out of range.</summary>
    public AsciiFont? Get(int index) => (uint)index < (uint)_fonts.Length ? _fonts[index] : null;
}

/// <summary>One bitmap font from <c>fonts.mul</c>.</summary>
public sealed class AsciiFont
{
    private readonly AsciiGlyph[] _glyphs;

    internal AsciiFont(int index, AsciiGlyph[] glyphs)
    {
        Index = index;
        _glyphs = glyphs;
        Height = glyphs.Max(g => g.Height);
    }

    public int Index { get; }

    /// <summary>Height of the tallest glyph, used as the line height.</summary>
    public int Height { get; }

    /// <summary>Gets the glyph for a character, or <see langword="null"/> if it has none.</summary>
    public AsciiGlyph? GetGlyph(char c)
    {
        int index = c - FirstCharacterValue;

        return (uint)index < (uint)_glyphs.Length ? _glyphs[index] : null;
    }

    private const int FirstCharacterValue = ' ';

    /// <summary>Measures a string without rendering it.</summary>
    public (int Width, int Height) Measure(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        int width = 0;
        int height = 0;

        foreach (char c in text)
        {
            if (GetGlyph(c) is not { } glyph)
            {
                continue;
            }

            width += glyph.Width;
            height = Math.Max(height, glyph.Height);
        }

        return (width, height);
    }

    /// <summary>Renders a string into a new image.</summary>
    /// <returns>The rendered text, or <see langword="null"/> when it would be empty.</returns>
    public Argb1555Image? Render(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        (int width, int height) = Measure(text);

        if (width <= 0 || height <= 0)
        {
            return null;
        }

        Argb1555Image image = new(width, height);
        int x = 0;

        foreach (char c in text)
        {
            if (GetGlyph(c) is not { } glyph)
            {
                continue;
            }

            // Glyphs sit on a common baseline at the bottom of the line box.
            glyph.Blit(image, x, height - glyph.Height);

            x += glyph.Width;
        }

        return image;
    }
}

/// <summary>One glyph: a small block of ARGB1555 pixels.</summary>
public sealed class AsciiGlyph
{
    private readonly byte[] _pixels;

    internal AsciiGlyph(int width, int height, sbyte metric, byte[] pixels)
    {
        Width = width;
        Height = height;
        Metric = metric;
        _pixels = pixels;
    }

    /// <summary>A glyph that occupies no space.</summary>
    public static AsciiGlyph Empty { get; } = new(0, 0, 0, []);

    public int Width { get; }

    public int Height { get; }

    /// <summary>A per-glyph metric the client stores but this application does not use.</summary>
    public sbyte Metric { get; }

    /// <summary>Copies the glyph into <paramref name="target"/> at the given position.</summary>
    internal void Blit(Argb1555Image target, int originX, int originY)
    {
        for (int y = 0; y < Height; y++)
        {
            int targetY = originY + y;

            if ((uint)targetY >= (uint)target.Height)
            {
                continue;
            }

            Span<ushort> row = target.Row(targetY);

            for (int x = 0; x < Width; x++)
            {
                int targetX = originX + x;

                if ((uint)targetX >= (uint)row.Length)
                {
                    continue;
                }

                ushort color = BinaryPrimitives.ReadUInt16LittleEndian(
                    _pixels.AsSpan(((y * Width) + x) * sizeof(ushort)));

                // Zero means transparent. The old renderer drew every pixel
                // unconditionally, turning the background opaque white, then
                // tried to undo it with a corner-pixel transparency guess that
                // failed whenever a descender landed in the corner.
                if (color == 0)
                {
                    continue;
                }

                row[targetX] = (ushort)(color | Color16.AlphaMask);
            }
        }
    }
}
