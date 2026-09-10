namespace GumpStudio.App.ViewModels;

/// <summary>
/// The things only the window itself can do, which a command still needs.
/// </summary>
/// <remarks>
/// <para>
/// Not a general escape hatch, and not meant to last in this shape. Each member
/// is here because the operation genuinely belongs to the view: the zoom depends
/// on the scroll viewport's size, hiding a panel goes through Dock's factory,
/// and closing the window is the window's own business.
/// </para>
/// <para>
/// <see cref="RefreshDocumentView"/> is the exception and the temporary one. It
/// stands in for the window's <c>RefreshAll</c> — the whole-world rebuild this
/// refactor exists to remove — so that the commands could move first. Observable
/// collections take the page strip and the element list off it, leaving only the
/// property panel, which is a later piece of work.
/// </para>
/// </remarks>
public interface IShellView
{
    /// <summary>
    /// Tells the panels to re-read the document.
    /// </summary>
    /// <remarks>
    /// Shrinks as granular notification replaces it. Nothing new should call it.
    /// </remarks>
    void RefreshDocumentView();

    /// <summary>Redraws the design surface.</summary>
    void InvalidateCanvas();

    /// <summary>Whether page 0 is drawn beneath the page being edited.</summary>
    bool ShowSharedPage { get; set; }

    /// <summary>Moves one step along the zoom ladder.</summary>
    void StepZoom(bool up);

    /// <summary>Sets an exact zoom.</summary>
    void SetZoom(double zoom);

    /// <summary>Fits the gump to the space available, which only the view knows.</summary>
    void ZoomToFit();

    /// <summary>Hides or restores one of the Dock panels.</summary>
    void ShowPanel(string dockableId, bool show);

    /// <summary>Brings every hidden panel back and restores the declared sizes.</summary>
    void ResetLayout();

    /// <summary>
    /// Drops the cached hue and font rows.
    /// </summary>
    /// <remarks>
    /// They are rendered from client art, so they belong to the client that was
    /// open when they were built.
    /// </remarks>
    void ForgetPickerEntries();

    /// <summary>Closes the shell, having already asked about unsaved work.</summary>
    void CloseShell();
}
