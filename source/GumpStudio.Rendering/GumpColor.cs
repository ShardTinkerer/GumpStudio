using SkiaSharp;

namespace GumpStudio.Rendering;

/// <summary>
/// The explicit text colour an HTML gump command carries.
/// </summary>
/// <remarks>
/// <para>
/// Not a hue. The client mixes two unrelated colour encodings, and the decompiled
/// client notes call confusing them "the most common source of wrong assumptions":
/// a <b>hue index</b> looks up <c>hues.mul</c>, while an <b>RGB555</b> value is a
/// packed <c>R5G5B5</c> colour that never touches it. The colour slot on
/// <c>xmfhtmlgumpcolor</c> and <c>xmfhtmltok</c> is the second kind.
/// </para>
/// <para>
/// Servers write it both ways, and one captured gump uses both in the same
/// definition: <c>32767</c> is <c>0x7FFF</c>, RGB555 white, and <c>16777215</c> is
/// <c>0xFFFFFF</c>, 24-bit white. So a value that fits in fifteen bits is read as
/// RGB555 and anything larger as 24-bit RGB, which resolves both to white and
/// keeps an ordinary <c>#RRGGBB</c> from collapsing to near-black.
/// </para>
/// <para>
/// The client reference documents the packing but not which form the gump path
/// accepts, and its notes describe a different build from the installed ones — so
/// this is a reading of what servers actually send, not a transcription.
/// </para>
/// </remarks>
public static class GumpColor
{
    /// <summary>The largest value that can be an RGB555 colour.</summary>
    private const int MaxRgb555 = 0x7FFF;

    /// <summary>
    /// The colour to draw text in, or null to leave it to the renderer's default.
    /// </summary>
    /// <remarks>
    /// Zero means "unset" rather than black: it is what a gump carries when it
    /// uses the plain <c>xmfhtmlgump</c> form, which names no colour at all.
    /// </remarks>
    public static SKColor? ToSkColor(int value)
    {
        if (value == 0)
        {
            return null;
        }

        return value <= MaxRgb555 ? FromRgb555(value) : FromRgb24(value);
    }

    /// <summary>Levels the client can show per channel.</summary>
    /// <remarks>
    /// Five bits. A picker offering 8-bit channels would promise 16.7 million
    /// colours where the client has 32,768, and two nearby picks would come out
    /// identical.
    /// </remarks>
    public const int Levels = 32;

    /// <summary>
    /// Packs five-bit channels into the value a gump command carries.
    /// </summary>
    /// <remarks>
    /// RGB555 rather than 24-bit, because that is what the client's own UI colour
    /// format is and what scripts already write — <c>0x7FFF</c> is the familiar
    /// spelling of white, and it appears in captured gumps beside
    /// <c>0xFFFFFF</c> where both plainly mean the same thing. Reading still
    /// accepts either.
    /// </remarks>
    public static int Pack(int red, int green, int blue) =>
        ((Clamp(red) & 0x1F) << 10) | ((Clamp(green) & 0x1F) << 5) | (Clamp(blue) & 0x1F);

    /// <summary>The five-bit channels of a value, whichever way it is spelled.</summary>
    public static (int Red, int Green, int Blue) ToChannels(int value)
    {
        if (ToSkColor(value) is not { } colour)
        {
            return (0, 0, 0);
        }

        return (colour.Red * 31 / 255, colour.Green * 31 / 255, colour.Blue * 31 / 255);
    }

    private static int Clamp(int channel) => Math.Clamp(channel, 0, Levels - 1);

    /// <summary>
    /// Expands a packed <c>R5G5B5</c> value.
    /// </summary>
    /// <remarks>
    /// Each channel is scaled so that full five-bit saturation reaches 255 rather
    /// than 248, which is what leaves white looking faintly grey.
    /// </remarks>
    private static SKColor FromRgb555(int value)
    {
        int r = (value >> 10) & 0x1F;
        int g = (value >> 5) & 0x1F;
        int b = value & 0x1F;

        return new SKColor(Expand(r), Expand(g), Expand(b));
    }

    private static SKColor FromRgb24(int value) =>
        new((byte)((value >> 16) & 0xFF), (byte)((value >> 8) & 0xFF), (byte)(value & 0xFF));

    private static byte Expand(int channel) => (byte)((channel * 255) / 31);
}
