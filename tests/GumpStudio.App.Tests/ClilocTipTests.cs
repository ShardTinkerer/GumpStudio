using Avalonia.Controls;

using GumpStudio.App.Controls;

using Xunit;

namespace GumpStudio.App.Tests;

/// <summary>
/// The hover card behind a cliloc id.
/// </summary>
/// <remarks>
/// The builder takes values rather than a session, so every case it has to word
/// differently — resolved, resolved with arguments, unknown, none, no client —
/// is assertable with no client and no pointer.
/// </remarks>
[Collection("Headless")]
public class ClilocTipTests
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

    private static List<string> Lines(Control card) =>
        [.. ((StackPanel)card).Children.OfType<TextBlock>().Select(t => t.Text ?? string.Empty)];

    private static List<string> Build(
        int id, string arguments, Func<int, string?> lookup, string? unavailable = null) =>
        Lines(ClilocTip.Build(id, arguments, "enu", lookup, unavailable));

    [Fact]
    public void ShowsTheIdTheLanguageAndTheText()
    {
        List<string> lines = Build(1062724, string.Empty, Lookup((1062724, "vendor price")));

        Assert.Equal(["1062724 — ENU", "vendor price"], lines);
    }

    [Fact]
    public void SubstitutesTheArgumentsAndShowsBothForms()
    {
        List<string> lines = Build(1062724, "250", Lookup((1062724, "price: ~1_VAL~")));

        Assert.Equal(["1062724 — ENU", "price: ~1_VAL~", "with 250", "price: 250"], lines);
        Assert.DoesNotContain("~1_VAL~", lines[3], StringComparison.Ordinal);
    }

    /// <summary>A string with no placeholders must not be printed twice.</summary>
    [Fact]
    public void OmitsTheSubstitutedFormWhenItIsUnchanged()
    {
        List<string> lines = Build(1062724, "250", Lookup((1062724, "vendor price")));

        Assert.Equal(["1062724 — ENU", "vendor price"], lines);
    }

    /// <summary>The wording names what the canvas draws instead.</summary>
    [Fact]
    public void SaysWhenTheIdIsNotInTheClientsFile()
    {
        List<string> lines = Build(1044017, string.Empty, Lookup());

        Assert.Equal(2, lines.Count);
        Assert.Contains("#1044017", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void SaysWhenThereIsNoCliloc()
    {
        List<string> lines = Build(0, string.Empty, Lookup());

        Assert.Contains("0 means none", lines[1], StringComparison.Ordinal);
    }

    /// <summary>
    /// An unavailable reason short-circuits before any lookup.
    /// </summary>
    /// <remarks>
    /// The resolver throws, so a card that reached it would fail rather than
    /// quietly resolve a table that is not ready.
    /// </remarks>
    [Fact]
    public void ReportsWhyThereIsNothingToResolveWith()
    {
        List<string> lines = Build(
            1062724,
            "250",
            static _ => throw new InvalidOperationException("must not be asked"),
            "No client loaded.");

        Assert.Equal(["1062724 — ENU", "No client loaded."], lines);
    }

    [Fact]
    public void OmitsTheLanguageWhenThereIsNone()
    {
        List<string> lines =
            Lines(ClilocTip.Build(1062724, string.Empty, null, Lookup(), "No client loaded."));

        Assert.Equal("1062724", lines[0]);
    }
}
