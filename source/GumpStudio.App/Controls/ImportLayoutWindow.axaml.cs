using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;

using GumpStudio.Core.Document;
using GumpStudio.Core.Layout;

namespace GumpStudio.App.Controls;

/// <summary>
/// Reads a gump captured off the wire.
/// </summary>
/// <remarks>
/// <para>
/// Packet-sniffing tools dump the layout string a server sent, which is the same
/// grammar the editor's own <c>layout</c> export writes — so a capture can be
/// pasted straight in and edited as a document.
/// </para>
/// <para>
/// It opens with whatever is on the clipboard already in the box when that looks
/// like a layout, because arriving here having just copied one is the whole
/// point. The text stays editable: captures are often truncated mid-command, and
/// being able to delete the broken line beats being told the paste is unusable.
/// </para>
/// </remarks>
public sealed partial class ImportLayoutWindow : Window
{
    private readonly TextBox _layout = null!;
    private readonly TextBlock _summary = null!;

    public ImportLayoutWindow()
    {
        AvaloniaXamlLoader.Load(this);

        _layout = this.FindControl<TextBox>("LayoutBox")!;
        _summary = this.FindControl<TextBlock>("Summary")!;

        _layout.TextChanged += (_, _) => Preview();

        this.FindControl<Button>("PasteButton")!.Click += async (_, _) =>
            await PasteAsync().ConfigureAwait(true);

        this.FindControl<Button>("AcceptButton")!.Click += (_, _) => Accept();
        this.FindControl<Button>("CancelButton")!.Click += (_, _) => Close();

        Opened += async (_, _) =>
        {
            if (_layout.Text is null or "")
            {
                await PasteAsync(onlyIfLayout: true).ConfigureAwait(true);
            }

            Preview();
        };
    }

    /// <summary>The imported document, or null when cancelled or unusable.</summary>
    public GumpDocument? Result { get; private set; }

    /// <summary>What the parser had to say about the import that was accepted.</summary>
    public IReadOnlyList<string> Warnings { get; private set; } = [];

    private async Task PasteAsync(bool onlyIfLayout = false)
    {
        if (Clipboard is not { } clipboard)
        {
            return;
        }

        string? text = await ReadTextAsync(clipboard).ConfigureAwait(true);

        if (text is null || (onlyIfLayout && !LayoutStringParser.LooksLikeLayout(text)))
        {
            return;
        }

        _layout.Text = text;
    }

    /// <summary>
    /// The clipboard's text, or null.
    /// </summary>
    /// <remarks>
    /// The clipboard holds whatever was last copied anywhere, so text that is not
    /// a layout — or no text at all — is an ordinary outcome rather than an error.
    /// The transfer object owns platform resources and must be disposed.
    /// </remarks>
    private static async Task<string?> ReadTextAsync(IClipboard clipboard)
    {
        try
        {
            using IAsyncDataTransfer? transfer =
                await clipboard.TryGetDataAsync().ConfigureAwait(true);

            return transfer is null
                ? null
                : await transfer.TryGetTextAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return null;
        }
    }

    /// <summary>Says what the text in the box would produce, before committing to it.</summary>
    private void Preview()
    {
        string text = _layout.Text ?? string.Empty;

        if (text.Trim().Length == 0)
        {
            _summary.Text = "Nothing to import yet.";

            return;
        }

        LayoutImportResult result = GumpLayoutReader.Import(text);
        int elements = result.Document.Pages.Sum(page => page.Leaves().Count());

        if (elements == 0)
        {
            _summary.Text = "No gump commands were recognised in that text.";

            return;
        }

        string summary =
            $"{elements} elements across {result.Document.PageCount} pages.";

        _summary.Text = result.Warnings.Count == 0
            ? summary
            : $"{summary} {result.Warnings.Count} line(s) could not be used: {result.Warnings[0]}";
    }

    private void Accept()
    {
        LayoutImportResult result = GumpLayoutReader.Import(_layout.Text ?? string.Empty);

        if (result.Document.Pages.Sum(page => page.Leaves().Count()) == 0)
        {
            _summary.Text = "No gump commands were recognised in that text.";

            return;
        }

        Result = result.Document;
        Warnings = result.Warnings;

        Close();
    }
}
