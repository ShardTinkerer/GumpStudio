using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.Input;

using GumpStudio.Core.Document;
using GumpStudio.Core.Elements;

namespace GumpStudio.App.ViewModels;

/// <summary>
/// The page strip and the elements list, kept in step with the document.
/// </summary>
/// <remarks>
/// <para>
/// Both were rebuilt wholesale. The page strip cleared a <c>StackPanel</c> and
/// made a fresh <c>Button</c> per page, with its click handler; the elements
/// list reassigned its <c>ItemsSource</c>. Between them they were most of what
/// <c>RefreshAll</c> did, and <c>RefreshAll</c> ran after every edit.
/// </para>
/// <para>
/// These are synchronised in place instead — only the entries that actually
/// differ are added, removed or moved. That is not only cheaper: it is what
/// fixes the selection race. Reassigning the item source made the list reset its
/// own selection on every refresh, and the resulting event raced the guard flag
/// that existed to suppress it.
/// </para>
/// </remarks>
public sealed partial class MainViewModel
{
    /// <summary>The pages, as tabs above the canvas.</summary>
    public ObservableCollection<PageTabViewModel> Pages { get; } = [];

    /// <summary>The active page's elements, in drawing order.</summary>
    /// <remarks>
    /// Back to front, which is the order the client draws them in and the order
    /// the panel labels.
    /// </remarks>
    public ObservableCollection<ElementRowViewModel> Elements { get; } = [];

    /// <summary>Switches to the page a tab stands for.</summary>
    [RelayCommand]
    private void ActivatePage(PageTabViewModel? tab)
    {
        if (tab is null)
        {
            return;
        }

        _session.ActivePageIndex = tab.Index;
    }

    /// <summary>
    /// Brings both collections in line with the document.
    /// </summary>
    /// <remarks>
    /// Driven by the undo history and the interaction controller, the two things
    /// that know when the document or the selection changed. Never by anything
    /// the canvas raises during a drag: a drag moves elements without adding or
    /// removing any, so there is nothing here for it to change.
    /// </remarks>
    private void SyncCollections()
    {
        SyncPages();
        SyncElements();
    }

    private void SyncPages()
    {
        IReadOnlyList<GumpPage> pages = _session.Document.Pages;

        // Rebuilt only when the page list itself changed. A tab carries its own
        // index, so inserting or removing a page renumbers everything after it.
        bool sameShape = Pages.Count == pages.Count;

        for (int i = 0; sameShape && i < pages.Count; i++)
        {
            sameShape = ReferenceEquals(Pages[i].Page, pages[i]);
        }

        if (!sameShape)
        {
            Pages.Clear();

            for (int i = 0; i < pages.Count; i++)
            {
                Pages.Add(new PageTabViewModel(pages[i], i, i == _session.ActivePageIndex));
            }

            return;
        }

        foreach (PageTabViewModel tab in Pages)
        {
            tab.IsActive = tab.Index == _session.ActivePageIndex;
        }
    }

    private void SyncElements()
    {
        IReadOnlyList<Element> children = _session.ActivePage.Root.Children;

        // The rows already standing for these elements, so a row survives a
        // reorder rather than being made again.
        if (Elements.Count == children.Count
            && !Elements.Where((row, i) => !ReferenceEquals(row.Element, children[i])).Any())
        {
            return;
        }

        Dictionary<Element, ElementRowViewModel> existing = [];

        foreach (ElementRowViewModel row in Elements)
        {
            existing[row.Element] = row;
        }

        List<ElementRowViewModel> wanted = [];

        foreach (Element child in children)
        {
            if (existing.Remove(child, out ElementRowViewModel? row))
            {
                wanted.Add(row);
            }
            else
            {
                wanted.Add(new ElementRowViewModel(child, SelectFromList));
            }
        }

        // Whatever is left is no longer on the page, so it stops listening.
        foreach (ElementRowViewModel orphan in existing.Values)
        {
            orphan.Dispose();
        }

        ApplyOrder(wanted);
    }

    /// <summary>
    /// Moves the collection to match <paramref name="wanted"/> in place.
    /// </summary>
    /// <remarks>
    /// Move rather than remove-and-add: a list rebuilds a container for an added
    /// item but keeps it for a moved one, so a z-order change no longer makes the
    /// panel flicker or drop what it had selected.
    /// </remarks>
    private void ApplyOrder(List<ElementRowViewModel> wanted)
    {
        for (int i = Elements.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(Elements[i]))
            {
                Elements.RemoveAt(i);
            }
        }

        for (int i = 0; i < wanted.Count; i++)
        {
            int current = Elements.IndexOf(wanted[i]);

            if (current < 0)
            {
                Elements.Insert(i, wanted[i]);
            }
            else if (current != i)
            {
                Elements.Move(current, i);
            }
        }
    }

    /// <summary>
    /// Takes a selection change made in the list through to the controller.
    /// </summary>
    /// <remarks>
    /// Through the controller rather than by setting the element, so the canvas
    /// hears about it and the two stay in step.
    ///
    /// A toggle, because the row only reports what changed. The list allows
    /// several, so it handles replacing a selection itself: clicking one row
    /// deselects the others, and each of those arrives here as its own change.
    /// Treating a pick as "replace the selection" instead would leave the rows
    /// the list had just deselected still selected on the canvas.
    /// </remarks>
    private void SelectFromList(Element element, bool selected)
    {
        if (element.IsSelected == selected)
        {
            return;
        }

        _session.Canvas.Toggle(element);
    }
}
