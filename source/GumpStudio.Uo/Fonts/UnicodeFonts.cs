using System.Buffers.Binary;

using GumpStudio.Uo.Primitives;

namespace GumpStudio.Uo.Fonts;

/// <summary>
/// The Unicode fonts in <c>unifont.mul</c> and <c>unifont1..12.mul</c>.
/// </summary>
/// <remarks>
/// <para>
/// Each file opens with a 65536-entry table of glyph offsets, one per code
/// point, followed by 1-bit alpha masks. That table size is the whole point: any
/// code point can have a glyph.
/// </para>
/// <para>
/// The old implementation hard-coded seven files (a current client ships
/// thirteen) and cached glyphs in a 1120-entry array indexed by the raw
/// character, so any code point at or above U+0460 threw
/// <see cref="IndexOutOfRangeException"/> and took label rendering down with it.
/// </para>
/// </remarks>
public sealed class UnicodeFonts : IDisposable
{
    /// <summary>One glyph offset per UTF-16 code unit.</summary>
    public const int CodePointCount = 0x10000;

    /// <summary>Slot 0 is <c>unifont.mul</c>; slots 1-12 are <c>unifont1..12.mul</c>.</summary>
    public const int MaxFonts = 13;

    private readonly UnicodeFont[] _fonts;

    private UnicodeFonts(UnicodeFont[] fonts) => _fonts = fonts;

    /// <summary>Font slots that were actually found, in slot order.</summary>
    public IReadOnlyList<UnicodeFont> Fonts => _fonts;

    public int Count => _fonts.Length;

    /// <summary>An empty collection, used when the client has no unicode fonts.</summary>
    public static UnicodeFonts Empty { get; } = new([]);

    /// <summary>
    /// Discovers and opens every <c>unifont*.mul</c> in <paramref name="directory"/>.
    /// </summary>
    /// <remarks>
    /// Discovery is by probing each slot rather than by a fixed count, so a
    /// client with more or fewer font files works without a code change.
    /// </remarks>
    public static UnicodeFonts Load(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        List<UnicodeFont> fonts = [];

        for (int slot = 0; slot < MaxFonts; slot++)
        {
            string name = slot == 0 ? "unifont.mul" : $"unifont{slot}.mul";
            string path = Path.Combine(directory, name);

            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                fonts.Add(UnicodeFont.Open(slot, path));
            }
            catch (IOException)
            {
                // A font we cannot open is not worth failing startup over; the
                // remaining slots still render.
            }
        }

        return new UnicodeFonts([.. fonts]);
    }

    /// <summary>Gets a font by its slot number, or <see langword="null"/>.</summary>
    public UnicodeFont? Get(int slot) => _fonts.FirstOrDefault(f => f.Slot == slot);

    public void Dispose()
    {
        foreach (UnicodeFont font in _fonts)
        {
            font.Dispose();
        }
    }
}

/// <summary>One <c>unifont*.mul</c> file.</summary>
public sealed class UnicodeFont : IDisposable
{
    private readonly Files.SafeFileHandleOwner _file;
    private readonly int[] _offsets;

    /// <summary>
    /// Decoded glyphs, cached on demand and keyed by character.
    /// </summary>
    /// <remarks>
    /// A dictionary rather than a fixed array: the old code used a 1120-entry
    /// array indexed by the raw character value, which crashed on any code point
    /// at or above U+0460 and was never actually read from.
    /// </remarks>
    private readonly Dictionary<char, UnicodeGlyph?> _cache = [];

    private UnicodeFont(int slot, Files.SafeFileHandleOwner file, int[] offsets)
    {
        Slot = slot;
        _file = file;
        _offsets = offsets;
    }

    public int Slot { get; }

