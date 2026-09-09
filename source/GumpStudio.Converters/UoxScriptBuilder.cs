using System.Globalization;
using System.Text;

using GumpStudio.Core.Document;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Layout;
using GumpStudio.Core.Primitives;

namespace GumpStudio.Converters;

/// <summary>
/// Settings specific to the UOX3 exporter.
/// </summary>
/// <remarks>
/// A record class rather than a record struct, for the same reason as every
/// other options type here: defaulted members on a struct are silently skipped
/// by <c>default</c> and <c>new()</c>.
/// </remarks>
public sealed record UoxExportOptions
{
    /// <summary>Name of the generated function, and of the gump variable.</summary>
    public string FunctionName { get; init; } = "MyGump";

    /// <summary>Emit element names and comments into the output.</summary>
    public bool IncludeComments { get; init; } = true;

    /// <summary>Also generate the <c>onGumpPress</c> handler.</summary>
    public bool IncludeHandlers { get; init; } = true;
}

/// <summary>
/// Turns a document into a UOX3 JavaScript gump script.
/// </summary>
/// <remarks>
/// <para>
/// UOX3 has the most complete gump API of any core this ships a converter for.
/// It is the only one that exposes <c>endgroup</c>, and it covers <c>picinpic</c>,
/// <c>buttontileart</c>, <c>croppedtext</c>, <c>textentrylimited</c>,
/// <c>itemproperty</c>, <c>tooltip</c> and all three <c>xmfhtml</c> forms. The
/// method names are read off <c>CGump_Methods</c> in <c>UOXJSMethods.h</c> and
/// the parameter order out of each <c>CGump_Add*</c> body, since several differ
/// from the order the client's own command takes.
/// </para>
/// <para>
/// The one that most invites a mistake is <c>AddCroppedText</c>, whose hue comes
/// <em>third</em> — before the width and height — where the layout command puts
/// it last. <c>AddPicInPic</c> is the other: it takes the source offset before
/// the size, matching the client, where the RunUO-family cores transpose them.
/// </para>
/// <para>
/// Four things a UOX3 gump cannot express, which the output says rather than
/// silently dropping:
/// </para>
/// <list type="bullet">
/// <item>
/// <b>A screen position.</b> <c>new Gump()</c> takes no coordinates and
/// <c>Send()</c> takes only a socket, so <see cref="GumpProperties.Location"/>
/// has nowhere to go.
/// </item>
/// <item>
/// <b><c>mastergump</c>.</b> <c>CGump_MasterGump</c> formats five values from one
/// argument, so the command it appends is garbage. Emitting the call would be
/// worse than not.
/// </item>
/// <item>
/// <b>A tooltip's arguments.</b> <c>CGump_AddToolTip</c> starts its argument loop
/// at index two rather than one, so the first argument after the cliloc is
/// skipped and a single-argument call emits an empty <c>@@</c>. Passing a
/// placeholder first would work today and break when that is fixed, so the
/// cliloc goes out alone.
/// </item>
/// <item>
/// <b>A partial hue, <c>tilepicasgumppic</c>, and the parser toggles.</b> No call
/// exists for any of them.
/// </item>
/// </list>
/// <para>
/// Text slots need care. UOX3 assigns the index itself for <c>AddText</c>,
/// <c>AddCroppedText</c> and <c>AddHTMLGump</c>, from a counter it advances as it
/// goes — but <c>AddTextEntry</c> pushes a string onto the same list
/// <em>without</em> advancing that counter. So an entry silently shifts every
/// later index by one. The entry's own index is passed explicitly and is
/// therefore right; where a later text element would be wrong, the output says
/// so beside it.
/// </para>
/// </remarks>
public static class UoxScriptBuilder
{
    /// <summary>UOX3's own scripts indent with tabs, so this does too.</summary>
    private const string Indent = "\t";

