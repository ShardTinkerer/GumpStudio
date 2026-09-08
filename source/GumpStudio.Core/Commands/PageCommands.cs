using GumpStudio.Core.Document;
using GumpStudio.Core.Elements;

namespace GumpStudio.Core.Commands;

/// <summary>
/// Adds a page at the end of the document.
/// </summary>
/// <remarks>
/// The page instance is created once and reused by every redo, so anything
/// placed on it before an undo comes back with it.
/// </remarks>
public sealed class AddPageCommand : IUndoableCommand
{
    private readonly GumpDocument _document;
    private readonly GumpPage _page;
    private int _index = -1;

    public AddPageCommand(GumpDocument document, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        _document = document;
        _page = new GumpPage(name ?? $"Page {document.PageCount}");
    }

    /// <summary>The page this command adds, valid before it is first executed.</summary>
    public GumpPage Page => _page;

    public string Description => "Add page";

    public void Execute()
    {
        _index = _document.PageCount;

        _document.InsertPage(_index, _page);
    }

    public void Undo() => _document.RemovePage(_index);
}

/// <summary>Inserts a new page at a position, shifting the rest along.</summary>
public sealed class InsertPageCommand : IUndoableCommand
{
    private readonly GumpDocument _document;
    private readonly GumpPage _page;
    private readonly int _index;

    public InsertPageCommand(GumpDocument document, int index, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, document.PageCount);

        _document = document;
        _index = index;
        _page = new GumpPage(name ?? $"Page {index}");
    }

    /// <summary>The page this command inserts, valid before it is first executed.</summary>
    public GumpPage Page => _page;

    /// <summary>Where the page lands.</summary>
    public int Index => _index;

    public string Description => "Insert page";

    public void Execute() => _document.InsertPage(_index, _page);

    public void Undo() => _document.RemovePage(_index);
}

/// <summary>
/// Removes a page, remembering it and everything on it.
/// </summary>
/// <remarks>
/// This exists because removing a page used to bypass the undo history
/// altogether, which made it the one action in the editor that destroyed work
/// with no way back.
/// </remarks>
public sealed class RemovePageCommand : IUndoableCommand
{
    private readonly GumpDocument _document;
    private readonly GumpPage _page;
    private readonly int _index;

    public RemovePageCommand(GumpDocument document, int index)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, document.PageCount);

        if (document.PageCount == 1)
        {
            throw new InvalidOperationException("A document must keep at least one page.");
        }

        _document = document;
        _index = index;
        _page = document.Pages[index];
        ActiveIndexAfterRemoval = Math.Min(index, document.PageCount - 2);
    }

    /// <summary>
    /// The index that should become active after the page is removed.
    /// </summary>
    /// <remarks>
    /// The page that slid into the removed slot, or the last page when the
    /// removed one was last — matching <see cref="GumpDocument.RemovePage"/>.
    /// </remarks>
    public int ActiveIndexAfterRemoval { get; }

    public string Description => $"Remove {_page.Name}";

    public void Execute() => _document.RemovePage(_index);

    public void Undo() => _document.InsertPage(_index, _page);
}

/// <summary>Removes every element from a page, keeping the page itself.</summary>
public sealed class ClearPageCommand : IUndoableCommand
{
    private readonly GumpPage _page;
    private readonly List<Element> _removed;

    public ClearPageCommand(GumpPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        _page = page;
        _removed = [.. page.Root.Children];
    }

    /// <summary>Whether the page had anything to clear.</summary>
    public bool HasContent => _removed.Count > 0;

    public string Description => $"Clear {_page.Name}";

    public void Execute()
    {
        foreach (Element element in _removed)
        {
            _page.Root.Remove(element);
        }
    }

    public void Undo()
    {
        // Re-added in their original order, so drawing order survives the undo.
        foreach (Element element in _removed)
        {
            _page.Root.Add(element);
        }
    }
}
