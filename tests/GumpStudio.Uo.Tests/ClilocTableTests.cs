using GumpStudio.TestSupport;
using GumpStudio.Uo.Data;

using Xunit;

namespace GumpStudio.Uo.Tests;

/// <summary>
/// The cliloc reader, driven from synthetic files.
/// </summary>
/// <remarks>
/// Every fact here runs with no client installed, which is what
/// <see cref="ClilocFixture"/> exists for. Before it, a populated table could
/// only be obtained from a real installation, so the reader's edge cases — the
/// tolerated tail, the truncated tail, noise — were untested.
/// </remarks>
public class ClilocTableTests
{
    private static ClilocTable Parse(ClilocFixture fixture, int trailingSlack = 0, int truncateBy = 0) =>
        ClilocTable.Parse(fixture.ToBytes(trailingSlack: trailingSlack, truncateBy: truncateBy));

    [Fact]
    public void ParsesEveryEntryAndItsText()
    {
        ClilocTable table = Parse(new ClilocFixture()
            .Add(1000, "hammer pick")
            .Add(1001, "Zaubertränke", flag: 1)
            .Add(1002, "~1_VAL~ stones", flag: 2));

        Assert.Equal(3, table.Count);
        Assert.Equal("hammer pick", table.GetText(1000));

        // Non-ASCII is the case that catches a length written in characters
        // rather than in UTF-8 bytes.
        Assert.Equal("Zaubertränke", table.GetText(1001));

        Assert.True(table.TryGet(1002, out ClilocEntry added));
        Assert.Equal(ClilocEntryKind.Added, added.Flag);
        Assert.Equal(ClilocEntryKind.Patched, table.Entries.Single(e => e.Id == 1001).Flag);
    }

    [Fact]
    public void OrdersEntriesById()
    {
        ClilocTable table = Parse(new ClilocFixture()
            .Add(500000, "last")
            .Add(1000, "first")
            .Add(1114057, "middle-ish"));

        Assert.Equal([1000, 500000, 1114057], table.Entries.Select(e => e.Id));
    }

    /// <summary>
    /// The ordered snapshot is kept, not rebuilt.
    /// </summary>
    /// <remarks>
    /// The property used to be <c>_entries.Values.OrderBy(...)</c>, which sorted
    /// 124,000 entries on every enumeration — once per keystroke for a browser
    /// that filters as you type. Ordering alone would pass forever without this.
    /// </remarks>
    [Fact]
    public void EnumeratingEntriesTwiceDoesNotResort()
    {
        ClilocTable table = Parse(new ClilocFixture().Add(1, "a").Add(2, "b"));

        Assert.Same(table.Entries, table.Entries);
    }

    [Fact]
    public void ToleratesTrailingBytesWithinTheSlack()
    {
        Assert.Equal(2, Parse(new ClilocFixture().Add(1, "a").Add(2, "b"), trailingSlack: 16).Count);
    }

    /// <summary>
    /// One byte past the tolerated tail and the buffer is judged not to be a
    /// record stream at all.
    /// </summary>
    /// <remarks>
    /// The rule exists because a wrapped file is tried as a plain one first, and
    /// noise parses into a few entries before desynchronising. A genuine file
    /// consumes essentially all of itself; 17 bytes of tail does not.
    /// </remarks>
    [Fact]
    public void RejectsTrailingBytesBeyondTheSlack()
    {
        Assert.Equal(0, Parse(new ClilocFixture().Add(1, "a").Add(2, "b"), trailingSlack: 17).Count);
    }

    [Fact]
    public void ATruncationInsideTheSlackKeepsTheEntriesBeforeIt()
    {
        // The last entry's text is short enough that losing it leaves an
        // unparsed tail within the tolerance.
        ClilocTable table = Parse(
            new ClilocFixture().Add(1, "kept").Add(2, "gone"), truncateBy: 4);

        Assert.Equal("kept", table.GetText(1));
        Assert.Null(table.GetText(2));
    }

    [Fact]
    public void ATruncationBeyondTheSlackYieldsAnEmptyTable()
    {
        ClilocFixture fixture = new();

        for (int id = 1; id <= 40; id++)
        {
            fixture.Add(id, "a string long enough to matter");
        }

        Assert.Equal(0, Parse(fixture, truncateBy: 200).Count);
    }

    [Fact]
    public void AnEmptyOrTooShortBufferYieldsTheEmptyTable()
    {
        Assert.Same(ClilocTable.Empty, ClilocTable.Parse([]));
        Assert.Same(ClilocTable.Empty, ClilocTable.Parse(new byte[5]));
    }

    /// <summary>Noise must fall through both paths without throwing.</summary>
    [Fact]
    public void GarbageParsesToAnEmptyTableWithoutThrowing()
    {
        byte[] noise = new byte[2048];

        new Random(1234).NextBytes(noise);

        Assert.Equal(0, ClilocTable.Parse(noise).Count);
    }

    [Fact]
    public void AVersionOfZeroIsStillARealFile()
    {
        // Version 0 ships, which is why the wrapper cannot be detected by
        // sniffing the header.
        ClilocTable table =
            ClilocTable.Parse(new ClilocFixture().Add(7, "seven").ToBytes(version: 0));

        Assert.Equal("seven", table.GetText(7));
    }

    /// <summary>
    /// The wrapped form modern clients ship reads back the same as the plain one.
    /// </summary>
    /// <remarks>
    /// This used to be reachable only through a real client, because writing it
    /// needs a MegaCliloc encoder. Non-ASCII is in the sample on purpose: a
    /// wrapper that lost or gained a byte would surface as mojibake here rather
    /// than as a clean failure.
    /// </remarks>
    [Fact]
    public void ReadsTheWrappedFormModernClientsShip()
    {
        ClilocFixture fixture = new ClilocFixture()
            .Add(1000, "hammer pick")
            .Add(1001, "Zaubertränke", flag: 1)
            .Add(1002, "~1_VAL~ stones", flag: 2);

        ClilocTable wrapped = ClilocTable.Parse(fixture.ToBytes(wrapped: true));

        Assert.Equal(3, wrapped.Count);
        Assert.Equal("hammer pick", wrapped.GetText(1000));
        Assert.Equal("Zaubertränke", wrapped.GetText(1001));
        Assert.Equal("~1_VAL~ stones", wrapped.GetText(1002));

        Assert.Equal(
            ClilocTable.Parse(fixture.ToBytes()).Entries.Select(entry => entry.Text),
            wrapped.Entries.Select(entry => entry.Text));
    }

    /// <summary>
    /// A wrapped file opening with version 0 is the trap the reader guards
    /// against: the version cannot say which of the two forms this is.
    /// </summary>
    [Fact]
    public void ReadsAWrappedFileThatOpensWithVersionZero()
    {
        ClilocTable table = ClilocTable.Parse(
            new ClilocFixture().Add(7, "seven").ToBytes(version: 0, wrapped: true));

        Assert.Equal("seven", table.GetText(7));
    }

    [Fact]
    public void AMissingIdReadsAsNullTextAndADefaultedEntry()
    {
        ClilocTable table = Parse(new ClilocFixture().Add(1, "a"));

        Assert.Null(table.GetText(999));
        Assert.False(table.TryGet(999, out ClilocEntry missing));
        Assert.Equal(string.Empty, missing.Text);
    }
}
