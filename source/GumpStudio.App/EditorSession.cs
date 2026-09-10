using GumpStudio.Core.Commands;
using GumpStudio.Core.Document;
using GumpStudio.Core.Editing;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Export;
using GumpStudio.Core.Serialization;
using GumpStudio.Converters;
using GumpStudio.Rendering;
using GumpStudio.Uo;
using GumpStudio.Uo.Data;

namespace GumpStudio.App;

/// <summary>
/// Everything the editor is currently working on: the open document, the undo
/// history, the client data and the canvas state.
/// </summary>
/// <remarks>
/// The counterpart of the old <c>DesignerForm</c>'s field soup, but with no UI
/// in it — the window binds to this rather than owning the state itself.
/// </remarks>
public sealed class EditorSession : IDisposable
{

    private UoDataContext? _data;
    private UoArtSource? _art;
    private GumpDocument _document = new();
    private int _activePageIndex;
    private long _savedStateId;

    /// <summary>The in-flight cliloc read, shared by everything that wants it.</summary>
    private Task? _clilocLoad;

    public EditorSession()
        : this(AppSettings.Load())
    {
    }

    /// <param name="settings">
    /// The settings this session reads and writes. Injected so a test can point
    /// at a scratch file instead of the real one in the application-data folder.
    /// </param>
    public EditorSession(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Settings = settings;
        History = new UndoHistory();
        Canvas = new CanvasInteractionController(History) { Page = _document.Pages[0] };

        History.Changed += (_, _) => ModifiedChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// User preferences, loaded once and shared by every window.
    /// </summary>
    /// <remarks>
    /// One instance rather than a static read before each write: the editor used
    /// to reload the file at eight separate call sites, and one of them saved a
    /// freshly constructed instance, which silently reset every other setting.
    /// </remarks>
    public AppSettings Settings { get; }

    public GumpDocument Document => _document;

    public UndoHistory History { get; }

    public CanvasInteractionController Canvas { get; }

    /// <summary>Path the document was last saved to or loaded from.</summary>
    public string? DocumentPath { get; private set; }

    /// <summary>Client art, or null until a client directory is configured.</summary>
    public IGumpArtSource? Art => _art;

    /// <summary>The loaded client, or null.</summary>
    public UoDataContext? Data => _data;

    /// <summary>
    /// The client's cliloc strings, or null until they have been read.
    /// </summary>
    /// <remarks>
    /// Held here rather than reached through <see cref="Data"/> so that nothing
    /// can touch the table by accident: reading it is a MegaCliloc decode and
    /// around 124,000 strings, which is why opening a client deliberately
    /// defers it.
    /// </remarks>
    public IReadOnlyList<ClilocEntry>? ClilocStrings { get; private set; }

    /// <summary>Whether a cliloc lookup can answer without blocking.</summary>
    public bool AreClilocsReady => ClilocStrings is not null;

    /// <summary>The cliloc files the loaded client ships, as extension codes.</summary>
    public IReadOnlyList<string> ClilocLanguages => _data?.ClilocLanguages ?? [];

    /// <summary>The cliloc file currently being read, or null.</summary>
    public string? ClilocLanguage => _data?.ClilocLanguage;

    /// <summary>Raised when the cliloc strings, or the language, have changed.</summary>
    public event EventHandler? ClilocsChanged;

    /// <summary>
    /// Reads the cliloc table off the UI thread, once.
    /// </summary>
    /// <remarks>
    /// The same shape as <see cref="OpenClientAsync"/>: the reading happens on a
    /// thread pool thread and the result is adopted back on the caller's
    /// context, so nothing that listens has to think about threads. The task is
    /// cached, so a panel filling itself and a tooltip opening at the same
    /// moment share one parse instead of starting two.
    /// </remarks>
    public Task LoadClilocsAsync() => _clilocLoad ??= ReadClilocsAsync();

    private async Task ReadClilocsAsync()
    {
        if (_data is not { } data)
        {
            return;
        }

        IReadOnlyList<ClilocEntry> read =
            await Task.Run(() => data.Clilocs.Entries).ConfigureAwait(true);

        // The client may have been swapped while this was reading, in which case
        // these strings belong to a context nothing is looking at any more.
        if (!ReferenceEquals(_data, data))
        {
            return;
        }

        ClilocStrings = read;

        ClilocsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Switches cliloc language, remembers the choice and re-reads the table.
    /// </summary>
    /// <remarks>
    /// Silently does nothing when the client ships no such file. The caller is
    /// usually a list built from <see cref="ClilocLanguages"/>, but a remembered
    /// setting can name a language the current installation lacks.
    /// </remarks>
    public async Task UseClilocLanguageAsync(string language)
    {
        ArgumentNullException.ThrowIfNull(language);

        if (_data is not { } data || !data.UseClilocLanguage(language))
        {
            return;
        }

        Settings.ClilocLanguage = data.ClilocLanguage;
        Settings.Save();

        ClilocStrings = null;
        _clilocLoad = null;

        await LoadClilocsAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// One cliloc string, or null when it is unknown or not read yet.
    /// </summary>
    /// <remarks>
    /// Deliberately never starts the read. This is what a tooltip calls while
    /// the pointer rests on a property row, and forcing 124,000 strings there
    /// would freeze the pointer over the row.
    /// </remarks>
    public string? ResolveCliloc(int id) =>
        AreClilocsReady ? _data?.Clilocs.GetText(id) : null;

    /// <summary>The converters the export menu offers.</summary>
    public static IReadOnlyList<IGumpConverter> Converters => GumpConverters.All;

    public int ActivePageIndex
    {
        get => _activePageIndex;
        set
        {
            int clamped = Math.Clamp(value, 0, _document.PageCount - 1);

            if (clamped == _activePageIndex)
            {
                return;
            }

            _activePageIndex = clamped;
            Canvas.Page = _document.Pages[clamped];

            PageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The page being edited.</summary>
    /// <remarks>
    /// Clamped rather than indexed directly. Removing a page is two steps - the
    /// command takes it out of the document, and the caller then corrects
    /// <see cref="ActivePageIndex"/> - and anything listening to the undo history
    /// runs in between, when the index still points past the end. The setter has
    /// always clamped; this makes the pair agree.
    /// </remarks>
    public GumpPage ActivePage =>
        _document.Pages[Math.Clamp(_activePageIndex, 0, _document.PageCount - 1)];

    public event EventHandler? DocumentChanged;

    public event EventHandler? PageChanged;

    /// <summary>Raised when <see cref="IsModified"/> changes.</summary>
    public event EventHandler? ModifiedChanged;

    /// <summary>
    /// Whether the document differs from what was last saved or opened.
    /// </summary>
    /// <remarks>
    /// Derived from the undo history rather than a flag set by each mutation,
    /// so undoing back to the saved state reports clean again. It is only
    /// honest because every change to the document goes through the history —
    /// adding and removing a page used to bypass it.
    /// </remarks>
    public bool IsModified => History.StateId != _savedStateId;

    /// <summary>Opens a client installation, replacing any already loaded.</summary>
    /// <returns>The files that were missing, or empty on success.</returns>
    public IReadOnlyList<string> OpenClient(string clientPath)
    {
        IReadOnlyList<string> missing = UoDataContext.Validate(clientPath);

        if (missing.Count > 0)
        {
            return missing;
        }

        Adopt(UoDataContext.Open(clientPath));

        return [];
    }

    /// <summary>
    /// Opens a client installation without blocking the caller's thread.
    /// </summary>
    /// <returns>The files that were missing, or empty on success.</returns>
    /// <remarks>
    /// Opening a client reads the hue table, the tile data, the cliloc table and
    /// up to thirteen font files, and walks every index of every UOP container.
    /// It ran on the UI thread, so the window was frozen for the whole of it —
    /// including at startup, before anything had been drawn.
    ///
    /// Only the reading moves off the thread. The context and the art source are
    /// adopted, and <see cref="DocumentChanged"/> raised, back on the caller's
    /// context, so nothing that listens has to think about threads.
    /// </remarks>
    public async Task<IReadOnlyList<string>> OpenClientAsync(string clientPath)
    {
        IReadOnlyList<string> missing =
            await Task.Run(() => UoDataContext.Validate(clientPath)).ConfigureAwait(true);

        if (missing.Count > 0)
        {
            return missing;
        }

        UoDataContext opened =
            await Task.Run(() => UoDataContext.Open(clientPath)).ConfigureAwait(true);

        Adopt(opened);

        return [];
    }

    /// <summary>Takes over a freshly opened client, releasing any previous one.</summary>
    /// <remarks>
    /// Replacing rather than mutating is what lets the client path change
    /// without restarting, which the old application could not do.
    /// </remarks>
    private void Adopt(UoDataContext data)
    {
        _art?.Dispose();
        _data?.Dispose();

        _data = data;
        _art = new UoArtSource(data);

        // Before anything can read the table. A remembered language the new
        // installation does not ship is refused here and leaves its own choice
        // standing, which is why this is not checked first.
        if (Settings.ClilocLanguage is { } language)
        {
            data.UseClilocLanguage(language);
        }

        ClilocStrings = null;
        _clilocLoad = null;

        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void NewDocument() => Replace(new GumpDocument(), null);

    public void Open(string path)
    {
        Replace(GumpXmlSerializer.Load(path), path);
    }

    /// <summary>Imports a GumpStudio 1.8 file. The result must be saved as the new format.</summary>
    public void ImportLegacy(string path)
    {
        Replace(Core.Legacy.LegacyGumpImporter.ImportDocument(path), null);
    }

    /// <summary>
    /// Imports a 1.8 <c>.gumpling</c> as a group on the active page.
    /// </summary>
    /// <returns>The group that was added.</returns>
    /// <remarks>
    /// A gumpling is one saved group, not a document, so it is added to what is
    /// open rather than replacing it — and it goes through the undo history like
    /// any other insertion. The import picker offered <c>*.gumpling</c> long
    /// before anything could read one: every such file went to
    /// <see cref="ImportLegacy"/>, which only understands a whole document.
    /// </remarks>
    public GroupElement ImportGumpling(string path)
    {
        GroupElement group = Core.Legacy.LegacyGumpImporter.ImportGumpling(path);

        Apply(new AddElementCommand(ActivePage.Root, group));

        return group;
    }

    /// <summary>
    /// Adopts a document imported from captured layout text.
    /// </summary>
    /// <remarks>
    /// It has no path, like a legacy import: the capture is not a place the
    /// document can be saved back to.
    /// </remarks>
    public void AdoptImported(GumpDocument document) => Replace(document, null);

    public void Save(string path)
    {
        GumpXmlSerializer.Save(_document, path);

        DocumentPath = path;

        MarkSaved();
    }

    /// <summary>Treats the current state as the saved one.</summary>
    private void MarkSaved()
    {
        _savedStateId = History.StateId;

        ModifiedChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Applies an undoable change to the document.</summary>
    public void Apply(IUndoableCommand command) => History.Push(command);

    /// <summary>
    /// Measures art-derived element sizes on every page the canvas draws.
    /// </summary>
    /// <remarks>
    /// That includes page 0, which stays visible beneath whatever page is being
    /// edited, so its elements need real sizes even when it is not active.
    /// </remarks>
    public void MeasureActivePage()
    {
        if (_art is null)
        {
            return;
        }

        new GumpRenderer(_art).MeasureDocument(_document, _activePageIndex);
    }

    private void Replace(GumpDocument document, string? path)
    {
        _document = document;
        _activePageIndex = 0;
        DocumentPath = path;

        Canvas.Page = document.Pages[0];
        History.Clear();

        MarkSaved();

        MeasureActivePage();

        DocumentChanged?.Invoke(this, EventArgs.Empty);
        PageChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _art?.Dispose();
        _data?.Dispose();
    }
}
