using System.Globalization;
using System.Text;

using GumpStudio.Core.Document;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Primitives;

namespace GumpStudio.Plugins.Sphere;

/// <summary>Which Sphere script dialect to emit.</summary>
public enum SphereDialect
{
    /// <summary>
    /// 0.56 and the Revisions line: bare layout commands, with the strings in a
    /// separate <c>[DIALOG name TEXT]</c> block and referenced by index.
    /// </summary>
    Revision,

    /// <summary>
    /// 0.99 and 1.0: function-call syntax, with strings written inline.
    /// </summary>
    Modern,
}

/// <summary>
/// Settings specific to the Sphere exporter.
/// </summary>
/// <remarks>
/// A record class rather than a record struct, for the same reason as every
/// other options type here: defaulted members on a struct are silently skipped
/// by <c>default</c> and <c>new()</c>.
/// </remarks>
public sealed record SphereExportOptions
{
    /// <summary>Which dialect to emit.</summary>
    public SphereDialect Dialect { get; init; } = SphereDialect.Revision;

    /// <summary>Name of the generated dialog.</summary>
    public string DialogName { get; init; } = "d_mygump";

    /// <summary>Emit element names and comments into the button block.</summary>
    public bool IncludeComments { get; init; } = true;
}

/// <summary>
/// Turns a document into a Sphere server script.
/// </summary>
/// <remarks>
/// <para>
/// Ported from the 1.8 Sphere exporter by Francesco Furiani. Both dialects it
/// emitted are preserved, with these corrections:
/// </para>
/// <list type="bullet">
/// <item>
/// <b>Checkboxes and radios had their two graphics the wrong way round.</b> The
/// layout command takes the released id first and the pressed id second; the
/// original emitted checked first, so every checkbox and radio in a generated
/// dialog rendered inverted.
/// </item>
/// <item>
/// <b>Every button closed the gump.</b> The quit flag was hard-coded to 1, so a
/// page button dismissed the dialog instead of switching page. It is derived from
/// the button's kind now, and the page and return-value slots are filled the way
/// the client's parser reads them.
/// </item>
/// <item>
/// <b>Radio groups leaked across pages.</b> <c>page</c> resets the client's
/// current group, but the tracker spanned the whole document, so a second page
/// whose first radio matched the previous page's group never got its <c>Group</c>
/// command.
/// </item>
/// <item>
/// Coordinates come from <see cref="Element.GetAbsolutePosition"/>, fixing the
/// same nested-group defect the other exporters had, and all numbers format
/// invariantly.
/// </item>
/// </list>
/// <para>
/// The commands the client gained after 1.8 are emitted in the Revision dialect,
/// which is raw layout syntax and so accepts anything the client parses. The 0.99
/// dialect is a fixed set of script functions and has no equivalent for most of
/// them. Where only a refinement is missing — a partial hue, a crop rectangle, a
/// tile overlay — the nearest function is emitted and the lost command is noted
/// beside it, because deleting the whole command would take a visible element off
/// the dialog. Where nothing comes close, the command is written as a comment
/// rather than as a call that would not run.
/// </para>
/// </remarks>
public static class SphereScriptBuilder
{
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

        options ??= new SphereExportOptions();

        bool revision = options.Dialect == SphereDialect.Revision;
        string name = DialogName(options.DialogName);

        StringBuilder script = new();
        List<string> texts = [];
        List<(int Id, string Text)> handlers = [];

        AppendHeader(script, options, timestamp, revision);

        script.AppendLine(CultureInfo.InvariantCulture, $"[DIALOG {name}]");

        GumpPoint at = document.Properties.Location;

        script.AppendLine(revision
            ? Invariant($"{at.X},{at.Y}")
            : Invariant($"SetLocation={at.X},{at.Y}"));

        if (!document.Properties.Closable)
        {
            script.AppendLine(revision ? "NOCLOSE" : "NoClose");
        }

        if (!document.Properties.Movable)
        {
            script.AppendLine(revision ? "NOMOVE" : "NoMove");
        }

        if (!document.Properties.Disposable)
        {
            script.AppendLine(revision ? "NODISPOSE" : "NoDispose");
        }

