using GumpStudio.TestSupport;
using GumpStudio.Uo.Files;

using Xunit;

namespace GumpStudio.Uo.Tests;

/// <summary>
/// The window of recently decoded payloads.
/// </summary>
/// <remarks>
/// This used to be a memo of the single most recent payload, which only helped
/// while requests arrived one at a time in the order the reader expected. An art
/// browser interleaves indices, and on a container that keeps its dimensions
/// inside the payload every entry lookup is itself a decode — so revisiting an
/// entry meant a fresh inflate, and a MegaCliloc pass with it.
///
/// What matters here is that widening the window changed nothing about what is
/// read back, including once entries start being evicted.
/// </remarks>
public class UopPayloadWindowTests
{
    private const string Pattern = "build/gumpartlegacymul/{0:D8}.tga";

    /// <summary>A payload that compresses, so the decode path is real work.</summary>
    private static byte[] Payload(int index, int length)
    {
        byte[] payload = new byte[length];

        for (int i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)((i + index) % 251);
        }

        return payload;
    }

    [Fact]
    public void RereadingAnEntryReturnsTheSameBytes()
    {
        using TempDirectory dir = new();

        byte[] payload = Payload(3, 4096);

        string path = new UopFixture()
            .Add(UopHash.ComputeForIndex(Pattern, 3), payload, compress: true)
            .Write(dir.Path);

        using UopFileProvider provider = UopFileProvider.Open(path, Pattern, maxEntries: 16);

        byte[] first = provider.Read(3).ToArray();
        byte[] second = provider.Read(3).ToArray();
        byte[] third = provider.Read(3).ToArray();

        Assert.Equal(payload, first);
        Assert.Equal(payload, second);
        Assert.Equal(payload, third);
    }

    [Fact]
    public void InterleavedReadsEachReturnTheirOwnPayload()
    {
        using TempDirectory dir = new();

        UopFixture fixture = new();

        for (int index = 0; index < 8; index++)
        {
            fixture.Add(UopHash.ComputeForIndex(Pattern, index), Payload(index, 2048), compress: true);
        }

        string path = fixture.Write(dir.Path);

        using UopFileProvider provider = UopFileProvider.Open(path, Pattern, maxEntries: 16);

        // Alternating, then reversed: the pattern the one-entry memo could not
        // help with at all.
        foreach (int index in new[] { 0, 7, 1, 6, 2, 5, 3, 4, 4, 3, 5, 2, 6, 1, 7, 0 })
        {
            Assert.Equal(Payload(index, 2048), provider.Read(index).ToArray());
        }
    }

    /// <summary>
    /// The window is bounded by bytes, so a container larger than the budget
    /// evicts — and an evicted entry has to decode again to the same bytes.
    /// </summary>
    [Fact]
    public void EvictionDoesNotChangeWhatIsReadBack()
    {
        using TempDirectory dir = new();

        // Two megabytes an entry, twenty entries: forty megabytes against a
        // thirty-two megabyte budget, so the window certainly turns over.
        const int Length = 2 * 1024 * 1024;
        const int Count = 20;

        UopFixture fixture = new();

        for (int index = 0; index < Count; index++)
        {
            fixture.Add(UopHash.ComputeForIndex(Pattern, index), Payload(index, Length), compress: true);
        }

        string path = fixture.Write(dir.Path);

        using UopFileProvider provider = UopFileProvider.Open(path, Pattern, maxEntries: 32);

        for (int index = 0; index < Count; index++)
        {
            Assert.Equal(Length, provider.Read(index).Length);
        }

        // Back to the first, which must have been evicted by now.
        Assert.Equal(Payload(0, Length), provider.Read(0).ToArray());
        Assert.Equal(Payload(Count - 1, Length), provider.Read(Count - 1).ToArray());
    }

    [Fact]
    public void AnAbsentIndexIsStillAbsentAfterOtherReads()
    {
        using TempDirectory dir = new();

        string path = new UopFixture()
            .Add(UopHash.ComputeForIndex(Pattern, 2), Payload(2, 512), compress: true)
            .Write(dir.Path);

        using UopFileProvider provider = UopFileProvider.Open(path, Pattern, maxEntries: 16);

        Assert.True(provider.Read(5).IsEmpty);

        _ = provider.Read(2);

        Assert.True(provider.Read(5).IsEmpty);
        Assert.False(provider.GetEntry(5).Exists);
    }

    [Fact]
    public void ConcurrentReadsOfDifferentEntriesAgree()
    {
        using TempDirectory dir = new();

        UopFixture fixture = new();

        for (int index = 0; index < 16; index++)
        {
            fixture.Add(UopHash.ComputeForIndex(Pattern, index), Payload(index, 8192), compress: true);
        }

        string path = fixture.Write(dir.Path);

        using UopFileProvider provider = UopFileProvider.Open(path, Pattern, maxEntries: 32);

        // The window is shared mutable state behind one lock; a browser decoding
        // thumbnails on the pool while the canvas reads on another thread is the
        // case it has to survive.
        Parallel.For(0, 256, i =>
        {
            int index = i % 16;

            Assert.Equal(Payload(index, 8192), provider.Read(index).ToArray());
        });
    }
}
