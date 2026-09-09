using System.Globalization;
using System.Text;

using GumpStudio.Core.Document;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Layout;
using GumpStudio.Core.Primitives;

namespace GumpStudio.Converters;

/// <summary>Which POL dialect to emit.</summary>
public enum PolScriptStyle
{
    /// <summary>
    /// The distro gump package: <c>GFCreateGump</c> and friends from
    /// <c>:gumps:gumps</c>.
    /// </summary>
    GumpPackage,

    /// <summary>
    /// A raw layout-string array passed to <c>SendDialogGump</c>.
    /// </summary>
    LayoutStrings,
}

/// <summary>
/// Settings specific to POL output.
/// </summary>
/// <remarks>
/// A record class rather than a record struct on purpose. Defaulted
/// primary-constructor parameters on a struct are silently skipped by
/// <c>default</c> and by <c>new()</c>, so every "default" would have come out
/// <see langword="false"/> — which is exactly the sort of quiet wrong answer
/// this codebase is meant to remove.
/// </remarks>
public sealed record PolExportOptions
{
    /// <summary>Which dialect to emit.</summary>
    public PolScriptStyle Style { get; init; } = PolScriptStyle.GumpPackage;

    /// <summary>Emit element comments above each command.</summary>
    public bool IncludeComments { get; init; } = true;

    /// <summary>Emit element names above each command.</summary>
    public bool IncludeNames { get; init; } = true;

    /// <summary>Substitute a placeholder where text is empty.</summary>
    public bool PlaceholderText { get; init; } = true;
}

/// <summary>
/// Turns a gump layout into a POL script.
/// </summary>
/// <remarks>
/// <para>
/// Reads <see cref="GumpLayout"/> rather than the document, so what a gump means
/// — absolute coordinates, page boundaries, radio-group scoping, text-slot
/// allocation, the order a tooltip follows its element in — is decided once in
/// <see cref="GumpLayoutBuilder"/> and shared with every other converter. What is
/// left here is only how POL spells it.
/// </para>
/// <para>
/// Both dialects the original had are preserved, with the corrections it needed:
/// </para>
/// <list type="bullet">
/// <item>
/// Coordinates are absolute, fixing the inherited defect where an element inside
/// a group exported at its parent-relative position.
/// </item>
/// <item>
/// Text is escaped before being interpolated into a quoted POL string. The
/// original emitted a script that would not compile if any text held a quote.
/// </item>
/// <item>
/// Numbers format invariantly and the header timestamp is injectable, so two
/// exports of one gump are byte-identical and can be diffed.
/// </item>
/// <item>
/// The gump-package dialect emits a <c>GF*</c> call for every command in the
/// client's table, where the original knew only the element set of its day. That
/// needs a current <c>:gumps:gumps</c>; the layout-string dialect stays portable.
/// </item>
/// </list>
/// </remarks>
public static class PolScriptBuilder
{
    private const string PluginName = "POLGumpExporter";
    private const string PluginVersion = "2.0";

    /// <summary>POL lowercases the group command and closes the group at page end.</summary>
    private static readonly LayoutStringOptions Layout = new();

    /// <summary>
    /// Builds a POL script for a document.
    /// </summary>
    /// <param name="document">The gump to export.</param>
    /// <param name="gumpName">Identifier for the generated program.</param>
    /// <param name="options">Dialect and comment settings.</param>
    /// <param name="timestamp">
    /// Header timestamp. Injectable so that two exports of the same gump are
    /// byte-identical; the original stamped <c>DateTime.Now</c>, which made its
    /// output impossible to diff.
    /// </param>
    public static string Build(
        GumpDocument document,
        string gumpName,
        PolExportOptions? options = null,
        DateTimeOffset? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        return Build(GumpLayoutBuilder.Build(document), gumpName, options, timestamp);
    }

    /// <summary>Builds a POL script for a layout that has already been produced.</summary>
    /// <param name="layout">The gump to export.</param>
    /// <param name="gumpName">Identifier for the generated program.</param>
    /// <param name="options">Dialect and comment settings.</param>
    /// <param name="timestamp">Header timestamp; injectable for reproducible output.</param>
    public static string Build(
        GumpLayout layout,
        string gumpName,
        PolExportOptions? options = null,
        DateTimeOffset? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(layout);

        options ??= new PolExportOptions();

        string name = NormaliseName(gumpName);

        return options.Style == PolScriptStyle.GumpPackage
            ? BuildGumpPackage(layout, name, options, timestamp)
            : BuildLayoutStrings(layout, name, options, timestamp);
    }

