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
/// three corrections:
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
/// </list>
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

        int radioGroup = -1;

        for (int page = 0; page < document.PageCount; page++)
        {
            if (page > 0)
            {
                body.Add(string.Empty);
            }

            body.Add(Invariant($"GFPage({name}, {page});"));

            foreach (Element element in document.Pages[page].Leaves())
            {
                AppendComment(body, element, options);

                GumpPoint at = element.GetAbsolutePosition();

                switch (element)
                {
                    case HtmlElement html when html.ContentKind == HtmlContentKind.Html:
                        body.Add(GfHtmlArea(name, html, at, options));
                        break;

                    case HtmlElement html:
                        body.Add(GfHtmlLocalized(name, html, at));
                        break;

                    case TextEntryElement entry:
                        body.Add(GfTextEntry(name, entry, at, options));
                        break;

                    case LabelElement label:
                        body.Add(GfTextLine(name, label, at, options));
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
                        break;

                    case ItemElement item:
                        body.Add(Invariant(
                            $"GFTilePic({name}, {at.X}, {at.Y}, {item.ItemId}, {item.Hue});"));
                        break;

                    case TiledElement tiled:
                        // The gump package has no tiled-image call, so the original
                        // emitted the layout-string form commented out. Preserved.
                        body.Add(string.Empty);
                        body.Add("//Gump package does not support GumpPicTiled");
                        body.Add("//" + LayoutGumpPicTiled(tiled, at));
                        body.Add(string.Empty);
                        break;

                    case ButtonElement button:
                        body.Add(Concat(Invariant($"GFAddButton({name}, {at.X}, {at.Y}, {button.NormalId}, {button.PressedId}, "), Invariant($"{(button.Kind == ButtonKind.Page ? "GF_PAGE_BTN" : "GF_CLOSE_BTN")}, "), Invariant($"{button.Param});")));
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

                    default:
                        break;
                }
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

        int radioGroup = -1;

        for (int page = 0; page < document.PageCount; page++)
        {
            layout.Add(Invariant($"page {page}"));

            foreach (Element element in document.Pages[page].Leaves())
            {
                GumpPoint at = element.GetAbsolutePosition();

                switch (element)
                {
                    case HtmlElement html when html.ContentKind == HtmlContentKind.Html:
                        layout.Add(Concat(Invariant($"htmlgump {at.X} {at.Y} {html.Width} {html.Height} "), Invariant($"{AddText(texts, html.Html, "HtmlGump", options)} "), Invariant($"{Flag(html.ShowBackground)} {Flag(html.ShowScrollbar)}")));
                        break;

                    case HtmlElement html:
                        layout.Add(Concat(Invariant($"xmfhtmlgump {at.X} {at.Y} {html.Width} {html.Height} {html.ClilocId} "), Invariant($"{Flag(html.ShowBackground)} {Flag(html.ShowScrollbar)}")));
                        break;

                    case TextEntryElement entry:
                        layout.Add(Concat(Invariant($"textentry {at.X} {at.Y} {entry.Width} {entry.Height} {entry.Hue} "), Invariant($"{entry.EntryId} {AddText(texts, entry.InitialText, "TextEntry", options)}")));
                        break;

                    case LabelElement label:
                        layout.Add(Concat(Invariant($"text {at.X} {at.Y} {label.Hue} "), Invariant($"{AddText(texts, label.Text, "Text", options)}")));
                        break;

                    case AlphaElement alpha:
                        layout.Add(Invariant(
                            $"checkertrans {at.X} {at.Y} {alpha.Width} {alpha.Height}"));
                        break;

                    case BackgroundElement background:
                        layout.Add(Concat(Invariant($"resizepic {at.X} {at.Y} {background.GumpId} "), Invariant($"{background.Width} {background.Height}")));
                        break;

                    case ImageElement image:
                        layout.Add(image.Hue != 0
                            ? Invariant($"gumppic {at.X} {at.Y} {image.GumpId} {image.Hue}")
                            : Invariant($"gumppic {at.X} {at.Y} {image.GumpId}"));
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

                    default:
                        break;
                }
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

    private static string LayoutButton(ButtonElement button, GumpPoint at)
    {
        // A page button carries its target in the page slot; a reply button
        // carries its id in the reply slot and leaves the page slot at zero.
        bool isPage = button.Kind == ButtonKind.Page;

        int pageSlot = isPage ? button.Param : 0;
        int replySlot = isPage ? 0 : button.Param;

        return Concat(Invariant($"button {at.X} {at.Y} {button.NormalId} {button.PressedId} "), Invariant($"{Flag(isPage)} {replySlot} {pageSlot}"));
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
