using System.Globalization;
using System.Text;

using GumpStudio.Core.Document;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Layout;

namespace GumpStudio.Converters;

/// <summary>How response ids appear in the generated source.</summary>
public enum RunUoButtonIdStyle
{
    /// <summary>A generated <c>Buttons</c> enum, named after the elements.</summary>
    Named,

    /// <summary>Plain integers.</summary>
    Numeric,
}

/// <summary>
/// Settings specific to the RunUO exporter.
/// </summary>
/// <remarks>
/// A record class rather than a record struct, for the same reason as every
/// other options type here: defaulted members on a struct are silently skipped
/// by <c>default</c> and <c>new()</c>.
/// </remarks>
public sealed record RunUoExportOptions
{
    /// <summary>Whether response ids are named or numeric.</summary>
    public RunUoButtonIdStyle ButtonIdStyle { get; init; } = RunUoButtonIdStyle.Named;

    /// <summary>Namespace for the generated class.</summary>
    public string Namespace { get; init; } = "Server.Gumps";

    /// <summary>Name of the generated class.</summary>
    public string ClassName { get; init; } = "MyGump";

    /// <summary>
    /// Also generate an in-game command that opens the gump.
    /// </summary>
    /// <remarks>
    /// The command name is the class name. The original asked for it separately
    /// and then produced a class that would not compile — see the remarks on
    /// <see cref="RunUoScriptBuilder"/>.
    /// </remarks>
    public bool RegisterCommand { get; init; } = true;

    /// <summary>Emit element names and comments into the output.</summary>
    public bool IncludeComments { get; init; } = true;
}

/// <summary>
/// Turns a document into a RunUO-style C# <c>Gump</c> subclass.
/// </summary>
/// <remarks>
/// <para>
/// Ported from the 1.8 RunUO exporter, which was itself based on Daegon's
/// original. The generated shape is the same — a <c>Gump</c> subclass with an
/// optional command registration, an <c>OnResponse</c> switch and text relays —
/// but the following were wrong in the original and are fixed here.
/// </para>
/// <list type="bullet">
/// <item>
/// <b>The command form did not compile.</b> Its static command handler assigned
/// to an instance field (<c>Mobile caller</c>), which is a compile error in C#.
/// The handler here uses a local.
/// </item>
/// <item>
/// <b>Text was escaped for the wrong kind of string literal.</b> It emitted
/// verbatim literals (<c>@"…"</c>) but escaped quotes as <c>\"</c>, which is a
/// syntax error in one. Quotes are doubled here, as a verbatim literal requires.
/// </item>
/// <item>
/// <b>Checkboxes and radios were mixed into the button enum.</b> Their switch ids
/// went into the same <c>Buttons</c> enum as button ids, and got <c>case</c>
/// labels in the <c>info.ButtonID</c> switch — where a checkbox never appears,
/// and where its id could collide with a real button's. Switch ids and button
/// ids are separate namespaces and are kept apart.
/// </item>
/// <item>
/// <b>The named style discarded the author's response ids.</b> Enum members had
/// no values, so a button's <c>Param</c> was replaced by the member's ordinal.
/// Members now carry their <c>Param</c> explicitly.
/// </item>
/// <item>
/// Coordinates come from <see cref="Element.GetAbsolutePosition"/>, so elements
/// nested in a group no longer export at the wrong place, and all numbers format
/// invariantly.
/// </item>
/// </list>
/// <para>
/// Elements the client only gained after 1.8 map onto the calls modern cores
/// provide: <c>AddImageTiledButton</c>, <c>AddTooltip</c>, <c>AddItemProperty</c>,
/// <c>AddLabelCropped</c>, <c>AddPicInPic</c>, <c>AddMasterGump</c> and
/// <c>AddECHandleInput</c>. The last four need a ServUO-era core; RunUO 2.x does
/// not define them.
/// </para>
/// </remarks>
public static class RunUoScriptBuilder
{
    private const string Indent = "    ";