    /// <summary>
    /// Reduces a user-supplied name to a single identifier token.
    /// </summary>
    /// <remarks>Matches the original, which took the first whitespace-separated word.</remarks>
    private static string NormaliseName(string? gumpName)
    {
        if (string.IsNullOrWhiteSpace(gumpName))
        {
            return "gump";
        }

        string[] parts = gumpName.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return parts.Length > 0 ? parts[0] : "gump";
    }

    private static string BuildGumpPackage(
        GumpLayout layout, string name, PolExportOptions options, DateTimeOffset? timestamp)
    {
        List<string> body = [];

        GumpPoint location = layout.Properties.Location;

        body.Add(location is { X: 0, Y: 0 }
            ? $"var {name} := GFCreateGump();"
            : Invariant($"var {name} := GFCreateGump({location.X},{location.Y});"));

        body.Add(string.Empty);

        if (!layout.Properties.Movable)
        {
            body.Add($"GFMovable({name}, 0);");
        }

        if (!layout.Properties.Closable)
        {
            body.Add($"GFClosable({name}, 0);");
        }

        if (!layout.Properties.Disposable)
        {
            body.Add($"GFDisposable({name}, 0);");
        }

        // The four commands LayoutStringWriter.GumpLevelTokens spells for the
        // layout-string dialects. The conditions are repeated rather than shared
        // because this dialect needs the call, not the token.
        if (layout.Properties.MasterGumpId != 0)
        {
            body.Add(Invariant($"GFMasterGump({name}, {layout.Properties.MasterGumpId});"));
        }

        if (layout.Properties.UpperWordCase)
        {
            body.Add($"GFToggleUpperWordCase({name});");
        }

        if (layout.Properties.CroppedText)
        {
            body.Add($"GFToggleCroppedText({name});");
        }

        if (layout.Properties.EnhancedClientInput)
        {
            body.Add($"GFECHandleInput({name});");
        }

        foreach (LayoutCommand command in layout.Commands)
        {
            AppendComment(body, command, options);
            AppendGumpPackageCommand(body, name, command, layout, options);
        }

        StringBuilder script = new();

        AppendHeader(script, timestamp, "for gump pkg", layout.Properties.TypeId);
        script.AppendLine("use uo;");
        script.AppendLine("use os;");
        script.AppendLine();
        script.AppendLine("include \":gumps:gumps\";");
        script.AppendLine();
        script.AppendLine(CultureInfo.InvariantCulture, $"program gump_{name}(who)");
        script.AppendLine();

        foreach (string line in body)
        {
            script.AppendLine("\t" + line);
        }

        script.AppendLine();
        script.AppendLine(CultureInfo.InvariantCulture, $"\tGFSendGump(who, {name});");
        script.AppendLine();
        script.AppendLine("endprogram");

        return script.ToString();
    }

