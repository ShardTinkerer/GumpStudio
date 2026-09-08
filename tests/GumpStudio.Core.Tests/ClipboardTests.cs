using GumpStudio.Core.Commands;
using GumpStudio.Core.Document;
using GumpStudio.Core.Editing;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Primitives;
using GumpStudio.Core.Serialization;

using Xunit;

namespace GumpStudio.Core.Tests;

/// <summary>
/// The clipboard fragment format and pasting.
/// </summary>
/// <remarks>
/// Elements travel as XML text rather than as a serialised object graph. That
/// survives between instances, can be read by pasting it anywhere, and cannot
/// carry anything executable — which the original's <c>BinaryFormatter</c>
/// payload could.
/// </remarks>
public class ClipboardTests
{
    private static (CanvasInteractionController Canvas, UndoHistory History, GumpPage Page) Build()
    {
        UndoHistory history = new();
        CanvasInteractionController canvas = new(history);
        GumpPage page = new();

        canvas.Page = page;

        return (canvas, history, page);
    }

    [Fact]
    public void AFragmentRoundTripsEveryPropertyOfAnElement()
    {
        ImageElement original = new()
        {
            Name = "Badge",
            Location = new GumpPoint(30, 40),
            GumpId = 1417,
            Hue = 33,
            PartialHue = true,
            Comment = "a note",
            TooltipClilocId = 1042971,
            TooltipArguments = "Bob@7",
        };

        string fragment = GumpXmlSerializer.ToFragment([original]);
        ImageElement copy = Assert.IsType<ImageElement>(Assert.Single(GumpXmlSerializer.FromFragment(fragment)));

        Assert.Equal("Badge", copy.Name);
        Assert.Equal(new GumpPoint(30, 40), copy.Location);
        Assert.Equal(1417, copy.GumpId);
        Assert.Equal(33, copy.Hue);
        Assert.True(copy.PartialHue);
        Assert.Equal("a note", copy.Comment);
        Assert.Equal(1042971, copy.TooltipClilocId);
        Assert.Equal("Bob@7", copy.TooltipArguments);
    }

    [Fact]
    public void AFragmentKeepsGroupsAndTheirContents()
    {
        GroupElement group = new() { Name = "Pair", Location = new GumpPoint(10, 20) };

        group.Add(new ImageElement { Name = "a", Location = new GumpPoint(1, 2) });
        group.Add(new ItemElement { Name = "b", Location = new GumpPoint(3, 4) });

        GroupElement copy = Assert.IsType<GroupElement>(
            Assert.Single(GumpXmlSerializer.FromFragment(GumpXmlSerializer.ToFragment([group]))));

        Assert.Equal(new GumpPoint(10, 20), copy.Location);
        Assert.Equal(["a", "b"], copy.Children.Select(c => c.Name));
    }

    /// <summary>
    /// The clipboard holds whatever was last copied anywhere, so text that is not
    /// ours is an ordinary outcome and must not throw.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("just some text a user copied")]
    [InlineData("<html><body>markup</body></html>")]
    [InlineData("<gumpstudio-elements")]
    public void ForeignClipboardTextYieldsNothingRatherThanThrowing(string? text)
    {
        Assert.Empty(GumpXmlSerializer.FromFragment(text));
    }

    [Fact]
    public void AFragmentFromANewerFormatIsRefused()
    {
        string fragment = GumpXmlSerializer
            .ToFragment([new ImageElement()])
            .Replace(
                $"version=\"{GumpXmlSerializer.CurrentVersion}\"",
                $"version=\"{GumpXmlSerializer.CurrentVersion + 1}\"",
                StringComparison.Ordinal);

        Assert.Empty(GumpXmlSerializer.FromFragment(fragment));
    }

    [Fact]
    public void PastingAddsTheElementsAndSelectsThem()
    {
        (CanvasInteractionController canvas, _, GumpPage page) = Build();

        int pasted = canvas.Paste([new ImageElement { Name = "a" }, new ItemElement { Name = "b" }]);

        Assert.Equal(2, pasted);
        Assert.Equal(["a", "b"], page.Root.Children.Select(c => c.Name));
        Assert.Equal(["a", "b"], canvas.Selection.Select(c => c.Name));
    }

    /// <summary>
    /// Pasting on top of the original leaves nothing on screen to say a second
    /// copy appeared, which is how the original behaved.
    /// </summary>
    [Fact]
    public void PastingOffsetsSoTheCopyIsNotBuried()
    {
        (CanvasInteractionController canvas, _, GumpPage page) = Build();

        canvas.Paste([new ImageElement { Location = new GumpPoint(30, 40) }]);

        Assert.Equal(new GumpPoint(40, 50), page.Root.Children[0].Location);
    }

    [Fact]
    public void PastingOffsetsByAGridCellWhenSnapping()
    {
        (CanvasInteractionController canvas, _, GumpPage page) = Build();

        canvas.Grid.SnapEnabled = true;
        canvas.Grid.Width = 25;
        canvas.Grid.Height = 40;

        canvas.Paste([new ImageElement { Location = new GumpPoint(0, 0) }]);

        Assert.Equal(new GumpPoint(25, 40), page.Root.Children[0].Location);
    }

    /// <summary>
    /// The original added the clipboard's own objects, so a second paste
    /// re-parented the first paste's elements instead of duplicating them.
    /// </summary>
    [Fact]
    public void PastingTheSameSetTwiceGivesIndependentCopies()
    {
        (CanvasInteractionController canvas, _, GumpPage page) = Build();

        ImageElement source = new() { Name = "a", Location = new GumpPoint(0, 0) };

        canvas.Paste([source]);
        canvas.Paste([source]);

        Assert.Equal(2, page.Root.Children.Count);
        Assert.NotSame(page.Root.Children[0], page.Root.Children[1]);

        // The element handed in is never itself parented, so it can be pasted
        // again and again.
        Assert.Null(source.Parent);
    }

    [Fact]
    public void PastingIsOneUndoEntry()
    {
        (CanvasInteractionController canvas, UndoHistory history, GumpPage page) = Build();

        canvas.Paste([new ImageElement(), new ItemElement()]);

        Assert.Equal(2, page.Root.Children.Count);

        history.Undo();

        Assert.Empty(page.Root.Children);

        history.Redo();

        Assert.Equal(2, page.Root.Children.Count);
    }

    [Fact]
    public void PastingNothingDoesNothing()
    {
        (CanvasInteractionController canvas, UndoHistory history, GumpPage page) = Build();

        Assert.Equal(0, canvas.Paste([]));
        Assert.Empty(page.Root.Children);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void CopyAndPasteTogetherDuplicateASelection()
    {
        (CanvasInteractionController canvas, _, GumpPage page) = Build();

        page.Root.Add(new LabelElement { Name = "Title", Location = new GumpPoint(5, 6), Text = "hi" });
        canvas.Select(page.Root.Children[0]);

        string fragment = GumpXmlSerializer.ToFragment(canvas.Selection);

        canvas.Paste(GumpXmlSerializer.FromFragment(fragment));

        Assert.Equal(2, page.Root.Children.Count);

        LabelElement copy = Assert.IsType<LabelElement>(page.Root.Children[1]);

        Assert.Equal("hi", copy.Text);
        Assert.Equal(new GumpPoint(15, 16), copy.Location);
    }
}
