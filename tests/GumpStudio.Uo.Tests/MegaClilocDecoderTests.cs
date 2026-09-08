using System.Buffers.Binary;
using System.Text;

using GumpStudio.TestSupport;
using GumpStudio.Uo.Files;

using Xunit;

namespace GumpStudio.Uo.Tests;

/// <summary>
/// Covers the MegaCliloc codec without a client.
/// </summary>
/// <remarks>
/// The decoder used to be reachable only through real client files, so CI —
/// which has none — never exercised it at all. <see cref="MegaClilocFixture"/>
/// is an independent encoder written from the format, which is what lets these
/// run anywhere.
/// </remarks>
public class MegaClilocDecoderTests
{
    /// <summary>
    /// The mask is the one thing here that cannot be derived, so it is pinned
    /// against a real file's header.
    /// </summary>
    /// <remarks>
    /// These four bytes open the compressed <c>Cliloc.deu</c> of a 7.0.114.4
    /// client, and 706,468 is the exact size of the same file shipped
    /// uncompressed by a 7.0.50.0 client. No client data is needed to check it:
    /// the header is four bytes and the answer is a number.
    /// </remarks>
    [Fact]
    public void ReadsTheDeclaredLengthOutOfARealHeader()
    {
        byte[] header = [0x99, 0x5D, 0x26, 0x8E];

        Assert.Equal(
            706468u, BinaryPrimitives.ReadUInt32LittleEndian(header) ^ 0x8E2C9A3D);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(4096)]
    public void RoundTripsARepeatedByte(int length) =>
        AssertRoundTrips(Enumerable.Repeat((byte)0x42, length).ToArray());

    [Fact]
    public void RoundTripsEveryByteValueOnce() =>
        AssertRoundTrips(Enumerable.Range(0, 256).Select(value => (byte)value).ToArray());

    [Fact]
    public void RoundTripsEveryByteValueManyTimes() =>
        AssertRoundTrips(
            Enumerable.Range(0, 256 * 40).Select(value => (byte)(value % 256)).ToArray());

    /// <summary>
    /// Runs are what the zero move code exists for, so a payload made of them
    /// drives the branch a random buffer barely touches.
    /// </summary>
    [Fact]
    public void RoundTripsLongRuns()
    {
        List<byte> plain = [];

        for (int symbol = 0; symbol < 16; symbol++)
        {
            plain.AddRange(Enumerable.Repeat((byte)(symbol * 16), 500));
        }

        AssertRoundTrips([.. plain]);
    }

    /// <summary>
    /// Two symbols alternating force a reinsertion at rank one on every single
    /// byte, which is the tightest the symbol table is ever driven.
    /// </summary>
    [Fact]
    public void RoundTripsAnAlternatingPair() =>
        AssertRoundTrips(
            Enumerable.Range(0, 2000).Select(i => (byte)(i % 2 == 0 ? 'a' : 'b')).ToArray());

    [Fact]
    public void RoundTripsTextThatLooksLikeACliloc() =>
        AssertRoundTrips(
            Encoding.UTF8.GetBytes(
                string.Concat(
                    Enumerable.Range(500000, 400)
                        .Select(id => $"{id}\tYou see a ~1_val~ here, and it is ~2_val~.\n"))));

    [Theory]
    [InlineData(1)]
    [InlineData(64)]
    [InlineData(1000)]
    [InlineData(65536)]
    public void RoundTripsRandomBytes(int length)
    {
        byte[] plain = new byte[length];

        new Random(length).NextBytes(plain);

        AssertRoundTrips(plain);
    }

    /// <summary>
    /// A skewed distribution is what real files look like, and it is what makes
    /// the descending-frequency ordering do any work.
    /// </summary>
    [Fact]
    public void RoundTripsASkewedDistribution()
    {
        Random random = new(1337);
        byte[] plain = new byte[20000];

        for (int i = 0; i < plain.Length; i++)
        {
            plain[i] = random.Next(100) switch
            {
                < 70 => (byte)' ',
                < 90 => (byte)random.Next('a', 'z' + 1),
                _ => (byte)random.Next(256),
            };
        }

        AssertRoundTrips(plain);
    }

