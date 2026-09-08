using System.Globalization;

using GumpStudio.Core.Document;
using GumpStudio.Core.Elements;

namespace GumpStudio.Core.Layout;

/// <summary>
/// Syntax knobs for <see cref="LayoutStringWriter"/>.
/// </summary>
/// <remarks>
/// The element commands are identical between every dialect that speaks layout
/// strings — verified command by command, including the awkward ones. Only the
/// surrounding conventions differ, and this is the whole of that difference.
/// </remarks>
public sealed record LayoutStringOptions
{
    /// <summary>How the radio-group command is spelled. POL lowercases it; Sphere does not.</summary>
    public string GroupKeyword { get; init; } = "group";

    /// <summary>
    /// Whether a page's open radio group is closed at its end.
    /// </summary>
    /// <remarks>
    /// POL emits <c>endgroup</c>; the Sphere exporter never has. Emitting one
    /// where it was previously absent changes every Sphere script that uses a
    /// radio, so it stays a dialect setting rather than becoming a shared rule.
    /// </remarks>
    public bool EmitEndGroup { get; init; } = true;
}

/// <summary>
/// Writes the client's own layout-string grammar.
/// </summary>
/// <remarks>
/// <para>
/// One implementation of the grammar, where there were four. Two dialects emit
/// it as their output; the other two build it only to comment it out, as the note
/// beside a command their target has no function for — which is why
/// <see cref="Format(LayoutCommand, LayoutStringOptions, Func{TextRef, string})"/>
/// formats a single command as well as
/// <see cref="Write(GumpLayout, LayoutStringOptions, Func{TextRef, string})"/>
/// writing a whole document.
/// </para>
/// <para>
/// Text slots are resolved by a caller-supplied function rather than baked in. A
/// dialect with a data array passes the slot index; one that writes strings
/// inline passes the string; and the gump package's notes pass a literal
/// <c>0</c>, because that output has no array for an index to point into.
/// </para>
/// </remarks>
public static class LayoutStringWriter
{
    /// <summary>Resolves a text slot to its index, for dialects with a data array.</summary>
    public static string ByIndex(TextRef text) =>
        text.Index.ToString(CultureInfo.InvariantCulture);

    /// <summary>Resolves every text slot to <c>0</c>, for notes with no data array.</summary>
    public static string AsZero(TextRef text) => "0";

    /// <summary>
    /// The commands set by gump-level flags rather than by an element.
    /// </summary>
    /// <remarks>
    /// The movable, closable and disposable flags are deliberately absent: their
    /// spelling and their order both differ per dialect, so each converter emits
    /// its own. These four do not differ.
    /// </remarks>
    public static IEnumerable<string> GumpLevelTokens(GumpProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        if (properties.MasterGumpId != 0)
        {
            yield return Invariant($"mastergump {properties.MasterGumpId}");
        }

        // Parser toggles: emitting one flips it for the rest of the definition,
        // which is why they carry no argument and appear once.
        if (properties.UpperWordCase)
        {
            yield return "toggleupperwordcase";
        }

        if (properties.CroppedText)
        {
            yield return "togglecroppedtext";
        }

        if (properties.EnhancedClientInput)
        {
            yield return "echandleinput";
        }
    }

    /// <summary>Writes every command in a layout, skipping those the dialect omits.</summary>
    public static IEnumerable<string> Write(
        GumpLayout layout, LayoutStringOptions options, Func<TextRef, string> resolveText)
    {
        ArgumentNullException.ThrowIfNull(layout);

        foreach (LayoutCommand command in layout.Commands)
        {
            if (Format(command, options, resolveText) is { } line)
            {
                yield return line;
            }
        }
    }

    /// <summary>
    /// Formats one command, or returns null when the dialect does not emit it.
    /// </summary>
    public static string? Format(
        LayoutCommand command, LayoutStringOptions options, Func<TextRef, string> resolveText)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(resolveText);

