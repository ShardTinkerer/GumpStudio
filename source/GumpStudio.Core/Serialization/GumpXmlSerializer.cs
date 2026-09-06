using System.Globalization;
using System.Xml;
using System.Xml.Linq;

using GumpStudio.Core.Document;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Primitives;

namespace GumpStudio.Core.Serialization;

/// <summary>
/// Reads and writes the <c>.gump</c> document format.
/// </summary>
/// <remarks>
/// <para>
/// Hand-written XML over an explicit element-name mapping, deliberately not
/// <c>XmlSerializer</c> reflecting over the live classes. The old format was
/// <c>BinaryFormatter</c>, which bound the file to CLR type identity — renaming a
/// class broke every saved document, and the format could not be read at all on
/// modern .NET.
/// </para>
/// <para>
/// Unknown elements and attributes are preserved in spirit by being ignored
/// rather than fatal, so a file written by a newer build still opens.
/// </para>
/// </remarks>
public static class GumpXmlSerializer
{
    /// <summary>Format version written to new files.</summary>
    public const int CurrentVersion = 4;

    private const string RootName = "gump";

    /// <summary>Maps the stable on-disk type name to a factory.</summary>
    private static readonly Dictionary<string, Func<Element>> Factories =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Group"] = () => new GroupElement(),
            ["Alpha"] = () => new AlphaElement(),
            ["Background"] = () => new BackgroundElement(),
            ["Button"] = () => new ButtonElement(),
            ["Checkbox"] = () => new CheckboxElement(),
            ["Radio"] = () => new RadioElement(),
            ["Html"] = () => new HtmlElement(),
            ["Image"] = () => new ImageElement(),
            ["Item"] = () => new ItemElement(),
            ["Label"] = () => new LabelElement(),
            ["PicInPic"] = () => new PicInPicElement(),
            ["TileAsGump"] = () => new TileAsGumpElement(),
            ["TextEntry"] = () => new TextEntryElement(),
            ["Tiled"] = () => new TiledElement(),
        };

    public static void Save(GumpDocument document, string path)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(path);

        XmlWriterSettings settings = new()
        {
            Indent = true,
            IndentChars = "  ",
            Encoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };

        using XmlWriter writer = XmlWriter.Create(path, settings);

        ToXml(document).WriteTo(writer);
    }

    public static GumpDocument Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        // DTD processing off and no resolver: a document file is untrusted input.
        XmlReaderSettings settings = new()
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreWhitespace = true,
            IgnoreComments = true,
        };

        using XmlReader reader = XmlReader.Create(path, settings);

        return FromXml(XDocument.Load(reader));
    }

    /// <summary>Root element of a loose group of elements, as put on the clipboard.</summary>
    private const string FragmentName = "gumpstudio-elements";

    /// <summary>
    /// Serialises a loose set of elements, for the clipboard.
    /// </summary>
    /// <remarks>
    /// The same writer the document format uses, so a copied element carries
    /// everything a saved one does and nothing has to be kept in step separately.
    /// Plain text on the clipboard rather than a binary blob: it survives between
    /// instances, can be pasted into an editor to be read, and — unlike the
    /// original's <c>BinaryFormatter</c> payload — cannot execute anything.
    /// </remarks>
    public static string ToFragment(IEnumerable<Element> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);

        XElement root = new(FragmentName, new XAttribute("version", CurrentVersion));

        foreach (Element element in elements)
        {
            root.Add(WriteElement(element));
        }

        return root.ToString();
    }

    /// <summary>
    /// Reads elements back from a clipboard fragment.
    /// </summary>
    /// <returns>
    /// The elements, or an empty list when the text is not one of ours — the
    /// clipboard holds arbitrary text and a failed paste must not be an error.
    /// </returns>
    public static IReadOnlyList<Element> FromFragment(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        XElement root;

        try
        {
            root = XElement.Parse(text, LoadOptions.None);
        }
        catch (System.Xml.XmlException)
        {
            return [];
        }

        if (!string.Equals(root.Name.LocalName, FragmentName, StringComparison.OrdinalIgnoreCase)
            || ReadInt(root, "version", CurrentVersion) > CurrentVersion)
        {
            return [];
        }

        List<Element> elements = [];

        foreach (XElement node in root.Elements())
        {
            if (ReadElement(node) is { } element)
            {
                elements.Add(element);
            }
        }

        return elements;
    }

    /// <summary>Serialises to an in-memory document, for tests and round-tripping.</summary>
    public static XDocument ToXml(GumpDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        XElement root = new(
            RootName,
            new XAttribute("version", CurrentVersion),
            WriteProperties(document.Properties));

        foreach (GumpPage page in document.Pages)
        {
            XElement pageElement = new("page");

            if (!string.IsNullOrEmpty(page.Name))
            {
                pageElement.SetAttributeValue("name", page.Name);
            }

            foreach (Element child in page.Root.Children)
            {
                pageElement.Add(WriteElement(child));
            }

            root.Add(pageElement);
        }

        return new XDocument(root);
    }

    /// <summary>Deserialises from an in-memory document.</summary>
    public static GumpDocument FromXml(XDocument xml)
    {
        ArgumentNullException.ThrowIfNull(xml);

        XElement root = xml.Root
            ?? throw new InvalidDataException("The document is empty.");

        if (!string.Equals(root.Name.LocalName, RootName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Expected a <{RootName}> root element but found <{root.Name.LocalName}>.");
        }

        int version = ReadInt(root, "version", CurrentVersion);

        if (version > CurrentVersion)
        {
            throw new InvalidDataException(
                $"This file was written by a newer version of GumpStudio (format {version}; "
                + $"this build understands up to {CurrentVersion}).");
        }

        GumpDocument document = new();

        // The constructor seeds one page; the file supplies the real set.
        List<GumpPage> pages = [];

        if (root.Element("properties") is { } properties)
        {
            document.Properties = ReadProperties(properties);
        }

        foreach (XElement pageElement in root.Elements("page"))
        {
            GumpPage page = new((string?)pageElement.Attribute("name"));

            foreach (XElement child in pageElement.Elements())
            {
                if (ReadElement(child) is { } element)
                {
                    page.Root.Add(element);
                }
            }

            pages.Add(page);
        }

        if (pages.Count == 0)
        {
            pages.Add(new GumpPage("Page 0"));
        }

        document.ReplacePages(pages);

        return document;
    }

    private static XElement WriteProperties(GumpProperties properties) =>
        new(
            "properties",
            new XAttribute("x", properties.Location.X),
            new XAttribute("y", properties.Location.Y),
            new XAttribute("movable", properties.Movable),
            new XAttribute("closable", properties.Closable),
            new XAttribute("disposable", properties.Disposable),
            new XAttribute("typeId", properties.TypeId),
            new XAttribute("masterGumpId", properties.MasterGumpId),
            new XAttribute("upperWordCase", properties.UpperWordCase),
            new XAttribute("croppedText", properties.CroppedText),
            new XAttribute("echandleInput", properties.EnhancedClientInput));

    private static GumpProperties ReadProperties(XElement element) => new()
    {
        Location = new GumpPoint(ReadInt(element, "x", 0), ReadInt(element, "y", 0)),
        Movable = ReadBool(element, "movable", true),
        Closable = ReadBool(element, "closable", true),
        Disposable = ReadBool(element, "disposable", true),
        TypeId = ReadInt(element, "typeId", 0),
        MasterGumpId = ReadInt(element, "masterGumpId", 0),
        UpperWordCase = ReadBool(element, "upperWordCase", false),
        CroppedText = ReadBool(element, "croppedText", false),
        EnhancedClientInput = ReadBool(element, "echandleInput", false),
    };

    private static XElement WriteElement(Element element)
    {
        XElement node = new(element.TypeName.ToLowerInvariant());

        if (!string.IsNullOrEmpty(element.Name))
        {
            node.SetAttributeValue("name", element.Name);
        }

        node.SetAttributeValue("x", element.X);
        node.SetAttributeValue("y", element.Y);

        if (element.IsResizable)
        {
            node.SetAttributeValue("w", element.Width);
            node.SetAttributeValue("h", element.Height);
        }

        if (!string.IsNullOrEmpty(element.Comment))
        {
            node.SetAttributeValue("comment", element.Comment);
        }

        // Written only when set, so a document with no tooltips looks exactly as
        // it did before these attributes existed.
        if (element.TooltipClilocId != 0)
        {
            node.SetAttributeValue("tooltip", element.TooltipClilocId);
        }

        if (!string.IsNullOrEmpty(element.TooltipArguments))
        {
            node.SetAttributeValue("tooltipArgs", element.TooltipArguments);
        }

        if (element.ItemPropertySerial != 0)
        {
            node.SetAttributeValue("itemProperty", element.ItemPropertySerial);
        }

        WriteSpecific(element, node);

        return node;
    }

    private static void WriteSpecific(Element element, XElement node)
    {
        switch (element)
        {
            case GroupElement group:
                // A group's size is derived from its children, so writing it
                // would persist a value that is stale the moment anything moves.
                foreach (Element child in group.Children)
                {
                    node.Add(WriteElement(child));
                }

                break;

            case AlphaElement:
                break;

            case BackgroundElement background:
                node.SetAttributeValue("gumpId", background.GumpId);
                break;

            case TiledElement tiled:
                node.SetAttributeValue("gumpId", tiled.GumpId);
                node.SetAttributeValue("hue", tiled.Hue);
                break;

            case ImageElement image:
                node.SetAttributeValue("gumpId", image.GumpId);
                node.SetAttributeValue("hue", image.Hue);
                node.SetAttributeValue("partialHue", image.PartialHue);
                break;

            case PicInPicElement pic:
                node.SetAttributeValue("gumpId", pic.GumpId);
                node.SetAttributeValue("sourceX", pic.SourceX);
                node.SetAttributeValue("sourceY", pic.SourceY);
                node.SetAttributeValue("hue", pic.Hue);
                node.SetAttributeValue("partialHue", pic.PartialHue);
                break;

            case TileAsGumpElement tile:
                node.SetAttributeValue("itemId", tile.ItemId);
                node.SetAttributeValue("linkId", tile.LinkId);
                node.SetAttributeValue("paramB", tile.ParamB);
                node.SetAttributeValue("paramC", tile.ParamC);
                break;

            case ItemElement item:
                node.SetAttributeValue("itemId", item.ItemId);
                node.SetAttributeValue("hue", item.Hue);
                break;

            case ButtonElement button:
                node.SetAttributeValue("normalId", button.NormalId);
                node.SetAttributeValue("pressedId", button.PressedId);
                node.SetAttributeValue("kind", button.Kind);
                node.SetAttributeValue("param", button.Param);
                node.SetAttributeValue("tileId", button.TileId);
                node.SetAttributeValue("tileHue", button.TileHue);
                node.SetAttributeValue("tileX", button.TileX);
                node.SetAttributeValue("tileY", button.TileY);

                if (!string.IsNullOrEmpty(button.CodeBehind))
                {
                    node.Add(new XElement("codeBehind", button.CodeBehind));
                }

                break;

            // Radio must precede Checkbox: it derives from it.
            case RadioElement radio:
                WriteCheckbox(radio, node);
                node.SetAttributeValue("value", radio.Value);
                break;

            case CheckboxElement checkbox:
                WriteCheckbox(checkbox, node);
                break;

            case HtmlElement html:
                node.SetAttributeValue("kind", html.ContentKind);
                node.SetAttributeValue("clilocId", html.ClilocId);
                node.SetAttributeValue("scrollbar", html.ShowScrollbar);
                node.SetAttributeValue("background", html.ShowBackground);
                node.SetAttributeValue("color", html.Color);

                if (!string.IsNullOrEmpty(html.Arguments))
                {
                    node.SetAttributeValue("args", html.Arguments);
                }

                if (!string.IsNullOrEmpty(html.Html))
                {
                    node.Add(new XElement("html", html.Html));
                }

                break;

            case LabelElement label:
                node.SetAttributeValue("hue", label.Hue);
                node.SetAttributeValue("font", label.FontIndex);
                node.SetAttributeValue("cropped", label.Cropped);
                node.Add(new XElement("text", label.Text));
                break;

            case TextEntryElement entry:
                node.SetAttributeValue("hue", entry.Hue);
                node.SetAttributeValue("entryId", entry.EntryId);
                node.SetAttributeValue("maxLength", entry.MaxLength);

                if (!string.IsNullOrEmpty(entry.InitialText))
                {
                    node.Add(new XElement("text", entry.InitialText));
                }

                break;

            default:
                throw new NotSupportedException($"No serializer for element type '{element.TypeName}'.");
        }
    }

    private static void WriteCheckbox(CheckboxElement checkbox, XElement node)
    {
        node.SetAttributeValue("checkedId", checkbox.CheckedId);
        node.SetAttributeValue("uncheckedId", checkbox.UncheckedId);
        node.SetAttributeValue("checked", checkbox.IsChecked);
        node.SetAttributeValue("groupId", checkbox.GroupId);
    }

    private static Element? ReadElement(XElement node)
    {
        // An unrecognised element is skipped rather than fatal, so a document
        // written by a newer build still opens.
        if (!Factories.TryGetValue(node.Name.LocalName, out Func<Element>? factory))
        {
            return null;
        }

        Element element = factory();

        element.Name = (string?)node.Attribute("name") ?? element.TypeName;
        element.Comment = (string?)node.Attribute("comment") ?? string.Empty;
        element.Location = new GumpPoint(ReadInt(node, "x", 0), ReadInt(node, "y", 0));
        element.TooltipClilocId = ReadInt(node, "tooltip", 0);
        element.TooltipArguments = (string?)node.Attribute("tooltipArgs") ?? string.Empty;
        element.ItemPropertySerial = ReadInt(node, "itemProperty", 0);

        // Before the size: a label only becomes resizable once it is cropped, so
        // reading w/h first would drop the crop rectangle on the floor.
        ReadResizeGate(element, node);

        if (element.IsResizable)
        {
            element.Size = new GumpSize(ReadInt(node, "w", 1), ReadInt(node, "h", 1));
        }

        ReadSpecific(element, node);

        return element;
    }

    private static void ReadSpecific(Element element, XElement node)
    {
        switch (element)
        {
            case GroupElement group:
                foreach (XElement child in node.Elements())
                {
                    if (ReadElement(child) is { } nested)
                    {
                        group.Add(nested);
                    }
                }

                break;

            case AlphaElement:
                break;

            case BackgroundElement background:
                background.GumpId = ReadInt(node, "gumpId", background.GumpId);
                break;

            case TiledElement tiled:
                tiled.GumpId = ReadInt(node, "gumpId", tiled.GumpId);
                tiled.Hue = ReadInt(node, "hue", 0);
                break;

            case ImageElement image:
                image.GumpId = ReadInt(node, "gumpId", image.GumpId);
                image.Hue = ReadInt(node, "hue", 0);
                image.PartialHue = ReadBool(node, "partialHue", false);
                break;

            case PicInPicElement pic:
                pic.GumpId = ReadInt(node, "gumpId", pic.GumpId);
                pic.SourceX = ReadInt(node, "sourceX", 0);
                pic.SourceY = ReadInt(node, "sourceY", 0);
                pic.Hue = ReadInt(node, "hue", 0);
                pic.PartialHue = ReadBool(node, "partialHue", false);
                break;

            case TileAsGumpElement tile:
                tile.ItemId = ReadInt(node, "itemId", tile.ItemId);
                tile.LinkId = ReadInt(node, "linkId", 0);
                tile.ParamB = ReadInt(node, "paramB", 0);
                tile.ParamC = ReadInt(node, "paramC", 0);
                break;

            case ItemElement item:
                item.ItemId = ReadInt(node, "itemId", item.ItemId);
                item.Hue = ReadInt(node, "hue", 0);
                break;

            case ButtonElement button:
                button.NormalId = ReadInt(node, "normalId", button.NormalId);
                button.PressedId = ReadInt(node, "pressedId", button.PressedId);
                button.Kind = ReadEnum(node, "kind", ButtonKind.Reply);
                button.Param = ReadInt(node, "param", 0);
                button.TileId = ReadInt(node, "tileId", 0);
                button.TileHue = ReadInt(node, "tileHue", 0);
                button.TileX = ReadInt(node, "tileX", 0);
                button.TileY = ReadInt(node, "tileY", 0);
                button.CodeBehind = (string?)node.Element("codeBehind") ?? string.Empty;
                break;

            case RadioElement radio:
                ReadCheckbox(radio, node);
                radio.Value = ReadInt(node, "value", 0);
                break;

            case CheckboxElement checkbox:
                ReadCheckbox(checkbox, node);
                break;

            case HtmlElement html:
                html.ContentKind = ReadEnum(node, "kind", HtmlContentKind.Html);
                html.ClilocId = ReadInt(node, "clilocId", html.ClilocId);
                html.ShowScrollbar = ReadBool(node, "scrollbar", false);
                html.ShowBackground = ReadBool(node, "background", false);
                html.Color = ReadInt(node, "color", 0);
                html.Arguments = (string?)node.Attribute("args") ?? string.Empty;
                html.Html = (string?)node.Element("html") ?? string.Empty;
                break;

            case LabelElement label:
                label.Hue = ReadInt(node, "hue", 0);
                label.FontIndex = ReadInt(node, "font", 0);
                label.Text = (string?)node.Element("text") ?? string.Empty;
                break;

            case TextEntryElement entry:
                entry.Hue = ReadInt(node, "hue", 0);
                entry.EntryId = ReadInt(node, "entryId", 0);
                entry.MaxLength = ReadInt(node, "maxLength", 0);
                entry.InitialText = (string?)node.Element("text") ?? string.Empty;
                break;

            default:
                throw new NotSupportedException($"No deserializer for element type '{element.TypeName}'.");
        }
    }

    /// <summary>
    /// Applies the properties that decide whether an element is resizable.
    /// </summary>
    /// <remarks>
    /// Only a cropped label qualifies today. Its rectangle can only be assigned
    /// once <c>Cropped</c> is set, because <see cref="Element.Size"/> ignores
    /// writes to an element that is not resizable.
    /// </remarks>
    private static void ReadResizeGate(Element element, XElement node)
    {
        if (element is LabelElement label)
        {
            label.Cropped = ReadBool(node, "cropped", false);
        }
    }

    private static void ReadCheckbox(CheckboxElement checkbox, XElement node)
    {
        checkbox.CheckedId = ReadInt(node, "checkedId", checkbox.CheckedId);
        checkbox.UncheckedId = ReadInt(node, "uncheckedId", checkbox.UncheckedId);
        checkbox.GroupId = ReadInt(node, "groupId", 0);

        // Set last: a radio's setter clears its siblings, so the group must be
        // known first.
        checkbox.IsChecked = ReadBool(node, "checked", false);
    }

    private static int ReadInt(XElement node, string name, int fallback) =>
        node.Attribute(name) is { } attribute
        && int.TryParse(attribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : fallback;

    private static bool ReadBool(XElement node, string name, bool fallback) =>
        node.Attribute(name) is { } attribute && bool.TryParse(attribute.Value, out bool value)
            ? value
            : fallback;

    private static TEnum ReadEnum<TEnum>(XElement node, string name, TEnum fallback)
        where TEnum : struct, Enum =>
        node.Attribute(name) is { } attribute
        && Enum.TryParse(attribute.Value, ignoreCase: true, out TEnum value)
            ? value
            : fallback;
}
