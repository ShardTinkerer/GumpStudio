using System.Globalization;
using System.Text;

using GumpStudio.Core.Document;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Primitives;

namespace GumpStudio.Plugins.Pol;

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
/// Turns a document into a POL script.
/// </summary>
/// <remarks>
/// <para>
/// Ported from the 1.8 POL exporter, preserving both dialects it emitted, with
/// four corrections:
/// </para>
/// <list type="bullet">
/// <item>
/// Coordinates come from <see cref="Element.GetAbsolutePosition"/>. The original
/// flattened groups and then emitted parent-relative coordinates, so anything
/// inside a group exported to the wrong place.
/// </item>
/// <item>
/// Text is escaped before being interpolated into a quoted POL string. The
/// original produced a syntactically broken script for any text containing a
/// quote.
/// </item>
/// <item>
/// All numbers format with the invariant culture, so output does not change with
/// the machine's locale.
/// </item>
/// <item>
/// The <c>button</c> layout command puts its values in the right slots. See
/// <see cref="LayoutButton"/>.
/// </item>
/// </list>
/// <para>
/// The layout-string dialect also emits the commands the client gained after 1.8
/// was written: <c>picinpic</c>, <c>buttontileart</c>, <c>textentrylimited</c>,
/// <c>croppedtext</c>, <c>xmfhtmlgumpcolor</c>, <c>xmfhtmltok</c>,
/// <c>tooltip</c>, <c>itemproperty</c>, <c>mastergump</c> and friends. The gump
/// package has no function for most of those, so there they are emitted
/// commented out beside the call that comes closest — the same thing the
/// original did for <c>gumppictiled</c>.
/// </para>
/// </remarks>
public sealed class PolScriptBuilder
{
    private const string PluginName = "POLGumpExporter";
    private const string PluginVersion = "2.0";