        return command switch
        {
            PageCommand page => Invariant($"page {page.Page}"),

            GroupCommand group => Invariant($"{options.GroupKeyword} {group.Group}"),

            EndGroupCommand => options.EmitEndGroup ? "endgroup" : null,

            ResizePicCommand c =>
                Invariant($"resizepic {c.X} {c.Y} {c.GumpId} {c.Width} {c.Height}"),

            CheckerTransCommand c =>
                Invariant($"checkertrans {c.X} {c.Y} {c.Width} {c.Height}"),

            GumpPicTiledCommand c =>
                Invariant($"gumppictiled {c.X} {c.Y} {c.Width} {c.Height} {c.GumpId}"),

            GumpPicCommand c => GumpPic(c),

            PicInPicCommand c => PicInPic(c),

            TilePicCommand c => c.Hue != 0
                ? Invariant($"tilepichue {c.X} {c.Y} {c.ItemId} {c.Hue}")
                : Invariant($"tilepic {c.X} {c.Y} {c.ItemId}"),

            TileAsGumpPicCommand c =>
                Invariant($"tilepicasgumppic {c.X} {c.Y} {c.ItemId} {c.LinkId} {c.ParamB} {c.ParamC}"),

            TextCommand c =>
                Invariant($"text {c.X} {c.Y} {c.Hue} ") + resolveText(c.Text),

            CroppedTextCommand c =>
                Invariant($"croppedtext {c.X} {c.Y} {c.Width} {c.Height} {c.Hue} ") + resolveText(c.Text),

            TextEntryCommand c => TextEntry(c, resolveText),

            HtmlGumpCommand c => string.Concat(
                Invariant($"htmlgump {c.X} {c.Y} {c.Width} {c.Height} "),
                resolveText(c.Text),
                Invariant($" {Flag(c.Background)} {Flag(c.Scrollbar)}")),

            XmfHtmlCommand c => XmfHtml(c),

            ButtonCommand c => Button(c),

            CheckboxCommand c => Invariant(
                $"checkbox {c.X} {c.Y} {c.UncheckedId} {c.CheckedId} {Flag(c.IsChecked)} {c.Group}"),

            RadioCommand c => Invariant(
                $"radio {c.X} {c.Y} {c.UncheckedId} {c.CheckedId} {Flag(c.IsChecked)} {c.Value}"),

            TooltipCommand c => c.Arguments.Length > 0
                ? Invariant($"tooltip {c.ClilocId} @{c.Arguments}@")
                : Invariant($"tooltip {c.ClilocId}"),

            ItemPropertyCommand c => Invariant($"itemproperty {c.Serial}"),

            _ => throw new NotSupportedException(
                $"No layout-string form for '{command.GetType().Name}'."),
        };
    }

    /// <summary>
    /// A gump image, tinted fully, tinted partially, or plain.
    /// </summary>
    /// <remarks>
    /// <c>gumppicphued</c> tints only the grayscale pixels, which is what dyeable
    /// art needs; the plain hued form flattens the graphic to a single shade.
    /// </remarks>
    private static string GumpPic(GumpPicCommand c) => c switch
    {
        { Hue: 0 } => Invariant($"gumppic {c.X} {c.Y} {c.GumpId}"),
        { PartialHue: true } => Invariant($"gumppicphued {c.X} {c.Y} {c.GumpId} {c.Hue}"),
        _ => Invariant($"gumppic {c.X} {c.Y} {c.GumpId} {c.Hue}"),
    };

    private static string PicInPic(PicInPicCommand c)
    {
        string command = c switch
        {
            { Hue: 0 } => "picinpic",
            { PartialHue: true } => "picinpicphued",
            _ => "picinpichued",
        };

        string region = Invariant($"{c.X} {c.Y} {c.GumpId} {c.SourceX} {c.SourceY} {c.Width} {c.Height}");

        return c.Hue == 0
            ? $"{command} {region}"
            : string.Concat($"{command} {region}", Invariant($" {c.Hue}"));
    }

    private static string TextEntry(TextEntryCommand c, Func<TextRef, string> resolveText) =>
        c.MaxLength > 0
            ? string.Concat(
                Invariant($"textentrylimited {c.X} {c.Y} {c.Width} {c.Height} {c.Hue} {c.EntryId} "),
                resolveText(c.Text),
                Invariant($" {c.MaxLength}"))
            : string.Concat(
                Invariant($"textentry {c.X} {c.Y} {c.Width} {c.Height} {c.Hue} {c.EntryId} "),
                resolveText(c.Text));

    private static string XmfHtml(XmfHtmlCommand c)
    {
        string rect = Invariant($"{c.X} {c.Y} {c.Width} {c.Height}");
        string flags = Invariant($"{Flag(c.Background)} {Flag(c.Scrollbar)}");

        if (c.Arguments.Length > 0)
        {
            return string.Concat(
                $"xmfhtmltok {rect} {flags} ",
                Invariant($"{c.Color} {c.ClilocId} @{c.Arguments}@"));
        }

        return c.Color != 0
            ? Invariant($"xmfhtmlgumpcolor {rect} {c.ClilocId} {flags} {c.Color}")
            : Invariant($"xmfhtmlgump {rect} {c.ClilocId} {flags}");
    }

    /// <summary>
    /// A button, or a button with tile art over it.
    /// </summary>
    /// <remarks>
    /// The slots are <c>quit</c>, <c>page-id</c>, <c>return-value</c>. A page
    /// button does not quit and carries its target in the page slot; a reply
    /// button quits and carries its response id in the return slot. The 1.8
    /// exporter had all three wrong, so a page button closed the gump and a reply
    /// button jumped to a page numbered after its reply id.
    /// </remarks>
    private static string Button(ButtonCommand c)
    {
        bool isPage = c.Kind == ButtonKind.Page;

        int quit = isPage ? 0 : 1;
        int pageId = isPage ? c.Param : 0;
        int returnValue = isPage ? 0 : c.Param;

        string command = c.Tile is null ? "button" : "buttontileart";
        string line = Invariant(
            $"{command} {c.X} {c.Y} {c.NormalId} {c.PressedId} {quit} {pageId} {returnValue}");

        return c.Tile is { } tile
            ? string.Concat(line, Invariant($" {tile.ItemId} {tile.Hue} {tile.X} {tile.Y}"))
            : line;
    }

    private static string Flag(bool value) => value ? "1" : "0";

    private static string Invariant(FormattableString text) =>
        text.ToString(CultureInfo.InvariantCulture);
}