    /// <summary>Builds the C# source for a document.</summary>
    /// <param name="document">The gump to export.</param>
    /// <param name="options">Naming and style settings.</param>
    /// <param name="timestamp">
    /// Stamped into the header comment. Supply a fixed value for reproducible
    /// output.
    /// </param>
    public static string Build(
        GumpDocument document,
        RunUoExportOptions? options = null,
        DateTimeOffset? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        return Build(GumpLayoutBuilder.Build(document), options, timestamp);
    }

    /// <summary>Builds the C# source for a layout that has already been produced.</summary>
    /// <param name="layout">The gump to export.</param>
    /// <param name="options">Naming and style settings.</param>
    /// <param name="timestamp">Header timestamp; injectable for reproducible output.</param>
    public static string Build(
        GumpLayout layout,
        RunUoExportOptions? options = null,
        DateTimeOffset? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(layout);

        options ??= new RunUoExportOptions();

        string className = Identifier(options.ClassName, "MyGump");
        ResponseNames names = ResponseNames.Collect(layout, options);

        StringBuilder script = new();

        AppendHeader(script, timestamp);

        script.AppendLine(CultureInfo.InvariantCulture, $"namespace {Namespace(options.Namespace)}");
        script.AppendLine("{");
        script.AppendLine(CultureInfo.InvariantCulture, $"{Indent}public class {className} : Gump");
        script.AppendLine(Indent + "{");

        AppendButtonEnum(script, names);
        AppendCommand(script, options, className);
        AppendConstructor(script, layout, options, className, names);
        AppendResponse(script, layout, names);

        script.AppendLine(Indent + "}");
        script.AppendLine("}");

        return script.ToString();
    }

    private static void AppendHeader(StringBuilder script, DateTimeOffset? timestamp)
    {
        string stamp = (timestamp ?? DateTimeOffset.Now).ToString("u", CultureInfo.InvariantCulture);

        script.AppendLine(CultureInfo.InvariantCulture, $"// Created {stamp}, with Gump Studio.");
        script.AppendLine("// Exported with the RunUO exporter, after the original by Daegon and roadmaster.");
        script.AppendLine();
        script.AppendLine("using Server;");
        script.AppendLine("using Server.Commands;");
        script.AppendLine("using Server.Gumps;");
        script.AppendLine("using Server.Network;");
        script.AppendLine();
    }

    private static void AppendButtonEnum(StringBuilder script, ResponseNames names)
    {
        if (names.Buttons.Count == 0)
        {
            return;
        }

        script.AppendLine(Indent + Indent + "public enum Buttons");
        script.AppendLine(Indent + Indent + "{");

        foreach ((string name, int value) in names.Buttons)
        {
            script.AppendLine(CultureInfo.InvariantCulture, $"{Indent}{Indent}{Indent}{name} = {value},");
        }

        script.AppendLine(Indent + Indent + "}");
        script.AppendLine();
    }

    private static void AppendCommand(StringBuilder script, RunUoExportOptions options, string className)
    {
        if (!options.RegisterCommand)
        {
            return;
        }

        string body = Indent + Indent;

        script.AppendLine(body + "public static void Initialize()");
        script.AppendLine(body + "{");
        script.AppendLine(CultureInfo.InvariantCulture,
            $"{body}{Indent}CommandSystem.Register(\"{className}\", AccessLevel.Administrator, OnCommand);");
        script.AppendLine(body + "}");
        script.AppendLine();

        script.AppendLine(CultureInfo.InvariantCulture, $"{body}[Usage(\"{className}\")]");
        script.AppendLine(CultureInfo.InvariantCulture, $"{body}[Description(\"Opens the {className} gump.\")]");
        script.AppendLine(body + "public static void OnCommand(CommandEventArgs e)");
        script.AppendLine(body + "{");

        // A local, not a field. The original assigned e.Mobile to an instance
        // field from this static method, which does not compile.
        script.AppendLine(body + Indent + "Mobile from = e.Mobile;");
        script.AppendLine();
        script.AppendLine(CultureInfo.InvariantCulture, $"{body}{Indent}from.CloseGump(typeof({className}));");
        script.AppendLine(CultureInfo.InvariantCulture, $"{body}{Indent}from.SendGump(new {className}());");
        script.AppendLine(body + "}");
        script.AppendLine();
    }

