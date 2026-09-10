using System.Linq;
using System.Runtime.CompilerServices;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using GumpStudio.Core.Elements;

namespace GumpStudio.App.ViewModels;

/// <summary>
/// What the shell shows and what it will let you do, derived from the session.
/// </summary>
/// <remarks>
/// <para>
/// A wrapper around <see cref="EditorSession"/> rather than a replacement for it.
/// The session already holds the document, the undo history, the canvas state
/// and the client, and already has no UI in it; what it does not have is
/// per-property change notification, so the window read it by hand and rebuilt
/// everything it showed on any change at all.
/// </para>
/// <para>
/// This is where the window's <c>RefreshEditMenu</c>, <c>RefreshTitle</c> and
/// <c>SetStatus</c> end up. The point is not that they move — it is that the
/// enable state existed <em>twice</em>, once here for the menu bar and once in
/// the context menu's <c>Opening</c> handler, and the two disagreed: the menu
/// bar showed every item available whatever was selected, and undo and redo
/// never said what they would reverse.
/// </para>
/// </remarks>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly EditorSession _session;
    private readonly IEditorDialogs _dialogs;
    private readonly ITextClipboard _clipboard;
    private readonly IShellView _view;

    /// <summary>
    /// True while a remembered layout is being applied, so that seeding a toggle
    /// does not act on it.
    /// </summary>
    /// <remarks>
    /// Restoring the panels sets each visibility to what was remembered, and a
    /// setter that then told Dock to hide or restore it would both undo the
    /// restore in progress and save the layout back over itself.
    /// </remarks>
    private bool _applyingLayout;

    /// <param name="session">The session this shell edits.</param>
    /// <param name="dialogs">Whatever has to be asked of the user.</param>
    /// <param name="clipboard">The system clipboard.</param>
    /// <param name="view">The few operations only the window can carry out.</param>
    public MainViewModel(
        EditorSession session,
        IEditorDialogs dialogs,
        ITextClipboard clipboard,
        IShellView view)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(view);

        _session = session;
        _dialogs = dialogs;
        _clipboard = clipboard;
        _view = view;

        // The two fine-grained sources the window ignored in favour of
        // rebuilding the world: the history knows when undo became possible and
        // what it would undo, and the controller knows when the selection
        // changed. Everything the Edit menu paints follows from those two.
        _session.History.Changed += (_, _) => OnEditStateChanged();
        _session.Canvas.Changed += (_, _) => OnEditStateChanged();

        _session.ModifiedChanged += (_, _) => OnPropertyChanged(nameof(Title));
        _session.DocumentChanged += (_, _) => OnDocumentChanged();
        _session.PageChanged += (_, _) => OnEditStateChanged();

        SyncCollections();
    }

    /// <summary>The session, for the parts of the shell still driving it directly.</summary>
    public EditorSession Session => _session;

    /// <summary>
    /// The document name, and whether it has unsaved changes.
    /// </summary>
    /// <remarks>
    /// <see cref="EditorSession.IsModified"/> is derived from the undo history
    /// rather than a flag, so undoing back to the saved state drops the asterisk
    /// again.
    /// </remarks>
    public string Title
    {
        get
        {
            string name = _session.DocumentPath is { } path
                ? Path.GetFileName(path)
                : "Untitled";

            return _session.IsModified
                ? $"GumpStudio — {name}*"
                : $"GumpStudio — {name}";
        }
    }

    /// <summary>The status line's text.</summary>
    [ObservableProperty]
    private string _status = "Ready.";

    /// <summary>
    /// Whether the status line is reporting a failure.
    /// </summary>
    /// <remarks>
    /// A flag rather than a brush: the two colours belong in a style selector,
    /// so nothing outside the view decides what a failure looks like.
    /// </remarks>
    [ObservableProperty]
    private bool _isStatusError;

    /// <summary>Reports what just happened, or what just failed.</summary>
    public void SetStatus(string message, bool isError = false)
    {
        Status = message;
        IsStatusError = isError;
    }

    /// <summary>
    /// Runs an action, reporting failures in the status bar.
    /// </summary>
    /// <remarks>
    /// The original showed a modal message box from twenty-odd catch blocks and
    /// carried on regardless. A status line is less intrusive and does not
    /// interrupt what the user was doing.
    ///
    /// The filter stays narrow deliberately: <c>.editorconfig</c> reports
    /// <c>CA1031</c>, so a bare <c>catch (Exception)</c> fails the build rather
    /// than quietly swallowing a defect.
    /// </remarks>
    public void RunGuarded(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            action();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    /// <inheritdoc cref="RunGuarded"/>
    public async Task RunGuardedAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            await action().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    /// <summary>
    /// What undo would reverse, as the menu item's own label.
    /// </summary>
    /// <remarks>
    /// Naming what will be undone is the difference between a safe click and a
    /// guess. The accelerator is part of the string because the header is what
    /// carries it.
    /// </remarks>
    public string UndoHeader =>
        _session.History.UndoDescription is { } undoing ? $"_Undo {undoing}" : "_Undo";

    /// <inheritdoc cref="UndoHeader"/>
    public string RedoHeader =>
        _session.History.RedoDescription is { } redoing ? $"_Redo {redoing}" : "_Redo";

    /// <summary>The same two labels, without the menu accelerator.</summary>
    /// <remarks>
    /// A context menu paints no accelerators, so it cannot show the underscore
    /// the menu bar needs.
    /// </remarks>
    public string UndoContextHeader =>
        _session.History.UndoDescription is { } undoing ? $"Undo {undoing}" : "Undo";

    /// <inheritdoc cref="UndoContextHeader"/>
    public string RedoContextHeader =>
        _session.History.RedoDescription is { } redoing ? $"Redo {redoing}" : "Redo";

    public bool CanUndo => _session.History.CanUndo;

    public bool CanRedo => _session.History.CanRedo;

    /// <summary>How many elements are selected, which most of the Edit menu follows.</summary>
    private int SelectionCount => _session.Canvas.Selection.Count;

    public bool CanCut => SelectionCount > 0;

    public bool CanCopy => SelectionCount > 0;

    public bool CanDelete => SelectionCount > 0;

    /// <summary>Reordering needs something to reorder.</summary>
    public bool CanReorder => SelectionCount > 0;

    /// <summary>Grouping needs at least two, or the group would hold one element.</summary>
    public bool CanGroup => SelectionCount >= 2;

    /// <summary>
    /// Whether anything in the selection is a group that could be dissolved.
    /// </summary>
    /// <remarks>
    /// A page's root is a group too, and dissolving it would empty the page, so
    /// it is excluded.
    /// </remarks>
    public bool CanUngroup =>
        _session.Canvas.Selection.Any(e => e is GroupElement { IsPageRoot: false });

    /// <summary>Aligning and spacing need at least two to align to each other.</summary>
    public bool CanArrange => SelectionCount >= 2;

    /// <summary>
    /// Raises every property the Edit menu and the context menu read.
    /// </summary>
    /// <remarks>
    /// Called from the history and the controller only — never per pointer move.
    /// Every menu item declared in markup is a live logical child from load, so
    /// each one is already listening; fanning this out during a drag would cost
    /// a raise per item per mouse event.
    /// </remarks>
    private void OnEditStateChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(UndoHeader));
        OnPropertyChanged(nameof(RedoHeader));
        OnPropertyChanged(nameof(UndoContextHeader));
        OnPropertyChanged(nameof(RedoContextHeader));
        OnPropertyChanged(nameof(CanCut));
        OnPropertyChanged(nameof(CanCopy));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(CanReorder));
        OnPropertyChanged(nameof(CanGroup));
        OnPropertyChanged(nameof(CanUngroup));
        OnPropertyChanged(nameof(CanArrange));

        RefreshCommands();
        SyncCollections();
    }

    /// <summary>
    /// Tells every command whose availability depends on the selection or the
    /// history to ask again.
    /// </summary>
    /// <remarks>
    /// Called from the history and the controller only. A menu item bound to a
    /// command reads <c>CanExecute</c> rather than the property behind it, so
    /// both have to be announced - and neither may be announced per pointer
    /// move, which is why this is not wired to anything the canvas raises during
    /// a drag.
    /// </remarks>
    private void RefreshCommands()
    {
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        CutCommand.NotifyCanExecuteChanged();
        CopyCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        GroupCommand.NotifyCanExecuteChanged();
        UngroupCommand.NotifyCanExecuteChanged();
        BringToFrontCommand.NotifyCanExecuteChanged();
        BringForwardCommand.NotifyCanExecuteChanged();
        SendBackwardCommand.NotifyCanExecuteChanged();
        SendToBackCommand.NotifyCanExecuteChanged();
        AlignLeftCommand.NotifyCanExecuteChanged();
        AlignRightCommand.NotifyCanExecuteChanged();
        AlignTopCommand.NotifyCanExecuteChanged();
        AlignBottomCommand.NotifyCanExecuteChanged();
        CentreHorizontallyCommand.NotifyCanExecuteChanged();
        CentreVerticallyCommand.NotifyCanExecuteChanged();
        SpaceHorizontallyCommand.NotifyCanExecuteChanged();
        SpaceVerticallyCommand.NotifyCanExecuteChanged();
    }

    // ---- Toggles -------------------------------------------------------

    /// <summary>
    /// Whether page 0 is drawn beneath the page being edited.
    /// </summary>
    /// <remarks>
    /// It is on by default because that is what the player sees; hiding it helps
    /// when a full-page background on page 0 obscures the page being edited.
    /// </remarks>
    public bool ShowSharedPage
    {
        get => _view.ShowSharedPage;
        set
        {
            if (_view.ShowSharedPage == value)
            {
                return;
            }

            _view.ShowSharedPage = value;

            OnPropertyChanged();

            _view.InvalidateCanvas();
        }
    }

    /// <summary>Whether the design grid is drawn.</summary>
    /// <remarks>
    /// Reads and writes the settings object the controller already holds rather
    /// than replacing it: the canvas compares it by reference to decide whether
    /// to rebuild its render options, so a new instance would stop it noticing.
    /// </remarks>
    public bool ShowGrid
    {
        get => _session.Canvas.Grid.Visible;
        set
        {
            if (_session.Canvas.Grid.Visible == value)
            {
                return;
            }

            _session.Canvas.Grid.Visible = value;

            SaveGridSettings();
            OnPropertyChanged();

            _view.InvalidateCanvas();
        }
    }

    /// <inheritdoc cref="ShowGrid"/>
    public bool SnapToGrid
    {
        get => _session.Canvas.Grid.SnapEnabled;
        set
        {
            if (_session.Canvas.Grid.SnapEnabled == value)
            {
                return;
            }

            _session.Canvas.Grid.SnapEnabled = value;

            SaveGridSettings();
            OnPropertyChanged();
        }
    }

    private bool _toolboxVisible = true;
    private bool _elementsVisible = true;
    private bool _propertiesVisible = true;
    private bool _clilocVisible = true;

    /// <summary>Whether the toolbox panel is showing.</summary>
    /// <remarks>
    /// Hiding and showing a panel lives on the View menu rather than on the
    /// tab's close button: Dock's close removes a dockable from its owner
    /// outright, so nothing could bring it back, while hide stores it on the
    /// root for <c>RestoreDockable</c> to find.
    /// </remarks>
    public bool ToolboxVisible
    {
        get => _toolboxVisible;
        set => SetPanel(ref _toolboxVisible, value, "ToolboxTool");
    }

    /// <inheritdoc cref="ToolboxVisible"/>
    public bool ElementsVisible
    {
        get => _elementsVisible;
        set => SetPanel(ref _elementsVisible, value, "ElementsTool");
    }

    /// <inheritdoc cref="ToolboxVisible"/>
    public bool PropertiesVisible
    {
        get => _propertiesVisible;
        set => SetPanel(ref _propertiesVisible, value, "PropertiesTool");
    }

    /// <inheritdoc cref="ToolboxVisible"/>
    public bool ClilocVisible
    {
        get => _clilocVisible;
        set => SetPanel(ref _clilocVisible, value, "ClilocTool");
    }

    private void SetPanel(
        ref bool field,
        bool value,
        string dockableId,
        [CallerMemberName] string? property = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;

        OnPropertyChanged(property);

        if (!_applyingLayout)
        {
            _view.ShowPanel(dockableId, value);
        }
    }

    /// <summary>
    /// Seeds the panel toggles from a remembered layout without acting on them.
    /// </summary>
    public void AdoptPanelVisibility(bool toolbox, bool elements, bool properties, bool cliloc)
    {
        _applyingLayout = true;

        try
        {
            ToolboxVisible = toolbox;
            ElementsVisible = elements;
            PropertiesVisible = properties;
            ClilocVisible = cliloc;
        }
        finally
        {
            _applyingLayout = false;
        }
    }

    private void OnDocumentChanged()
    {
        OnPropertyChanged(nameof(Title));

        OnEditStateChanged();
    }
}
