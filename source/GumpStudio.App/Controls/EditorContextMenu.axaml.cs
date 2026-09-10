using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GumpStudio.App.Controls;

/// <summary>
/// The context menu the canvas and the element list share.
/// </summary>
/// <remarks>
/// Declared once and instantiated per host: a <see cref="ContextMenu"/> belongs
/// to a single control, so the canvas and the list need one each — but they no
/// longer need a hundred lines of tree-building each to get it.
/// </remarks>
public partial class EditorContextMenu : ContextMenu
{
    public EditorContextMenu()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// The submenu of pages to move the selection to.
    /// </summary>
    /// <remarks>
    /// Filled by the window as the menu opens, rather than bound: its items
    /// carry a page index each, and pages are added and removed while the editor
    /// is open, so a stale entry would point at a page that no longer exists.
    /// </remarks>
    internal MenuItem MoveToPageItem => this.FindControl<MenuItem>("MoveToPage")!;
}