        foreach (string token in GumpLevelTokens(document.Properties, revision))
        {
            script.AppendLine(token);
        }

        for (int page = 0; page < document.PageCount; page++)
        {
            // `page` resets the client's current group, so the tracker resets with
            // it. The original carried it across the whole document.
            int radioGroup = -1;

            script.AppendLine(revision
                ? Invariant($"page {page}")
                : Invariant($"Page({page})"));

            foreach (Element element in document.Pages[page].Leaves())
            {
                AppendElement(script, element, options, revision, texts, handlers, ref radioGroup);
            }
        }

        script.AppendLine();

        if (revision)
        {
            script.AppendLine(CultureInfo.InvariantCulture, $"[DIALOG {name} TEXT]");

            foreach (string text in texts)
            {
                script.AppendLine(text);
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

    private static void AppendHeader(
        StringBuilder script, SphereExportOptions options, DateTimeOffset? timestamp, bool revision)
    {
        string stamp = (timestamp ?? DateTimeOffset.Now).ToString("u", CultureInfo.InvariantCulture);

        script.AppendLine(CultureInfo.InvariantCulture, $"// Created {stamp}, with Gump Studio.");
        script.AppendLine("// Exported with the Sphere exporter, after the original by Francesco Furiani.");
        script.AppendLine(CultureInfo.InvariantCulture,
            $"// Script for {(revision ? "0.56 / Revisions" : "0.99 / 1.0")}");
        script.AppendLine();

        _ = options;
    }

    private static void AppendElement(
        StringBuilder script,
        Element element,
        SphereExportOptions options,
        bool revision,
        List<string> texts,
        List<(int Id, string Text)> handlers,
        ref int radioGroup)
    {
        GumpPoint at = element.GetAbsolutePosition();

        switch (element)
        {
            case AlphaElement alpha:
                script.AppendLine(revision
                    ? Invariant($"checkertrans {at.X} {at.Y} {alpha.Width} {alpha.Height}")
                    : Invariant($"CheckerTrans({at.X},{at.Y},{alpha.Width},{alpha.Height})"));
                break;

            case BackgroundElement background:
                script.AppendLine(revision
                    ? Invariant($"resizepic {at.X} {at.Y} {background.GumpId} {background.Width} {background.Height}")
                    : Invariant($"ResizePic({at.X},{at.Y},{background.GumpId},{background.Width},{background.Height})"));
                break;

            case TiledElement tiled:
                script.AppendLine(revision
                    ? Invariant($"gumppictiled {at.X} {at.Y} {tiled.Width} {tiled.Height} {tiled.GumpId}")
                    : Invariant($"GumpPicTiled({at.X},{at.Y},{tiled.Width},{tiled.Height},{tiled.GumpId})"));
                break;

            case ImageElement image:
                GumpPic(script, image, at, revision);
                break;

            case PicInPicElement pic:
                script.AppendLine(revision
                    ? PicInPic(pic, at)
                    : Comment(PicInPic(pic, at), "no 0.99 function"));
                break;

            case ItemElement item:
                script.AppendLine(revision
                    ? item.Hue != 0
                        ? Invariant($"tilepichue {at.X} {at.Y} {item.ItemId} {item.Hue}")
                        : Invariant($"tilepic {at.X} {at.Y} {item.ItemId}")
                    : item.Hue != 0
                        ? Invariant($"TilePicHue({at.X},{at.Y},{item.ItemId},{item.Hue})")
                        : Invariant($"TilePic({at.X},{at.Y},{item.ItemId})"));
                break;

            case TileAsGumpElement tile:
                string tileAsGump = Invariant(
                    $"tilepicasgumppic {at.X} {at.Y} {tile.ItemId} {tile.LinkId} {tile.ParamB} {tile.ParamC}");

                script.AppendLine(revision ? tileAsGump : Comment(tileAsGump, "no 0.99 function"));
                break;

            case LabelElement label:
                Text(script, label, at, revision, texts);
                break;

            case TextEntryElement entry:
                script.AppendLine(TextEntry(entry, at, revision, texts));
                break;

            case HtmlElement html:
                Html(script, html, at, revision, texts);
                break;

            case ButtonElement button:
                Button(script, button, at, revision);

                if (button.Kind == ButtonKind.Reply)
                {
                    handlers.Add((button.Param, HandlerBody(button, options)));
                }

                break;

            // Radio must precede Checkbox: it derives from it.
            case RadioElement radio:
                if (radio.GroupId != radioGroup)
                {
                    script.AppendLine(revision
                        ? Invariant($"Group {radio.GroupId}")
                        : Invariant($"Group({radio.GroupId})"));

                    radioGroup = radio.GroupId;
                }

                // Released id first, pressed id second. The original had them the
                // other way round, so every radio rendered inverted.
                script.AppendLine(revision
                    ? Invariant($"radio {at.X} {at.Y} {radio.UncheckedId} {radio.CheckedId} {Flag(radio.IsChecked)} {radio.Value}")
                    : Invariant($"Radio({at.X},{at.Y},{radio.UncheckedId},{radio.CheckedId},{Flag(radio.IsChecked)},{radio.Value})"));
                break;

            case CheckboxElement checkbox:
                script.AppendLine(revision
                    ? Invariant($"checkbox {at.X} {at.Y} {checkbox.UncheckedId} {checkbox.CheckedId} {Flag(checkbox.IsChecked)} {checkbox.GroupId}")
                    : Invariant($"CheckBox({at.X},{at.Y},{checkbox.UncheckedId},{checkbox.CheckedId},{Flag(checkbox.IsChecked)},{checkbox.GroupId})"));
                break;

            case GroupElement:
                // Leaves() never yields one; groups are an editor construct.
                break;

            default:
                throw new NotSupportedException(
                    $"No Sphere output for element type '{element.TypeName}'.");
        }

        AppendTooltip(script, element, revision);
    }

    /// <summary>
    /// Emits the tooltip commands attached to an element.
    /// </summary>
    /// <remarks>
    /// Both attach to whichever element the client created last, so they follow
    /// their own element immediately. Neither has a 0.99 script function.
    /// </remarks>
    private static void AppendTooltip(StringBuilder script, Element element, bool revision)
    {
        if (element.TooltipClilocId != 0)
        {
            string tooltip = element.TooltipArguments.Length > 0
                ? Invariant($"tooltip {element.TooltipClilocId} @{element.TooltipArguments}@")
                : Invariant($"tooltip {element.TooltipClilocId}");

            script.AppendLine(revision ? tooltip : Comment(tooltip, "no 0.99 function"));
        }

        if (element.ItemPropertySerial != 0)
        {
            string property = Invariant($"itemproperty {element.ItemPropertySerial}");

            script.AppendLine(revision ? property : Comment(property, "no 0.99 function"));
        }
    }

    /// <summary>The commands set by gump-level flags rather than by an element.</summary>
    private static IEnumerable<string> GumpLevelTokens(GumpProperties properties, bool revision)
    {
        if (properties.MasterGumpId != 0)
        {
            string token = Invariant($"mastergump {properties.MasterGumpId}");

            yield return revision ? token : Comment(token, "no 0.99 function");
        }

        // Parser toggles: emitting one flips it for the rest of the definition,
        // which is why they carry no argument and appear once.
        foreach ((bool set, string token) in new[]
        {
            (properties.UpperWordCase, "toggleupperwordcase"),
            (properties.CroppedText, "togglecroppedtext"),
            (properties.EnhancedClientInput, "echandleinput"),
        })
        {
            if (set)
            {
                yield return revision ? token : Comment(token, "no 0.99 function");
            }
        }
    }

    private static void GumpPic(StringBuilder script, ImageElement image, GumpPoint at, bool revision)
    {
        if (image.Hue == 0)
        {
            script.AppendLine(revision
                ? Invariant($"gumppic {at.X} {at.Y} {image.GumpId}")
                : Invariant($"GumpPic({at.X},{at.Y},{image.GumpId})"));

            return;
        }

        if (image.PartialHue && revision)
        {
            script.AppendLine(Invariant($"gumppicphued {at.X} {at.Y} {image.GumpId} {image.Hue}"));

            return;
        }

        string full = revision
            ? Invariant($"gumppic {at.X} {at.Y} {image.GumpId} {image.Hue}")
            : Invariant($"GumpPic({at.X},{at.Y},{image.GumpId},{image.Hue})");

        if (image.PartialHue)
        {
            // 0.99 has no partial-hue function, but a full tint still draws the
            // image. Dropping the command would lose it altogether.
            Downgraded(script, full, Invariant($"gumppicphued {at.X} {at.Y} {image.GumpId} {image.Hue}"));

            return;
        }

        script.AppendLine(full);
    }

    private static string PicInPic(PicInPicElement pic, GumpPoint at)
    {
        string command = pic switch
        {
            { Hue: 0 } => "picinpic",
            { PartialHue: true } => "picinpicphued",
            _ => "picinpichued",
        };

        string region = Invariant(
            $"{at.X} {at.Y} {pic.GumpId} {pic.SourceX} {pic.SourceY} {pic.Width} {pic.Height}");

        return pic.Hue == 0
            ? $"{command} {region}"
            : Concat($"{command} {region}", Invariant($" {pic.Hue}"));
    }

    /// <summary>
    /// A label, as a text-array reference or an inline string.
    /// </summary>
    /// <remarks>
    /// The Revision dialect keeps its strings in a separate block and refers to
    /// them by index; 0.99 writes them inline with an <c>A</c>-suffixed function.
    /// </remarks>
    private static void Text(
        StringBuilder script, LabelElement label, GumpPoint at, bool revision, List<string> texts)
    {
        if (!revision)
        {
            string plain = Invariant($"TextA({at.X},{at.Y},{label.Hue},\"{Escape(label.Text)}\")");

            if (label.Cropped)
            {
                // Only the clipping is lost; the text itself still belongs on the
                // dialog.
                Downgraded(
                    script,
                    plain,
                    Invariant($"croppedtext {at.X} {at.Y} {label.Width} {label.Height} {label.Hue} 0"));

                return;
            }

            script.AppendLine(plain);

            return;
        }

        int index = AddText(texts, label.Text, "Text");

        script.AppendLine(label.Cropped
            ? Invariant($"croppedtext {at.X} {at.Y} {label.Width} {label.Height} {label.Hue} {index}")
            : Invariant($"text {at.X} {at.Y} {label.Hue} {index}"));
    }

    private static string TextEntry(
        TextEntryElement entry, GumpPoint at, bool revision, List<string> texts)
    {
        if (!revision)
        {
            return Concat(
                Invariant($"TextEntryA({at.X},{at.Y},{entry.Width},{entry.Height},"),
                Invariant($"{entry.Hue},{entry.EntryId},\"{Escape(entry.InitialText)}\")"));
        }

        int index = AddText(texts, entry.InitialText, "Textentry");

        return entry.MaxLength > 0
            ? Concat(
                Invariant($"textentrylimited {at.X} {at.Y} {entry.Width} {entry.Height} "),
                Invariant($"{entry.Hue} {entry.EntryId} {index} {entry.MaxLength}"))
            : Concat(
                Invariant($"textentry {at.X} {at.Y} {entry.Width} {entry.Height} "),
                Invariant($"{entry.Hue} {entry.EntryId} {index}"));
    }

    private static void Html(
        StringBuilder script, HtmlElement html, GumpPoint at, bool revision, List<string> texts)
    {
        string rectSpaced = Invariant($"{at.X} {at.Y} {html.Width} {html.Height}");
        string flagsSpaced = Invariant($"{Flag(html.ShowBackground)} {Flag(html.ShowScrollbar)}");

        if (html.ContentKind == HtmlContentKind.Html)
        {
            if (!revision)
            {
                script.AppendLine(Concat(
                    Invariant($"HtmlGumpA({at.X},{at.Y},{html.Width},{html.Height},"),
                    Invariant($"\"{Escape(html.Html)}\",{Flag(html.ShowBackground)},{Flag(html.ShowScrollbar)})")));

                return;
            }

            int index = AddText(texts, html.Html, "HtmlGump");

            script.AppendLine(Concat($"htmlgump {rectSpaced} ", Invariant($"{index} {flagsSpaced}")));

            return;
        }

        string plain = revision
            ? Concat($"xmfhtmlgump {rectSpaced} ", Invariant($"{html.ClilocId} {flagsSpaced}"))
            : Concat(
                Invariant($"XmfHtmlGump({at.X},{at.Y},{html.Width},{html.Height},"),
                Invariant($"{html.ClilocId},{Flag(html.ShowBackground)},{Flag(html.ShowScrollbar)})"));

        if (html.Arguments.Length > 0)
        {
            string tok = Concat(
                $"xmfhtmltok {rectSpaced} {flagsSpaced} ",
                Invariant($"{html.Color} {html.ClilocId} @{html.Arguments}@"));

            // Losing the colour and the substitution arguments still leaves a
            // readable localised area; losing the command leaves nothing.
            if (revision)
            {
                script.AppendLine(tok);
            }
            else
            {
                Downgraded(script, plain, tok);
            }

            return;
        }

        if (html.Color != 0)
        {
            string coloured = Concat(
                $"xmfhtmlgumpcolor {rectSpaced} ",
                Invariant($"{html.ClilocId} {flagsSpaced} {html.Color}"));

            if (revision)
            {
                script.AppendLine(coloured);
            }
            else
            {
                Downgraded(script, plain, coloured);
            }

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
    private static void Button(StringBuilder script, ButtonElement button, GumpPoint at, bool revision)
    {
        bool isPage = button.Kind == ButtonKind.Page;

        int quit = isPage ? 0 : 1;
        int pageId = isPage ? button.Param : 0;
        int returnValue = isPage ? 0 : button.Param;

        string plain = revision
            ? Concat(
                Invariant($"button {at.X} {at.Y} {button.NormalId} {button.PressedId} "),
                Invariant($"{quit} {pageId} {returnValue}"))
            : Concat(
                Invariant($"Button({at.X},{at.Y},{button.NormalId},{button.PressedId},"),
                Invariant($"{quit},{pageId},{returnValue})"));

        if (button.TileId == 0)
        {
            script.AppendLine(plain);

            return;
        }

        string tileArt = Concat(
            Invariant($"buttontileart {at.X} {at.Y} {button.NormalId} {button.PressedId} "),
            Invariant($"{quit} {pageId} {returnValue} {button.TileId} {button.TileHue} "),
            Invariant($"{button.TileX} {button.TileY}"));

        if (revision)
        {
            script.AppendLine(tileArt);

            return;
        }

        // Only the overlay is lost. Commenting the whole command out would take
        // the button with it, and the dialog would have no way to be dismissed.
        Downgraded(script, plain, tileArt);
    }

    /// <summary>The body written under a reply button's <c>ON=</c> handler.</summary>
    private static string HandlerBody(ButtonElement button, SphereExportOptions options)
    {
        if (!options.IncludeComments)
        {
            return button.CodeBehind;
        }

        StringBuilder body = new();

        if (button.Name.Length > 0)
        {
            body.AppendLine(CultureInfo.InvariantCulture, $"// {Sanitise(button.Name)}");
        }

        if (button.Comment.Length > 0)
        {
            body.AppendLine(CultureInfo.InvariantCulture, $"// {Sanitise(button.Comment)}");
        }

        if (button.CodeBehind.Length > 0)
        {
            body.Append(button.CodeBehind);
        }

        return body.ToString().TrimEnd('\r', '\n');
    }

    /// <summary>Adds a string to the text block and returns its index.</summary>
    private static int AddText(List<string> texts, string value, string placeholder)
    {
        int index = texts.Count;

        texts.Add(value.Length == 0 ? $"{placeholder} id.{index}" : Sanitise(value));

        return index;
    }

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
    private static void Downgraded(StringBuilder script, string fallback, string preferred)
    {
        script.AppendLine(fallback);
        script.AppendLine(Comment(preferred, "no 0.99 function; the line above is the closest it has"));
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
