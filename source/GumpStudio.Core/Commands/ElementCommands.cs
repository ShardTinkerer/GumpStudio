using GumpStudio.Core.Document;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Primitives;

namespace GumpStudio.Core.Commands;

/// <summary>Adds an element to a group.</summary>
public sealed class AddElementCommand(GroupElement parent, Element element, int? index = null)
    : IUndoableCommand
{
    public string Description => $"Add {element.TypeName}";

    public void Execute() => parent.Insert(index ?? parent.Children.Count, element);

    public void Undo() => parent.Remove(element);
}

/// <summary>Removes an element, remembering where it came from.</summary>
public sealed class RemoveElementCommand : IUndoableCommand
{
    private readonly Element _element;
    private readonly GroupElement _parent;
    private readonly int _index;

    public RemoveElementCommand(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);

        _element = element;
        _parent = element.Parent
            ?? throw new InvalidOperationException("Cannot remove an element that has no parent.");
        _index = _parent.IndexOf(element);
    }

    public string Description => $"Delete {_element.TypeName}";

    public void Execute() => _parent.Remove(_element);

    public void Undo() => _parent.Insert(Math.Min(_index, _parent.Children.Count), _element);
}

/// <summary>
/// Moves an element by a delta.
/// </summary>
/// <remarks>
/// Consecutive moves of the same element merge, so a drag or a run of arrow-key
/// nudges collapses into one undo entry.
/// </remarks>
public sealed class MoveElementCommand(Element element, GumpPoint from, GumpPoint to) : IUndoableCommand
{
    private GumpPoint _to = to;

    public string Description => $"Move {element.TypeName}";

    public void Execute() => element.Location = _to;

    public void Undo() => element.Location = from;

    public bool TryMerge(IUndoableCommand following)
    {
        if (following is not MoveElementCommand move || !ReferenceEquals(move.Target, element))
        {
            return false;
        }

        // Keep the original starting point so undo returns all the way home.
        _to = move._to;

        return true;
    }

    internal Element Target => element;
}

/// <summary>Resizes an element, merging consecutive resizes of the same element.</summary>
public sealed class ResizeElementCommand(Element element, GumpRect from, GumpRect to) : IUndoableCommand
{
    private GumpRect _to = to;

    public string Description => $"Resize {element.TypeName}";

    public void Execute()
    {
        element.Location = _to.Location;
        element.Size = _to.Size;
    }

    public void Undo()
    {
        element.Location = from.Location;
        element.Size = from.Size;
    }

    public bool TryMerge(IUndoableCommand following)
    {
        if (following is not ResizeElementCommand resize || !ReferenceEquals(resize.Target, element))
        {
            return false;
        }

        _to = resize._to;

        return true;
    }

    internal Element Target => element;
}

/// <summary>
/// Sets a property through a getter and setter pair.
/// </summary>
/// <remarks>
/// Used by the property panel, so every edit is undoable without a bespoke
/// command per property. Consecutive edits to the same property of the same
/// element merge, so typing into a text box is one undo entry rather than one
/// per keystroke.
/// </remarks>
public sealed class SetPropertyCommand<T>(
    Element element,
    string propertyName,
    Func<Element, T> getter,
    Action<Element, T> setter,
    T oldValue,
    T newValue)
    : IUndoableCommand
{
    private T _newValue = newValue;

    public string Description => $"Change {propertyName}";

    public void Execute() => setter(element, _newValue);

    public void Undo() => setter(element, oldValue);

    public bool TryMerge(IUndoableCommand following)
    {
        if (following is not SetPropertyCommand<T> other
            || !ReferenceEquals(other.Target, element)
            || !string.Equals(other.PropertyName, propertyName, StringComparison.Ordinal))
        {
            return false;
        }

        _newValue = other._newValue;

        return true;
    }

    /// <summary>Reads the current value, for callers building the command.</summary>
    public T Read() => getter(element);

    internal Element Target => element;

    internal string PropertyName => propertyName;
}

/// <summary>Changes an element's position in its parent's z-order.</summary>
public sealed class ReorderElementCommand : IUndoableCommand
{
    private readonly GroupElement _parent;
    private readonly Element _element;
    private readonly int _from;
    private readonly int _to;

    public ReorderElementCommand(Element element, int toIndex)
    {
        ArgumentNullException.ThrowIfNull(element);

        _element = element;
        _parent = element.Parent
            ?? throw new InvalidOperationException("Cannot reorder an element that has no parent.");
        _from = _parent.IndexOf(element);
        _to = Math.Clamp(toIndex, 0, Math.Max(0, _parent.Children.Count - 1));
    }

    public string Description => "Reorder";

    public void Execute() => _parent.MoveTo(_element, _to);

    public void Undo() => _parent.MoveTo(_element, _from);
}

/// <summary>Moves elements into a new group, preserving their absolute positions.</summary>
public sealed class GroupElementsCommand : IUndoableCommand
{
    private readonly GroupElement _parent;
    private readonly List<(Element Element, int Index, GumpPoint Location)> _originals = [];
    private readonly GroupElement _group = new() { Name = "Group" };

    public GroupElementsCommand(IReadOnlyList<Element> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);

        if (elements.Count == 0)
        {
            throw new ArgumentException("Nothing to group.", nameof(elements));
        }

        _parent = elements[0].Parent
            ?? throw new InvalidOperationException("Cannot group elements that have no parent.");

        if (elements.Any(e => !ReferenceEquals(e.Parent, _parent)))
        {
            throw new InvalidOperationException("All elements must share a parent to be grouped.");
        }

