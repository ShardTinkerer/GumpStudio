using GumpStudio.TestSupport;
using GumpStudio.Uo.Files;

using Xunit;

namespace GumpStudio.Uo.Tests;

/// <summary>
/// Container-level tests for the UOP reader.
/// </summary>
/// <remarks>
/// These build packages with <see cref="UopFixture"/> and address entries with
/// the production hash, so they prove the header/block/payload plumbing and the
/// compression and dimension-prefix handling. They cannot prove the hash
/// constant itself is the one real clients use — only a real
/// <c>gumpartLegacyMUL.uop</c> can, which is what
/// <c>UopRealClientTests</c> is for.
/// </remarks>
public class UopFileProviderTests
{
    private const string Pattern = "build/gumpartlegacymul/{0:D8}.tga";

    private static readonly byte[] PayloadZero = [0x10, 0x20, 0x30, 0x40, 0x50];
    private static readonly byte[] PayloadSeven = [0xDE, 0xAD, 0xBE, 0xEF];

    [Fact]
    public void ReadsUncompressedEntriesByIndex()
    {
        using TempDirectory dir = new();

        string path = new UopFixture()
            .Add(UopHash.ComputeForIndex(Pattern, 0), PayloadZero)
            .Add(UopHash.ComputeForIndex(Pattern, 7), PayloadSeven)
            .Write(dir.Path);

        using UopFileProvider provider = UopFileProvider.Open(path, Pattern, maxEntries: 16);

        Assert.Equal(PayloadZero, provider.Read(0).ToArray());
        Assert.Equal(PayloadSeven, provider.Read(7).ToArray());

        // Indices with no matching hash are simply absent.
        Assert.False(provider.GetEntry(1).Exists);
        Assert.True(provider.Read(1).IsEmpty);
    }

    [Fact]
    public void ReadsCompressedEntries()
    {
        using TempDirectory dir = new();

        // Compressible payload, so the deflate path is genuinely exercised.
        byte[] payload = new byte[4096];

        for (int i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(i % 7);
        }

        string path = new UopFixture()
            .Add(UopHash.ComputeForIndex(Pattern, 3), payload, compress: true)
            .Write(dir.Path);

        using UopFileProvider provider = UopFileProvider.Open(path, Pattern, maxEntries: 8);

        Assert.Equal(payload, provider.Read(3).ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StripsGumpDimensionPrefixAndExposesItAsExtra(bool compressed)
    {
        using TempDirectory dir = new();

        string path = new UopFixture()
            .AddWithDimensions(
                UopHash.ComputeForIndex(Pattern, 2),
                PayloadZero,
                width: 300,
                height: 150,
                compress: compressed)
            .Write(dir.Path);

        using UopFileProvider provider =
            UopFileProvider.Open(path, Pattern, maxEntries: 8, hasExtraDimensions: true);

        UoFileEntry entry = provider.GetEntry(2);

        Assert.Equal(300, entry.ExtraWidth);
        Assert.Equal(150, entry.ExtraHeight);

        // The prefix must not leak into the pixel data, whichever way it is stored.
        Assert.Equal(PayloadZero, provider.Read(2).ToArray());
        Assert.Equal(PayloadZero.Length, entry.Length);
    }

    [Fact]
    public void WalksMultipleBlocks()
    {
        using TempDirectory dir = new();

        UopFixture fixture = new() { BlockSize = 4 };
        const int Count = 19;

        for (int i = 0; i < Count; i++)
        {
            fixture.Add(UopHash.ComputeForIndex(Pattern, i), [(byte)i, (byte)(i + 1)]);
        }

        string path = fixture.Write(dir.Path);

        using UopFileProvider provider = UopFileProvider.Open(path, Pattern, maxEntries: Count);

        for (int i = 0; i < Count; i++)
        {
            Assert.Equal([(byte)i, (byte)(i + 1)], provider.Read(i).ToArray());
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(99)]
    public void OutOfRangeIndexIsMissingRatherThanThrowing(int index)
    {
        using TempDirectory dir = new();

        string path = new UopFixture()
            .Add(UopHash.ComputeForIndex(Pattern, 0), PayloadZero)
            .Write(dir.Path);

        using UopFileProvider provider = UopFileProvider.Open(path, Pattern, maxEntries: 4);

        Assert.False(provider.GetEntry(index).Exists);
        Assert.True(provider.Read(index).IsEmpty);
    }

    [Fact]
    public void RejectsAFileThatIsNotAUopPackage()
    {
        using TempDirectory dir = new();

        string path = Path.Combine(dir.Path, "not-a-package.uop");
        File.WriteAllBytes(path, new byte[64]);

        InvalidDataException error = Assert.Throws<InvalidDataException>(
            () => UopFileProvider.Open(path, Pattern, maxEntries: 4));

        Assert.Contains("not a UOP package", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsATruncatedHeader()
    {
        using TempDirectory dir = new();

        string path = Path.Combine(dir.Path, "truncated.uop");
        File.WriteAllBytes(path, [0x4D, 0x59, 0x50, 0x00]);

        Assert.Throws<InvalidDataException>(() => UopFileProvider.Open(path, Pattern, maxEntries: 4));
    }
}
