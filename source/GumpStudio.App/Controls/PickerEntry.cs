using Avalonia.Media;
using Avalonia.Media.Imaging;

using GumpStudio.Core.Elements;
using GumpStudio.Uo;
using GumpStudio.Uo.Fonts;
using GumpStudio.Uo.Graphics;
using GumpStudio.Uo.Primitives;

using SkiaSharp;

namespace GumpStudio.App.Controls;

/// <summary>
/// One row of a searchable picker: a value, a label, and something to look at.
/// </summary>
/// <remarks>
/// Hues and fonts are both opaque as numbers — nobody knows what hue 1153 or font
/// 4 looks like — so the picker shows each one rendered. A hue draws as its own
/// colour ramp; a font draws a line of text set in it.
/// </remarks>
public sealed class PickerEntry
{
    /// <summary>What choosing this row means, in the element's own terms.</summary>
    public required object Value { get; init; }

    /// <summary>The number the row is identified by, for searching.</summary>
    public required int Index { get; init; }

    /// <summary>The name shown beside the index, or empty.</summary>
    public required string Name { get; init; }

    /// <summary>A colour ramp to draw, for a hue.</summary>
    public IReadOnlyList<Color>? Ramp { get; init; }

    /// <summary>A rendered sample, for a font.</summary>
    public Bitmap? Sample { get; init; }

    /// <summary>What the field shows once this row is chosen.</summary>
    public string Display => Name.Length == 0
        ? Index.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : $"{Index} — {Name}";

    /// <summary>Whether a search term matches this row by index or by name.</summary>
    /// <remarks>
    /// The index matches on a prefix so typing <c>11</c> narrows rather than
    /// jumping, and the name matches anywhere so a partial word finds it.
    /// </remarks>
    public bool Matches(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        string term = search.Trim();

        return Index.ToString(System.Globalization.CultureInfo.InvariantCulture)
                .StartsWith(term, StringComparison.OrdinalIgnoreCase)
            || Name.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    public override string ToString() => Display;
}

/// <summary>Builds the rows a hue or font picker offers.</summary>
public static class PickerEntries
{
    /// <summary>How many shades of a hue the swatch shows.</summary>
    private const int RampShades = 16;

    /// <summary>The text a font sample is drawn with.</summary>
    private const string FontSample = "Sample Text 123";

    /// <summary>
    /// Every hue in the client, plus zero for none.
    /// </summary>
    /// <remarks>
    /// Hue 0 is not an entry in <c>hues.mul</c> — it means "leave the art alone" —
    /// so it is added by hand rather than looked up.
    /// </remarks>
    public static IReadOnlyList<PickerEntry> Hues(UoDataContext? data)
    {
        List<PickerEntry> entries =
        [
            new() { Value = 0, Index = 0, Name = "none", Ramp = [] },
        ];

        if (data is null)
        {
            return entries;
        }

        foreach (Hue hue in data.Hues.Hues)
        {
            // ScriptValue, not Index. An element and a gump script both store
            // the one-based value; the raw index labels every row with the wrong
            // number, shows its neighbour's colours, and writes a value one
            // lower than the one displayed.
            entries.Add(new PickerEntry
            {
                Value = hue.ScriptValue,
                Index = hue.ScriptValue,
                Name = hue.Name,
                Ramp = Ramp(hue),
            });
        }

        return entries;
    }

    /// <summary>Every font face the client ships, in both families.</summary>
    public static IReadOnlyList<PickerEntry> Fonts(UoDataContext? data)
    {
        List<PickerEntry> entries = [];

        if (data is null)
        {
            return entries;
        }

        foreach (UnicodeFont font in data.UnicodeFonts.Fonts)
        {
            Add(entries, GumpFontFamily.Unicode, font.Slot, font.Render(FontSample));
        }

        for (int i = 0; i < data.AsciiFonts.Count; i++)
        {
            Add(entries, GumpFontFamily.Ascii, i, data.AsciiFonts.Get(i)?.Render(FontSample));
        }

        return entries;
    }

    private static void Add(
        List<PickerEntry> entries, GumpFontFamily family, int index, Argb1555Image? rendered)
    {
        entries.Add(new PickerEntry
        {
            Value = new FontChoice(family, index),
            Index = index,
            Name = family.ToString(),
            Sample = rendered is null ? null : ToBitmap(rendered),
        });
    }

    /// <summary>An evenly spaced sample of a hue's 32 shades.</summary>
    private static List<Color> Ramp(Hue hue)
    {
        List<Color> ramp = new(RampShades);

        for (int i = 0; i < RampShades; i++)
        {
            uint argb = hue.GetColor(i * (Hue.ColorCount / RampShades));

            ramp.Add(Color.FromArgb(
                (byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        }

        return ramp;
    }

    /// <summary>
    /// Turns a rendered glyph run into something Avalonia can draw.
    /// </summary>
    /// <remarks>
    /// Unicode faces are a white mask on transparent, which is invisible against
    /// a light row, so the sample is drawn onto the dark ground the picker uses.
    /// </remarks>
    private static Bitmap ToBitmap(Argb1555Image rendered)
    {
        using SKBitmap decoded = Rendering.UoImageConverter.ToSkBitmap(rendered.ToUoImage());

        return SkiaBitmap.ToAvalonia(decoded);
    }
}

/// <summary>A font face, as a picker chooses it.</summary>
public readonly record struct FontChoice(GumpFontFamily Family, int Index);
