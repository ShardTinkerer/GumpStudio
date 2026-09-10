using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;

using GumpStudio.App.ViewModels;

namespace GumpStudio.App.Controls;

/// <summary>The system clipboard, reached through one window.</summary>
/// <param name="owner">The window whose clipboard is used.</param>
internal sealed class WindowClipboard(Window owner) : ITextClipboard
{
    private readonly Window _owner = owner
        ?? throw new ArgumentNullException(nameof(owner));

    public bool IsAvailable => _owner.Clipboard is not null;

    public async Task<string?> GetTextAsync()
    {
        if (_owner.Clipboard is not { } clipboard)
        {
            return null;
        }

        // The transfer object owns platform resources and must be disposed.
        using IAsyncDataTransfer? transfer = await clipboard.TryGetDataAsync().ConfigureAwait(true);

        return transfer is null
            ? null
            : await transfer.TryGetTextAsync().ConfigureAwait(true);
    }

    public async Task SetTextAsync(string text)
    {
        if (_owner.Clipboard is not { } clipboard)
        {
            return;
        }

        // Avalonia 12 replaced SetTextAsync with a data-transfer object that can
        // carry several representations; text is the only one we offer.
        //
        // Deliberately not disposed: the clipboard takes ownership and may call
        // back into it to serve the data, so releasing it here would be handing
        // the system a payload we had already torn down.
#pragma warning disable CA2000
        DataTransfer payload = new();
#pragma warning restore CA2000

        payload.Add(DataTransferItem.CreateText(text));

        await clipboard.SetDataAsync(payload).ConfigureAwait(true);

        // Hands the data to the OS so it outlives this process. Windows only;
        // elsewhere the clipboard is served by the owning application anyway and
        // the call does nothing.
        await clipboard.FlushAsync().ConfigureAwait(true);
    }
}
