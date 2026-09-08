using GumpStudio.Core.Document;

namespace GumpStudio.Core.Layout;

/// <summary>
/// A gump reduced to the client's own layout commands.
/// </summary>
/// <remarks>
/// <para>
/// The common export format. Every converter reads this rather than the
/// document, so the rules that are the same for all of them — absolute
/// coordinates, page boundaries, radio-group scoping, text-slot allocation, the
/// order a tooltip follows its element in — are decided once, in
/// <see cref="GumpLayoutBuilder"/>, instead of once per exporter.
/// </para>
/// <para>
/// It is an immutable snapshot. Nothing here points back at an
/// <see cref="Elements.Element"/>: the document is mutable and raises change
/// notifications, so a converter holding a live element could read a value that
/// moved after the layout was taken.
/// </para>
/// </remarks>
/// <param name="Properties">
/// Gump-level settings, kept as data rather than turned into commands. Each
/// target spells them differently and some emit nothing at all for them, so
/// tokenising here would only force converters to parse the tokens back.
/// </param>
/// <param name="Commands">The command stream, in emission order.</param>
/// <param name="Texts">
/// The strings the commands refer to, in allocation order. Dialects that write a
/// data array emit this verbatim; the rest resolve each reference inline.
/// </param>
public sealed record GumpLayout(
    GumpProperties Properties,
    IReadOnlyList<LayoutCommand> Commands,
    IReadOnlyList<LayoutText> Texts);

/// <summary>
/// One string in the layout's text table.
/// </summary>
/// <remarks>
/// The value is raw. Escaping and empty-text placeholders are writer policy,
/// because they differ per dialect in both trigger and wording — the POL gump
/// package substitutes a bare <c>TextLine</c>, its layout-string form
/// substitutes <c>Text id.2</c>, and RunUO substitutes nothing at all.
/// </remarks>
public readonly record struct LayoutText(string Value, TextRole Role);

/// <summary>What a text slot is for, which is what picks a placeholder word.</summary>
public enum TextRole
{
    Label,
    Entry,
    Html,
}

/// <summary>
/// A reference to a slot in <see cref="GumpLayout.Texts"/>.
/// </summary>
/// <remarks>
/// Deliberately not the resolved index or the resolved string. The same slot
/// reaches output three ways: as an index, as an inline literal, and as a literal
/// <c>0</c> in the gump package's notes about commands it cannot express — that
/// output has no data array for a real index to point into.
/// </remarks>
public readonly record struct TextRef(int Index);

/// <summary>
/// Where a command came from, for the parts of output the client never sees.
/// </summary>
/// <remarks>
/// <para>
/// Element names and comments have no layout representation, but all three
/// script formats emit them, and RunUO builds its <c>Buttons</c> enum member
/// names out of them.
/// </para>
/// <para>
/// <paramref name="Ordinal"/> is load-bearing. Converters run pre-passes over the
/// command stream and join the results back to individual commands; keying that
/// join on the command value would collapse two genuinely distinct commands that
/// happen to be identical — two reply buttons with the same name, response id and
/// position. Including the ordinal in the record also makes such commands compare
/// unequal, so value equality stays safe.
/// </para>
/// </remarks>
/// <param name="Ordinal">Position in the command stream. Unique within a layout.</param>
/// <param name="PageIndex">The page this command belongs to.</param>
/// <param name="IsPrimary">
/// True for the first command an element produces. A comment is emitted once per
/// element, but an element with a tooltip and an item property is three commands.
/// </param>
/// <param name="Name">The element's editor name, or empty.</param>
/// <param name="Comment">The element's editor comment, or empty.</param>
public readonly record struct CommandOrigin(
    int Ordinal,
    int PageIndex,
    bool IsPrimary,
    string Name,
    string Comment)
{
    /// <summary>An origin for a command no element produced, such as a page break.</summary>
    public static CommandOrigin Structural(int ordinal, int pageIndex) =>
        new(ordinal, pageIndex, IsPrimary: false, string.Empty, string.Empty);
}
