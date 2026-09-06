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
/// this rewrite is meant to remove.
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

        foreach (string token in LayoutStringWriter.GumpLevelTokens(layout.Properties))
        {
            Unsupported(body, token);
        }

        foreach (LayoutCommand command in layout.Commands)
        {
            AppendComment(body, command, options);
            AppendGumpPackageCommand(body, name, command, layout, options);
        }

        StringBuilder script = new();

        AppendHeader(script, timestamp, "for gump pkg");
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
    /// The package has a <c>GF*</c> function for the original element set only.
    /// Where a command has no function at all, or where only a refinement is
    /// missing — a partial hue, a crop rectangle, a character cap, tile art on a
    /// button — the layout-string form is written out as a note beside the
    /// nearest call. Dropping the command instead would delete a visible element
    /// from the gump, which is a far worse answer than drawing it slightly wrong.
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
                // The gump package tracks the group as per-gump state and has no
                // call that closes one.
                break;

            case CheckerTransCommand c:
                body.Add(Invariant($"GFAddAlphaRegion({name}, {c.X}, {c.Y}, {c.Width}, {c.Height});"));
                break;

            case ResizePicCommand c:
                body.Add(Concat(
                    Invariant($"GFResizePic({name}, {c.X}, {c.Y}, {c.GumpId}, "),
                    Invariant($"{c.Width}, {c.Height});")));
                break;

            case GumpPicCommand c:
                body.Add(Invariant($"GFGumpPic({name}, {c.X}, {c.Y}, {c.GumpId}, {c.Hue});"));

                // GFGumpPic always applies a full tint.
                if (c is { PartialHue: true, Hue: not 0 })
                {
                    Unsupported(body, command);
                }

                break;

            case TilePicCommand c:
                body.Add(Invariant($"GFTilePic({name}, {c.X}, {c.Y}, {c.ItemId}, {c.Hue});"));
                break;

            case TextCommand c:
                body.Add(GfTextLine(name, c, layout, options));
                break;

            case CroppedTextCommand c:
                body.Add(GfTextLine(name, c, layout, options));

                // GFTextLine has no crop rectangle.
                Unsupported(body, command);
                break;

            case TextEntryCommand c:
                body.Add(GfTextEntry(name, c, layout, options));

                if (c.MaxLength > 0)
                {
                    Unsupported(body, command);
                }

                break;

            case HtmlGumpCommand c:
                body.Add(GfHtmlArea(name, c, layout, options));
                break;

            case XmfHtmlCommand c:
                body.Add(GfHtmlLocalized(name, c));

                // GFAddHTMLLocalized takes neither a colour nor cliloc arguments.
                if (c.Color != 0 || c.Arguments.Length > 0)
                {
                    Unsupported(body, command);
                }

                break;

            case ButtonCommand c:
                body.Add(Concat(
                    Invariant($"GFAddButton({name}, {c.X}, {c.Y}, {c.NormalId}, {c.PressedId}, "),
                    Invariant($"{(c.Kind == ButtonKind.Page ? "GF_PAGE_BTN" : "GF_CLOSE_BTN")}, "),
                    Invariant($"{c.Param});")));

                if (c.Tile is not null)
                {
                    Unsupported(body, command);
                }

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

            // No package call at all for these.
            case GumpPicTiledCommand:
            case PicInPicCommand:
            case TileAsGumpPicCommand:
            case TooltipCommand:
            case ItemPropertyCommand:
                Unsupported(body, command);
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

        AppendHeader(script, timestamp, null);
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
    /// Records a command the gump package has no function for.
    /// </summary>
    /// <remarks>
    /// The note is the client's own layout string, formatted by the shared
    /// writer. Text slots resolve to <c>0</c> rather than to their real index,
    /// because this output has no data array for an index to point into.
    /// </remarks>
    private static void Unsupported(List<string> body, LayoutCommand command)
    {
        if (LayoutStringWriter.Format(command, Layout, LayoutStringWriter.AsZero) is { } line)
        {
            Unsupported(body, line);
        }
    }

    private static void Unsupported(List<string> body, string layoutCommand)
    {
        string command = layoutCommand.Split(' ', 2)[0];

        body.Add(string.Empty);
        body.Add(Invariant($"//Gump package does not support {command}"));
        body.Add("//" + layoutCommand);
        body.Add(string.Empty);
    }

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

    private static void AppendHeader(StringBuilder script, DateTimeOffset? timestamp, string? suffix)
    {
        string stamp = (timestamp ?? DateTimeOffset.Now).ToString("u", CultureInfo.InvariantCulture);

        script.AppendLine(CultureInfo.InvariantCulture, $"// Created {stamp}, with Gump Studio.");
        script.AppendLine(CultureInfo.InvariantCulture,
            $"// Exported with {PluginName} ver {PluginVersion}{(suffix is null ? string.Empty : " " + suffix)}");
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

    private static string GfTextLine(
        string name, CroppedTextCommand c, GumpLayout layout, PolExportOptions options) =>
        Invariant($"GFTextLine({name}, {c.X}, {c.Y}, {c.Hue}, \"{Escape(Inline(c.Text, layout, options))}\");");

    private static string GfTextEntry(
        string name, TextEntryCommand c, GumpLayout layout, PolExportOptions options) =>
        Concat(
            Invariant($"GFTextEntry({name}, {c.X}, {c.Y}, {c.Width}, {c.Height}, {c.Hue}, "),
            Invariant($"\"{Escape(Inline(c.Text, layout, options))}\", {c.EntryId});"));

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

    private static string GfHtmlLocalized(string name, XmfHtmlCommand c) =>
        c.Scrollbar || c.Background
            ? Concat(
                Invariant($"GFAddHTMLLocalized({name}, {c.X}, {c.Y}, {c.Width}, {c.Height}, {c.ClilocId}, "),
                Invariant($"{Flag(c.Background)}, {Flag(c.Scrollbar)});"))
            : Invariant(
                $"GFAddHTMLLocalized({name}, {c.X}, {c.Y}, {c.Width}, {c.Height}, {c.ClilocId});");

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
