using GumpStudio.Core.Commands;
using GumpStudio.Core.Document;
using GumpStudio.Core.Export;

namespace GumpStudio.Plugins;

/// <summary>Identifying information a plugin reports about itself.</summary>
/// <param name="Id">
/// A stable identifier used to persist which plugins are enabled and in what
/// order. Unlike the original, which compared all five description fields by
/// value, changing a version or a description does not change identity.
/// </param>
/// <param name="Name">Name shown in the plugin manager.</param>
/// <param name="Version">Display version.</param>
/// <param name="Author">Attribution shown in the plugin manager.</param>
/// <param name="Description">One-line summary.</param>
public readonly record struct PluginInfo(
    string Id,
    string Name,
    string Version = "1.0",
    string Author = "",
    string Description = "");

/// <summary>
/// A plugin.
/// </summary>
/// <remarks>
/// Nothing in this contract references a UI toolkit. The old
/// <c>BasePlugin.Load(DesignerForm)</c> handed plugins the WinForms window and
/// its menu items, which made every plugin a WinForms plugin and made the plugin
/// API impossible to keep when the UI changed.
/// </remarks>
public interface IGumpStudioPlugin
{
    PluginInfo Info { get; }

    /// <summary>Called once when the plugin is enabled.</summary>
    void Initialize(IPluginHost host);

    /// <summary>Called when the plugin is disabled or the host shuts down.</summary>
    void Shutdown()
    {
    }
}

/// <summary>What the application offers a plugin.</summary>
public interface IPluginHost
{
    /// <summary>The document being edited, and the way to change it.</summary>
    IGumpDocumentSession Session { get; }

    /// <summary>Adds an exporter to the export menu.</summary>
    void RegisterExporter(IGumpExporter exporter);

    /// <summary>Adds a command to the menus.</summary>
    void RegisterMenuCommand(MenuCommandDescriptor descriptor);

    /// <summary>Reports a message to the user without assuming any UI.</summary>
    void Notify(PluginNotificationLevel level, string message);
}

/// <summary>Severity of a plugin notification.</summary>
public enum PluginNotificationLevel
{
    Information,
    Warning,
    Error,
}

/// <summary>
/// A menu entry contributed by a plugin.
/// </summary>
/// <remarks>
/// Declarative, so the shell builds the widget. Plugins never construct UI
/// objects, which is what lets the same plugin work under any front end.
/// </remarks>
/// <param name="Id">Stable identifier, unique within the plugin.</param>
/// <param name="Path">
/// Menu location as a slash-separated path, for example <c>File/Export</c>.
/// </param>
/// <param name="Title">Text shown in the menu.</param>
/// <param name="Execute">Runs the command.</param>
/// <param name="CanExecute">Optional predicate controlling whether it is enabled.</param>
public readonly record struct MenuCommandDescriptor(
    string Id,
    string Path,
    string Title,
    Action Execute,
    Func<bool>? CanExecute = null);

/// <summary>
/// The editing session a plugin may read and change.
/// </summary>
/// <remarks>
/// Mutation goes through <see cref="Apply"/> so a plugin's changes are undoable
/// like any other edit. The old API handed plugins the live object graph with no
/// undo integration at all.
/// </remarks>
public interface IGumpDocumentSession
{
    /// <summary>The document currently open.</summary>
    GumpDocument Document { get; }

    /// <summary>Index of the page being edited.</summary>
    int ActivePageIndex { get; }

    /// <summary>Applies a change as a single undoable step.</summary>
    void Apply(IUndoableCommand command);

    /// <summary>Raised when the open document is replaced.</summary>
    event EventHandler? DocumentChanged;
}
