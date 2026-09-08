using GumpStudio.Core.Primitives;

namespace GumpStudio.Core.Elements;

/// <summary>
/// A container holding other elements, whose positions are relative to it.
/// </summary>
/// <remarks>
/// Also serves as the root of a page. The old implementation conflated parenting
/// with geometry — adding a child both re-parented it and shifted every existing
/// sibling to keep the union rectangle anchored — and exposed its children as an
/// array that returned <see langword="null"/> when empty. Here membership and
/// geometry are separate operations.
/// </remarks>
public sealed class GroupElement : Element
{
    private readonly List<Element> _children = [];

    public override string TypeName => "Group";

    /// <summary>True when this group is a page root rather than a user-made group.</summary>
    public bool IsPageRoot { get; init; }

    /// <summary>The direct children, in z-order: first is behind, last is in front.</summary>
    public IReadOnlyList<Element> Children => _children;

    /// <summary>
    /// The union of the children's bounds, in this group's coordinate space.
    /// </summary>
    /// <remarks>
    /// Computed rather than stored, so it cannot drift out of sync with the
    /// children the way the original's cached bounds could.
    /// </remarks>
    public override GumpSize Size
    {
        get
        {
            if (_children.Count == 0)
            {
                return GumpSize.Empty;
            }

            GumpRect union = GumpRect.Empty;

            foreach (Element child in _children)
            {
                union = GumpRect.Union(union, child.Bounds);
            }

            // Bounds are measured from this group's origin, so the extent is the
            // far edge, not the union's width.
            return new GumpSize(union.Right, union.Bottom);
        }

        set
        {
            // A group's size follows its contents; there is nothing to set.
        }
    }

    /// <summary>Adds a child at the top of the z-order.</summary>
    public void Add(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);

        Insert(_children.Count, element);
    }

    /// <summary>Adds a child at a specific z-order position.</summary>
    public void Insert(int index, Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, _children.Count);

        // A cycle forms either by adding a group to itself, or by adding one of
        // this group's own ancestors into it.
        if (ReferenceEquals(element, this)
            || (element is GroupElement candidate && candidate.IsAncestorOf(this)))
        {
            throw new InvalidOperationException(
                "An element cannot be added to itself or to one of its own descendants.");
        }

        element.Parent?.Remove(element);

        _children.Insert(index, element);
        element.Parent = this;
    }

    /// <summary>Removes a child. Returns false when it was not in this group.</summary>
    public bool Remove(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (!_children.Remove(element))
        {
            return false;
        }

        element.Parent = null;

        return true;
    }

    /// <summary>Position of a child in the z-order, or -1.</summary>
    public int IndexOf(Element element) => _children.IndexOf(element);

    /// <summary>Moves a child to a new z-order position.</summary>
    public void MoveTo(Element element, int index)
    {
        ArgumentNullException.ThrowIfNull(element);

        int current = _children.IndexOf(element);

        if (current < 0)
        {
            throw new InvalidOperationException($"'{element.Name}' is not a child of this group.");
        }

        int target = Math.Clamp(index, 0, _children.Count - 1);

        if (current == target)
        {
            return;
        }

        _children.RemoveAt(current);
        _children.Insert(target, element);
    }

    /// <summary>Every descendant, depth-first, groups included.</summary>
    public IEnumerable<Element> Descendants()
    {
        foreach (Element child in _children)
        {
            yield return child;

            if (child is GroupElement group)
            {
                foreach (Element nested in group.Descendants())
                {
                    yield return nested;
                }
            }
        }
    }

    /// <summary>
    /// Every descendant that is not itself a group, depth-first.
    /// </summary>
    /// <remarks>
    /// This is what exporters walk. Note that the positions of the elements it
    /// yields are relative to their own parents — callers must use
    /// <see cref="Element.GetAbsolutePosition"/>, which is precisely the step the
    /// original exporters skipped.
    /// </remarks>
    public IEnumerable<Element> Leaves()
    {
        foreach (Element child in _children)
        {
            if (child is GroupElement group)
            {
                foreach (Element nested in group.Leaves())
                {
                    yield return nested;
                }
            }
            else
            {
                yield return child;
            }
        }
    }

    /// <summary>True when <paramref name="element"/> is this group or nested inside it.</summary>
    public bool IsAncestorOf(Element element)
    {
        for (GroupElement? parent = element?.Parent; parent is not null; parent = parent.Parent)
        {
            if (ReferenceEquals(parent, this))
            {
                return true;
            }
        }

        return false;
    }

    protected override void CopyTo(Element target)
    {
        GroupElement clone = (GroupElement)target;

        foreach (Element child in _children)
        {
            clone.Add(child.Clone());
        }
    }

    protected override Element CreateInstance() => new GroupElement { IsPageRoot = IsPageRoot };

    public override void Accept(IElementVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.Visit(this);
    }
}