    /// <summary>
    /// Emits one command as a gump-package call.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every command has a call. Some had been in the package for years and the
    /// 1.8 exporter did not know them — <c>GFPicTiled</c>, <c>GFTextCrop</c>,
    /// <c>GFTooltip</c>, <c>GFItemProperty</c>, <c>GFAddImageTileButton</c>, and
    /// the trailing parameters on <c>GFTextEntry</c> and
    /// <c>GFAddHTMLLocalized</c> — so it commented the command out or emitted a
    /// lossy near-miss. The rest did not exist and were added to the package.
    /// </para>
    /// <para>
    /// The exception is a value the package would renumber, which
    /// <see cref="RawLayout"/> writes out as a layout string instead.
    /// </para>
    /// </remarks>
    private static void AppendGumpPackageCommand(
        List<string> body,
        string name,
        LayoutCommand command,
        GumpLayout layout,
        PolExportOptions options)
    {
        switch (command)
        {
            case PageCommand page:
                if (page.Page > 0)
                {
                    body.Add(string.Empty);
                }

                body.Add(Invariant($"GFPage({name}, {page.Page});"));
                break;

            case GroupCommand group:
                body.Add(Invariant($"GFSetRadioGroup({name}, {group.Group});"));
                break;

            case EndGroupCommand:
                // Not optional, though the package went years without a call for
                // it: a group that is never closed does not work on pages above
                // the first, which is what made GFSetRadioGroup look page-1-only.
                body.Add(Invariant($"GFEndRadioGroup({name});"));
                break;

            case CheckerTransCommand c:
                body.Add(Invariant($"GFAddAlphaRegion({name}, {c.X}, {c.Y}, {c.Width}, {c.Height});"));
                break;

            case ResizePicCommand c:
                body.Add(Concat(
                    Invariant($"GFResizePic({name}, {c.X}, {c.Y}, {c.GumpId}, "),
                    Invariant($"{c.Width}, {c.Height});")));
                break;

            case GumpPicTiledCommand c:
                body.Add(Concat(
                    Invariant($"GFPicTiled({name}, {c.X}, {c.Y}, "),
                    Invariant($"{c.Width}, {c.Height}, {c.GumpId});")));
                break;

            // The trailing flag selects the partial form, which tints only the
            // grayscale pixels. Without it dyeable art flattens to one shade.
            case GumpPicCommand { PartialHue: true, Hue: not 0 } c:
                body.Add(Invariant($"GFGumpPic({name}, {c.X}, {c.Y}, {c.GumpId}, {c.Hue}, 1);"));
                break;

            case GumpPicCommand c:
                body.Add(Invariant($"GFGumpPic({name}, {c.X}, {c.Y}, {c.GumpId}, {c.Hue});"));
                break;

            case TilePicCommand c:
                body.Add(Invariant($"GFTilePic({name}, {c.X}, {c.Y}, {c.ItemId}, {c.Hue});"));
                break;

            case TextCommand c:
                body.Add(GfTextLine(name, c, layout, options));
                break;

            case CroppedTextCommand c:
                body.Add(GfTextCrop(name, c, layout, options));
                break;

            case TextEntryCommand c:
                body.Add(GfTextEntry(name, c, layout, options));

                if (c.EntryId <= 0)
                {
                    Reassigned(body, "GFTextEntry");
                }

                break;

            case HtmlGumpCommand c:
                body.Add(GfHtmlArea(name, c, layout, options));
                break;

            case XmfHtmlCommand c:
                body.Add(GfHtmlLocalized(name, c));
                break;

            // GFAddButton, GFCheckBox and GFRadioButton all replace a value below
            // one with the next free id, so a page-0 target or a zero response
            // survives only if the command is written out directly. None of the
            // three carries text, so the layout string is exact.
            case ButtonCommand { Param: <= 0 }:
                RawLayout(body, name, command, "GFAddButton would assign an id of its own");
                break;

            case CheckboxCommand { Group: <= 0 }:
                RawLayout(body, name, command, "GFCheckBox would assign an id of its own");
                break;

            case RadioCommand { Value: <= 0 }:
                RawLayout(body, name, command, "GFRadioButton would assign an id of its own");
                break;

            case ButtonCommand { Tile: { } tile } c:
                body.Add(Concat(
                    Invariant($"GFAddImageTileButton({name}, {c.X}, {c.Y}, {c.NormalId}, {c.PressedId}, "),
                    Invariant($"{ButtonType(c)}, {c.Param}, "),
                    Invariant($"{tile.ItemId}, {tile.Hue}, {tile.X}, {tile.Y});")));
                break;

            case ButtonCommand c:
                body.Add(Concat(
                    Invariant($"GFAddButton({name}, {c.X}, {c.Y}, {c.NormalId}, {c.PressedId}, "),
                    Invariant($"{ButtonType(c)}, {c.Param});")));
                break;

            case RadioCommand c:
                body.Add(Concat(
                    Invariant($"GFRadioButton({name}, {c.X}, {c.Y}, {c.UncheckedId}, {c.CheckedId}, "),
                    Invariant($"{Flag(c.IsChecked)}, {c.Value});")));
                break;

            case CheckboxCommand c:
                body.Add(Concat(
                    Invariant($"GFCheckBox({name}, {c.X}, {c.Y}, {c.UncheckedId}, {c.CheckedId}, "),
                    Invariant($"{Flag(c.IsChecked)}, {c.Group});")));
                break;

            case TooltipCommand c:
                body.Add(c.Arguments.Length > 0
                    ? Invariant($"GFTooltip({name}, {c.ClilocId}, \"{Escape(c.Arguments)}\");")
                    : Invariant($"GFTooltip({name}, {c.ClilocId});"));
                break;

            case ItemPropertyCommand c:
                body.Add(Invariant($"GFItemProperty({name}, {c.Serial});"));
                break;

            case PicInPicCommand c:
                body.Add(Concat(
                    Invariant($"GFPicInPic({name}, {c.X}, {c.Y}, {c.GumpId}, "),
                    Invariant($"{c.SourceX}, {c.SourceY}, {c.Width}, {c.Height}, "),
                    Invariant($"{c.Hue}, {Flag(c.PartialHue)});")));
                break;

            case TileAsGumpPicCommand c:
                body.Add(Concat(
                    Invariant($"GFTilePicAsGumpPic({name}, {c.X}, {c.Y}, {c.ItemId}, "),
                    Invariant($"{c.LinkId}, {c.ParamB}, {c.ParamC});")));
                break;

            default:
                throw new NotSupportedException(
                    $"No POL gump-package output for '{command.GetType().Name}'.");
        }
    }

