namespace GumpStudio.App.ViewModels;

/// <summary>The system clipboard, as far as the shell uses it.</summary>
/// <remarks>
/// Text only. The editor puts a selection on the clipboard as the same XML
/// fragment it saves, which survives between instances, can be inspected by
/// pasting it anywhere, and cannot carry anything executable — unlike the
/// original's <c>BinaryFormatter</c> payload, which is a large part of why that
/// format had to go.
/// </remarks>
public interface ITextClipboard
{
    /// <summary>
    /// Whether there is a clipboard at all.
    /// </summary>
    /// <remarks>
    /// A window that has not been shown yet has no clipboard, and a headless
    /// one never does — so this is a real state rather than a defensive check.
    /// </remarks>
    bool IsAvailable { get; }

    /// <summary>The clipboard's text, or null when it holds none.</summary>
    Task<string?> GetTextAsync();

    /// <summary>Puts text on the clipboard.</summary>
    Task SetTextAsync(string text);
}
