using GumpStudio.Core.Elements;
using GumpStudio.Core.Primitives;

namespace GumpStudio.Core.Document;

/// <summary>Gump-level flags that apply to the whole window.</summary>
public sealed class GumpProperties
{
    /// <summary>Where the gump opens on screen.</summary>
    public GumpPoint Location { get; set; }

    /// <summary>Whether the player can drag the gump.</summary>
    public bool Movable { get; set; } = true;

    /// <summary>Whether the player can close the gump with right-click.</summary>
    public bool Closable { get; set; } = true;

    /// <summary>Whether the gump is disposed when closed.</summary>
    public bool Disposable { get; set; } = true;

    /// <summary>A user-defined type id some exporters emit.</summary>
    public int TypeId { get; set; }

    public GumpProperties Clone() => new()
    {
        Location = Location,
        Movable = Movable,
        Closable = Closable,
        Disposable = Disposable,
        TypeId = TypeId,
    };
}

/// <summary>One page of a gump. Pages are switched at runtime by page buttons.</summary>
public sealed class GumpPage
{
    public GumpPage(string? name = null)
    {
        Root = new GroupElement { IsPageRoot = true, Name = "Page root" };
        Name = name ?? string.Empty;
    }

    /// <summary>The group holding this page's elements.</summary>
    public GroupElement Root { get; }

    /// <summary>A display name for the page tab.</summary>
    public string Name { get; set; }

    /// <summary>Every element on the page that is not a group, depth-first.</summary>
    public IEnumerable<Element> Leaves() => Root.Leaves();

    /// <summary>Every element on the page, groups included, depth-first.</summary>
    public IEnumerable<Element> Descendants() => Root.Descendants();
}

/// <summary>
/// A gump being edited: its window-level properties and its pages.
/// </summary>
/// <remarks>
/// Replaces the old untyped <c>ArrayList Stacks</c> plus a parallel
/// <c>GumpProperties</c> field held on the designer form. Deliberately has no
/// undo, selection or file-path state — those belong to the editing session, not
/// the document.
/// </remarks>
public sealed class GumpDocument
{
    private readonly List<GumpPage> _pages = [];

    public GumpDocument()
    {
        // A document always has at least one page; an empty page list is a state
        // the editor would otherwise have to special-case everywhere.
        _pages.Add(new GumpPage("Page 0"));
    }

    public GumpProperties Properties { get; set; } = new();

    public IReadOnlyList<GumpPage> Pages => _pages;

    public int PageCount => _pages.Count;

    /// <summary>Adds a page at the end and returns it.</summary>
    public GumpPage AddPage(string? name = null)
    {
        GumpPage page = new(name ?? $"Page {_pages.Count}");

        _pages.Add(page);

        return page;
    }

    /// <summary>Inserts a page at a position.</summary>
    public void InsertPage(int index, GumpPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, _pages.Count);

        _pages.Insert(index, page);
    }

    /// <summary>
    /// Removes a page.
    /// </summary>
    /// <returns>
    /// The index that should become active afterwards: the page that slid into
    /// the removed slot, or the last page when the removed one was last.
    /// </returns>
    /// <remarks>
    /// The original always activated <c>selectedIndex - 1</c>, so deleting a
    /// middle page jumped one page further back than it should.
    /// </remarks>
    public int RemovePage(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _pages.Count);

        if (_pages.Count == 1)
        {
            throw new InvalidOperationException("A document must keep at least one page.");
        }

        _pages.RemoveAt(index);

        return Math.Min(index, _pages.Count - 1);
    }

    /// <summary>
    /// Replaces every page in one step.
    /// </summary>
    /// <remarks>
    /// Used when loading. Without it, callers end up juggling the always-present
    /// first page with insert-then-remove sequences that are easy to get wrong.
    /// </remarks>
    public void ReplacePages(IEnumerable<GumpPage> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);

        List<GumpPage> replacement = [.. pages];

        if (replacement.Count == 0)
        {
            throw new ArgumentException("A document must have at least one page.", nameof(pages));
        }

        _pages.Clear();
        _pages.AddRange(replacement);
    }

    /// <summary>Deep-copies the document.</summary>
    public GumpDocument Clone()
    {
        GumpDocument clone = new();

        clone._pages.Clear();
        clone.Properties = Properties.Clone();

        foreach (GumpPage page in _pages)
        {
            GumpPage copy = new(page.Name);

            foreach (Element child in page.Root.Children)
            {
                copy.Root.Add(child.Clone());
            }

            clone._pages.Add(copy);
        }

        return clone;
    }
}
