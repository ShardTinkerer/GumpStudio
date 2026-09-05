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

    [Fact]
    public void HandlesPathsShorterThanOneMixingBlock()
    {
        // The tail-only branch is a separate code path from the 12-char loop.
        Assert.NotEqual(0UL, UopHash.Compute("a"));
        Assert.NotEqual(UopHash.Compute("a"), UopHash.Compute("b"));
        Assert.NotEqual(UopHash.Compute("abc"), UopHash.Compute("abd"));
    }
}