    private static void AppendConstructor(
        StringBuilder script,
        GumpLayout layout,
        RunUoExportOptions options,
        string className,
        ResponseNames names)
    {
        GumpProperties properties = layout.Properties;
        string body = Indent + Indent;
        string line = body + Indent;

        script.AppendLine(CultureInfo.InvariantCulture,
            $"{body}public {className}() : base({properties.Location.X}, {properties.Location.Y})");
        script.AppendLine(body + "{");

        script.AppendLine(CultureInfo.InvariantCulture, $"{line}Closable = {Bool(properties.Closable)};");
        script.AppendLine(CultureInfo.InvariantCulture, $"{line}Disposable = {Bool(properties.Disposable)};");
        script.AppendLine(CultureInfo.InvariantCulture, $"{line}Dragable = {Bool(properties.Movable)};");

        if (properties.MasterGumpId != 0)
        {
            script.AppendLine(CultureInfo.InvariantCulture,
                $"{line}AddMasterGump({properties.MasterGumpId});");
        }

        if (properties.EnhancedClientInput)
        {
            script.AppendLine(line + "AddECHandleInput();");
        }

        foreach (LayoutCommand command in layout.Commands)
        {
            if (command is PageCommand page)
            {
                script.AppendLine();
                script.AppendLine(CultureInfo.InvariantCulture, $"{line}AddPage({page.Page});");

                continue;
            }

            AppendComment(script, line, command, options);

            foreach (string call in Calls(command, layout, options, names))
            {
                script.AppendLine(line + call);
            }
        }

        script.AppendLine(body + "}");
        script.AppendLine();
    }

    /// <summary>
    /// Writes an element's name and comment above its first call.
    /// </summary>
    /// <remarks>
    /// Once per element, not once per command: an element with a tooltip and an
    /// item property produces three commands but wants one comment, which is what
    /// the origin's primary flag marks.
    /// </remarks>
    private static void AppendComment(
        StringBuilder script, string indent, LayoutCommand command, RunUoExportOptions options)
    {
        if (!command.Origin.IsPrimary || !options.IncludeComments)
        {
            return;
        }

        string comment = command.Origin.Comment;
        string text = command.Origin.Name + (comment.Length > 0 ? ": " + comment : string.Empty);

        if (text.Length == 0)
        {
            return;
        }

        script.AppendLine(CultureInfo.InvariantCulture, $"{indent}// {Sanitise(text)}");
    }

