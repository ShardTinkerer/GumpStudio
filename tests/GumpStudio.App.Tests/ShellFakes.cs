using GumpStudio.App.ViewModels;
using GumpStudio.Core.Document;
using GumpStudio.Core.Export;
using GumpStudio.Core.Primitives;

namespace GumpStudio.App.Tests;

/// <summary>
/// A view that records what it was asked to do instead of doing it.
/// </summary>
/// <remarks>
/// What makes the shell's commands testable without a window. Every one of these
/// used to be a direct call into <c>MainWindow</c>, so proving that Ctrl+S saves
/// meant booting a dispatcher and a real Dock layout.
/// </remarks>
internal sealed class FakeShellView : IShellView
{
    public int Refreshes { get; private set; }

    public int CanvasInvalidations { get; private set; }

    public bool ShowSharedPage { get; set; } = true;

    public double Zoom { get; private set; } = 1.0;

    public int ZoomSteps { get; private set; }

    public bool FitRequested { get; private set; }

    public bool LayoutReset { get; private set; }

    public bool PickerEntriesForgotten { get; private set; }

    public bool Closed { get; private set; }

    /// <summary>Every panel the shell asked to hide or show, in order.</summary>
    public List<(string DockableId, bool Show)> PanelChanges { get; } = [];

    public void RefreshDocumentView() => Refreshes++;

    public void InvalidateCanvas() => CanvasInvalidations++;

    public void StepZoom(bool up) => ZoomSteps += up ? 1 : -1;

    public void SetZoom(double zoom) => Zoom = zoom;

    public void ZoomToFit() => FitRequested = true;

    public void ShowPanel(string dockableId, bool show) => PanelChanges.Add((dockableId, show));

    public void ResetLayout() => LayoutReset = true;

    public void ForgetPickerEntries() => PickerEntriesForgotten = true;

    public void CloseShell() => Closed = true;
}

/// <summary>
/// Dialogs that answer with whatever a test told them to.
/// </summary>
/// <remarks>
/// Each answer is a nullable field standing for "cancelled" when it is null,
/// which is the same shape the real dialogs report through.
/// </remarks>
internal sealed class FakeEditorDialogs : IEditorDialogs
{
    /// <summary>What <see cref="ConfirmAsync"/> answers. Null means it was never asked.</summary>
    public bool? ConfirmAnswer { get; set; }

    public string? LastConfirmMessage { get; private set; }

    public int Confirmations { get; private set; }

    public GumpSize? GridSizeAnswer { get; set; }

    public GumpProperties? GumpPropertiesAnswer { get; set; }

    public GumpExportOptions? ExportOptionsAnswer { get; set; }

    public (GumpDocument Document, IReadOnlyList<string> Warnings)? LayoutImportAnswer { get; set; }

    public string? OpenPathAnswer { get; set; }

    public string? SavePathAnswer { get; set; }

    public string? FolderAnswer { get; set; }

    public int AboutsShown { get; private set; }

    public FilePickerRequest? LastRequest { get; private set; }

    public Task<bool> ConfirmAsync(string message, string acceptText = "Discard")
    {
        Confirmations++;
        LastConfirmMessage = message;

        return Task.FromResult(ConfirmAnswer ?? true);
    }

    public Task<GumpSize?> ChooseGridSizeAsync(int width, int height) =>
        Task.FromResult(GridSizeAnswer);

    public Task<GumpProperties?> EditGumpPropertiesAsync(GumpProperties current) =>
        Task.FromResult(GumpPropertiesAnswer);

    public Task<GumpExportOptions?> ChooseExportOptionsAsync(
        string title, IReadOnlyList<ConverterDialect> dialects, GumpExportOptions defaults) =>
        Task.FromResult(ExportOptionsAnswer);

    public Task<(GumpDocument Document, IReadOnlyList<string> Warnings)?> ImportLayoutAsync() =>
        Task.FromResult(LayoutImportAnswer);

    public Task<string?> PickOpenPathAsync(FilePickerRequest request)
    {
        LastRequest = request;

        return Task.FromResult(OpenPathAnswer);
    }

    public Task<string?> PickSavePathAsync(FilePickerRequest request)
    {
        LastRequest = request;

        return Task.FromResult(SavePathAnswer);
    }

    public Task<string?> PickFolderAsync(string title) => Task.FromResult(FolderAnswer);

    public Task ShowAboutAsync()
    {
        AboutsShown++;

        return Task.CompletedTask;
    }
}

/// <summary>A clipboard held in a field.</summary>
internal sealed class FakeTextClipboard : ITextClipboard
{
    public bool IsAvailable { get; set; } = true;

    public string? Text { get; set; }

    public Task<string?> GetTextAsync() => Task.FromResult(Text);

    public Task SetTextAsync(string text)
    {
        Text = text;

        return Task.CompletedTask;
    }
}
