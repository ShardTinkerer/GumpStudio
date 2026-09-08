using GumpStudio.Core.Document;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Primitives;
using GumpStudio.TestSupport;

using SkiaSharp;

using Xunit;

namespace GumpStudio.Rendering.Tests;

/// <summary>
/// What a localised text area shows in the editor.
/// </summary>
/// <remarks>
/// It used to show the bare cliloc id. That was tolerable while gumps were
/// authored by hand and the author had just typed the id — but a gump imported
/// from a wire capture is often built from nothing but clilocs, and previewed as
/// a wall of numbers.
/// </remarks>
public class ClilocPreviewTests
{
    [Fact]
    public void DrawsTheClilocStringRatherThanItsId()
    {
        List<string> drawn = Render(
            new HtmlElement { ContentKind = HtmlContentKind.Localized, ClilocId = 1044017 },
            clilocs: new() { [1044017] = "MARK ITEM" });

        Assert.Contains("MARK ITEM", drawn);
        Assert.DoesNotContain("#1044017", drawn);
    }

    /// <summary>With no client loaded there is nothing to resolve against.</summary>
    [Fact]
    public void FallsBackToTheIdWhenTheClilocIsUnknown()
    {
        List<string> drawn = Render(
            new HtmlElement { ContentKind = HtmlContentKind.Localized, ClilocId = 1044017 },
            clilocs: []);

        Assert.Contains("#1044017", drawn);
    }

    [Fact]
    public void LeavesLiteralMarkupAlone()
    {
        List<string> drawn = Render(
            new HtmlElement { ContentKind = HtmlContentKind.Html, Html = "<b>hello</b>" },
            clilocs: new() { [1044017] = "MARK ITEM" });

        Assert.Contains("<b>hello</b>", drawn);
    }

    /// <summary>The client numbers placeholders from one and separates values with @.</summary>
    [Fact]
    public void FillsPlaceholdersFromTheArguments()
    {
        List<string> drawn = Render(
            new HtmlElement
            {
                ContentKind = HtmlContentKind.Localized,
                ClilocId = 1000,
                Arguments = "Bob@42",
            },
            clilocs: new() { [1000] = "~1_NAME~ has ~2_COUNT~ left" });

        Assert.Contains("Bob has 42 left", drawn);
    }

    /// <summary>
    /// An argument of the form <c>#1234</c> is itself a cliloc id.
    /// </summary>
    /// <remarks>
    /// This is how <c>xmfhtmltok</c> nests one localised string inside another,
    /// and it is what the captured bulk-order gump uses to name its item.
    /// </remarks>
    [Fact]
    public void ResolvesAnArgumentThatIsItselfACliloc()
    {
        List<string> drawn = Render(
            new HtmlElement
            {
                ContentKind = HtmlContentKind.Localized,
                ClilocId = 1114057,
                Arguments = "#1025181",
            },
            clilocs: new() { [1114057] = "~1_val~", [1025181] = "hammer pick" });

        Assert.Contains("hammer pick", drawn);
    }

    /// <summary>
    /// A run of delimiters is one separator, not an empty argument.
    /// </summary>
    /// <remarks>
    /// Real captures write <c>@@#1072325</c> for a single argument. Treating the
    /// gap as an empty first value fed nothing to <c>~1_TOKEN~</c> and the area
    /// previewed blank.
    /// </remarks>
    [Fact]
    public void TreatsARunOfDelimitersAsOneSeparator()
    {
        List<string> drawn = Render(
            new HtmlElement
            {
                ContentKind = HtmlContentKind.Localized,
                ClilocId = 1114513,
                Arguments = "@#1072325",
            },
            clilocs: new()
            {
                [1114513] = "~1_TOKEN~",
                [1072325] = "An escort to the New Haven Bank",
            });

        Assert.Contains("An escort to the New Haven Bank", drawn);
    }

    /// <summary>
    /// A placeholder with no argument is left as it stands.
    /// </summary>
    /// <remarks>
    /// Blanking it would hide the fact that the gump is missing a value, which is
    /// precisely what an author needs to see.
    /// </remarks>
    [Fact]
    public void KeepsAPlaceholderThatHasNoArgument()
    {
        List<string> drawn = Render(
            new HtmlElement
            {
                ContentKind = HtmlContentKind.Localized,
                ClilocId = 1000,
                Arguments = "Bob",
            },
            clilocs: new() { [1000] = "~1_NAME~ has ~2_COUNT~ left" });

        Assert.Contains("Bob has ~2_COUNT~ left", drawn);
    }

    /// <summary>Renders one element and returns every string the renderer drew.</summary>
    private static List<string> Render(HtmlElement element, Dictionary<int, string> clilocs)
    {
        using FakeArtSource art = new();

        element.Location = new GumpPoint(0, 0);
        element.Size = new GumpSize(200, 40);

        GumpPage page = new();
        page.Root.Add(element);

        TestArtSource source = new(art);

        foreach ((int id, string text) in clilocs)
        {
            source.Clilocs[id] = text;
        }

        using SKBitmap output = new GumpRenderer(source).RenderToBitmap(page, 220, 60, RenderOptions.Plain);

        return source.TextRequests;
    }
}
