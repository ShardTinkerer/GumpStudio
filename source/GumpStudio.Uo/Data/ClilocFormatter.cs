using System.Globalization;
using System.Text;

namespace GumpStudio.Uo.Data;

/// <summary>
/// Turns a cliloc id and its argument list into the text a player would read.
/// </summary>
/// <remarks>
/// <para>
/// This lived inside the renderer, which was fine while the canvas was the only
/// thing that resolved a cliloc. The properties editor now previews one on
/// hover, and two implementations of the same substitution rules would drift —
/// so the rules live here, above the data layer and below every consumer.
/// </para>
/// <para>
/// The lookup arrives as a delegate rather than a table, which keeps this free
/// of any dependency on how clilocs are stored: the renderer passes
/// <c>IGumpArtSource.GetCliloc</c>, the editor passes the loaded table's
/// <c>GetText</c>, and a test passes a dictionary.
/// </para>
/// </remarks>
public static class ClilocFormatter
{
    /// <summary>The argument separator the client uses.</summary>
    private const char Delimiter = '@';

    /// <summary>
    /// The text for a cliloc id, falling back to <c>#id</c>.
    /// </summary>
    /// <remarks>
    /// The fallback is what the editor showed for every localised element before
    /// clilocs were resolved at all: readable only to someone who had the id
    /// memorised. It is kept because an id the client does not have still has to
    /// render as something, and <c>#1234</c> at least says which id is missing.
    /// </remarks>
    public static string Format(int clilocId, string? arguments, Func<int, string?> lookup)
    {
        ArgumentNullException.ThrowIfNull(lookup);

        if (lookup(clilocId) is not { } text)
        {
            return string.Create(CultureInfo.InvariantCulture, $"#{clilocId}");
        }

        // Substituting an empty argument list is already a no-op — every
        // placeholder falls out of range and is emitted verbatim — so this is
        // purely about not copying the string through a builder to learn that.
        return string.IsNullOrEmpty(arguments) ? text : Substitute(text, arguments, lookup);
    }

    /// <summary>
    /// Fills a cliloc's <c>~1_THING~</c> placeholders from the argument list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client numbers them from one and separates the values with <c>@</c>,
    /// running consecutive delimiters together. A placeholder with no argument is
    /// left as it stands rather than blanked, so a missing value is visible
    /// instead of silently disappearing.
    /// </para>
    /// <para>
    /// An argument of the form <c>#1234</c> is itself a cliloc id — the
    /// <c>xmfhtmltok</c> form uses that to nest one localised string inside
    /// another — so it is resolved in turn, once, without recursing.
    /// </para>
    /// </remarks>
    public static string Substitute(string text, string? arguments, Func<int, string?> lookup)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(lookup);

        // Empty tokens are dropped, because the client runs consecutive
        // delimiters together the way strtok does: `@@#1072325` names one
        // argument, not an empty one followed by a real one.
        string[] values =
            arguments?.Split(Delimiter, StringSplitOptions.RemoveEmptyEntries) ?? [];
        StringBuilder built = new(text.Length);

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '~')
            {
                built.Append(text[i]);

                continue;
            }

            int close = text.IndexOf('~', i + 1);

            if (close < 0)
            {
                built.Append(text[i..]);

                break;
            }

            built.Append(
                Ordinal(text[(i + 1)..close]) is { } index
                && index >= 1
                && index <= values.Length
                    ? Value(values[index - 1], lookup)
                    : text[i..(close + 1)]);

            i = close;
        }

        return built.ToString();
    }

    /// <summary>
    /// How many <c>@</c>-separated values a cliloc string needs.
    /// </summary>
    /// <remarks>
    /// The highest ordinal, not the number of <c>~…~</c> runs and not how many
    /// are distinct. A string using only <c>~2_VAL~</c> still needs two values,
    /// because the client fills slots by number; and one using <c>~1_VAL~</c>
    /// twice needs one. Used to tell the author how many arguments the string
    /// they are picking expects.
    /// </remarks>
    public static int ArgumentCount(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        int highest = 0;

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '~')
            {
                continue;
            }

            int close = text.IndexOf('~', i + 1);

            if (close < 0)
            {
                break;
            }

            if (Ordinal(text[(i + 1)..close]) is { } index && index > highest)
            {
                highest = index;
            }

            i = close;
        }

        return highest;
    }

    /// <summary>The placeholder's 1-based number, or null when it has none.</summary>
    /// <remarks>
    /// The name after the underscore is documentation for whoever wrote the
    /// string — <c>~1_VAL~</c> and <c>~1_NAME~</c> are the same slot — so only
    /// the leading number is read.
    /// </remarks>
    private static int? Ordinal(string placeholder)
    {
        int underscore = placeholder.IndexOf('_', StringComparison.Ordinal);
        string ordinal = underscore < 0 ? placeholder : placeholder[..underscore];

        return int.TryParse(
            ordinal, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
            ? index
            : null;
    }

    /// <summary>One substitution value, resolving a nested cliloc reference.</summary>
    private static string Value(string argument, Func<int, string?> lookup) =>
        argument.StartsWith('#')
        && int.TryParse(
            argument[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int nested)
            ? lookup(nested) ?? argument
            : argument;
}
