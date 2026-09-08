using GumpStudio.Rendering;

using SkiaSharp;

using Xunit;

namespace GumpStudio.Rendering.Tests;

/// <summary>
/// How an HTML command's colour slot is read.
/// </summary>
/// <remarks>
/// Not a hue. The client mixes two colour encodings and the decompiled client
/// notes call confusing them "the most common source of wrong assumptions": a hue
/// index looks up <c>hues.mul</c>, an RGB555 value never touches it. Servers write
/// this slot both ways, and one captured gump uses both forms in the same
/// definition.
/// </remarks>
public class GumpColorTests
{
    /// <summary>Zero means the command named no colour, not that it named black.</summary>
    [Fact]
    public void TreatsZeroAsUnset() => Assert.Null(GumpColor.ToSkColor(0));

    /// <summary>Both spellings of white appear in the same real capture.</summary>
    [Theory]
    [InlineData(32767)]     // 0x7FFF, RGB555
    [InlineData(16777215)]  // 0xFFFFFF, 24-bit
    public void ReadsEitherSpellingOfWhite(int value) =>
        Assert.Equal(new SKColor(0xFF, 0xFF, 0xFF), GumpColor.ToSkColor(value));

    [Theory]
    [InlineData(0x7C00, 0xFF, 0x00, 0x00)]
    [InlineData(0x03E0, 0x00, 0xFF, 0x00)]
    [InlineData(0x001F, 0x00, 0x00, 0xFF)]
    public void ReadsAFifteenBitValueAsRgb555(int value, byte r, byte g, byte b) =>
        Assert.Equal(new SKColor(r, g, b), GumpColor.ToSkColor(value));

    /// <summary>
    /// Anything past fifteen bits is plain <c>#RRGGBB</c>.
    /// </summary>
    /// <remarks>
    /// Masking it into RGB555 instead would turn ordinary red into black, which
    /// is the failure this split exists to avoid.
    /// </remarks>
    [Theory]
    [InlineData(0xFF0000, 0xFF, 0x00, 0x00)]
    [InlineData(0x00FF00, 0x00, 0xFF, 0x00)]
    [InlineData(0x0080FF, 0x00, 0x80, 0xFF)]
    public void ReadsALargerValueAsTwentyFourBitRgb(int value, byte r, byte g, byte b) =>
        Assert.Equal(new SKColor(r, g, b), GumpColor.ToSkColor(value));

    /// <summary>
    /// Packing writes the RGB555 spelling.
    /// </summary>
    /// <remarks>
    /// That is the client's own UI colour format, and the one scripts already
    /// use — <c>0x7FFF</c> is the familiar white. Writing 24-bit instead would be
    /// read as RGB555 by anything that masks, turning red into black.
    /// </remarks>
    [Theory]
    [InlineData(31, 31, 31, 0x7FFF)]
    [InlineData(31, 0, 0, 0x7C00)]
    [InlineData(0, 31, 0, 0x03E0)]
    [InlineData(0, 0, 31, 0x001F)]
    [InlineData(0, 0, 0, 0)]
    public void PacksFiveBitChannels(int r, int g, int b, int expected) =>
        Assert.Equal(expected, GumpColor.Pack(r, g, b));

    /// <summary>A channel outside the five-bit range is clamped, not wrapped.</summary>
    [Fact]
    public void ClampsChannelsToTheClientsRange()
    {
        Assert.Equal(GumpColor.Pack(31, 31, 31), GumpColor.Pack(99, 40, 32));
        Assert.Equal(GumpColor.Pack(0, 0, 0), GumpColor.Pack(-1, -8, 0));
    }

    /// <summary>Packing and unpacking are inverses, whichever spelling came in.</summary>
    [Theory]
    [InlineData(0x7C00)]     // RGB555 red
    [InlineData(0xFF0000)]   // the same red, 24-bit
    [InlineData(0x7FFF)]
    [InlineData(0xFFFFFF)]
    public void RoundTripsThroughChannels(int value)
    {
        (int r, int g, int b) = GumpColor.ToChannels(value);

        Assert.Equal(GumpColor.ToSkColor(value), GumpColor.ToSkColor(GumpColor.Pack(r, g, b)));
    }

    /// <summary>
    /// A saturated five-bit channel reaches 255, not 248.
    /// </summary>
    /// <remarks>
    /// Shifting left by three is the obvious expansion and leaves white looking
    /// faintly grey, which is visible when it sits next to real white.
    /// </remarks>
    [Fact]
    public void ExpandsFiveBitChannelsToFullRange() =>
        Assert.Equal(new SKColor(0xFF, 0xFF, 0xFF), GumpColor.ToSkColor(0x7FFF));
}