    /// <summary>Builds the script for a document.</summary>
    /// <param name="document">The gump to export.</param>
    /// <param name="gumpName">Identifier used for the variable and program name.</param>
    /// <param name="options">Dialect and comment settings.</param>
    /// <param name="timestamp">
    /// Stamped into the header comment. Supply a fixed value for reproducible
    /// output; the original always used <c>DateTime.Now</c>, which made its
    /// output impossible to diff.
    /// </param>
    public static string Build(
        GumpDocument document,
        string gumpName,
        PolExportOptions? options = null,
        DateTimeOffset? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        options ??= new PolExportOptions();

        string name = NormaliseName(gumpName);

        return options.Style == PolScriptStyle.GumpPackage
            ? BuildGumpPackage(document, name, options, timestamp)
            : BuildLayoutStrings(document, name, options, timestamp);
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
        GumpDocument document, string name, PolExportOptions options, DateTimeOffset? timestamp)
    {
        List<string> body = [];

        GumpPoint location = document.Properties.Location;

        body.Add(location is { X: 0, Y: 0 }
            ? $"var {name} := GFCreateGump();"
            : Invariant($"var {name} := GFCreateGump({location.X},{location.Y});"));

        body.Add(string.Empty);

        if (!document.Properties.Movable)
        {
            body.Add($"GFMovable({name}, 0);");
        }

        if (!document.Properties.Closable)
        {
            body.Add($"GFClosable({name}, 0);");
        }

        if (!document.Properties.Disposable)
        {
            body.Add($"GFDisposable({name}, 0);");
        }

        foreach (string token in GumpLevelTokens(document.Properties))
        {
            Unsupported(body, token);
        }

        for (int page = 0; page < document.PageCount; page++)
        {
            // The gump package's radio group is per-gump state, exactly as the
            // client's is, and switching page resets it — so the tracker resets
            // too. Carrying it across pages made the exporter skip the group
            // call for a page whose first radio happened to match the last group
            // used on the previous page.
            int radioGroup = -1;

            if (page > 0)
            {
                body.Add(string.Empty);
            }

            body.Add(Invariant($"GFPage({name}, {page});"));

            foreach (Element element in document.Pages[page].Leaves())
            {
                AppendComment(body, element, options);
                AppendGumpPackageElement(body, name, element, options, ref radioGroup);
                AppendTooltip(body, element, Unsupported);
            }
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

    private static void AppendGumpPackageElement(
        List<string> body, string name, Element element, PolExportOptions options, ref int radioGroup)
    {
        GumpPoint at = element.GetAbsolutePosition();

        switch (element)
        {
            case HtmlElement html when html.ContentKind == HtmlContentKind.Html:
                body.Add(GfHtmlArea(name, html, at, options));
                break;

            case HtmlElement html:
                body.Add(GfHtmlLocalized(name, html, at));

                // GFAddHTMLLocalized takes neither a colour nor cliloc arguments.
                if (html.Color != 0 || html.Arguments.Length > 0)
                {
                    Unsupported(body, LayoutLocalizedHtml(html, at));
                }

                break;

            case TextEntryElement entry:
                body.Add(GfTextEntry(name, entry, at, options));

                if (entry.MaxLength > 0)
                {
                    Unsupported(body, LayoutTextEntry(entry, at, textIndex: 0));
                }

                break;

            case LabelElement label:
                body.Add(GfTextLine(name, label, at, options));

                // GFTextLine has no crop rectangle.
                if (label.Cropped)
                {
                    Unsupported(body, LayoutCroppedText(label, at, textIndex: 0));
                }

                break;

            case AlphaElement alpha:
                body.Add(Invariant(
                    $"GFAddAlphaRegion({name}, {at.X}, {at.Y}, {alpha.Width}, {alpha.Height});"));
                break;

            case BackgroundElement background:
                body.Add(Concat(Invariant($"GFResizePic({name}, {at.X}, {at.Y}, {background.GumpId}, "), Invariant($"{background.Width}, {background.Height});")));
                break;

            case ImageElement image:
                body.Add(Invariant(
                    $"GFGumpPic({name}, {at.X}, {at.Y}, {image.GumpId}, {image.Hue});"));

                // GFGumpPic always applies a full tint.
                if (image.PartialHue && image.Hue != 0)
                {
                    Unsupported(body, LayoutGumpPic(image, at));
                }

                break;

            case PicInPicElement pic:
                Unsupported(body, LayoutPicInPic(pic, at));
                break;

            case TileAsGumpElement tile:
                Unsupported(body, LayoutTileAsGump(tile, at));
                break;

            case ItemElement item:
                body.Add(Invariant(
                    $"GFTilePic({name}, {at.X}, {at.Y}, {item.ItemId}, {item.Hue});"));
                break;

            case TiledElement tiled:
                Unsupported(body, LayoutGumpPicTiled(tiled, at));
                break;

            case ButtonElement button:
                body.Add(Concat(Invariant($"GFAddButton({name}, {at.X}, {at.Y}, {button.NormalId}, {button.PressedId}, "), Invariant($"{(button.Kind == ButtonKind.Page ? "GF_PAGE_BTN" : "GF_CLOSE_BTN")}, "), Invariant($"{button.Param});")));

                if (button.TileId != 0)
                {
                    Unsupported(body, LayoutButton(button, at));
                }

                break;

            case RadioElement radio:
                if (radio.GroupId != radioGroup)
                {
                    body.Add(Invariant($"GFSetRadioGroup({name}, {radio.GroupId});"));
                    radioGroup = radio.GroupId;
                }

                body.Add(Concat(Invariant($"GFRadioButton({name}, {at.X}, {at.Y}, {radio.UncheckedId}, {radio.CheckedId}, "), Invariant($"{Flag(radio.IsChecked)}, {radio.Value});")));
                break;

            case CheckboxElement checkbox:
                body.Add(Concat(Invariant($"GFCheckBox({name}, {at.X}, {at.Y}, {checkbox.UncheckedId}, {checkbox.CheckedId}, "), Invariant($"{Flag(checkbox.IsChecked)}, {checkbox.GroupId});")));
                break;

            case GroupElement:
                // Leaves() never yields one; groups are an editor construct.
                break;

            default:
                throw new NotSupportedException(
                    $"No POL gump-package output for element type '{element.TypeName}'.");
        }
    }

    private static string BuildLayoutStrings(
        GumpDocument document, string name, PolExportOptions options, DateTimeOffset? timestamp)
    {
        List<string> layout = [];
        List<string> texts = [];

        if (!document.Properties.Movable)
        {
            layout.Add("NoMove");
        }

        if (!document.Properties.Closable)
        {
            layout.Add("NoClose");
        }

        if (!document.Properties.Disposable)
        {
            layout.Add("NoDispose");
        }

        layout.AddRange(GumpLevelTokens(document.Properties));

        for (int page = 0; page < document.PageCount; page++)
        {
            // `page` resets the client's current group, so the tracker resets with
            // it. See the matching comment in the gump-package path.
            int radioGroup = -1;

            layout.Add(Invariant($"page {page}"));

            foreach (Element element in document.Pages[page].Leaves())
            {
                AppendLayoutElement(layout, texts, element, options, ref radioGroup);
                AppendTooltip(layout, element, static (list, line) => list.Add(line));
            }

            if (radioGroup > 0)
            {
                // Without this every radio placed after the last group on the page
                // would silently join it.
                layout.Add("endgroup");
            }
        }

        StringBuilder script = new();

        AppendHeader(script, timestamp, null);
        script.AppendLine("use uo;");
        script.AppendLine("use os;");
        script.AppendLine();
        script.AppendLine(CultureInfo.InvariantCulture, $"program gump_{name}(who)");
        script.AppendLine();

        AppendArray(script, "gump", layout);
        AppendArray(script, "data", texts);

        script.AppendLine();

        GumpPoint location = document.Properties.Location;
        string trailing = location is { X: 0, Y: 0 }
            ? string.Empty
            : Invariant($", {location.X}, {location.Y}");

        script.AppendLine(CultureInfo.InvariantCulture, $"\tSendDialogGump(who, gump, data{trailing});");
        script.AppendLine();
        script.AppendLine("endprogram");

        return script.ToString();
    }

    private static void AppendLayoutElement(
        List<string> layout,
        List<string> texts,
        Element element,
        PolExportOptions options,
        ref int radioGroup)
    {
        GumpPoint at = element.GetAbsolutePosition();

        switch (element)
        {
            case HtmlElement html when html.ContentKind == HtmlContentKind.Html:
                layout.Add(Concat(Invariant($"htmlgump {at.X} {at.Y} {html.Width} {html.Height} "), Invariant($"{AddText(texts, html.Html, "HtmlGump", options)} "), Invariant($"{Flag(html.ShowBackground)} {Flag(html.ShowScrollbar)}")));
                break;

            case HtmlElement html:
                layout.Add(LayoutLocalizedHtml(html, at));
                break;

            case TextEntryElement entry:
                layout.Add(LayoutTextEntry(
                    entry, at, AddText(texts, entry.InitialText, "TextEntry", options)));
                break;

            case LabelElement label:
                layout.Add(label.Cropped
                    ? LayoutCroppedText(label, at, AddText(texts, label.Text, "Text", options))
                    : Concat(Invariant($"text {at.X} {at.Y} {label.Hue} "), Invariant($"{AddText(texts, label.Text, "Text", options)}")));
                break;

            case AlphaElement alpha:
                layout.Add(Invariant(
                    $"checkertrans {at.X} {at.Y} {alpha.Width} {alpha.Height}"));
                break;

            case BackgroundElement background:
                layout.Add(Concat(Invariant($"resizepic {at.X} {at.Y} {background.GumpId} "), Invariant($"{background.Width} {background.Height}")));
                break;

            case ImageElement image:
                layout.Add(LayoutGumpPic(image, at));
                break;

            case PicInPicElement pic:
                layout.Add(LayoutPicInPic(pic, at));
                break;

            case TileAsGumpElement tile:
                layout.Add(LayoutTileAsGump(tile, at));
                break;

            case ItemElement item:
                layout.Add(item.Hue != 0
                    ? Invariant($"tilepichue {at.X} {at.Y} {item.ItemId} {item.Hue}")
                    : Invariant($"tilepic {at.X} {at.Y} {item.ItemId}"));
                break;

            case TiledElement tiled:
                layout.Add(LayoutGumpPicTiled(tiled, at));
                break;

            case ButtonElement button:
                layout.Add(LayoutButton(button, at));
                break;

            case RadioElement radio:
                if (radio.GroupId != radioGroup)
                {
                    layout.Add(Invariant($"group {radio.GroupId}"));
                    radioGroup = radio.GroupId;
                }

                layout.Add(Concat(Invariant($"radio {at.X} {at.Y} {radio.UncheckedId} {radio.CheckedId} "), Invariant($"{Flag(radio.IsChecked)} {radio.Value}")));
                break;

            case CheckboxElement checkbox:
                layout.Add(Concat(Invariant($"checkbox {at.X} {at.Y} {checkbox.UncheckedId} {checkbox.CheckedId} "), Invariant($"{Flag(checkbox.IsChecked)} {checkbox.GroupId}")));
                break;

            case GroupElement:
                break;

            default:
                throw new NotSupportedException(
                    $"No POL layout-string output for element type '{element.TypeName}'.");
        }
    }

    /// <summary>The commands set by gump-level flags rather than by an element.</summary>
    private static IEnumerable<string> GumpLevelTokens(GumpProperties properties)
    {
        if (properties.MasterGumpId != 0)
        {
            yield return Invariant($"mastergump {properties.MasterGumpId}");
        }

        // These three are parser toggles: emitting one flips it for the rest of
        // the definition, which is why they carry no argument and appear once.
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
    /// Emits the tooltip commands attached to an element.
    /// </summary>
    /// <remarks>
    /// Both attach to whichever element the client created last, so they must
    /// follow their element's own command immediately.
    /// </remarks>
    private static void AppendTooltip(List<string> lines, Element element, Action<List<string>, string> add)
    {
        if (element.TooltipClilocId != 0)
        {
            add(lines, element.TooltipArguments.Length > 0
                ? Invariant($"tooltip {element.TooltipClilocId} @{element.TooltipArguments}@")
                : Invariant($"tooltip {element.TooltipClilocId}"));
        }

        if (element.ItemPropertySerial != 0)
        {
            add(lines, Invariant($"itemproperty {element.ItemPropertySerial}"));
        }
    }

    /// <summary>Records a layout command the gump package has no function for.</summary>
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

    private static void AppendComment(List<string> body, Element element, PolExportOptions options)
    {
        if (!options.IncludeComments && !options.IncludeNames)
        {
            return;
        }

        string comment = options.IncludeComments ? element.Comment : string.Empty;
        string label = options.IncludeNames
            ? element.Name + (comment.Length > 0 ? ": " : string.Empty)
            : string.Empty;

        string text = label + comment;

        if (text.Length == 0)
        {
            return;
        }

        body.Add(string.Empty);
        body.Add("//" + text);
    }

    /// <summary>Adds a string to the data array and returns its index.</summary>
    private static int AddText(List<string> texts, string value, string placeholder, PolExportOptions options)
    {
        int index = texts.Count;

        texts.Add(string.IsNullOrEmpty(value) && options.PlaceholderText
            ? $"{placeholder} id.{index}"
            : value);

        return index;
    }

    private static string LayoutGumpPicTiled(TiledElement tiled, GumpPoint at) =>
        Invariant($"gumppictiled {at.X} {at.Y} {tiled.Width} {tiled.Height} {tiled.GumpId}");

    /// <summary>
    /// A gump image, hued fully, hued partially, or plain.
    /// </summary>
    /// <remarks>
    /// <c>gumppicphued</c> tints only the grayscale pixels, which is what dyeable
    /// art needs; <c>gumppichued</c> flattens everything to the hue.
    /// </remarks>
    private static string LayoutGumpPic(ImageElement image, GumpPoint at) => image switch
    {
        { Hue: 0 } => Invariant($"gumppic {at.X} {at.Y} {image.GumpId}"),
        { PartialHue: true } => Invariant($"gumppicphued {at.X} {at.Y} {image.GumpId} {image.Hue}"),
        _ => Invariant($"gumppic {at.X} {at.Y} {image.GumpId} {image.Hue}"),
    };

    private static string LayoutPicInPic(PicInPicElement pic, GumpPoint at)
    {
        string command = pic switch
        {
            { Hue: 0 } => "picinpic",
            { PartialHue: true } => "picinpicphued",
            _ => "picinpichued",
        };

        string region = Invariant($"{at.X} {at.Y} {pic.GumpId} {pic.SourceX} {pic.SourceY} {pic.Width} {pic.Height}");

        return pic.Hue == 0 ? $"{command} {region}" : Concat($"{command} {region}", Invariant($" {pic.Hue}"));
    }

    private static string LayoutTileAsGump(TileAsGumpElement tile, GumpPoint at) =>
        Invariant($"tilepicasgumppic {at.X} {at.Y} {tile.ItemId} {tile.LinkId} {tile.ParamB} {tile.ParamC}");

    private static string LayoutCroppedText(LabelElement label, GumpPoint at, int textIndex) =>
        Concat(Invariant($"croppedtext {at.X} {at.Y} {label.Width} {label.Height} "), Invariant($"{label.Hue} {textIndex}"));

    private static string LayoutTextEntry(TextEntryElement entry, GumpPoint at, int textIndex) =>
        entry.MaxLength > 0
            ? Concat(Invariant($"textentrylimited {at.X} {at.Y} {entry.Width} {entry.Height} {entry.Hue} "), Invariant($"{entry.EntryId} {textIndex} {entry.MaxLength}"))
            : Concat(Invariant($"textentry {at.X} {at.Y} {entry.Width} {entry.Height} {entry.Hue} "), Invariant($"{entry.EntryId} {textIndex}"));

    /// <summary>
    /// A localised HTML area, in whichever of its three forms the settings ask for.
    /// </summary>
    /// <remarks>
    /// <c>xmfhtmltok</c> is not <c>xmfhtmlgumpcolor</c> with arguments bolted on:
    /// its background and scrollbar flags come <em>before</em> the colour and its
    /// cliloc id comes last.
    /// </remarks>
    private static string LayoutLocalizedHtml(HtmlElement html, GumpPoint at)
    {
        string rect = Invariant($"{at.X} {at.Y} {html.Width} {html.Height}");
        string flags = Invariant($"{Flag(html.ShowBackground)} {Flag(html.ShowScrollbar)}");

        if (html.Arguments.Length > 0)
        {
            return Concat(
                $"xmfhtmltok {rect} {flags} ",
                Invariant($"{html.Color} {html.ClilocId} @{html.Arguments}@"));
        }

        return html.Color != 0
            ? Concat(Invariant($"xmfhtmlgumpcolor {rect} {html.ClilocId} {flags} "), Invariant($"{html.Color}"))
            : Invariant($"xmfhtmlgump {rect} {html.ClilocId} {flags}");
    }

    /// <summary>
    /// A button, or a button with tile art overlaid on it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The slots are <c>quit</c>, <c>page-id</c>, <c>return-value</c>. The
    /// original got all three wrong and its source says so: the field carries a
    /// <c>// TODO: Page or Reply???</c> comment. It emitted <c>quit</c> inverted,
    /// put a page button's target page in the return-value slot, and a reply
    /// button's return value in the page slot — so a page button closed the gump
    /// and a reply button jumped to a page numbered after its reply id.
    /// </para>
    /// <para>
    /// The layout here is what both the POL command reference and the client's
    /// own parser describe, and it matches what RunUO emits.
    /// </para>
    /// </remarks>
    private static string LayoutButton(ButtonElement button, GumpPoint at)
    {
        bool isPage = button.Kind == ButtonKind.Page;

        int quit = isPage ? 0 : 1;
        int pageId = isPage ? button.Param : 0;
        int returnValue = isPage ? 0 : button.Param;

        string command = button.TileId != 0 ? "buttontileart" : "button";
        string line = Concat(
            Invariant($"{command} {at.X} {at.Y} {button.NormalId} {button.PressedId} "),
            Invariant($"{quit} {pageId} {returnValue}"));

        return button.TileId != 0
            ? Concat(line, Invariant($" {button.TileId} {button.TileHue} {button.TileX} {button.TileY}"))
            : line;
    }

    private static string GfTextLine(
        string name, LabelElement label, GumpPoint at, PolExportOptions options)
    {
        string text = Resolve(label.Text, "TextLine", options);

        return Invariant($"GFTextLine({name}, {at.X}, {at.Y}, {label.Hue}, \"{Escape(text)}\");");
    }

    private static string GfTextEntry(
        string name, TextEntryElement entry, GumpPoint at, PolExportOptions options)
    {
        string text = Resolve(entry.InitialText, "TextEntry", options);

        return Concat(Invariant($"GFTextEntry({name}, {at.X}, {at.Y}, {entry.Width}, {entry.Height}, {entry.Hue}, "), Invariant($"\"{Escape(text)}\", {entry.EntryId});"));
    }

    private static string GfHtmlArea(
        string name, HtmlElement html, GumpPoint at, PolExportOptions options)
    {
        string text = Escape(Resolve(html.Html, "HtmlElement", options));

        return html.ShowScrollbar || html.ShowBackground
            ? Concat(Invariant($"GFHTMLArea({name}, {at.X}, {at.Y}, {html.Width}, {html.Height}, \"{text}\", "), Invariant($"{Flag(html.ShowBackground)}, {Flag(html.ShowScrollbar)});"))
            : Invariant($"GFHTMLArea({name}, {at.X}, {at.Y}, {html.Width}, {html.Height}, \"{text}\");");
    }

    private static string GfHtmlLocalized(string name, HtmlElement html, GumpPoint at) =>
        html.ShowScrollbar || html.ShowBackground
            ? Concat(Invariant($"GFAddHTMLLocalized({name}, {at.X}, {at.Y}, {html.Width}, {html.Height}, {html.ClilocId}, "), Invariant($"{Flag(html.ShowBackground)}, {Flag(html.ShowScrollbar)});"))
            : Invariant(
                $"GFAddHTMLLocalized({name}, {at.X}, {at.Y}, {html.Width}, {html.Height}, {html.ClilocId});");

    private static string Resolve(string value, string placeholder, PolExportOptions options) =>
        string.IsNullOrEmpty(value) && options.PlaceholderText ? placeholder : value;

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