    private static string BuildLayoutStrings(
        GumpLayout layout, string name, PolExportOptions options, DateTimeOffset? timestamp)
    {
        List<string> commands = [];

        if (!layout.Properties.Movable)
        {
            commands.Add("NoMove");
        }

        if (!layout.Properties.Closable)
        {
            commands.Add("NoClose");
        }

        if (!layout.Properties.Disposable)
        {
            commands.Add("NoDispose");
        }

        commands.AddRange(LayoutStringWriter.GumpLevelTokens(layout.Properties));
        commands.AddRange(LayoutStringWriter.Write(layout, Layout, LayoutStringWriter.ByIndex));

        List<string> texts = [];

        for (int i = 0; i < layout.Texts.Count; i++)
        {
            texts.Add(ArrayText(layout.Texts[i], i, options));
        }

        StringBuilder script = new();

        AppendHeader(script, timestamp, null, layout.Properties.TypeId);
        script.AppendLine("use uo;");
        script.AppendLine("use os;");
        script.AppendLine();
        script.AppendLine(CultureInfo.InvariantCulture, $"program gump_{name}(who)");
        script.AppendLine();

        AppendArray(script, "gump", commands);
        AppendArray(script, "data", texts);

        script.AppendLine();

        GumpPoint location = layout.Properties.Location;
        string trailing = location is { X: 0, Y: 0 }
            ? string.Empty
            : Invariant($", {location.X}, {location.Y}");

        script.AppendLine(CultureInfo.InvariantCulture, $"\tSendDialogGump(who, gump, data{trailing});");
        script.AppendLine();
        script.AppendLine("endprogram");

        return script.ToString();
    }

    /// <summary>
    /// Writes a command out as a layout string, with a note saying why it looks
    /// unlike its neighbours.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only for the commands whose id the package would overwrite:
    /// <c>GFAddButton</c>, <c>GFCheckBox</c> and <c>GFRadioButton</c> replace a
    /// value below one with the next free id, so a page-0 target or a zero
    /// response cannot go through the call at all. Everything else the package
    /// has a function for, and the exporter uses it.
    /// </para>
    /// <para>
    /// <c>XGFAddToLayout</c> is the package's own escape hatch, in preference to
    /// reaching into <c>gump.layout</c> from generated code. The line itself comes
    /// from the shared writer, so the grammar stays defined in one place, and text
    /// slots resolve to <c>0</c>: nothing reaching here carries text, and this
    /// dialect writes its strings inline with no data array for an index to point
    /// into.
    /// </para>
    /// </remarks>
    private static void RawLayout(
        List<string> body, string name, LayoutCommand command, string reason)
    {
        if (LayoutStringWriter.Format(command, Layout, LayoutStringWriter.AsZero) is { } line)
        {
            body.Add(Invariant($"//{reason}; written out as a layout string."));
            body.Add(Invariant($"XGFAddToLayout({name}, \"{Escape(line)}\");"));
        }
    }

    /// <summary>
    /// Notes an id the package is going to overwrite.
    /// </summary>
    /// <remarks>
    /// <c>GFTextEntry</c> replaces an id below one with the next free slot. Unlike
    /// a button or a checkbox the command cannot be written out directly instead,
    /// because it carries text and this dialect has no data array for a layout
    /// string to index into, so the export says so rather than looking exact.
    /// </remarks>
    private static void Reassigned(List<string> body, string function) =>
        body.Add(Invariant($"//{function} assigns an id of its own; this one was left at 0."));

