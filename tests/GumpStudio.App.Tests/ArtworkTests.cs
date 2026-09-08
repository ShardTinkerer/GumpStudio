using System.Buffers.Binary;

using Avalonia.Platform;

using GumpStudio.App.Controls;

using Xunit;

namespace GumpStudio.App.Tests;

/// <summary>
/// The artwork recovered from GumpStudio 1.8.
/// </summary>
/// <remarks>
/// <para>
/// These assets are the one part of this repository that cannot be regenerated from
/// source: the splash graphic came out of a base64 blob in a WinForms
/// <c>.resx</c>, and the icon out of the PE resource directory of a 2004
/// executable. If a build drops them, the failure at runtime is a missing-asset
/// exception during startup, before there is a window to report it in.
/// </para>
/// <para>
/// The image headers are parsed here rather than decoded through
/// <c>Bitmap</c>: the headless platform stubs drawing, so a decoded bitmap
/// reports a 1x1 placeholder and would make a size assertion meaningless. Going
/// at the bytes also tests what actually ships rather than what a decoder makes
/// of it.
/// </para>
/// </remarks>
[Collection("Headless")]
public class ArtworkTests
{
    private const string SplashUri = "avares://GumpStudio.App/Assets/splash.jpg";
    private const string IconUri = "avares://GumpStudio.App/Assets/gumpstudio.ico";

    private static byte[] Asset(string uri)
    {
        using Stream stream = AssetLoader.Open(new Uri(uri));
        using MemoryStream copy = new();

        stream.CopyTo(copy);

        return copy.ToArray();
    }

    /// <summary>
    /// A JPEG's dimensions, from its start-of-frame marker.
    /// </summary>
    /// <remarks>
    /// Segments are walked rather than searched for, so a byte pair inside
    /// compressed data cannot be mistaken for a marker.
    /// </remarks>
    private static (int Width, int Height) JpegSize(byte[] raw)
    {
        Assert.Equal(0xFF, raw[0]);
        Assert.Equal(0xD8, raw[1]);

        int cursor = 2;

        while (cursor + 9 < raw.Length)
        {
            Assert.Equal(0xFF, raw[cursor]);

            byte marker = raw[cursor + 1];
            int length = BinaryPrimitives.ReadUInt16BigEndian(raw.AsSpan(cursor + 2));

            // SOF0 through SOF15, less the ones that are not frame headers.
            if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
            {
                return (
                    BinaryPrimitives.ReadUInt16BigEndian(raw.AsSpan(cursor + 7)),
                    BinaryPrimitives.ReadUInt16BigEndian(raw.AsSpan(cursor + 5)));
            }

            cursor += 2 + length;
        }

        throw new InvalidDataException("No start-of-frame marker.");
    }

    /// <summary>The sizes an .ico declares, in order.</summary>
    private static List<int> IconSizes(byte[] raw)
    {
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(raw));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(2)));

        int count = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(4));
        List<int> sizes = [];

        for (int i = 0; i < count; i++)
        {
            int entry = 6 + (i * 16);

            // The width byte holds 0 for 256, which does not fit in it.
            sizes.Add(raw[entry] == 0 ? 256 : raw[entry]);

            int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(entry + 8));
            int offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(entry + 12));

            // Every entry has to point at bytes that are really there; a
            // malformed directory is the failure mode of hand-built icons.
            Assert.InRange(offset, 6 + (count * 16), raw.Length);
            Assert.InRange(offset + size, offset, raw.Length);
        }

        return sizes;
    }

    [Fact]
    public void TheGraphicAndTheIconBothShip()
    {
        HeadlessAppSession.Run(() =>
        {
            Assert.True(AssetLoader.Exists(new Uri(SplashUri)), SplashUri);
            Assert.True(AssetLoader.Exists(new Uri(IconUri)), IconUri);
        });
    }

    /// <summary>
    /// The graphic is the size the original composed it at.
    /// </summary>
    /// <remarks>
    /// Both windows show it unscaled and the about box's width is chosen to
    /// match, so a replacement of another size would letterbox or crop rather
    /// than fail outright.
    /// </remarks>
    [Fact]
    public void TheGraphicIsTheOriginalSize()
    {
        HeadlessAppSession.Run(() =>
        {
            Assert.Equal((454, 158), JpegSize(Asset(SplashUri)));
        });
    }

    /// <summary>
    /// The icon carries the 1.8 original plus its integer upscales.
    /// </summary>
    /// <remarks>
    /// The 2004 executable shipped 32x32 alone, which Windows blurs wherever it
    /// wants something bigger. The larger entries are exact nearest-neighbour
    /// multiples of it, so the pixel art stays crisp and nothing is invented.
    /// </remarks>
    [Fact]
    public void TheIconDeclaresTheOriginalSizeAndItsMultiples()
    {
        HeadlessAppSession.Run(() =>
        {
            Assert.Equal([32, 64, 128, 256], IconSizes(Asset(IconUri)));
        });
    }

    /// <summary>One decode, shared by the splash and the about box.</summary>
    [Fact]
    public void TheGraphicIsDecodedOnce()
    {
        HeadlessAppSession.Run(() => Assert.Same(Artwork.Splash(), Artwork.Splash()));
    }
}