    /// <summary>
    /// Shipped files carry several kilobytes past the end of the code stream, so
    /// the decoder has to take its size from the header rather than the payload.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(15368)]
    public void IgnoresTrailingPadding(int padding)
    {
        byte[] plain = Encoding.UTF8.GetBytes("Reputation aversion triggered.");
        byte[] encoded = MegaClilocFixture.Encode(plain, padding);

        Assert.Equal(plain, MegaClilocDecoder.Decompress(encoded));
    }

    [Fact]
    public void RejectsAnEmptyInput() =>
        Assert.Empty(MegaClilocDecoder.Decompress([]));

    [Fact]
    public void RejectsAnInputTooShortToHoldAFrequencyTable() =>
        Assert.Empty(MegaClilocDecoder.Decompress(new byte[512]));

    /// <summary>
    /// A header claiming a length the frequency table does not agree with is the
    /// case that used to pass silently, yielding a buffer of the wrong size that
    /// a caller could not tell from a good one.
    /// </summary>
    [Fact]
    public void RejectsAHeaderTheFrequencyTableContradicts()
    {
        byte[] encoded = MegaClilocFixture.Encode(Encoding.UTF8.GetBytes("a decodable payload"));

        uint declared = BinaryPrimitives.ReadUInt32LittleEndian(encoded) ^ 0x8E2C9A3D;
        BinaryPrimitives.WriteUInt32LittleEndian(encoded, (declared - 1) ^ 0x8E2C9A3D);

        Assert.Empty(MegaClilocDecoder.Decompress(encoded));
    }

    [Fact]
    public void RejectsAPayloadTruncatedIntoItsCodeStream()
    {
        byte[] encoded = MegaClilocFixture.Encode(new byte[4096]);

        Assert.Empty(MegaClilocDecoder.Decompress(encoded.AsSpan(0, encoded.Length - 1)));
    }

    [Fact]
    public void RejectsAHeaderDeclaringAnImpossibleLength()
    {
        byte[] encoded = MegaClilocFixture.Encode(Encoding.UTF8.GetBytes("short"));

        BinaryPrimitives.WriteUInt32LittleEndian(encoded, 0x7FFFFFFF ^ 0x8E2C9A3D);

        Assert.Empty(MegaClilocDecoder.Decompress(encoded));
    }

    /// <summary>
    /// A zero-length payload is not a thing any file carries, and treating it as
    /// one would mean allocating on a header that is entirely mask.
    /// </summary>
    [Fact]
    public void RejectsAHeaderDeclaringNothing()
    {
        byte[] encoded = new byte[4 + (256 * 4) + 16];

        BinaryPrimitives.WriteUInt32LittleEndian(encoded, 0x8E2C9A3D);

        Assert.Empty(MegaClilocDecoder.Decompress(encoded));
    }

    /// <summary>
    /// Every byte of a real payload is load-bearing, so corrupting one has to
    /// either fail the length check or change the output — never be ignored.
    /// </summary>
    [Fact]
    public void DoesNotSilentlyAcceptACorruptedCodeStream()
    {
        byte[] plain = Encoding.UTF8.GetBytes(
            string.Concat(Enumerable.Repeat("the quick brown fox jumps over the lazy dog. ", 40)));

        byte[] encoded = MegaClilocFixture.Encode(plain);

        int at = encoded.Length - 32;
        encoded[at] ^= 0xFF;

        byte[] decoded = MegaClilocDecoder.Decompress(encoded);

        Assert.True(
            decoded.Length == 0 || !decoded.SequenceEqual(plain),
            "A corrupted code stream decoded back to the original.");
    }

    private static void AssertRoundTrips(byte[] plain)
    {
        byte[] encoded = MegaClilocFixture.Encode(plain);

        Assert.Equal(plain, MegaClilocDecoder.Decompress(encoded));
    }
}