    /// <summary>Builds the script for a document.</summary>
    /// <param name="document">The gump to export.</param>
    /// <param name="options">Naming and style settings.</param>
    /// <param name="timestamp">Header timestamp; injectable for reproducible output.</param>
    public static string Build(
        GumpDocument document,
        UoxExportOptions? options = null,
        DateTimeOffset? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        return Build(GumpLayoutBuilder.Build(document), options, timestamp);
    }

    /// <summary>Builds the script for a layout that has already been produced.</summary>
    /// <param name="layout">The gump to export.</param>
    /// <param name="options">Naming and style settings.</param>
    /// <param name="timestamp">Header timestamp; injectable for reproducible output.</param>
    public static string Build(
        GumpLayout layout,
        UoxExportOptions? options = null,
        DateTimeOffset? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(layout);

        options ??= new UoxExportOptions();

        string name = Identifier(options.FunctionName, "MyGump");
        string gump = char.ToLowerInvariant(name[0]) + name[1..];

        StringBuilder script = new();

        AppendHeader(script, layout, timestamp);
        AppendBuilder(script, layout, options, name, gump);

        if (options.IncludeHandlers)
        {
            AppendHandler(script, layout, options);
        }

        return script.ToString();
    }

    private static void AppendHeader(
        StringBuilder script, GumpLayout layout, DateTimeOffset? timestamp)
    {
        string stamp = (timestamp ?? DateTimeOffset.Now).ToString("u", CultureInfo.InvariantCulture);

        script.AppendLine(CultureInfo.InvariantCulture, $"// Created {stamp}, with Gump Studio.");
        script.AppendLine("// Exported with the UOX3 exporter.");

        GumpPoint location = layout.Properties.Location;

        if (location is not { X: 0, Y: 0 })
        {
            script.AppendLine();
            script.AppendLine(CultureInfo.InvariantCulture,
                $"// Opens at {location.X},{location.Y} in the editor. UOX3 gumps carry no screen");
            script.AppendLine("// position: new Gump() takes none and Send() takes only a socket.");
        }

        script.AppendLine();
    }

    private static void AppendBuilder(
        StringBuilder script, GumpLayout layout, UoxExportOptions options, string name, string gump)
    {
        script.AppendLine(CultureInfo.InvariantCulture, $"function Display{name}( pUser )");
        script.AppendLine("{");
        script.AppendLine($"{Indent}var pSock = pUser.socket;");
        script.AppendLine($"{Indent}if( pSock == null )");
        script.AppendLine($"{Indent}{Indent}return;");
        script.AppendLine();
        script.AppendLine(CultureInfo.InvariantCulture, $"{Indent}var {gump} = new Gump();");
        script.AppendLine();

        AppendGumpLevel(script, layout.Properties, gump);

        TextSlots slots = new();

        foreach (LayoutCommand command in layout.Commands)
        {
            AppendComment(script, command, options);

            foreach (string line in Lines(command, layout, gump, slots))
            {
                script.AppendLine(line.Length == 0 ? string.Empty : Indent + line);
            }
        }

        script.AppendLine();
        script.AppendLine(CultureInfo.InvariantCulture, $"{Indent}{gump}.Send( pSock );");
        script.AppendLine(CultureInfo.InvariantCulture, $"{Indent}{gump}.Free();");
        script.AppendLine("}");
    }

    private static void AppendGumpLevel(
        StringBuilder script, GumpProperties properties, string gump)
    {
        bool any = false;

        if (!properties.Movable)
        {
            script.AppendLine(CultureInfo.InvariantCulture, $"{Indent}{gump}.NoMove();");
            any = true;
        }

        if (!properties.Closable)
        {
            script.AppendLine(CultureInfo.InvariantCulture, $"{Indent}{gump}.NoClose();");
            any = true;
        }

        if (!properties.Disposable)
        {
            script.AppendLine(CultureInfo.InvariantCulture, $"{Indent}{gump}.NoDispose();");
            any = true;
        }

        // CGump_MasterGump formats five values from one argument, so the command
        // it appends is garbage rather than a mastergump the client can read.
        if (properties.MasterGumpId != 0)
        {
            script.AppendLine(CultureInfo.InvariantCulture,
                $"{Indent}// No usable call for mastergump {properties.MasterGumpId}: UOX3's MasterGump() formats five values from one argument.");
            any = true;
        }

        foreach (string toggle in Toggles(properties))
        {
            script.AppendLine(CultureInfo.InvariantCulture, $"{Indent}// No UOX3 call for {toggle}.");
            any = true;
        }

        if (any)
        {
            script.AppendLine();
        }
    }