        foreach (Element element in elements.OrderBy(_parent.IndexOf))
        {
            _originals.Add((element, _parent.IndexOf(element), element.Location));
        }
    }

    public string Description => "Group";

    /// <summary>The group this command creates.</summary>
    public GroupElement Group => _group;

    public void Execute()
    {
        // The group sits at the top-left of what it contains, and children are
        // rebased so nothing appears to move.
        GumpRect union = GumpRect.Empty;

        foreach ((Element element, _, GumpPoint location) in _originals)
        {
            union = GumpRect.Union(union, new GumpRect(location, element.Size));
        }

        _group.Location = union.Location;

        int insertAt = _originals[0].Index;

        foreach ((Element element, _, GumpPoint location) in _originals)
        {
            _parent.Remove(element);
            element.Location = location - union.Location;
            _group.Add(element);
        }

        _parent.Insert(Math.Min(insertAt, _parent.Children.Count), _group);
    }

    public void Undo()
    {
        _parent.Remove(_group);

        foreach ((Element element, int index, GumpPoint location) in _originals)
        {
            _group.Remove(element);
            element.Location = location;
            _parent.Insert(Math.Min(index, _parent.Children.Count), element);
        }
    }
}

/// <summary>Replaces the gump-level properties.</summary>
/// <remarks>
/// Whole-object rather than per-field, because the properties dialog commits
/// every field at once and one undo entry per checkbox would be tedious.
/// </remarks>
public sealed class SetGumpPropertiesCommand : IUndoableCommand
{
    private readonly GumpDocument _document;
    private readonly GumpProperties _before;
    private readonly GumpProperties _after;

    public SetGumpPropertiesCommand(GumpDocument document, GumpProperties properties)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(properties);

        _document = document;
        _before = document.Properties.Clone();
        _after = properties.Clone();
    }

    public string Description => "Gump properties";

    // Cloned on each application, so undoing and redoing cannot hand the document
    // an object the command still holds a reference to.
    public void Execute() => _document.Properties = _after.Clone();

    public void Undo() => _document.Properties = _before.Clone();
}

/// <summary>
/// Dissolves a group, returning its children to the group's own parent.
/// </summary>
/// <remarks>
/// The inverse of <see cref="GroupElementsCommand"/>. Children are rebased by
/// the group's offset so nothing appears to move, and they land where the group
/// sat in the z-order rather than on top of everything.
/// </remarks>
public sealed class UngroupElementsCommand : IUndoableCommand
{
    private readonly GroupElement _group;
    private readonly GroupElement _parent;
    private readonly List<(Element Element, GumpPoint Location)> _children = [];

    private int _index;

    public UngroupElementsCommand(GroupElement group)
    {
        ArgumentNullException.ThrowIfNull(group);

        if (group.IsPageRoot)
        {
            throw new InvalidOperationException("A page root cannot be ungrouped.");
        }

        _group = group;
        _parent = group.Parent
            ?? throw new InvalidOperationException("Cannot ungroup an element that has no parent.");

        foreach (Element child in group.Children)
        {
            _children.Add((child, child.Location));
        }
    }

    public string Description => "Ungroup";

    public void Execute()
    {
        _index = _parent.IndexOf(_group);

        GumpPoint offset = _group.Location;

        _parent.Remove(_group);

        // Inserted in order at the group's own slot, so the children keep both
        // their relative z-order and their depth relative to everything else.
        for (int i = 0; i < _children.Count; i++)
        {
            (Element element, GumpPoint location) = _children[i];

            _group.Remove(element);
            element.Location = location.Offset(offset.X, offset.Y);

            _parent.Insert(Math.Min(_index + i, _parent.Children.Count), element);
        }
    }

    public void Undo()
    {
        foreach ((Element element, GumpPoint location) in _children)
        {
            _parent.Remove(element);
            element.Location = location;
            _group.Add(element);
        }

        _parent.Insert(Math.Min(_index, _parent.Children.Count), _group);
    }
}

/// <summary>
/// Moves an element onto another page, keeping where it sits on screen.
/// </summary>
/// <remarks>
/// <para>
/// Every page is its own coordinate space rooted at the gump's origin, so an
/// element that was nested in a group has to be rebased on the way out:
/// its new location is the absolute position it had before, or it would jump by
/// the group's offset.
/// </para>
/// <para>
/// The original could not do this at all. Putting an element on the wrong page
/// meant deleting it and building it again on the right one.
/// </para>
/// </remarks>
public sealed class MoveToPageCommand : IUndoableCommand
{
    private readonly Element _element;
    private readonly GroupElement _target;
    private readonly GroupElement _source;
    private readonly int _index;
    private readonly GumpPoint _location;

    public MoveToPageCommand(Element element, GumpPage target)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(target);

        _element = element;
        _target = target.Root;
        _source = element.Parent
            ?? throw new InvalidOperationException("Cannot move an element that has no parent.");

        if (ReferenceEquals(_source, _target))
        {
            throw new InvalidOperationException("The element is already on that page.");
        }

        _index = _source.IndexOf(element);
        _location = element.Location;
    }

    public string Description => "Move to page";

    public void Execute()
    {
        GumpPoint absolute = _element.GetAbsolutePosition();

        _source.Remove(_element);

        _element.Location = absolute;
        _target.Add(_element);
    }

    public void Undo()
    {
        _target.Remove(_element);

        _element.Location = _location;
        _source.Insert(Math.Min(_index, _source.Children.Count), _element);
    }
}
