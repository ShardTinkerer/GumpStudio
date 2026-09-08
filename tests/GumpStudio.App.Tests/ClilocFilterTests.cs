using GumpStudio.App.Controls;

using Xunit;

namespace GumpStudio.App.Tests;

/// <summary>
/// The cliloc search rule.
/// </summary>
/// <remarks>
/// Pure, so these need no headless application and no dispatcher — which is the
/// reason the rule lives in its own class rather than inside the panel.
/// </remarks>
public class ClilocFilterTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyQueryMatchesEverything(string query)
    {
        Assert.True(ClilocFilter.Matches(1044017, "a sturdy pickaxe", query));
    }

    [Fact]
    public void ANumberMatchesAnIdOnItsPrefix()
    {
        Assert.True(ClilocFilter.Matches(1044017, "a sturdy pickaxe", "1044"));
        Assert.True(ClilocFilter.Matches(1044017, "a sturdy pickaxe", "1044017"));
    }

    /// <summary>
    /// An id matches on a prefix, not anywhere inside it.
    /// </summary>
    /// <remarks>
    /// Deliberately unlike the art browsers, which match an id substring. Cliloc
    /// ids are seven digits and consecutive, so a substring match returns
    /// hundreds of unrelated rows and typing more of the number makes the list
    /// no shorter.
    /// </remarks>
    [Fact]
    public void ANumberDoesNotMatchTheMiddleOfAnId()
    {
        Assert.False(ClilocFilter.Matches(31044017, "a sturdy pickaxe", "1044"));
    }

    /// <summary>A run of digits is tried against the text as well.</summary>
    [Fact]
    public void ANumberStillMatchesTheTextItAppearsIn()
    {
        Assert.True(ClilocFilter.Matches(31044017, "weighs 1044 stones", "1044"));
    }

    [Theory]
    [InlineData("vendor")]
    [InlineData("VENDOR")]
    [InlineData("Vendor")]
    public void TextMatchesAnywhereAndIgnoresCase(string query)
    {
        Assert.True(ClilocFilter.Matches(1062724, "the vendor's price", query));
    }

    [Fact]
    public void TextThatIsNotThereDoesNotMatch()
    {
        Assert.False(ClilocFilter.Matches(1062724, "the vendor's price", "blacksmith"));
    }

    /// <summary>
    /// Hex is not a cliloc spelling.
    /// </summary>
    /// <remarks>
    /// The art browsers accept <c>0x…</c> because server scripts quote gump and
    /// item ids in hex. Cliloc ids are decimal everywhere, so this stays a
    /// two-arm rule.
    /// </remarks>
    [Fact]
    public void AHexQueryIsNotTreatedAsAnId()
    {
        Assert.False(ClilocFilter.Matches(1044, "a sturdy pickaxe", "0x414"));
    }

    [Fact]
    public void SurroundingSpaceIsIgnored()
    {
        Assert.True(ClilocFilter.Matches(1044017, "a sturdy pickaxe", "  1044  "));
        Assert.True(ClilocFilter.Matches(1044017, "a sturdy pickaxe", "  sturdy "));
    }
}
