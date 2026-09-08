using System.Globalization;

using Avalonia.Controls;
using Avalonia.Media;

using GumpStudio.Uo.Data;

namespace GumpStudio.App.Controls;

/// <summary>
/// The hover card behind a cliloc id in the properties editor.
/// </summary>
/// <remarks>
/// <para>
/// A cliloc id is a seven-digit number, and the property grid showed nothing
/// else — readable only to someone who had it memorised. This shows what the
/// player will actually read, resolved through the same
/// <see cref="ClilocFormatter"/> the canvas uses, so the two cannot disagree.
/// </para>
/// <para>
/// A static builder taking values rather than a session, so what it shows for a
/// resolved string, an unknown id and an id of zero can be asserted without a
/// client, a window, or a pointer.
/// </para>
/// </remarks>
internal static class ClilocTip
{
    /// <summary>Widest the card grows before its text wraps.</summary>
    private const double MaxCardWidth = 420;

    /// <summary>Builds the card.</summary>
    /// <param name="clilocId">The id the row holds.</param>
    /// <param name="arguments">The sibling property's <c>@</c>-separated values.</param>
    /// <param name="language">The cliloc file being read, for the header.</param>
    /// <param name="resolve">Looks a cliloc up without blocking.</param>
    /// <param name="unavailable">
    /// Why there is nothing to resolve with, or null when there is.
    /// </param>
    public static Control Build(
        int clilocId,
        string arguments,
        string? language,
        Func<int, string?> resolve,
        string? unavailable)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(resolve);

        StackPanel card = new() { MaxWidth = MaxCardWidth, Spacing = 3 };

        card.Children.Add(new TextBlock
        {
            Text = language is null
                ? string.Create(CultureInfo.InvariantCulture, $"{clilocId}")
                : string.Create(
                    CultureInfo.InvariantCulture, $"{clilocId} — {language.ToUpperInvariant()}"),
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.LightSkyBlue,
        });

        if (unavailable is { } reason)
        {
            card.Children.Add(Note(reason));

            return card;
        }

        if (clilocId == 0)
        {
            card.Children.Add(Note("No cliloc — 0 means none."));

            return card;
        }

        if (resolve(clilocId) is not { } raw)
        {
            // Worded to match what ElementPainter actually draws for a cliloc
            // the client does not have.
            card.Children.Add(Note(string.Create(
                CultureInfo.InvariantCulture,
                $"Not in this client's cliloc file. The canvas shows #{clilocId}.")));

            return card;
        }

        card.Children.Add(Line(raw));

        if (arguments.Length == 0)
        {
            return card;
        }

        string filled = ClilocFormatter.Substitute(raw, arguments, resolve);

        // A string with no placeholders would otherwise be printed twice.
        if (!string.Equals(filled, raw, StringComparison.Ordinal))
        {
            card.Children.Add(Note($"with {arguments}"));
            card.Children.Add(Line(filled));
        }

        return card;
    }

    private static TextBlock Line(string text) =>
        new() { Text = text, Foreground = Brushes.Silver, TextWrapping = TextWrapping.Wrap };

    private static TextBlock Note(string text) =>
        new() { Text = text, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap };
}