    /// <summary>
    /// The calls one command turns into.
    /// </summary>
    /// <remarks>
    /// Radio grouping produces nothing: no core exposes the client's
    /// <c>group</c>, and the exporter has never emitted anything for it. That is
    /// preserved rather than quietly corrected, because inventing an
    /// <c>AddGroup</c> call would change every generated script.
    /// </remarks>
    private static IEnumerable<string> Calls(
        LayoutCommand command, GumpLayout layout, RunUoExportOptions options, ResponseNames names)
    {
        switch (command)
        {
            case CheckerTransCommand c:
                yield return Invariant($"AddAlphaRegion({c.X}, {c.Y}, {c.Width}, {c.Height});");
                break;

            case ResizePicCommand c:
                yield return Invariant(
                    $"AddBackground({c.X}, {c.Y}, {c.Width}, {c.Height}, {c.GumpId});");
                break;

            case GumpPicTiledCommand c:
                yield return Invariant(
                    $"AddImageTiled({c.X}, {c.Y}, {c.Width}, {c.Height}, {c.GumpId});");
                break;

            case GumpPicCommand c:
                yield return c.Hue != 0
                    ? Invariant($"AddImage({c.X}, {c.Y}, {c.GumpId}, {c.Hue});")
                    : Invariant($"AddImage({c.X}, {c.Y}, {c.GumpId});");
                break;

            case PicInPicCommand c:
                yield return Invariant(
                    $"AddPicInPic({c.X}, {c.Y}, {c.GumpId}, {c.Width}, {c.Height}, {c.SourceX}, {c.SourceY});");
                break;

            case TilePicCommand c:
                yield return c.Hue != 0
                    ? Invariant($"AddItem({c.X}, {c.Y}, {c.ItemId}, {c.Hue});")
                    : Invariant($"AddItem({c.X}, {c.Y}, {c.ItemId});");
                break;

            case TileAsGumpPicCommand c:
                // No core exposes tilepicasgumppic; say so rather than emit a call
                // that will not compile.
                yield return Invariant(
                    $"// No RunUO call for tilepicasgumppic: item {c.ItemId} at {c.X}, {c.Y}.");
                break;

            case TextCommand c:
                yield return Invariant(
                    $"AddLabel({c.X}, {c.Y}, {c.Hue}, {Literal(Text(c.Text, layout))});");
                break;

            case CroppedTextCommand c:
                yield return Invariant(
                    $"AddLabelCropped({c.X}, {c.Y}, {c.Width}, {c.Height}, {c.Hue}, {Literal(Text(c.Text, layout))});");
                break;

            case TextEntryCommand c:
                yield return c.MaxLength > 0
                    ? Invariant($"AddTextEntry({c.X}, {c.Y}, {c.Width}, {c.Height}, {c.Hue}, {c.EntryId}, {Literal(Text(c.Text, layout))}, {c.MaxLength});")
                    : Invariant($"AddTextEntry({c.X}, {c.Y}, {c.Width}, {c.Height}, {c.Hue}, {c.EntryId}, {Literal(Text(c.Text, layout))});");
                break;

            case HtmlGumpCommand c:
                yield return Concat(
                    Invariant($"AddHtml({c.X}, {c.Y}, {c.Width}, {c.Height}, {Literal(Text(c.Text, layout))}, "),
                    Invariant($"{Bool(c.Background)}, {Bool(c.Scrollbar)});"));
                break;

            case XmfHtmlCommand c:
                yield return HtmlCall(c);
                break;

            case ButtonCommand c:
                yield return ButtonCall(c, options, names);
                break;

            case RadioCommand c:
                yield return Invariant(
                    $"AddRadio({c.X}, {c.Y}, {c.UncheckedId}, {c.CheckedId}, {Bool(c.IsChecked)}, {c.Value});");
                break;

            case CheckboxCommand c:
                yield return Invariant(
                    $"AddCheck({c.X}, {c.Y}, {c.UncheckedId}, {c.CheckedId}, {Bool(c.IsChecked)}, {c.Group});");
                break;

            // Both attach to the element the core created last, so they follow it.
            case TooltipCommand c:
                yield return c.Arguments.Length > 0
                    ? Invariant($"AddTooltip({c.ClilocId}, {Literal(c.Arguments)});")
                    : Invariant($"AddTooltip({c.ClilocId});");
                break;

            case ItemPropertyCommand c:
                yield return Invariant($"AddItemProperty({c.Serial});");
                break;

            case GroupCommand:
            case EndGroupCommand:
                break;

            default:
                throw new NotSupportedException(
                    $"No RunUO output for '{command.GetType().Name}'.");
        }
    }

    /// <summary>The raw text a slot holds. RunUO substitutes no placeholder.</summary>
    private static string Text(TextRef reference, GumpLayout layout) =>
        layout.Texts[reference.Index].Value;

    private static string HtmlCall(XmfHtmlCommand html)
    {
        string rect = Invariant($"{html.X}, {html.Y}, {html.Width}, {html.Height}");
        string flags = Invariant($"{Bool(html.Background)}, {Bool(html.Scrollbar)}");

        if (html.Arguments.Length > 0)
        {
            return Concat(
                $"AddHtmlLocalized({rect}, ",
                Invariant($"{html.ClilocId}, {Literal(html.Arguments)}, {html.Color}, {flags});"));
        }

        return html.Color != 0
            ? Concat($"AddHtmlLocalized({rect}, ", Invariant($"{html.ClilocId}, {html.Color}, {flags});"))
            : Concat($"AddHtmlLocalized({rect}, ", Invariant($"{html.ClilocId}, {flags});"));
    }