    internal static UnicodeFont Open(int slot, string path)
    {
        Files.SafeFileHandleOwner file = Files.SafeFileHandleOwner.OpenRead(path);

        try
        {
            byte[] raw = new byte[UnicodeFonts.CodePointCount * sizeof(int)];
            int read = file.Read(0, raw);

            int[] offsets = new int[UnicodeFonts.CodePointCount];
            int usable = read / sizeof(int);

            for (int i = 0; i < usable; i++)
            {
                offsets[i] = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(i * sizeof(int)));
            }

            return new UnicodeFont(slot, file, offsets);
        }
        catch
        {
            file.Dispose();

            throw;
        }
    }

    /// <summary>Reads one glyph, or <see langword="null"/> when the font has none.</summary>
    public UnicodeGlyph? GetGlyph(char c)
    {
        if (_cache.TryGetValue(c, out UnicodeGlyph? cached))
        {
            return cached;
        }

        UnicodeGlyph? glyph = ReadGlyph(c);
        _cache[c] = glyph;

        return glyph;
    }

    private UnicodeGlyph? ReadGlyph(char c)
    {
        int offset = _offsets[c];

        // Offset 0 means the font defines no glyph for this code point.
        if (offset <= 0 || offset >= _file.Length)
        {
            return null;
        }

        Span<byte> header = stackalloc byte[4];

        if (_file.Read(offset, header) != header.Length)
        {
            return null;
        }

        sbyte xOffset = (sbyte)header[0];
        sbyte yOffset = (sbyte)header[1];
        int width = header[2];
        int height = header[3];

        if (width <= 0 || height <= 0)
        {
            return null;
        }

        // One bit per pixel, rows padded out to a whole byte.
        int stride = (width + 7) / 8;
        byte[] bits = new byte[stride * height];

        return _file.Read(offset + header.Length, bits) != bits.Length
            ? null
            : new UnicodeGlyph(width, height, xOffset, yOffset, stride, bits);
    }

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
                // The client advances by a fixed amount for an undefined glyph,
                // which is how spaces get their width in most unicode fonts.
                width += UnicodeGlyph.UndefinedAdvance;

                continue;
            }

            width += glyph.Advance;
            height = Math.Max(height, glyph.Height + glyph.YOffset);
        }

        return (width, height);
    }

    /// <summary>Renders a string as a solid-white mask, ready to be hued.</summary>
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
                x += UnicodeGlyph.UndefinedAdvance;

                continue;
            }

            glyph.Blit(image, x, glyph.YOffset);

            x += glyph.Advance;
        }

        return image;
    }

    public void Dispose() => _file.Dispose();
}

/// <summary>One Unicode glyph: a 1-bit alpha mask plus pen metrics.</summary>
public sealed class UnicodeGlyph
{
    /// <summary>Pen advance used for a code point the font does not define.</summary>
    public const int UndefinedAdvance = 8;

    private readonly byte[] _bits;
    private readonly int _stride;

    internal UnicodeGlyph(int width, int height, sbyte xOffset, sbyte yOffset, int stride, byte[] bits)
    {
        Width = width;
        Height = height;
        XOffset = xOffset;
        YOffset = yOffset;
        _stride = stride;
        _bits = bits;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Horizontal pen offset, which may be negative.</summary>
    public sbyte XOffset { get; }

    /// <summary>Vertical offset from the top of the line box.</summary>
    public sbyte YOffset { get; }

    /// <summary>How far the pen moves after drawing this glyph.</summary>
    public int Advance => Width + XOffset;

    /// <summary>True when the mask has a pixel set at this position.</summary>
    public bool IsSet(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            return false;
        }

        // Bits run most-significant-first within each byte.
        byte b = _bits[(y * _stride) + (x >> 3)];

        return (b & (0x80 >> (x & 7))) != 0;
    }

    /// <summary>Draws the mask into <paramref name="target"/> as opaque white.</summary>
    internal void Blit(Argb1555Image target, int originX, int originY)
    {
        const ushort White = Color16.AlphaMask | (31 << 10) | (31 << 5) | 31;

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
                int targetX = originX + XOffset + x;

                if ((uint)targetX >= (uint)row.Length || !IsSet(x, y))
                {
                    continue;
                }

                row[targetX] = White;
            }
        }
    }
}
