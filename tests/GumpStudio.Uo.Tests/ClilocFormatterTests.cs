using GumpStudio.Uo.Data;

using Xunit;

namespace GumpStudio.Uo.Tests;

/// <summary>
/// Argument substitution, which used to be private to the renderer.
/// </summary>
/// <remarks>
/// It moved here so the properties editor can preview the same string the canvas
/// draws. The renderer's own tests still cover it end to end; these cover the
/// cases a painter test cannot reach cheaply, several of which were untested
/// while the logic was private.
/// </remarks>
public class ClilocFormatterTests
{
    private static Func<int, string?> Lookup(params (int Id, string Text)[] entries)
    {
        Dictionary<int, string> strings = [];

        foreach ((int id, string text) in entries)
        {
            strings[id] = text;
        }

        return id => strings.TryGetValue(id, out string? text) ? text : null;
    }

    [Fact]
    public void FillsPlaceholdersFromTheArguments()
    {
        Assert.Equal(
            "Bob has 42 left",
            ClilocFormatter.Substitute("~1_NAME~ has ~2_COUNT~ left", "Bob@42", Lookup()));
    }

    /// <summary>Slots are filled by number, not by the order they appear.</summary>
    [Fact]
    public void SubstitutesByOrdinalNotByPosition()
    {
        Assert.Equal("y x", ClilocFormatter.Substitute("~2_B~ ~1_A~", "x@y", Lookup()));
    }

    /// <summary>
    /// A missing value is left visible rather than blanked.
    /// </summary>
    /// <remarks>
    /// Silently emptying it would make a gump look finished when its arguments
    /// are wrong, which is the opposite of what a preview is for.
    /// </remarks>
    [Fact]
    public void KeepsAPlaceholderThatHasNoArgument()
    {
        Assert.Equal("~2_COUNT~", ClilocFormatter.Substitute("~2_COUNT~", "only-one", Lookup()));
    }

    [Fact]
    public void KeepsAnUnterminatedTildeVerbatim()
    {
        Assert.Equal("half ~1_A", ClilocFormatter.Substitute("half ~1_A", "x", Lookup()));
    }

    [Theory]
    [InlineData("~FOO~")]
    [InlineData("~0_A~")]
    [InlineData("~-1_A~")]
    public void KeepsANonNumericOrOutOfRangePlaceholderVerbatim(string text)
    {
        Assert.Equal(text, ClilocFormatter.Substitute(text, "x@y", Lookup()));
    }

    /// <summary>The name after the underscore is documentation, so it is optional.</summary>
    [Fact]
    public void AcceptsAPlaceholderWithNoUnderscore()
    {
        Assert.Equal("x", ClilocFormatter.Substitute("~1~", "x", Lookup()));
    }

    /// <summary>
    /// The client runs consecutive delimiters together, the way strtok does.
    /// </summary>
    [Fact]
    public void TreatsARunOfDelimitersAsOneSeparator()
    {
        Assert.Equal(
            "potion",
            ClilocFormatter.Substitute("~1_ITEM~", "@@#1072325", Lookup((1072325, "potion"))));
    }

    [Fact]
    public void ResolvesAnArgumentThatIsItselfACliloc()
    {
        Assert.Equal(
            "a mortar and pestle",
            ClilocFormatter.Substitute(
                "~1_ITEM~", "#1025181", Lookup((1025181, "a mortar and pestle"))));
    }

    [Theory]
    [InlineData("#999")]
    [InlineData("#notanumber")]
    public void KeepsAnUnresolvableClilocArgumentAsWritten(string argument)
    {
        Assert.Equal(argument, ClilocFormatter.Substitute("~1_X~", argument, Lookup()));
    }

    /// <summary>
    /// A nested string is resolved once, never recursively.
    /// </summary>
    /// <remarks>
    /// The client does not recurse either, and a cliloc that referenced itself
    /// would otherwise be an infinite loop inside a repaint.
    /// </remarks>
    [Fact]
    public void DoesNotRecurseIntoASubstitutedString()
    {
        Assert.Equal(
            "holds ~1_INNER~",
            ClilocFormatter.Substitute("~1_X~", "#500", Lookup((500, "holds ~1_INNER~"))));
    }

    [Fact]
    public void FallsBackToTheHashIdWhenTheClilocIsUnknown()
    {
        Assert.Equal("#1044017", ClilocFormatter.Format(1044017, "", Lookup()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void FormatsWithoutArgumentsWhenThereAreNone(string? arguments)
    {
        Assert.Equal(
            "~1_VAL~ stones",
            ClilocFormatter.Format(1000, arguments, Lookup((1000, "~1_VAL~ stones"))));
    }

    [Fact]
    public void FormatsWithArgumentsWhenThereAre()
    {
        Assert.Equal("12 stones", ClilocFormatter.Format(1000, "12", Lookup((1000, "~1_VAL~ stones"))));
    }

    /// <summary>
    /// The count is the highest ordinal, not how many placeholders there are.
    /// </summary>
    /// <remarks>
    /// A string using only <c>~2_ONLY~</c> still needs two <c>@</c>-separated
    /// values, because the client fills slots by number. Counting distinct
    /// placeholders would report one and be wrong.
    /// </remarks>
    [Theory]
    [InlineData("~1_A~ ~3_B~", 3)]
    [InlineData("~2_ONLY~", 2)]
    [InlineData("~1_A~ and ~1_A~", 1)]
    [InlineData("plain text", 0)]
    [InlineData("~FOO~", 0)]
    [InlineData("unterminated ~1_A", 0)]
    public void ArgumentCountReportsTheHighestOrdinal(string text, int expected)
    {
        Assert.Equal(expected, ClilocFormatter.ArgumentCount(text));
    }
}
