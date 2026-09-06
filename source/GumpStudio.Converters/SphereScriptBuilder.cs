using System.Globalization;
using System.Text;

using GumpStudio.Core.Document;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Layout;
using GumpStudio.Core.Primitives;

namespace GumpStudio.Converters;

/// <summary>Which Sphere dialect to emit.</summary>
public enum SphereDialect
{
    /// <summary>
    /// 0.56 and the Revision builds: raw layout commands and a separate text block.
    /// </summary>
    Revision,

    /// <summary>
    /// 0.99 and 1.0: a fixed set of script functions, with strings written inline.
    /// </summary>
    Modern,
}

/// <summary>
/// Settings specific to Sphere output.
/// </summary>
/// <remarks>
/// A record class, not a record struct: defaulted primary-constructor parameters
/// on a struct are skipped by <c>default</c> and by <c>new()</c>.
/// </remarks>
public sealed record SphereExportOptions
{
    /// <summary>Which dialect to emit.</summary>
    public SphereDialect Dialect { get; init; } = SphereDialect.Revision;

    /// <summary>Name of the generated dialog.</summary>
    public string DialogName { get; init; } = "d_mygump";

    /// <summary>Emit element names and comments into the handler bodies.</summary>
    public bool IncludeComments { get; init; } = true;
}

/// <summary>
/// Turns a gump layout into a Sphere dialog script.
/// </summary>
/// <remarks>
/// <para>
/// Reads <see cref="GumpLayout"/> rather than the document. The Revision dialect
/// is the client's own layout grammar, so it is written by the shared
/// <see cref="LayoutStringWriter"/> — the same code that serves the POL
/// layout-string dialect and the raw format, rather than a second hand-written
/// copy of it.
/// </para>
/// <para>
/// Corrections carried over from the original, which this preserves:
/// </para>
/// <list type="bullet">
/// <item>
/// Checkboxes and radios take the released graphic first. The original emitted
/// the checked one first, so every one of them rendered inverted.
/// </item>
/// <item>
/// A page button no longer closes the dialog: the quit slot was hard-coded to 1.
/// </item>
/// <item>
/// Radio groups no longer leak across pages, and coordinates are absolute.
/// </item>
/// </list>
/// </remarks>
public static class SphereScriptBuilder
{
    /// <summary>
    /// Sphere capitalises the group command and has never closed a group.
    /// </summary>
    /// <remarks>
    /// Emitting an <c>endgroup</c> where the exporter previously emitted none
    /// would change every script that uses a radio button, so the omission is
    /// kept rather than quietly corrected.
    /// </remarks>
    private static readonly LayoutStringOptions Layout =
        new() { GroupKeyword = "Group", EmitEndGroup = false };

    /// <summary>Builds the script for a document.</summary>
    /// <param name="document">The gump to export.</param>
    /// <param name="options">Dialect and naming settings.</param>
    /// <param name="timestamp">
    /// Stamped into the header comment. Supply a fixed value for reproducible
    /// output; the original always used <c>DateTime.Now</c>.
    /// </param>
    public static string Build(
        GumpDocument document,
        SphereExportOptions? options = null,
        DateTimeOffset? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        return Build(GumpLayoutBuilder.Build(document), options, timestamp);
    }

