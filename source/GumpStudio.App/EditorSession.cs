using GumpStudio.Core.Commands;
using GumpStudio.Core.Document;
using GumpStudio.Core.Editing;
using GumpStudio.Core.Export;
using GumpStudio.Core.Serialization;
using GumpStudio.Plugins;
using GumpStudio.Rendering;
using GumpStudio.Uo;

namespace GumpStudio.App;

/// <summary>
/// Everything the editor is currently working on: the open document, the undo
/// history, the client data, the loaded plugins and the canvas state.
/// </summary>
/// <remarks>
/// The counterpart of the old <c>DesignerForm</c>'s field soup, but with no UI
/// in it — the window binds to this rather than owning the state itself.
/// </remarks>
public sealed class EditorSession : IPluginHost, IGumpDocumentSession, IDisposable
{
    private readonly List<IGumpExporter> _exporters = [];
    private readonly List<MenuCommandDescriptor> _menuCommands = [];
    private readonly PluginLoader _loader = new();

    private UoDataContext? _data;
    private UoArtSource? _art;
    private GumpDocument _document = new();
    private int _activePageIndex;

    public EditorSession()
    {
        History = new UndoHistory();
        Canvas = new CanvasInteractionController(History) { Page = _document.Pages[0] };
    }

    public GumpDocument Document => _document;

    public UndoHistory History { get; }

    public CanvasInteractionController Canvas { get; }

    /// <summary>Path the document was last saved to or loaded from.</summary>
    public string? DocumentPath { get; private set; }

    /// <summary>Client art, or null until a client directory is configured.</summary>
    public IGumpArtSource? Art => _art;

    /// <summary>The loaded client, or null.</summary>
    public UoDataContext? Data => _data;

    public IReadOnlyList<IGumpExporter> Exporters => _exporters;

    public IReadOnlyList<MenuCommandDescriptor> MenuCommands => _menuCommands;

    public IGumpDocumentSession Session => this;

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

    public GumpPage ActivePage => _document.Pages[_activePageIndex];

    public event EventHandler? DocumentChanged;

    public event EventHandler? PageChanged;

    /// <summary>Raised when a plugin or the app wants to tell the user something.</summary>
    public event EventHandler<PluginNotification>? Notified;

    /// <summary>Opens a client installation, replacing any already loaded.</summary>
    /// <returns>The files that were missing, or empty on success.</returns>
    public IReadOnlyList<string> OpenClient(string clientPath)
    {
        IReadOnlyList<string> missing = UoDataContext.Validate(clientPath);

        if (missing.Count > 0)
        {
            return missing;
        }

        // Replacing rather than mutating is what lets the client path change
        // without restarting, which the old application could not do.
        _art?.Dispose();
        _data?.Dispose();

        _data = UoDataContext.Open(clientPath);
        _art = new UoArtSource(_data);

        DocumentChanged?.Invoke(this, EventArgs.Empty);

        return [];
    }

    /// <summary>Discovers and initialises plugins from a directory.</summary>
    /// <remarks>
    /// Carries the loader's own restriction: a plugin is an assembly the build
    /// never saw, so a trimmed or ahead-of-time image cannot load it. Those
    /// builds call <see cref="RegisterBuiltInPlugins"/> instead.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(
        "Plugins are discovered from disk by reflection. Use RegisterBuiltInPlugins instead.")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode(
        "Plugins are discovered from disk by reflection. Use RegisterBuiltInPlugins instead.")]
    public IReadOnlyList<DiscoveredPlugin> LoadPlugins(string directory)
    {
        IReadOnlyList<DiscoveredPlugin> discovered = _loader.Discover(directory);

        foreach (DiscoveredPlugin plugin in discovered.Where(p => p.IsUsable))
        {
            try
            {
                plugin.Instance!.Initialize(this);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                // One misbehaving plugin must not take the editor down with it.
                Notify(PluginNotificationLevel.Error, $"{plugin.Info.Name} failed to start: {ex.Message}");
            }
        }

        return discovered;
    }

    /// <summary>Registers the exporters that ship in the box.</summary>
    public void RegisterBuiltInPlugins(params IGumpStudioPlugin[] plugins)
    {
        ArgumentNullException.ThrowIfNull(plugins);

        foreach (IGumpStudioPlugin plugin in plugins)
        {
            plugin.Initialize(this);
        }
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

    public void Save(string path)
    {
        GumpXmlSerializer.Save(_document, path);
        DocumentPath = path;
    }

    public void Apply(IUndoableCommand command) => History.Push(command);

    public void RegisterExporter(IGumpExporter exporter)
    {
        ArgumentNullException.ThrowIfNull(exporter);

        _exporters.Add(exporter);
    }

    public void RegisterMenuCommand(MenuCommandDescriptor descriptor) => _menuCommands.Add(descriptor);

    public void Notify(PluginNotificationLevel level, string message) =>
        Notified?.Invoke(this, new PluginNotification(level, message));

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

        MeasureActivePage();

        DocumentChanged?.Invoke(this, EventArgs.Empty);
        PageChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _loader.Dispose();
        _art?.Dispose();
        _data?.Dispose();
    }
}

/// <summary>A message destined for the user.</summary>
public readonly record struct PluginNotification(PluginNotificationLevel Level, string Message);