    private static string ButtonCall(
        ButtonCommand button, RunUoExportOptions options, ResponseNames names)
    {
        bool isPage = button.Kind == ButtonKind.Page;

        // buttonID is what comes back in OnResponse; param carries the target page
        // for a page button. A page button never reports, so its id stays zero.
        string id = isPage
            ? "0"
            : options.ButtonIdStyle == RunUoButtonIdStyle.Named && names.NameOf(button) is { } named
                ? "(int)Buttons." + named
                : Invariant($"{button.Param}");

        string type = isPage ? "GumpButtonType.Page" : "GumpButtonType.Reply";
        int param = isPage ? button.Param : 0;

        return button.Tile is { } tile
            ? Concat(
                Invariant($"AddImageTiledButton({button.X}, {button.Y}, {button.NormalId}, {button.PressedId}, "),
                Invariant($"{id}, {type}, {param}, {tile.ItemId}, {tile.Hue}, "),
                Invariant($"{tile.X}, {tile.Y});"))
            : Concat(
                Invariant($"AddButton({button.X}, {button.Y}, {button.NormalId}, {button.PressedId}, "),
                Invariant($"{id}, {type}, {param});"));
    }

    private static void AppendResponse(StringBuilder script, GumpLayout layout, ResponseNames names)
    {
        string body = Indent + Indent;
        string line = body + Indent;

        script.AppendLine(body + "public override void OnResponse(NetState sender, RelayInfo info)");
        script.AppendLine(body + "{");
        script.AppendLine(line + "Mobile from = sender.Mobile;");

        List<TextEntryCommand> entries = [.. layout.Commands.OfType<TextEntryCommand>()];

        if (entries.Count > 0)
        {
            script.AppendLine();

            foreach (TextEntryCommand entry in entries)
            {
                script.AppendLine(CultureInfo.InvariantCulture,
                    $"{line}TextRelay entry{entry.EntryId} = info.GetTextEntry({entry.EntryId});");
                script.AppendLine(CultureInfo.InvariantCulture,
                    $"{line}string text{entry.EntryId} = entry{entry.EntryId} == null ? \"\" : entry{entry.EntryId}.Text.Trim();");
            }
        }

        script.AppendLine();
        script.AppendLine(line + "switch (info.ButtonID)");
        script.AppendLine(line + "{");

        foreach (string label in names.CaseLabels())
        {
            script.AppendLine(CultureInfo.InvariantCulture, $"{line}{Indent}case {label}:");
            script.AppendLine(line + Indent + Indent + "break;");
            script.AppendLine();
        }

        script.AppendLine(line + Indent + "default:");
        script.AppendLine(line + Indent + Indent + "break;");
        script.AppendLine(line + "}");
        script.AppendLine(body + "}");
    }

    /// <summary>
    /// The response ids the gump can report, and the enum names for them.
    /// </summary>
    /// <remarks>
    /// Only reply buttons: a page button switches pages client-side and never
    /// reaches <c>OnResponse</c>, and a checkbox or radio reports through
    /// <c>info.Switches</c>, not <c>info.ButtonID</c>. The original mixed all four
    /// into one enum and gave every one of them a <c>case</c> label in the
    /// button-id switch.
    /// </remarks>
    private sealed class ResponseNames
    {
        private readonly Dictionary<int, string> _names = [];
        private readonly List<(string Name, int Value)> _buttons = [];
        private readonly RunUoButtonIdStyle _style;

        private ResponseNames(RunUoButtonIdStyle style) => _style = style;