    /// <summary>Builds the script for a layout that has already been produced.</summary>
    /// <param name="layout">The gump to export.</param>
    /// <param name="options">Dialect and naming settings.</param>
    /// <param name="timestamp">Header timestamp; injectable for reproducible output.</param>
    public static string Build(
        GumpLayout layout,
        SphereExportOptions? options = null,
        DateTimeOffset? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(layout);

        options ??= new SphereExportOptions();

        bool revision = options.Dialect == SphereDialect.Revision;
        string name = DialogName(options.DialogName);

        StringBuilder script = new();
        List<(int Id, string Text)> handlers = [];

        AppendHeader(script, timestamp, revision);

        script.AppendLine(CultureInfo.InvariantCulture, $"[DIALOG {name}]");

        GumpPoint at = layout.Properties.Location;

        script.AppendLine(revision
            ? Invariant($"{at.X},{at.Y}")
            : Invariant($"SetLocation={at.X},{at.Y}"));

        if (!layout.Properties.Closable)
        {
            script.AppendLine(revision ? "NOCLOSE" : "NoClose");
        }

        if (!layout.Properties.Movable)
        {
            script.AppendLine(revision ? "NOMOVE" : "NoMove");
        }

        if (!layout.Properties.Disposable)
        {
            script.AppendLine(revision ? "NODISPOSE" : "NoDispose");
        }

        foreach (string token in LayoutStringWriter.GumpLevelTokens(layout.Properties))
        {
            script.AppendLine(revision ? token : Comment(token, "no 0.99 function"));
        }

        foreach (LayoutCommand command in layout.Commands)
        {
            if (command is ButtonCommand { Kind: ButtonKind.Reply } reply)
            {
                handlers.Add((reply.Param, HandlerBody(reply, options)));
            }

            if (revision)
            {
                AppendRevisionCommand(script, command, layout);
            }
            else
            {
                AppendModernCommand(script, command, layout);
            }
        }

        script.AppendLine();

        if (revision)
        {
            script.AppendLine(CultureInfo.InvariantCulture, $"[DIALOG {name} TEXT]");

            for (int i = 0; i < layout.Texts.Count; i++)
            {
                script.AppendLine(BlockText(layout.Texts[i], i));
            }

            script.AppendLine();
        }

        script.AppendLine(CultureInfo.InvariantCulture, $"[DIALOG {name} BUTTON]");

        foreach ((int id, string text) in handlers)
        {
            script.AppendLine(CultureInfo.InvariantCulture, $"ON={id}");

            if (text.Length > 0)
            {
                script.AppendLine(text);
            }

            script.AppendLine();
        }

        script.AppendLine("[EOF]");

        return script.ToString();
    }

    private static void AppendHeader(StringBuilder script, DateTimeOffset? timestamp, bool revision)
    {
        string stamp = (timestamp ?? DateTimeOffset.Now).ToString("u", CultureInfo.InvariantCulture);

        script.AppendLine(CultureInfo.InvariantCulture, $"// Created {stamp}, with Gump Studio.");
        script.AppendLine("// Exported with the Sphere exporter, after the original by Francesco Furiani.");
        script.AppendLine(CultureInfo.InvariantCulture,
            $"// Script for {(revision ? "0.56 / Revisions" : "0.99 / 1.0")}");
        script.AppendLine();
    }

    /// <summary>
    /// The 0.56 dialect, which is the client's grammar verbatim.
    /// </summary>
    /// <remarks>
    /// Nothing is dialect-specific here beyond the two settings on
    /// <see cref="Layout"/>, which is the point: this dialect and POL's
    /// layout-string one were two separate hand-written copies of one grammar.
    /// </remarks>
    private static void AppendRevisionCommand(
        StringBuilder script, LayoutCommand command, GumpLayout layout)
    {
        _ = layout;

        if (LayoutStringWriter.Format(command, Layout, LayoutStringWriter.ByIndex) is { } line)
        {
            script.AppendLine(line);
        }
    }

