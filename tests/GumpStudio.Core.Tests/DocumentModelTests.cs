using GumpStudio.Core.Document;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Primitives;

using Xunit;

namespace GumpStudio.Core.Tests;

public class ElementModelTests
{
    [Fact]
    public void AbsolutePositionAccumulatesEveryParentOffset()
    {
        GroupElement outer = new() { Location = new GumpPoint(100, 50) };
        GroupElement inner = new() { Location = new GumpPoint(10, 5) };
        LabelElement label = new() { Location = new GumpPoint(3, 2) };

        outer.Add(inner);
        inner.Add(label);

        // The defect this guards: the original never called the equivalent method,
        // so nested elements exported at their parent-relative position (3, 2).
        Assert.Equal(new GumpPoint(3, 2), label.Location);
        Assert.Equal(new GumpPoint(113, 57), label.GetAbsolutePosition());
    }

    [Fact]
    public void AbsolutePositionEqualsLocationWithoutAParent()
    {
        LabelElement label = new() { Location = new GumpPoint(7, 9) };

        Assert.Equal(label.Location, label.GetAbsolutePosition());
    }

    [Fact]
    public void AddingToAGroupRemovesFromThePrevious()
    {
        GroupElement first = new();
        GroupElement second = new();
        LabelElement label = new();

        first.Add(label);
        second.Add(label);

        Assert.Empty(first.Children);
        Assert.Same(second, label.Parent);
        Assert.Single(second.Children);
    }

    [Fact]
    public void EmptyGroupExposesAnEmptyListNotNull()
    {
        GroupElement group = new();

        // The original returned null for an empty group, so every caller needed a
        // null check that most of them did not have.
        Assert.NotNull(group.Children);
        Assert.Empty(group.Children);
    }

    [Fact]
    public void AGroupCannotContainItselfOrItsAncestor()
    {
        GroupElement outer = new();
        GroupElement inner = new();

        outer.Add(inner);

        Assert.Throws<InvalidOperationException>(() => outer.Add(outer));
        Assert.Throws<InvalidOperationException>(() => inner.Add(outer));
    }

    [Fact]
    public void LeavesSkipsGroupsButYieldsTheirContents()
    {
        GroupElement root = new();
        GroupElement group = new();
        LabelElement direct = new();
        LabelElement nested = new();

        root.Add(direct);
        root.Add(group);
        group.Add(nested);

        Assert.Equal([direct, nested], root.Leaves());
        Assert.Equal([direct, group, nested], root.Descendants());
    }

    [Fact]
    public void GroupSizeFollowsItsContents()
    {
        GroupElement group = new();

        Assert.True(group.Size.IsEmpty);

        group.Add(new AlphaElement { Location = new GumpPoint(10, 20) });

        // Alpha defaults to 100x100, so the extent is 110 x 120.
        Assert.Equal(new GumpSize(110, 120), group.Size);
    }

    [Fact]
    public void CloneIsIndependentAndUnparented()
    {
        GroupElement group = new() { Name = "Original" };
        LabelElement label = new() { Text = "Hello", Hue = 5 };

        group.Add(label);

        GroupElement clone = (GroupElement)group.Clone();

        Assert.Null(clone.Parent);
        Assert.Single(clone.Children);
        Assert.NotSame(label, clone.Children[0]);

        label.Text = "Changed";

        Assert.Equal("Hello", ((LabelElement)clone.Children[0]).Text);
    }

    [Fact]
    public void NonResizableElementsIgnoreSizeChanges()
    {
        LabelElement label = new();
        GumpSize before = label.Size;

        label.Size = new GumpSize(999, 999);

        Assert.Equal(before, label.Size);
        Assert.False(label.IsResizable);
    }

    [Fact]
    public void CheckingARadioClearsOnlyItsOwnGroup()
    {
        GroupElement page = new();

        RadioElement a = new() { GroupId = 1, IsChecked = true };
        RadioElement b = new() { GroupId = 1 };
        RadioElement other = new() { GroupId = 2, IsChecked = true };

        page.Add(a);
        page.Add(b);
        page.Add(other);

        b.IsChecked = true;

        Assert.False(a.IsChecked);
        Assert.True(b.IsChecked);
        Assert.True(other.IsChecked);
    }

    [Fact]
    public void CheckingAnUnparentedRadioDoesNotThrow()
    {
        // The original walked the parent unconditionally and threw during load,
        // before the element had been added to anything.
        RadioElement radio = new();

        radio.IsChecked = true;

        Assert.True(radio.IsChecked);
    }
}

public class GumpDocumentTests
{
    [Fact]
    public void ANewDocumentHasExactlyOnePage()
    {
        GumpDocument document = new();

        Assert.Equal(1, document.PageCount);
    }

    [Fact]
    public void RemovingAMiddlePageActivatesTheOneThatSlidIntoItsPlace()
    {
        GumpDocument document = new();

        document.AddPage("Page 1");
        document.AddPage("Page 2");

        int active = document.RemovePage(1);

        // The original always returned index - 1, jumping one page too far back.
        Assert.Equal(1, active);
        Assert.Equal("Page 2", document.Pages[1].Name);
    }

    [Fact]
    public void RemovingTheLastPageActivatesTheNewLastPage()
    {
        GumpDocument document = new();

        document.AddPage("Page 1");

        Assert.Equal(0, document.RemovePage(1));
    }

    [Fact]
    public void ADocumentRefusesToLoseItsLastPage()
    {
        GumpDocument document = new();

        Assert.Throws<InvalidOperationException>(() => document.RemovePage(0));
    }

    [Fact]
    public void CloneDeepCopiesPagesAndProperties()
    {
        GumpDocument document = new();

        document.Properties.Movable = false;
        document.Pages[0].Root.Add(new LabelElement { Text = "Hello" });

        GumpDocument clone = document.Clone();

        Assert.False(clone.Properties.Movable);
        Assert.Single(clone.Pages[0].Root.Children);
        Assert.NotSame(document.Pages[0].Root.Children[0], clone.Pages[0].Root.Children[0]);
    }
}