        /// <summary>
        /// The enum members to declare, empty unless the named style is in use.
        /// </summary>
        public List<(string Name, int Value)> Buttons =>
            _style == RunUoButtonIdStyle.Named ? _buttons : [];

        public static ResponseNames Collect(GumpLayout layout, RunUoExportOptions options)
        {
            ResponseNames names = new(options.ButtonIdStyle);
            HashSet<string> used = new(StringComparer.Ordinal);

            foreach (ButtonCommand button in layout.Commands
                .OfType<ButtonCommand>()
                .Where(b => b.Kind == ButtonKind.Reply))
            {
                string name = Unique(Identifier(button.Origin.Name, "Button"), used);

                names._names[button.Origin.Ordinal] = name;

                // Explicit values, so the author's response id survives. Without
                // them the member's ordinal silently replaced it.
                names._buttons.Add((name, button.Param));
            }

            return names;
        }

        /// <summary>
        /// The enum member generated for one button.
        /// </summary>
        /// <remarks>
        /// Keyed on the command's ordinal rather than on the command itself.
        /// Commands are records, so two reply buttons with the same name, response
        /// id and position would compare equal, collapse into one entry, and leave
        /// the switch naming a member that nothing emits.
        /// </remarks>
        public string? NameOf(ButtonCommand button) =>
            _names.TryGetValue(button.Origin.Ordinal, out string? name) ? name : null;

        /// <summary>
        /// One case label per distinct response id.
        /// </summary>
        /// <remarks>
        /// Two buttons may legitimately share an id, and two enum members may
        /// legitimately alias one value — but two <c>case</c> labels for the same
        /// value do not compile.
        /// </remarks>
        public IEnumerable<string> CaseLabels()
        {
            HashSet<int> seen = [];

            foreach ((string name, int value) in _buttons)
            {
                if (!seen.Add(value))
                {
                    continue;
                }

                yield return _style == RunUoButtonIdStyle.Named
                    ? "(int)Buttons." + name
                    : value.ToString(CultureInfo.InvariantCulture);
            }
        }
    }

    private static string Unique(string name, HashSet<string> used)
    {
        if (used.Add(name))
        {
            return name;
        }

        for (int suffix = 2; ; suffix++)
        {
            string candidate = name + suffix.ToString(CultureInfo.InvariantCulture);

            if (used.Add(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// Reduces a user-supplied name to something C# will accept.
    /// </summary>
    /// <remarks>
    /// The original only stripped spaces, so an element named "OK?" or "2nd page"
    /// produced source that would not compile.
    /// </remarks>
    private static string Identifier(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        StringBuilder built = new(value.Length);

        foreach (char c in value)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                built.Append(c);
            }
        }

        if (built.Length == 0)
        {
            return fallback;
        }

        if (!char.IsLetter(built[0]) && built[0] != '_')
        {
            built.Insert(0, '_');
        }

        return built.ToString();
    }

    /// <summary>Reduces a namespace to dotted identifiers.</summary>
    private static string Namespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Server.Gumps";
        }

        IEnumerable<string> parts = value
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => Identifier(part, "Gumps"));

        return string.Join('.', parts);
    }

    /// <summary>
    /// Wraps text as a C# verbatim string literal.
    /// </summary>
    /// <remarks>
    /// The original emitted a verbatim literal but escaped quotes as
    /// <c>\"</c> — a syntax error inside one. In a verbatim literal the quote is
    /// escaped by doubling it.
    /// </remarks>
    private static string Literal(string value) =>
        "@\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    /// <summary>Keeps a comment on one line.</summary>
    private static string Sanitise(string value) =>
        value
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

    private static string Bool(bool value) => value ? "true" : "false";

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Joins already-formatted fragments.
    /// </summary>
    /// <remarks>
    /// A multi-line interpolation joined with <c>+</c> collapses to a plain string
    /// before it reaches <see cref="Invariant(FormattableString)"/>, so long calls
    /// are built from invariant pieces and concatenated here.
    /// </remarks>
    private static string Concat(params string[] parts) => string.Concat(parts);
}