    private static void AppendArray(StringBuilder script, string name, List<string> values)
    {
        script.AppendLine(CultureInfo.InvariantCulture, $"\tvar {name} := array {{");

        for (int i = 0; i < values.Count; i++)
        {
            script.Append(CultureInfo.InvariantCulture, $"\t\t\"{Escape(values[i])}\"");
            script.AppendLine(i == values.Count - 1 ? string.Empty : ",");
        }

        script.AppendLine("\t};");
    }

    /// <summary>
    /// Writes the header, naming the gump id the design was captured under when
    /// it has one.
    /// </summary>
    /// <remarks>
    /// <see cref="GumpProperties.TypeId"/> is read by the importer out of a
    /// capture tool's header and was then dropped by every converter. It is not a
    /// layout command and <c>SendDialogGump</c> takes no id, so a comment is the
    /// only place it can go: this documents which gump the script rebuilds rather
    /// than round-tripping, since a POL script is not itself importable.
    /// </remarks>
    private static void AppendHeader(
        StringBuilder script, DateTimeOffset? timestamp, string? suffix, int typeId)
    {
        string stamp = (timestamp ?? DateTimeOffset.Now).ToString("u", CultureInfo.InvariantCulture);

        script.AppendLine(CultureInfo.InvariantCulture, $"// Created {stamp}, with Gump Studio.");
        script.AppendLine(CultureInfo.InvariantCulture,
            $"// Exported with {PluginName} ver {PluginVersion}{(suffix is null ? string.Empty : " " + suffix)}");

        if (typeId != 0)
        {
            script.AppendLine(CultureInfo.InvariantCulture, $"// Gump 0x{typeId:X}");
        }

        script.AppendLine();
    }

    /// <summary>
    /// Writes an element's name and comment above its first command.
    /// </summary>
    /// <remarks>
    /// Once per element, not once per command: an element with a tooltip and an
    /// item property produces three commands but wants one comment, which is what
    /// the origin's primary flag marks.
    /// </remarks>
    private static void AppendComment(List<string> body, LayoutCommand command, PolExportOptions options)
    {
        if (!command.Origin.IsPrimary || (!options.IncludeComments && !options.IncludeNames))
        {
            return;
        }

        string comment = options.IncludeComments ? command.Origin.Comment : string.Empty;
        string label = options.IncludeNames
            ? command.Origin.Name + (comment.Length > 0 ? ": " : string.Empty)
            : string.Empty;

        string text = label + comment;

        if (text.Length == 0)
        {
            return;
        }

        body.Add(string.Empty);
        body.Add("//" + text);
    }

    /// <summary>
    /// One entry of the data array, with a placeholder where the text is empty.
    /// </summary>
    /// <remarks>
    /// The placeholder carries the slot index, so an author reading the generated
    /// script can tell which empty string is which.
    /// </remarks>
    private static string ArrayText(LayoutText text, int index, PolExportOptions options) =>
        text.Value.Length == 0 && options.PlaceholderText
            ? Invariant($"{ArrayPlaceholder(text.Role)} id.{index}")
            : text.Value;

    private static string ArrayPlaceholder(TextRole role) => role switch
    {
        TextRole.Entry => "TextEntry",
        TextRole.Html => "HtmlGump",
        _ => "Text",
    };

    /// <summary>
    /// The gump package's placeholder, which carries no index.
    /// </summary>
    /// <remarks>
    /// That dialect writes its strings inline, so there is no slot number for a
    /// reader to match a placeholder against.
    /// </remarks>
    private static string InlinePlaceholder(TextRole role) => role switch
    {
        TextRole.Entry => "TextEntry",
        TextRole.Html => "HtmlElement",
        _ => "TextLine",
    };

    private static string Inline(TextRef reference, GumpLayout layout, PolExportOptions options)
    {
        LayoutText text = layout.Texts[reference.Index];

        return text.Value.Length == 0 && options.PlaceholderText
            ? InlinePlaceholder(text.Role)
            : text.Value;
    }

