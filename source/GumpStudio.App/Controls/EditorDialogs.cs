using Avalonia.Controls;
using Avalonia.Platform.Storage;

using GumpStudio.App.ViewModels;
using GumpStudio.Core.Document;
using GumpStudio.Core.Export;
using GumpStudio.Core.Primitives;

namespace GumpStudio.App.Controls;

/// <summary>
/// Shows the editor's dialogs, owned by one window.
/// </summary>
/// <remarks>
/// The other half of <see cref="IEditorDialogs"/>: every method here is the body
/// that used to sit inline in <c>MainWindow</c>, unchanged except that the
/// result is returned rather than acted on. None of the dialogs themselves
/// changed.
/// </remarks>
/// <param name="owner">The window each dialog is modal against.</param>
internal sealed class EditorDialogs(Window owner) : IEditorDialogs
{
    private readonly Window _owner = owner
        ?? throw new ArgumentNullException(nameof(owner));

    public async Task<bool> ConfirmAsync(string message, string acceptText = "Discard")
    {
        ConfirmWindow dialog = new(message, acceptText);

        await dialog.ShowDialog(_owner).ConfigureAwait(true);

        return dialog.Confirmed;
    }

    public async Task<GumpSize?> ChooseGridSizeAsync(int width, int height)
    {
        GridSizeWindow dialog = new(width, height);

        await dialog.ShowDialog(_owner).ConfigureAwait(true);

        return dialog.Result;
    }

    public async Task<GumpProperties?> EditGumpPropertiesAsync(GumpProperties current)
    {
        GumpPropertiesWindow dialog = new(current);

        await dialog.ShowDialog(_owner).ConfigureAwait(true);

        return dialog.Result;
    }

    public async Task<GumpExportOptions?> ChooseExportOptionsAsync(
        string title, IReadOnlyList<ConverterDialect> dialects, GumpExportOptions defaults)
    {
        ExportOptionsWindow dialog = new(title, dialects, defaults);

        await dialog.ShowDialog(_owner).ConfigureAwait(true);

        return dialog.Result;
    }

    public async Task<(GumpDocument Document, IReadOnlyList<string> Warnings)?> ImportLayoutAsync()
    {
        ImportLayoutWindow dialog = new();

        await dialog.ShowDialog(_owner).ConfigureAwait(true);

        return dialog.Result is { } document ? (document, dialog.Warnings) : null;
    }

    public async Task<string?> PickOpenPathAsync(FilePickerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        IReadOnlyList<IStorageFile> files = await _owner.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = request.Title,
                AllowMultiple = false,
                FileTypeFilter = Filter(request),
            }).ConfigureAwait(true);

        return files.Count == 0 ? null : files[0].Path.LocalPath;
    }

    public async Task<string?> PickSavePathAsync(FilePickerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        IStorageFile? file = await _owner.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = request.Title,
                DefaultExtension = request.DefaultExtension,
                SuggestedFileName = request.SuggestedFileName,
            }).ConfigureAwait(true);

        return file?.Path.LocalPath;
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        IReadOnlyList<IStorageFolder> folders = await _owner.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
            }).ConfigureAwait(true);

        return folders.Count == 0 ? null : folders[0].Path.LocalPath;
    }

    public async Task ShowAboutAsync()
    {
        AboutWindow dialog = new();

        await dialog.ShowDialog(_owner).ConfigureAwait(true);
    }

    /// <summary>Turns a request's patterns into the picker's own filter list.</summary>
    /// <remarks>Null rather than an empty list, which the picker reads as "no filter".</remarks>
    private static IReadOnlyList<FilePickerFileType>? Filter(FilePickerRequest request) =>
        request is { FilterName: { } name, Patterns: { Count: > 0 } patterns }
            ? [new FilePickerFileType(name) { Patterns = [.. patterns] }]
            : null;
}