    /// <summary>
    /// The 0.99 dialect, which is a fixed set of script functions.
    /// </summary>
    /// <remarks>
    /// Where only a refinement is missing — a partial hue, a crop rectangle, a
    /// character cap, tile art on a button — the nearest function is emitted and
    /// the layout command it could not express is noted beside it. Dropping the
    /// command instead would delete a visible element from the dialog, and a
    /// button that cannot be drawn is a dialog the player cannot dismiss.
    /// </remarks>
    private static void AppendModernCommand(
        StringBuilder script, LayoutCommand command, GumpLayout layout)
    {
        switch (command)
        {
            case PageCommand c:
                script.AppendLine(Invariant($"Page({c.Page})"));
                break;

            case GroupCommand c:
                script.AppendLine(Invariant($"Group({c.Group})"));
                break;

            case EndGroupCommand:
                // Never emitted by either dialect.
                break;

            case CheckerTransCommand c:
                script.AppendLine(Invariant($"CheckerTrans({c.X},{c.Y},{c.Width},{c.Height})"));
                break;

            case ResizePicCommand c:
                script.AppendLine(Invariant(
                    $"ResizePic({c.X},{c.Y},{c.GumpId},{c.Width},{c.Height})"));
                break;

            case GumpPicTiledCommand c:
                script.AppendLine(Invariant(
                    $"GumpPicTiled({c.X},{c.Y},{c.Width},{c.Height},{c.GumpId})"));
                break;

            case GumpPicCommand c:
                GumpPic(script, c);
                break;

            case TilePicCommand c:
                script.AppendLine(c.Hue != 0
                    ? Invariant($"TilePicHue({c.X},{c.Y},{c.ItemId},{c.Hue})")
                    : Invariant($"TilePic({c.X},{c.Y},{c.ItemId})"));
                break;

            case TextCommand c:
                script.AppendLine(Invariant(
                    $"TextA({c.X},{c.Y},{c.Hue},\"{Escape(Inline(c.Text, layout))}\")"));
                break;

            case CroppedTextCommand c:
                // Only the clipping is lost; the text itself still belongs on the
                // dialog.
                Downgraded(
                    script,
                    Invariant($"TextA({c.X},{c.Y},{c.Hue},\"{Escape(Inline(c.Text, layout))}\")"),
                    command);
                break;

            case TextEntryCommand c:
                script.AppendLine(Concat(
                    Invariant($"TextEntryA({c.X},{c.Y},{c.Width},{c.Height},"),
                    Invariant($"{c.Hue},{c.EntryId},\"{Escape(Inline(c.Text, layout))}\")")));
                break;

            case HtmlGumpCommand c:
                script.AppendLine(Concat(
                    Invariant($"HtmlGumpA({c.X},{c.Y},{c.Width},{c.Height},"),
                    Invariant($"\"{Escape(Inline(c.Text, layout))}\",{Flag(c.Background)},{Flag(c.Scrollbar)})")));
                break;

            case XmfHtmlCommand c:
                XmfHtml(script, c);
                break;

            case ButtonCommand c:
                Button(script, c);
                break;

            case RadioCommand c:
                script.AppendLine(Invariant(
                    $"Radio({c.X},{c.Y},{c.UncheckedId},{c.CheckedId},{Flag(c.IsChecked)},{c.Value})"));
                break;

            case CheckboxCommand c:
                script.AppendLine(Invariant(
                    $"CheckBox({c.X},{c.Y},{c.UncheckedId},{c.CheckedId},{Flag(c.IsChecked)},{c.Group})"));
                break;

            // Nothing in 0.99 comes close to these, so they are written as a note
            // rather than as a call that would not run.
            case PicInPicCommand:
            case TileAsGumpPicCommand:
            case TooltipCommand:
            case ItemPropertyCommand:
                script.AppendLine(Comment(Preferred(command), "no 0.99 function"));
                break;

            default:
                throw new NotSupportedException(
                    $"No Sphere output for '{command.GetType().Name}'.");
        }
    }

    private static void GumpPic(StringBuilder script, GumpPicCommand c)
    {
        if (c.Hue == 0)
        {
            script.AppendLine(Invariant($"GumpPic({c.X},{c.Y},{c.GumpId})"));

            return;
        }

        string full = Invariant($"GumpPic({c.X},{c.Y},{c.GumpId},{c.Hue})");

        if (c.PartialHue)
        {
            // 0.99 has no partial-hue function, but a full tint still draws the
            // image. Dropping the command would lose it altogether.
            Downgraded(script, full, c);

            return;
        }

        script.AppendLine(full);
    }

    private static void XmfHtml(StringBuilder script, XmfHtmlCommand c)
    {
        string plain = Concat(
            Invariant($"XmfHtmlGump({c.X},{c.Y},{c.Width},{c.Height},"),
            Invariant($"{c.ClilocId},{Flag(c.Background)},{Flag(c.Scrollbar)})"));

        // Losing the colour and the substitution arguments still leaves a
        // readable localised area; losing the command leaves nothing.
        if (c.Arguments.Length > 0 || c.Color != 0)
        {
            Downgraded(script, plain, c);

            return;
        }

        script.AppendLine(plain);
    }

    /// <summary>
    /// A button, or a button with tile art overlaid on it.
    /// </summary>
    /// <remarks>
    /// The slots are quit, page-id, return-value. The original hard-coded quit to
    /// 1, so a page button dismissed the dialog instead of switching page.
    /// </remarks>
    private static void Button(StringBuilder script, ButtonCommand c)
    {
        bool isPage = c.Kind == ButtonKind.Page;

        int quit = isPage ? 0 : 1;
        int pageId = isPage ? c.Param : 0;
        int returnValue = isPage ? 0 : c.Param;

        string plain = Concat(
            Invariant($"Button({c.X},{c.Y},{c.NormalId},{c.PressedId},"),
            Invariant($"{quit},{pageId},{returnValue})"));

        if (c.Tile is null)
        {
            script.AppendLine(plain);

            return;
        }

        // Only the overlay is lost. Commenting the whole command out would take
        // the button with it, and the dialog would have no way to be dismissed.
        Downgraded(script, plain, c);
    }