    private static string GfTextLine(
        string name, TextCommand c, GumpLayout layout, PolExportOptions options) =>
        Invariant($"GFTextLine({name}, {c.X}, {c.Y}, {c.Hue}, \"{Escape(Inline(c.Text, layout, options))}\");");

    /// <summary>A cropped label, which owns its rectangle.</summary>
    /// <remarks>
    /// The previous version emitted <c>GFTextLine</c> and noted the crop
    /// rectangle as lost, which drew the label unclipped and at its full width.
    /// </remarks>
    private static string GfTextCrop(
        string name, CroppedTextCommand c, GumpLayout layout, PolExportOptions options) =>
        Concat(
            Invariant($"GFTextCrop({name}, {c.X}, {c.Y}, {c.Width}, {c.Height}, {c.Hue}, "),
            Invariant($"\"{Escape(Inline(c.Text, layout, options))}\");"));

    /// <summary>An entry field, with its character cap when it has one.</summary>
    /// <remarks>
    /// A non-zero cap in the trailing <c>lmt</c> parameter is what selects the
    /// package's <c>TextEntryLimited</c> form.
    /// </remarks>
    private static string GfTextEntry(
        string name, TextEntryCommand c, GumpLayout layout, PolExportOptions options) =>
        Concat(
            Invariant($"GFTextEntry({name}, {c.X}, {c.Y}, {c.Width}, {c.Height}, {c.Hue}, "),
            Invariant($"\"{Escape(Inline(c.Text, layout, options))}\", {c.EntryId}"),
            c.MaxLength > 0 ? Invariant($", {c.MaxLength});") : ");");

    private static string GfHtmlArea(
        string name, HtmlGumpCommand c, GumpLayout layout, PolExportOptions options)
    {
        string text = Escape(Inline(c.Text, layout, options));

        return c.Scrollbar || c.Background
            ? Concat(
                Invariant($"GFHTMLArea({name}, {c.X}, {c.Y}, {c.Width}, {c.Height}, \"{text}\", "),
                Invariant($"{Flag(c.Background)}, {Flag(c.Scrollbar)});"))
            : Invariant($"GFHTMLArea({name}, {c.X}, {c.Y}, {c.Width}, {c.Height}, \"{text}\");");
    }

    /// <summary>
    /// A localised HTML area, in whichever of its three forms applies.
    /// </summary>
    /// <remarks>
    /// The package picks the command from the arguments it is handed: a hue alone
    /// selects <c>XMFHTMLGumpColor</c>, and a custom string selects
    /// <c>XmfHtmlTok</c>, whose parameter order is genuinely different. So the
    /// colour and the arguments are forwarded and the branch is left to it, which
    /// is one fewer place for that ordering to be got wrong. The arguments go
    /// through raw: the package adds the <c>@...@</c> wrapper itself.
    /// </remarks>
    private static string GfHtmlLocalized(string name, XmfHtmlCommand c)
    {
        string head = Invariant(
            $"GFAddHTMLLocalized({name}, {c.X}, {c.Y}, {c.Width}, {c.Height}, {c.ClilocId}");

        if (c.Color != 0 || c.Arguments.Length > 0)
        {
            return Concat(
                head,
                Invariant($", {Flag(c.Background)}, {Flag(c.Scrollbar)}, {c.Color}, "),
                Invariant($"\"{Escape(c.Arguments)}\");"));
        }

        return c.Background || c.Scrollbar
            ? Concat(head, Invariant($", {Flag(c.Background)}, {Flag(c.Scrollbar)});"))
            : Concat(head, ");");
    }

    private static string ButtonType(ButtonCommand c) =>
        c.Kind == ButtonKind.Page ? "GF_PAGE_BTN" : "GF_CLOSE_BTN";

    /// <summary>
    /// Makes text safe inside a double-quoted POL string.
    /// </summary>
    /// <remarks>
    /// The original interpolated raw text, so a single quote character produced a
    /// script that would not compile.
    /// </remarks>
    private static string Escape(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

    private static string Flag(bool value) => value ? "1" : "0";

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Joins already-formatted fragments.
    /// </summary>
    /// <remarks>
    /// A multi-line interpolation joined with <c>+</c> collapses to a plain
    /// string before it reaches <see cref="Invariant(FormattableString)"/>, so
    /// long commands are built from invariant pieces and concatenated here.
    /// </remarks>
    private static string Concat(params string[] parts) => string.Concat(parts);
}