    private static IEnumerable<string> Toggles(GumpProperties properties)
    {
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

    /// <summary>
    /// Emits one command, as one call or as a note where UOX3 has none.
    /// </summary>
    private static IEnumerable<string> Lines(
        LayoutCommand command, GumpLayout layout, string gump, TextSlots slots)
    {
        switch (command)
        {
            case PageCommand c:
                if (c.Page > 0)
                {
                    yield return string.Empty;
                }

                yield return Invariant($"{gump}.AddPage( {c.Page} );");
                break;

            case GroupCommand c:
                yield return Invariant($"{gump}.AddGroup( {c.Group} );");
                break;

            // The only core that has this. A group left open does not work on
            // pages above the first.
            case EndGroupCommand:
                yield return Invariant($"{gump}.EndGroup();");
                break;

            case ResizePicCommand c:
                yield return Invariant(
                    $"{gump}.AddBackground( {c.X}, {c.Y}, {c.GumpId}, {c.Width}, {c.Height} );");
                break;

            case CheckerTransCommand c:
                yield return Invariant(
                    $"{gump}.AddCheckerTrans( {c.X}, {c.Y}, {c.Width}, {c.Height} );");
                break;

            case GumpPicTiledCommand c:
                yield return Invariant(
                    $"{gump}.AddTiledGump( {c.X}, {c.Y}, {c.Width}, {c.Height}, {c.GumpId} );");
                break;

            // AddGumpColor writes the hue= keyword form, which is the one the
            // client's gumppic handler actually reads.
            case GumpPicCommand { PartialHue: true, Hue: not 0 } c:
                yield return "// No UOX3 call for a partial hue; this tints every pixel.";
                yield return Invariant(
                    $"{gump}.AddGumpColor( {c.X}, {c.Y}, {c.GumpId}, {c.Hue} );");
                break;

            case GumpPicCommand { Hue: 0 } c:
                yield return Invariant($"{gump}.AddGump( {c.X}, {c.Y}, {c.GumpId} );");
                break;

            case GumpPicCommand c:
                yield return Invariant(
                    $"{gump}.AddGumpColor( {c.X}, {c.Y}, {c.GumpId}, {c.Hue} );");
                break;

            // Source offset before the size, as the client reads it.
            case PicInPicCommand c:
                if (c.Hue != 0)
                {
                    yield return "// AddPicInPic takes no hue; this region is untinted.";
                }

                yield return Invariant(
                    $"{gump}.AddPicInPic( {c.X}, {c.Y}, {c.GumpId}, {c.SourceX}, {c.SourceY}, {c.Width}, {c.Height} );");
                break;

            case TilePicCommand { Hue: 0 } c:
                yield return Invariant($"{gump}.AddPicture( {c.X}, {c.Y}, {c.ItemId} );");
                break;

            case TilePicCommand c:
                yield return Invariant(
                    $"{gump}.AddPictureColor( {c.X}, {c.Y}, {c.ItemId}, {c.Hue} );");
                break;

            case TileAsGumpPicCommand c:
                yield return Invariant(
                    $"// No UOX3 call for tilepicasgumppic: item {c.ItemId} at {c.X}, {c.Y}.");
                break;

            case TextCommand c:
                foreach (string drift in slots.Assigned())
                {
                    yield return drift;
                }

                yield return Invariant(
                    $"{gump}.AddText( {c.X}, {c.Y}, {c.Hue}, {Literal(Text(c.Text, layout))} );");
                break;

            // The hue is the third argument here, not the last: AddCroppedText
            // reorders it into the layout command itself.
            case CroppedTextCommand c:
                foreach (string drift in slots.Assigned())
                {
                    yield return drift;
                }

                yield return Invariant(
                    $"{gump}.AddCroppedText( {c.X}, {c.Y}, {c.Hue}, {c.Width}, {c.Height}, {Literal(Text(c.Text, layout))} );");
                break;

            case HtmlGumpCommand c:
                foreach (string drift in slots.Assigned())
                {
                    yield return drift;
                }

                yield return Invariant(
                    $"{gump}.AddHTMLGump( {c.X}, {c.Y}, {c.Width}, {c.Height}, {Bool(c.Background)}, {Bool(c.Scrollbar)}, {Literal(Text(c.Text, layout))} );");
                break;

            // The one call that is handed its own text index, because UOX3 does
            // not advance its counter for these.
            case TextEntryCommand c:
                yield return c.MaxLength > 0
                    ? Invariant(
                        $"{gump}.AddTextEntryLimited( {c.X}, {c.Y}, {c.Width}, {c.Height}, {c.Hue}, {c.EntryId}, {slots.Entry()}, {Literal(Text(c.Text, layout))}, {c.MaxLength} );")
                    : Invariant(
                        $"{gump}.AddTextEntry( {c.X}, {c.Y}, {c.Width}, {c.Height}, {c.Hue}, {c.EntryId}, {slots.Entry()}, {Literal(Text(c.Text, layout))} );");
                break;

            case XmfHtmlCommand c:
                yield return XmfHtml(c, gump);
                break;

            case ButtonCommand { Tile: { } tile } c:
                yield return Invariant(
                    $"{gump}.AddButtonTileArt( {c.X}, {c.Y}, {c.NormalId}, {c.PressedId}, {Quit(c)}, {Page(c)}, {Reply(c)}, {tile.ItemId}, {tile.Hue}, {tile.X}, {tile.Y} );");
                break;

            case ButtonCommand c:
                yield return Invariant(
                    $"{gump}.AddButton( {c.X}, {c.Y}, {c.NormalId}, {c.PressedId}, {Quit(c)}, {Page(c)}, {Reply(c)} );");
                break;

            case CheckboxCommand c:
                yield return Invariant(
                    $"{gump}.AddCheckbox( {c.X}, {c.Y}, {c.UncheckedId}, {c.CheckedId}, {Flag(c.IsChecked)}, {c.Group} );");
                break;

            case RadioCommand c:
                yield return Invariant(
                    $"{gump}.AddRadio( {c.X}, {c.Y}, {c.UncheckedId}, {c.CheckedId}, {Flag(c.IsChecked)}, {c.Value} );");
                break;

            // CGump_AddToolTip skips the first argument after the cliloc, so a
            // one-argument call emits an empty block. Working around that would
            // break the day it is fixed.
            case TooltipCommand c:
                if (c.Arguments.Length > 0)
                {
                    yield return Invariant(
                        $"// Tooltip arguments dropped, AddToolTip skips its first: {Literal(c.Arguments)}");
                }

                yield return Invariant($"{gump}.AddToolTip( {c.ClilocId} );");
                break;

            case ItemPropertyCommand c:
                yield return Invariant($"{gump}.AddItemProperty( {c.Serial} );");
                break;

            default:
                throw new NotSupportedException(
                    $"No UOX3 output for '{command.GetType().Name}'.");
        }
    }

    /// <summary>
    /// A localised HTML area, in whichever of its three forms applies.
    /// </summary>
    /// <remarks>
    /// <c>AddXMFHTMLTok</c> declares an arity of eight but reads eleven
    /// arguments unconditionally, so the three cliloc arguments are always
    /// passed, padded with empty strings. It emits exactly three, tab separated,
    /// so a fourth cannot be expressed.
    /// </remarks>
    private static string XmfHtml(XmfHtmlCommand c, string gump)
    {
        string rect = Invariant($"{c.X}, {c.Y}, {c.Width}, {c.Height}");
        string flags = Invariant($"{Bool(c.Background)}, {Bool(c.Scrollbar)}");

        if (c.Arguments.Length > 0)
        {
            string[] parts = c.Arguments.Split('\t');

            string args = string.Join(
                ", ",
                Enumerable.Range(0, 3).Select(i => Literal(i < parts.Length ? parts[i] : string.Empty)));

            return Concat(
                Invariant($"{gump}.AddXMFHTMLTok( {rect}, {flags}, "),
                Invariant($"{c.Color}, {c.ClilocId}, {args} );"));
        }

        return c.Color != 0
            ? Invariant($"{gump}.AddXMFHTMLGumpColor( {rect}, {c.ClilocId}, {flags}, {c.Color} );")
            : Invariant($"{gump}.AddXMFHTMLGump( {rect}, {c.ClilocId}, {flags} );");
    }

    /// <summary>
    /// Tracks what UOX3 will believe about its own text list.
    /// </summary>
    /// <remarks>
    /// <c>_position</c> is the length of the string list, which every
    /// text-bearing call adds to. <c>_counter</c> is the index UOX3 will assign
    /// next, which only <c>AddText</c>, <c>AddCroppedText</c> and
    /// <c>AddHTMLGump</c> advance. They agree until the first text entry, and
    /// never again.
    /// </remarks>
    private sealed class TextSlots
    {
        private int _position;
        private int _counter;

        /// <summary>Takes the next auto-assigned slot, warning if it has drifted.</summary>
        internal IEnumerable<string> Assigned()
        {
            if (_counter != _position)
            {
                yield return Invariant(
                    $"// UOX3 will index this string as {_counter}, but it is at {_position}: a text entry above did not advance its counter. Move text entries after the labels.");
            }

            _position++;
            _counter++;
        }

        /// <summary>Takes the next slot for a text entry, which UOX3 is told.</summary>
        internal int Entry() => _position++;
    }

    private static string Text(TextRef reference, GumpLayout layout) =>
        layout.Texts[reference.Index].Value;

    /// <summary>
    /// Writes an element's name and comment above its first command.
    /// </summary>
    /// <remarks>
    /// Once per element, not once per command: an element with a tooltip and an
    /// item property produces three commands but wants one comment.
    /// </remarks>
    private static void AppendComment(
        StringBuilder script, LayoutCommand command, UoxExportOptions options)
    {
        if (!command.Origin.IsPrimary || !options.IncludeComments)
        {
            return;
        }

        string comment = command.Origin.Comment;
        string label = command.Origin.Name + (comment.Length > 0 ? ": " : string.Empty);
        string text = label + comment;

        if (text.Length == 0)
        {
            return;
        }

        script.AppendLine(CultureInfo.InvariantCulture, $"{Indent}// {Sanitise(text)}");
    }

    /// <summary>
    /// The reply handler, with one case per reply button and the entry reads.
    /// </summary>
    /// <remarks>
    /// <c>onGumpInput</c> is deliberately not generated. It carries a socket, an
    /// index and a reply string, and fires for the client's separate text-prompt
    /// packet — not for a gump's text entries, which come back through
    /// <c>gumpData</c> here.
    /// </remarks>
    private static void AppendHandler(
        StringBuilder script, GumpLayout layout, UoxExportOptions options)
    {
        List<ButtonCommand> replies = [.. layout.Commands
            .OfType<ButtonCommand>()
            .Where(b => b.Kind == ButtonKind.Reply)];

        List<TextEntryCommand> entries = [.. layout.Commands.OfType<TextEntryCommand>()];

        script.AppendLine();
        script.AppendLine("function onGumpPress( pSock, pButton, gumpData )");
        script.AppendLine("{");
        script.AppendLine($"{Indent}var pUser = pSock.currentChar;");

        if (entries.Count > 0)
        {
            AppendEntryReads(script, entries, options);
        }

        script.AppendLine();
        script.AppendLine($"{Indent}switch( pButton )");
        script.AppendLine($"{Indent}{{");

        foreach (int value in replies.Select(b => b.Param).Distinct())
        {
            string named = replies.First(b => b.Param == value).Origin.Name;

            string label = named.Length > 0
                ? Invariant($"{Indent}{Indent}case {value}: // {Sanitise(named)}")
                : Invariant($"{Indent}{Indent}case {value}:");

            script.AppendLine(label);

            script.AppendLine($"{Indent}{Indent}{Indent}break;");
            script.AppendLine();
        }

        script.AppendLine($"{Indent}{Indent}default:");
        script.AppendLine($"{Indent}{Indent}{Indent}break;");
        script.AppendLine($"{Indent}}}");
        script.AppendLine("}");
    }

    /// <summary>
    /// Reads the entries back by id rather than by position.
    /// </summary>
    /// <remarks>
    /// <c>GetId</c> and <c>GetEdit</c> are filled in one loop from the reply
    /// packet, so they are parallel — but the client sends only the entries it
    /// has, so a position is not an id. Matching on the id costs one loop and
    /// cannot read the wrong field.
    /// </remarks>
    private static void AppendEntryReads(
        StringBuilder script, List<TextEntryCommand> entries, UoxExportOptions options)
    {
        script.AppendLine();

        foreach (TextEntryCommand entry in entries)
        {
            string named = options.IncludeComments && entry.Origin.Name.Length > 0
                ? $"{Indent}// {Sanitise(entry.Origin.Name)}"
                : string.Empty;

            script.AppendLine(CultureInfo.InvariantCulture,
                $"{Indent}var text{entry.EntryId} = \"\";{named}");
        }

        script.AppendLine();
        script.AppendLine($"{Indent}for( var i = 0; i < gumpData.IDs; i++ )");
        script.AppendLine($"{Indent}{{");
        script.AppendLine($"{Indent}{Indent}switch( gumpData.GetId( i ))");
        script.AppendLine($"{Indent}{Indent}{{");

        foreach (TextEntryCommand entry in entries)
        {
            script.AppendLine(CultureInfo.InvariantCulture,
                $"{Indent}{Indent}{Indent}case {entry.EntryId}: text{entry.EntryId} = gumpData.GetEdit( i ); break;");
        }

        script.AppendLine($"{Indent}{Indent}}}");
        script.AppendLine($"{Indent}}}");
    }

    /// <summary>
    /// The button's <c>quit</c>, <c>page</c> and <c>return</c> slots.
    /// </summary>
    /// <remarks>
    /// <c>AddButton</c> is used for both kinds rather than <c>AddPageButton</c>,
    /// which writes only six of the seven slots the client's command takes.
    /// </remarks>
    private static int Quit(ButtonCommand c) => c.Kind == ButtonKind.Page ? 0 : 1;

    private static int Page(ButtonCommand c) => c.Kind == ButtonKind.Page ? c.Param : 0;

    private static int Reply(ButtonCommand c) => c.Kind == ButtonKind.Page ? 0 : c.Param;

    /// <summary>Reduces a user-supplied name to a single JavaScript identifier.</summary>
    private static string Identifier(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        StringBuilder identifier = new();

        foreach (char c in value)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                identifier.Append(c);
            }
        }

        if (identifier.Length == 0 || char.IsDigit(identifier[0]))
        {
            return fallback;
        }

        return identifier.ToString();
    }

    /// <summary>Makes text safe inside a double-quoted JavaScript string.</summary>
    private static string Literal(string value) =>
        "\""
        + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
        + "\"";

    /// <summary>Keeps a comment on one line.</summary>
    private static string Sanitise(string value) =>
        value
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

    private static string Bool(bool value) => value ? "true" : "false";

    private static string Flag(bool value) => value ? "1" : "0";

    private static string Invariant(FormattableString text) =>
        text.ToString(CultureInfo.InvariantCulture);

    /// <summary>Joins already-formatted fragments.</summary>
    private static string Concat(params string[] parts) => string.Concat(parts);
}
