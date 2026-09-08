using System.Globalization;

using GumpStudio.Core.Document;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Primitives;

namespace GumpStudio.Core.Layout;

/// <summary>An imported document, with anything that could not be represented.</summary>
public sealed record LayoutImportResult(GumpDocument Document, IReadOnlyList<string> Warnings);

/// <summary>
/// Turns layout commands back into a document.
/// </summary>
/// <remarks>
/// <para>
/// The inverse of <see cref="GumpLayoutBuilder"/>, and the back half of importing
/// a gump captured off the wire.
/// </para>
/// <para>
/// The result is flat: the client has no notion of a group, so nothing here can
/// invent one. Every element lands at the absolute position the layout gave it,
/// which is exactly what the builder produces going the other way — so a
/// document that came in this way exports byte-identically.
/// </para>
/// </remarks>
public static class GumpLayoutReader
{
    /// <summary>
    /// The largest page number an import will honour.
    /// </summary>
    /// <remarks>
    /// Pages are a contiguous list here but a bare number in the layout, and a
    /// capture may use a sparse set — 0, 1, 2, 9, 10 in the one this was written
    /// against. The gap is filled with empty pages so page buttons keep pointing
    /// at the right place, which a malformed capture could otherwise turn into
    /// millions of them.
    /// </remarks>
    public const int MaxPageNumber = 255;

    /// <summary>Builds a document from parsed layout commands.</summary>
    public static LayoutImportResult Read(GumpLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        List<string> warnings = [];
        GumpDocument document = new() { Properties = layout.Properties.Clone() };

        int page = 0;
        int group = 0;
        Element? previous = null;

        foreach (LayoutCommand command in layout.Commands)
        {
            switch (command)
            {
                case PageCommand c:
                    if (c.Page < 0 || c.Page > MaxPageNumber)
                    {
                        warnings.Add(
                            $"Page {c.Page} is outside 0..{MaxPageNumber}; its elements were put on page {page}.");

                        break;
                    }

                    page = c.Page;
                    previous = null;

                    // The same page is opened more than once in real captures,
                    // so pages are grown to fit rather than appended per command.
                    while (document.PageCount <= page)
                    {
                        document.AddPage();
                    }

                    break;

                case GroupCommand c:
                    group = c.Group;

                    break;

                case EndGroupCommand:
                    group = 0;

                    break;

                // Both attach to whichever element the client created last.
                case TooltipCommand c:
                    if (previous is null)
                    {
                        warnings.Add("A tooltip had no element before it; dropped.");

                        break;
                    }

                    previous.TooltipClilocId = c.ClilocId;
                    previous.TooltipArguments = c.Arguments;

                    break;

                case ItemPropertyCommand c:
                    if (previous is null)
                    {
                        warnings.Add("An item property had no element before it; dropped.");

                        break;
                    }

                    previous.ItemPropertySerial = c.Serial;

                    break;

                default:
                    if (CreateElement(command, layout, group, warnings) is { } element)
                    {
                        while (document.PageCount <= page)
                        {
                            document.AddPage();
                        }

                        document.Pages[page].Root.Add(element);
                        previous = element;
                    }

                    break;
            }
        }

        return new LayoutImportResult(document, warnings);
    }

    /// <summary>Parses layout text and builds a document from it.</summary>
    public static LayoutImportResult Import(string? text)
    {
        LayoutParseResult parsed = LayoutStringParser.Parse(text);
        LayoutImportResult read = Read(parsed.Layout);

        return read with { Warnings = [.. parsed.Warnings, .. read.Warnings] };
    }