    /// <summary>The body written under a reply button's <c>ON=</c> handler.</summary>
    private static string HandlerBody(ButtonCommand button, SphereExportOptions options)
    {
        if (!options.IncludeComments)
        {
            return button.CodeBehind;
        }

        StringBuilder body = new();

        if (button.Origin.Name.Length > 0)
        {
            body.AppendLine(CultureInfo.InvariantCulture, $"// {Sanitise(button.Origin.Name)}");
        }

        if (button.Origin.Comment.Length > 0)
        {
            body.AppendLine(CultureInfo.InvariantCulture, $"// {Sanitise(button.Origin.Comment)}");
        }

        if (button.CodeBehind.Length > 0)
        {
            body.Append(button.CodeBehind);
        }

        return body.ToString().TrimEnd('\r', '\n');
    }

    /// <summary>One entry of the text block, with a placeholder where it is empty.</summary>
    private static string BlockText(LayoutText text, int index) =>
        text.Value.Length == 0
            ? Invariant($"{Placeholder(text.Role)} id.{index}")
            : Sanitise(text.Value);

    private static string Placeholder(TextRole role) => role switch
    {
        TextRole.Entry => "Textentry",
        TextRole.Html => "HtmlGump",
        _ => "Text",
    };

    private static string Inline(TextRef reference, GumpLayout layout) =>
        layout.Texts[reference.Index].Value;

    /// <summary>The layout command a 0.99 call could not fully express.</summary>
    private static string Preferred(LayoutCommand command) =>
        LayoutStringWriter.Format(command, Layout, LayoutStringWriter.AsZero)
        ?? command.GetType().Name;

    /// <summary>Marks a command the 0.99 dialect has no function for.</summary>
    private static string Comment(string command, string reason) => $"// {command}   // {reason}";

    /// <summary>
    /// Emits the nearest thing 0.99 can express, and records what was lost.
    /// </summary>
    /// <remarks>
    /// Used where only a refinement is unavailable — a partial hue, a crop
    /// rectangle, a tile overlay. Commenting the whole command out instead would
    /// delete a visible element from the dialog, which is a much worse answer
    /// than rendering it slightly wrong.
    /// </remarks>
    private static void Downgraded(StringBuilder script, string fallback, LayoutCommand preferred)
    {
        script.AppendLine(fallback);
        script.AppendLine(Comment(
            Preferred(preferred), "no 0.99 function; the line above is the closest it has"));
    }

    /// <summary>
    /// Reduces a user-supplied name to a single Sphere identifier token.
    /// </summary>
    /// <remarks>
    /// A dialog name reaches the script as a bare token, so whitespace in it
    /// would split the <c>[DIALOG …]</c> header into something Sphere cannot read.
    /// </remarks>
    private static string DialogName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "d_mygump";
        }

        StringBuilder built = new(value.Length);

        foreach (char c in value)
        {
            built.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        }

        return built.ToString();
    }

    /// <summary>Makes text safe inside a double-quoted Sphere string.</summary>
    private static string Escape(string value) =>
        Sanitise(value.Replace("\"", "\\\"", StringComparison.Ordinal));

    /// <summary>
    /// Keeps a value on one line.
    /// </summary>
    /// <remarks>
    /// Sphere's script format is line-based throughout: a newline inside a text
    /// entry would be read as the start of the next command.
    /// </remarks>
    private static string Sanitise(string value) =>
        value
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

    private static string Flag(bool value) => value ? "1" : "0";

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Joins already-formatted fragments.
    /// </summary>
    /// <remarks>
    /// A multi-line interpolation joined with <c>+</c> collapses to a plain string
    /// before it reaches <see cref="Invariant(FormattableString)"/>, so long
    /// commands are built from invariant pieces and concatenated here.
    /// </remarks>
    private static string Concat(params string[] parts) => string.Concat(parts);
}
