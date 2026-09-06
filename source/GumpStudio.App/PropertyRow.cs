using System.Globalization;

using GumpStudio.Core.Commands;
using GumpStudio.Core.Elements;

namespace GumpStudio.App;

/// <summary>What kind of editor a property needs.</summary>
public enum PropertyEditorKind
{
    Text,
    Number,
    Boolean,
    GumpId,
    ItemId,
    Hue,
    Choice,
}

/// <summary>
/// One editable property, described rather than hard-coded per element type.
/// </summary>
/// <remarks>
/// Avalonia has no built-in property grid, and the property set here is small
/// and fixed, so the panel is driven from these descriptors. That also keeps
/// every edit flowing through a command, so it is undoable.
/// </remarks>
public sealed class PropertyRow
{
    private readonly Func<Element, object?> _get;
    private readonly Action<Element, object?> _set;

    private PropertyRow(
        string name,
        PropertyEditorKind kind,
        Func<Element, object?> get,
        Action<Element, object?> set,
        IReadOnlyList<string>? choices = null)
    {
        Name = name;
        Kind = kind;
        _get = get;
        _set = set;
        Choices = choices ?? [];
    }

    public string Name { get; }

    public PropertyEditorKind Kind { get; }

    /// <summary>Options, for <see cref="PropertyEditorKind.Choice"/>.</summary>
    public IReadOnlyList<string> Choices { get; }

    public object? Read(Element element) => _get(element);

    /// <summary>Builds an undoable command that applies a new value.</summary>
    public IUndoableCommand? CreateSetCommand(Element element, object? value)
    {
        object? current = _get(element);

        if (Equals(current, value))
        {
            return null;
        }

        return new SetPropertyCommand<object?>(element, Name, _get, _set, current, value);
    }

    /// <summary>Describes the properties of one element.</summary>
    public static IReadOnlyList<PropertyRow> For(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);

        List<PropertyRow> rows =
        [
            Text("Name", e => e.Name, (e, v) => e.Name = v),
            Integer("X", e => e.X, (e, v) => e.Location = new Core.Primitives.GumpPoint(v, e.Y)),
            Integer("Y", e => e.Y, (e, v) => e.Location = new Core.Primitives.GumpPoint(e.X, v)),
        ];

        if (element.IsResizable)
        {
            rows.Add(Integer("Width", e => e.Width, (e, v) => e.Size = new Core.Primitives.GumpSize(v, e.Height)));
            rows.Add(Integer("Height", e => e.Height, (e, v) => e.Size = new Core.Primitives.GumpSize(e.Width, v)));
        }

        rows.AddRange(Specific(element));

        // Every element can carry a tooltip: the client attaches one to whichever
        // element it created last, which makes it a per-element property.
        rows.Add(Integer("Tooltip cliloc", e => e.TooltipClilocId, (e, v) => e.TooltipClilocId = v));
        rows.Add(Text("Tooltip args", e => e.TooltipArguments, (e, v) => e.TooltipArguments = v));
        rows.Add(Integer("Item property serial",
            e => e.ItemPropertySerial, (e, v) => e.ItemPropertySerial = v));

        rows.Add(Text("Comment", e => e.Comment, (e, v) => e.Comment = v));

