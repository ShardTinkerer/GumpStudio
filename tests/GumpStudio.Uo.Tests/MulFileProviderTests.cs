using GumpStudio.TestSupport;
using GumpStudio.Uo.Files;

using Xunit;

namespace GumpStudio.Uo.Tests;

public class MulFileProviderTests
{
    private static readonly byte[] First = [1, 2, 3, 4];
    private static readonly byte[] Second = [9, 9, 9];

    [Fact]
    public void ReadsRecordsBackInOrder()
    {
        using TempDirectory dir = new();

        (string indexPath, string dataPath) = new MulFixture()
            .Add(First, extra: 0x00280014)
            .Add(Second)
            .Write(dir.Path);

        using MulFileProvider provider = MulFileProvider.Open(indexPath, dataPath, UoFileKind.Art);

        Assert.Equal(2, provider.Count);
        Assert.Equal(First, provider.Read(0).ToArray());
        Assert.Equal(Second, provider.Read(1).ToArray());
    }

    [Fact]
    public void DecodesGumpDimensionsFromExtra()
    {
        using TempDirectory dir = new();

        // Width 40 (0x28) in the high half, height 20 (0x14) in the low half.
        (string indexPath, string dataPath) = new MulFixture()
            .Add(First, extra: 0x0028_0014)
            .Write(dir.Path);

        using MulFileProvider provider = MulFileProvider.Open(indexPath, dataPath, UoFileKind.Gumps);
        UoFileEntry entry = provider.GetEntry(0);

        Assert.Equal(40, entry.ExtraWidth);
        Assert.Equal(20, entry.ExtraHeight);
    }

    [Fact]
    public void TreatsNegativeLookupAsMissing()
    {
        using TempDirectory dir = new();

        (string indexPath, string dataPath) = new MulFixture()
            .AddMissing()
            .Add(First)
            .Write(dir.Path);

        using MulFileProvider provider = MulFileProvider.Open(indexPath, dataPath, UoFileKind.Art);

        Assert.False(provider.GetEntry(0).Exists);
        Assert.True(provider.Read(0).IsEmpty);
        Assert.True(provider.GetEntry(1).Exists);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(int.MaxValue)]
    public void OutOfRangeIndexIsMissingRatherThanThrowing(int index)
    {
        using TempDirectory dir = new();

        (string indexPath, string dataPath) = new MulFixture()
            .Add(First)
            .Add(Second)
            .Write(dir.Path);

        using MulFileProvider provider = MulFileProvider.Open(indexPath, dataPath, UoFileKind.Art);

        Assert.False(provider.GetEntry(index).Exists);
        Assert.True(provider.Read(index).IsEmpty);
    }

    [Fact]
    public void EntryPointingPastEndOfTruncatedDataFileIsRejected()
    {
        using TempDirectory dir = new();

        // The index still claims both records, but the .mul has lost the second.
        (string indexPath, string dataPath) = new MulFixture()
            .Add(First)
            .Add(Second)
            .Write(dir.Path, truncateDataBy: Second.Length);

        using MulFileProvider provider = MulFileProvider.Open(indexPath, dataPath, UoFileKind.Art);

        Assert.True(provider.GetEntry(0).Exists);

        // The old loader would hand a decoder a buffer of zeroes here, which then
        // decoded as real pixel data. It must read as absent instead.
        Assert.False(provider.GetEntry(1).Exists);
        Assert.True(provider.Read(1).IsEmpty);
    }

    [Fact]
    public void SizesIndexFromFileRatherThanAHardCodedMaximum()
    {
        using TempDirectory dir = new();

        MulFixture fixture = new();

        for (int i = 0; i < 300; i++)
        {
            fixture.Add([(byte)i]);
        }

        (string indexPath, string dataPath) = fixture.Write(dir.Path);

        using MulFileProvider provider = MulFileProvider.Open(indexPath, dataPath, UoFileKind.Art);

        Assert.Equal(300, provider.Count);
        Assert.Equal([255], provider.Read(255).ToArray());
    }

    [Fact]
    public void VerdataPatchOverridesPrimaryEntry()
    {
        using TempDirectory dir = new();

        byte[] patched = [0xAA, 0xBB, 0xCC];

        (string indexPath, string dataPath) = new MulFixture()
            .Add(First)
            .Add(Second)
            .Write(dir.Path);

        string verdataPath = MulFixture.WriteVerdata(
            dir.Path,
            [new VerdataRecord((int)UoFileKind.Art, Index: 1, patched, Extra: 0x1234)]);

        using VerdataPatchSet verdata = VerdataPatchSet.Load(verdataPath);
        using MulFileProvider provider =
            MulFileProvider.Open(indexPath, dataPath, UoFileKind.Art, verdata);

        Assert.Equal(1, verdata.Count);
        Assert.Equal(First, provider.Read(0).ToArray());
        Assert.Equal(patched, provider.Read(1).ToArray());
        Assert.Equal(UoEntrySource.Verdata, provider.GetEntry(1).Source);
        Assert.Equal(0x1234, provider.GetEntry(1).Extra);
    }

    [Fact]
    public void VerdataPatchForAnotherContainerIsIgnored()
    {
        using TempDirectory dir = new();

        (string indexPath, string dataPath) = new MulFixture()
            .Add(First)
            .Write(dir.Path);

        string verdataPath = MulFixture.WriteVerdata(
            dir.Path,
            [new VerdataRecord((int)UoFileKind.Gumps, Index: 0, [0xFF], Extra: 0)]);

        using VerdataPatchSet verdata = VerdataPatchSet.Load(verdataPath);
        using MulFileProvider provider =
            MulFileProvider.Open(indexPath, dataPath, UoFileKind.Art, verdata);

        Assert.Equal(First, provider.Read(0).ToArray());
        Assert.Equal(UoEntrySource.Primary, provider.GetEntry(0).Source);
    }

    [Fact]
    public void MissingVerdataLoadsAsEmptyRatherThanThrowing()
    {
        using TempDirectory dir = new();

        VerdataPatchSet verdata = VerdataPatchSet.Load(Path.Combine(dir.Path, "does-not-exist.mul"));

        Assert.Equal(0, verdata.Count);
        Assert.Same(VerdataPatchSet.Empty, verdata);
    }
}