    private static Element? CreateElement(
        LayoutCommand command, GumpLayout layout, int group, List<string> warnings)
    {
        switch (command)
        {
            case ResizePicCommand c:
                return Sized(
                    new BackgroundElement { GumpId = c.GumpId }, c.X, c.Y, c.Width, c.Height);

            case CheckerTransCommand c:
                return Sized(new AlphaElement(), c.X, c.Y, c.Width, c.Height);

            case GumpPicTiledCommand c:
                return Sized(new TiledElement { GumpId = c.GumpId }, c.X, c.Y, c.Width, c.Height);

            case GumpPicCommand c:
                return At(
                    new ImageElement { GumpId = c.GumpId, Hue = c.Hue, PartialHue = c.PartialHue },
                    c.X,
                    c.Y);

            case PicInPicCommand c:
                return Sized(
                    new PicInPicElement
                    {
                        GumpId = c.GumpId,
                        SourceX = c.SourceX,
                        SourceY = c.SourceY,
                        Hue = c.Hue,
                        PartialHue = c.PartialHue,
                    },
                    c.X,
                    c.Y,
                    c.Width,
                    c.Height);

            case TilePicCommand c:
                return At(new ItemElement { ItemId = c.ItemId, Hue = c.Hue }, c.X, c.Y);

            case TileAsGumpPicCommand c:
                return At(
                    new TileAsGumpElement
                    {
                        ItemId = c.ItemId,
                        LinkId = c.LinkId,
                        ParamB = c.ParamB,
                        ParamC = c.ParamC,
                    },
                    c.X,
                    c.Y);

            case TextCommand c:
                return At(
                    new LabelElement { Hue = c.Hue, Text = Text(c.Text, layout, warnings) },
                    c.X,
                    c.Y);

            case CroppedTextCommand c:
                // Cropped has to be set before the size: a label is not resizable
                // until it is, so an earlier size would be silently discarded.
                return Sized(
                    new LabelElement
                    {
                        Hue = c.Hue,
                        Text = Text(c.Text, layout, warnings),
                        Cropped = true,
                    },
                    c.X,
                    c.Y,
                    c.Width,
                    c.Height);

            case TextEntryCommand c:
                return Sized(
                    new TextEntryElement
                    {
                        Hue = c.Hue,
                        EntryId = c.EntryId,
                        MaxLength = c.MaxLength,
                        InitialText = Text(c.Text, layout, warnings),
                    },
                    c.X,
                    c.Y,
                    c.Width,
                    c.Height);

            case HtmlGumpCommand c:
                return Sized(
                    new HtmlElement
                    {
                        ContentKind = HtmlContentKind.Html,
                        Html = Text(c.Text, layout, warnings),
                        ShowBackground = c.Background,
                        ShowScrollbar = c.Scrollbar,
                    },
                    c.X,
                    c.Y,
                    c.Width,
                    c.Height);

            case XmfHtmlCommand c:
                return Sized(
                    new HtmlElement
                    {
                        ContentKind = HtmlContentKind.Localized,
                        ClilocId = c.ClilocId,
                        Color = c.Color,
                        Arguments = c.Arguments,
                        ShowBackground = c.Background,
                        ShowScrollbar = c.Scrollbar,
                    },
                    c.X,
                    c.Y,
                    c.Width,
                    c.Height);

            case ButtonCommand c:
                return At(
                    new ButtonElement
                    {
                        NormalId = c.NormalId,
                        PressedId = c.PressedId,
                        Kind = c.Kind,
                        Param = c.Param,
                        TileId = c.Tile?.ItemId ?? 0,
                        TileHue = c.Tile?.Hue ?? 0,
                        TileX = c.Tile?.X ?? 0,
                        TileY = c.Tile?.Y ?? 0,
                    },
                    c.X,
                    c.Y);

            case RadioCommand c:
                return At(
                    new RadioElement
                    {
                        UncheckedId = c.UncheckedId,
                        CheckedId = c.CheckedId,
                        IsChecked = c.IsChecked,
                        Value = c.Value,
                        GroupId = group,
                    },
                    c.X,
                    c.Y);

            case CheckboxCommand c:
                return At(
                    new CheckboxElement
                    {
                        UncheckedId = c.UncheckedId,
                        CheckedId = c.CheckedId,
                        IsChecked = c.IsChecked,
                        GroupId = c.Group,
                    },
                    c.X,
                    c.Y);

            default:
                warnings.Add($"No element for '{command.GetType().Name}'; dropped.");

                return null;
        }
    }

    /// <summary>
    /// The string a command refers to.
    /// </summary>
    /// <remarks>
    /// A capture often arrives with its text block truncated or missing
    /// altogether, so an index that points past the end is reported and left
    /// empty rather than treated as corrupt input.
    /// </remarks>
    private static string Text(TextRef reference, GumpLayout layout, List<string> warnings)
    {
        if (reference.Index >= 0 && reference.Index < layout.Texts.Count)
        {
            return layout.Texts[reference.Index].Value;
        }

        warnings.Add(string.Create(
            CultureInfo.InvariantCulture,
            $"Text {reference.Index} is not in the text block; left empty."));

        return string.Empty;
    }

    private static Element At(Element element, int x, int y)
    {
        element.Location = new GumpPoint(x, y);

        return element;
    }

    private static Element Sized(Element element, int x, int y, int width, int height)
    {
        element.Location = new GumpPoint(x, y);
        element.Size = new GumpSize(width, height);

        return element;
    }
}