        return rows;
    }

    private static IEnumerable<PropertyRow> Specific(Element element)
    {
        switch (element)
        {
            case BackgroundElement:
                yield return Id("Gump id", PropertyEditorKind.GumpId,
                    e => ((BackgroundElement)e).GumpId, (e, v) => ((BackgroundElement)e).GumpId = v);
                break;

            case TiledElement:
                yield return Id("Gump id", PropertyEditorKind.GumpId,
                    e => ((TiledElement)e).GumpId, (e, v) => ((TiledElement)e).GumpId = v);
                yield return Id("Hue", PropertyEditorKind.Hue,
                    e => ((TiledElement)e).Hue, (e, v) => ((TiledElement)e).Hue = v);
                break;

            case ImageElement:
                yield return Id("Gump id", PropertyEditorKind.GumpId,
                    e => ((ImageElement)e).GumpId, (e, v) => ((ImageElement)e).GumpId = v);
                yield return Id("Hue", PropertyEditorKind.Hue,
                    e => ((ImageElement)e).Hue, (e, v) => ((ImageElement)e).Hue = v);
                yield return Boolean("Partial hue",
                    e => ((ImageElement)e).PartialHue, (e, v) => ((ImageElement)e).PartialHue = v);
                break;

            case PicInPicElement:
                yield return Id("Gump id", PropertyEditorKind.GumpId,
                    e => ((PicInPicElement)e).GumpId, (e, v) => ((PicInPicElement)e).GumpId = v);
                yield return Integer("Source X",
                    e => ((PicInPicElement)e).SourceX, (e, v) => ((PicInPicElement)e).SourceX = v);
                yield return Integer("Source Y",
                    e => ((PicInPicElement)e).SourceY, (e, v) => ((PicInPicElement)e).SourceY = v);
                yield return Id("Hue", PropertyEditorKind.Hue,
                    e => ((PicInPicElement)e).Hue, (e, v) => ((PicInPicElement)e).Hue = v);
                yield return Boolean("Partial hue",
                    e => ((PicInPicElement)e).PartialHue, (e, v) => ((PicInPicElement)e).PartialHue = v);
                break;

            case TileAsGumpElement:
                yield return Id("Item id", PropertyEditorKind.ItemId,
                    e => ((TileAsGumpElement)e).ItemId, (e, v) => ((TileAsGumpElement)e).ItemId = v);
                yield return Integer("Link id",
                    e => ((TileAsGumpElement)e).LinkId, (e, v) => ((TileAsGumpElement)e).LinkId = v);
                yield return Integer("Param B",
                    e => ((TileAsGumpElement)e).ParamB, (e, v) => ((TileAsGumpElement)e).ParamB = v);
                yield return Integer("Param C",
                    e => ((TileAsGumpElement)e).ParamC, (e, v) => ((TileAsGumpElement)e).ParamC = v);
                break;

            case ItemElement:
                yield return Id("Item id", PropertyEditorKind.ItemId,
                    e => ((ItemElement)e).ItemId, (e, v) => ((ItemElement)e).ItemId = v);
                yield return Id("Hue", PropertyEditorKind.Hue,
                    e => ((ItemElement)e).Hue, (e, v) => ((ItemElement)e).Hue = v);
                break;

            case LabelElement:
                yield return Text("Text", e => ((LabelElement)e).Text, (e, v) => ((LabelElement)e).Text = v);
                yield return Id("Hue", PropertyEditorKind.Hue,
                    e => ((LabelElement)e).Hue, (e, v) => ((LabelElement)e).Hue = v);
                yield return Integer("Font", e => ((LabelElement)e).FontIndex, (e, v) => ((LabelElement)e).FontIndex = v);
                yield return Boolean("Cropped", e => ((LabelElement)e).Cropped, (e, v) => ((LabelElement)e).Cropped = v);
                break;

            case ButtonElement:
                yield return Id("Normal id", PropertyEditorKind.GumpId,
                    e => ((ButtonElement)e).NormalId, (e, v) => ((ButtonElement)e).NormalId = v);
                yield return Id("Pressed id", PropertyEditorKind.GumpId,
                    e => ((ButtonElement)e).PressedId, (e, v) => ((ButtonElement)e).PressedId = v);
                yield return Choice("Kind", [nameof(ButtonKind.Page), nameof(ButtonKind.Reply)],
                    e => ((ButtonElement)e).Kind.ToString(),
                    (e, v) => ((ButtonElement)e).Kind = Enum.Parse<ButtonKind>(v));
                yield return Integer("Param", e => ((ButtonElement)e).Param, (e, v) => ((ButtonElement)e).Param = v);
                yield return Id("Tile id", PropertyEditorKind.ItemId,
                    e => ((ButtonElement)e).TileId, (e, v) => ((ButtonElement)e).TileId = v);
                yield return Id("Tile hue", PropertyEditorKind.Hue,
                    e => ((ButtonElement)e).TileHue, (e, v) => ((ButtonElement)e).TileHue = v);
                yield return Integer("Tile X",
                    e => ((ButtonElement)e).TileX, (e, v) => ((ButtonElement)e).TileX = v);
                yield return Integer("Tile Y",
                    e => ((ButtonElement)e).TileY, (e, v) => ((ButtonElement)e).TileY = v);
                break;

            case RadioElement:
                foreach (PropertyRow row in CheckboxRows())
                {
                    yield return row;
                }

                yield return Integer("Value", e => ((RadioElement)e).Value, (e, v) => ((RadioElement)e).Value = v);
                break;

            case CheckboxElement:
                foreach (PropertyRow row in CheckboxRows())
                {
                    yield return row;
                }

                break;

            case TextEntryElement:
                yield return Text("Initial text",
                    e => ((TextEntryElement)e).InitialText, (e, v) => ((TextEntryElement)e).InitialText = v);
                yield return Id("Hue", PropertyEditorKind.Hue,
                    e => ((TextEntryElement)e).Hue, (e, v) => ((TextEntryElement)e).Hue = v);
                yield return Integer("Entry id",
                    e => ((TextEntryElement)e).EntryId, (e, v) => ((TextEntryElement)e).EntryId = v);
                yield return Integer("Max length",
                    e => ((TextEntryElement)e).MaxLength, (e, v) => ((TextEntryElement)e).MaxLength = v);
                break;

            case HtmlElement:
                yield return Choice("Content",
                    [nameof(HtmlContentKind.Html), nameof(HtmlContentKind.Localized)],
                    e => ((HtmlElement)e).ContentKind.ToString(),
                    (e, v) => ((HtmlElement)e).ContentKind = Enum.Parse<HtmlContentKind>(v));
                yield return Text("Html", e => ((HtmlElement)e).Html, (e, v) => ((HtmlElement)e).Html = v);
                yield return Integer("Cliloc id",
                    e => ((HtmlElement)e).ClilocId, (e, v) => ((HtmlElement)e).ClilocId = v);
                yield return Boolean("Scrollbar",
                    e => ((HtmlElement)e).ShowScrollbar, (e, v) => ((HtmlElement)e).ShowScrollbar = v);
                yield return Boolean("Background",
                    e => ((HtmlElement)e).ShowBackground, (e, v) => ((HtmlElement)e).ShowBackground = v);
                yield return Integer("Color",
                    e => ((HtmlElement)e).Color, (e, v) => ((HtmlElement)e).Color = v);
                yield return Text("Cliloc args",
                    e => ((HtmlElement)e).Arguments, (e, v) => ((HtmlElement)e).Arguments = v);
                break;

            default:
                break;
        }
    }

    private static IEnumerable<PropertyRow> CheckboxRows()
    {
        yield return Id("Checked id", PropertyEditorKind.GumpId,
            e => ((CheckboxElement)e).CheckedId, (e, v) => ((CheckboxElement)e).CheckedId = v);
        yield return Id("Unchecked id", PropertyEditorKind.GumpId,
            e => ((CheckboxElement)e).UncheckedId, (e, v) => ((CheckboxElement)e).UncheckedId = v);
        yield return Boolean("Checked",
            e => ((CheckboxElement)e).IsChecked, (e, v) => ((CheckboxElement)e).IsChecked = v);
        yield return Integer("Group",
            e => ((CheckboxElement)e).GroupId, (e, v) => ((CheckboxElement)e).GroupId = v);
    }

    private static PropertyRow Text(string name, Func<Element, string> get, Action<Element, string> set) =>
        new(name, PropertyEditorKind.Text,
            e => get(e), (e, v) => set(e, v as string ?? string.Empty));

    private static PropertyRow Integer(string name, Func<Element, int> get, Action<Element, int> set) =>
        new(name, PropertyEditorKind.Number, e => get(e), (e, v) => set(e, ToInt(v)));

    private static PropertyRow Boolean(string name, Func<Element, bool> get, Action<Element, bool> set) =>
        new(name, PropertyEditorKind.Boolean, e => get(e), (e, v) => set(e, v is true));

    private static PropertyRow Id(
        string name, PropertyEditorKind kind, Func<Element, int> get, Action<Element, int> set) =>
        new(name, kind, e => get(e), (e, v) => set(e, ToInt(v)));

    private static PropertyRow Choice(
        string name, IReadOnlyList<string> choices, Func<Element, string> get, Action<Element, string> set) =>
        new(name, PropertyEditorKind.Choice,
            e => get(e), (e, v) => set(e, v as string ?? choices[0]), choices);

    /// <summary>
    /// Parses a value that may arrive as an int or as user-typed text.
    /// </summary>
    /// <remarks>
    /// Hex is accepted because gump and item ids are conventionally written that
    /// way. Parsing is invariant so it does not change with the machine's locale.
    /// </remarks>
    private static int ToInt(object? value) => value switch
    {
        int number => number,
        string text when text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int hex) => hex,
        string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) => parsed,
        _ => 0,
    };
}
