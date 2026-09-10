using GumpStudio.Core.Document;
using GumpStudio.Core.Export;
using GumpStudio.Core.Primitives;

namespace GumpStudio.App.ViewModels;

/// <summary>
/// A file to ask the user for: what to call the dialog and what to accept.
/// </summary>
/// <remarks>
/// A plain record rather than Avalonia's <c>FilePickerOpenOptions</c>, so that
/// asking for a file is something a view model can express. The patterns are
/// the same glob strings the picker wants.
/// </remarks>
/// <param name="Title">The picker's title.</param>
/// <param name="FilterName">What to call the accepted kind of file.</param>
/// <param name="Patterns">The globs to accept, such as <c>*.gump</c>.</param>
/// <param name="DefaultExtension">The extension to add when saving, without a dot.</param>
/// <param name="SuggestedFileName">The name a save picker opens with.</param>
public sealed record FilePickerRequest(
    string Title,
    string? FilterName = null,
    IReadOnlyList<string>? Patterns = null,
    string? DefaultExtension = null,
    string? SuggestedFileName = null);

/// <summary>
/// Everything the shell needs to ask the user, as the view model sees it.
/// </summary>
/// <remarks>
/// <para>
/// A dialog needs an owner window to be modal against, and a view model has no
/// business holding one — so the dependency is inverted. The window implements
/// this, passes itself in, and the view models ask.
/// </para>
/// <para>
/// The dialogs themselves are unchanged behind it. Their convention — construct
/// with the current value, <c>ShowDialog</c>, read a nullable <c>Result</c> —
/// already carries no reflection and needs no view location, so it survives an
/// AOT publish as it stands. What it could not do is be called from anywhere
/// that does not have a <c>Window</c> to hand.
/// </para>
/// </remarks>
public interface IEditorDialogs
{
    /// <summary>Asks a yes-or-no question. False means the user declined.</summary>
    /// <param name="message">The question, as a whole sentence.</param>
    /// <param name="acceptText">The wording on the accepting button.</param>
    Task<bool> ConfirmAsync(string message, string acceptText = "Discard");

    /// <summary>Asks for a grid size, or null when cancelled.</summary>
    Task<GumpSize?> ChooseGridSizeAsync(int width, int height);

    /// <summary>Asks for the gump's own properties, or null when cancelled.</summary>
    Task<GumpProperties?> EditGumpPropertiesAsync(GumpProperties current);

    /// <summary>Asks how to export, or null when cancelled.</summary>
    Task<GumpExportOptions?> ChooseExportOptionsAsync(
        string title, IReadOnlyList<ConverterDialect> dialects, GumpExportOptions defaults);

    /// <summary>
    /// Asks for a gump captured as layout text, or null when cancelled.
    /// </summary>
    /// <returns>The imported document and the lines that could not be used.</returns>
    Task<(GumpDocument Document, IReadOnlyList<string> Warnings)?> ImportLayoutAsync();

    /// <summary>Asks for a file to read, or null when cancelled.</summary>
    Task<string?> PickOpenPathAsync(FilePickerRequest request);

    /// <summary>Asks for a file to write, or null when cancelled.</summary>
    Task<string?> PickSavePathAsync(FilePickerRequest request);

    /// <summary>Asks for a folder, or null when cancelled.</summary>
    Task<string?> PickFolderAsync(string title);

    /// <summary>Shows the about box.</summary>
    Task ShowAboutAsync();
}
