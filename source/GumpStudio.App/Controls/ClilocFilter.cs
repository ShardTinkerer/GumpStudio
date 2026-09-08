using System.Globalization;

namespace GumpStudio.App.Controls;

/// <summary>
/// How a typed query narrows the cliloc table.
/// </summary>
/// <remarks>
/// <para>
/// An id matches on a prefix and the text matches anywhere, both ordinal and
/// case-insensitive — the same split <see cref="PickerEntry.Matches"/>
/// documents, for the same reason: typing <c>1044</c> should narrow rather than
/// jump, while a partial word has to be found wherever it sits in the string. A
/// run of digits tries both, so <c>1044017</c> finds the id and <c>vendor</c>
/// finds the text without the field needing a mode.
/// </para>
/// <para>
/// Hex is not accepted, unlike the art browsers. Gump and item ids are quoted in
/// hex all over server scripts; cliloc ids never are — they are decimal in every
/// script and every table — so a <c>0x</c> branch would add an arm to a two-arm
/// rule for a spelling nobody writes.
/// </para>
/// <para>
/// Separate from the panel so the rule can be tested as a rule, with no
/// dispatcher and no controls involved.
/// </para>
/// </remarks>
public static class ClilocFilter
{
    /// <summary>Longest an int can be in decimal, with room for a sign.</summary>
    private const int MaxDigits = 12;

    /// <summary>Whether one entry survives a query.</summary>
    public static bool Matches(int id, string text, string query)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(query);

        ReadOnlySpan<char> trimmed = query.AsSpan().Trim();

        if (trimmed.IsEmpty)
        {
            return true;
        }

        if (text.AsSpan().Contains(trimmed, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Formatted into a stack buffer rather than allocated: this runs once
        // per entry per keystroke, over about 124,000 entries.
        Span<char> digits = stackalloc char[MaxDigits];

        return id.TryFormat(digits, out int written, provider: CultureInfo.InvariantCulture)
            && digits[..written].StartsWith(trimmed, StringComparison.Ordinal);
    }
}
