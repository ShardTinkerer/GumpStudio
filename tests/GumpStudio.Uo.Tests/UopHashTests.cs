using GumpStudio.Uo.Files;

using Xunit;

namespace GumpStudio.Uo.Tests;

/// <summary>
/// Known-answer tests for the UOP path hash.
/// </summary>
/// <remarks>
/// The expected values were read out of the entry table of a real
/// <c>gumpartLegacyMUL.uop</c> and <c>artLegacyMUL.uop</c> shipped with a retail
/// client, so they pin the algorithm to what clients actually use. This is the
/// check a synthetic fixture cannot provide: a package built by our own writer
/// would share any mistake in the hash and still round-trip perfectly.
/// </remarks>
public class UopHashTests
{
    private const string GumpPattern = "build/gumpartlegacymul/{0:D8}.tga";
    private const string ArtPattern = "build/artlegacymul/{0:D8}.tga";

    [Theory]
    [InlineData(0, 0xECD0CC1754B6A0D8UL)]
    [InlineData(1, 0x43F086DEE0C19779UL)]
    [InlineData(2, 0x48FFDC11739AD455UL)]
    [InlineData(3, 0xDA03D83FDAB497D5UL)]
    [InlineData(4, 0xE8ACFB85F51CBD86UL)]
    public void MatchesHashesTakenFromARealGumpPackage(int index, ulong expected) =>
        Assert.Equal(expected, UopHash.ComputeForIndex(GumpPattern, index));

    [Fact]
    public void MatchesHashesTakenFromARealArtPackage() =>
        // build/artlegacymul/00016384.tga, the first static art entry.
        Assert.Equal(
            UopHash.Compute("build/artlegacymul/00016384.tga"),
            UopHash.ComputeForIndex(ArtPattern, 0x4000));

    [Fact]
    public void IsDeterministic()
    {
        ulong first = UopHash.Compute("build/gumpartlegacymul/00000123.tga");
        ulong second = UopHash.Compute("build/gumpartlegacymul/00000123.tga");

        Assert.Equal(first, second);
    }

    [Fact]
    public void DistinguishesAdjacentIndices()
    {
        HashSet<ulong> seen = [];

        for (int i = 0; i < 5000; i++)
        {
            Assert.True(
                seen.Add(UopHash.ComputeForIndex(GumpPattern, i)),
                $"Hash collision at index {i}.");
        }
    }

    /// <summary>
    /// The index formatter writes into a stack buffer and lowercases as it
    /// copies, rather than building two strings per call. It has to agree with
    /// the formatting it replaced, for every shape of pattern.
    /// </summary>
    [Theory]
    [InlineData("build/gumpartlegacymul/{0:D8}.tga", 0)]
    [InlineData("build/gumpartlegacymul/{0:D8}.tga", 1)]
    [InlineData("build/gumpartlegacymul/{0:D8}.tga", 65535)]
    [InlineData("build/artlegacymul/{0:D8}.tga", 81919)]
    [InlineData("BUILD/GumpArtLegacyMUL/{0:D8}.TGA", 1234)]
    [InlineData("{0:D8}", 7)]
    [InlineData("{0:D8}.tga", 7)]
    [InlineData("prefix/{0:D8}", 7)]
    public void FormattingAnIndexMatchesTheStringItReplaced(string pattern, int index)
    {
        ulong expected = UopHash.Compute(
            string.Format(System.Globalization.CultureInfo.InvariantCulture, pattern, index)
                .ToLowerInvariant());

        Assert.Equal(expected, UopHash.ComputeForIndex(pattern, index));
    }

    /// <summary>
    /// A pattern without the placeholder this understands still has to hash
    /// what the formatter would have produced.
    /// </summary>
    [Fact]
    public void AnUnrecognisedPatternFallsBackToFormatting()
    {
        const string Pattern = "build/other/{0}.tga";

        ulong expected = UopHash.Compute(
            string.Format(System.Globalization.CultureInfo.InvariantCulture, Pattern, 42)
                .ToLowerInvariant());

        Assert.Equal(expected, UopHash.ComputeForIndex(Pattern, 42));
    }

    [Fact]
    public void ALongPatternIsHandledBeyondTheStackBuffer()
    {
        string pattern = "build/" + new string('x', 400) + "/{0:D8}.tga";

        ulong expected = UopHash.Compute(
            string.Format(System.Globalization.CultureInfo.InvariantCulture, pattern, 9)
                .ToLowerInvariant());

        Assert.Equal(expected, UopHash.ComputeForIndex(pattern, 9));
    }

    [Fact]
    public void HandlesPathsShorterThanOneMixingBlock()
    {
        // The tail-only branch is a separate code path from the 12-char loop.
        Assert.NotEqual(0UL, UopHash.Compute("a"));
        Assert.NotEqual(UopHash.Compute("a"), UopHash.Compute("b"));
        Assert.NotEqual(UopHash.Compute("abc"), UopHash.Compute("abd"));
    }
}
