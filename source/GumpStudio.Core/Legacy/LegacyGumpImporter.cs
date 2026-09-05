using System.Formats.Nrbf;
using System.Runtime.Serialization;

using GumpStudio.Core.Document;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Primitives;

namespace GumpStudio.Core.Legacy;

/// <summary>
/// Reads <c>.gump</c> and <c>.gumpling</c> files written by GumpStudio 1.8.
/// </summary>
/// <remarks>
/// <para>
/// The old format is two consecutive <c>BinaryFormatter</c> payloads: an
/// <c>ArrayList</c> of page groups, then a <c>GumpProperties</c>.
/// <c>BinaryFormatter</c> itself is removed from modern .NET and would be unsafe
/// to re-enable, so this uses <see cref="NrbfDecoder"/>, which parses the same
/// wire format into a read-only object graph <strong>without ever activating a
/// type</strong>. Nothing from the file is constructed; the records are walked
/// and mapped onto the new model by member name.
/// </para>
/// <para>
/// Import is one-way. Member names were recovered by decompiling the original
/// binary and are documented in <c>docs/uo-file-formats.md</c>.
/// </para>
/// </remarks>
public static class LegacyGumpImporter
{
    /// <summary>Reads a legacy <c>.gump</c> file.</summary>
    public static GumpDocument ImportDocument(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        using FileStream stream = File.OpenRead(path);

        return ImportDocument(stream);
    }

    /// <summary>Reads a legacy <c>.gump</c> stream.</summary>
    public static GumpDocument ImportDocument(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        GumpDocument document = new();
        List<GumpPage> pages = [];

        SerializationRecord stacks = Decode(stream, "page list");

        foreach (SerializationRecord? record in EnumerateList(stacks))
        {
            if (record is not ClassRecord group)
            {
                continue;
            }

            GumpPage page = new($"Page {pages.Count}");

            foreach (Element element in ReadChildren(group))
            {
                page.Root.Add(element);
            }

            pages.Add(page);
        }

        if (pages.Count == 0)
        {
            pages.Add(new GumpPage("Page 0"));
        }

        document.ReplacePages(pages);

        // The properties payload is a second, independent stream. Files written
        // by early builds stop after the pages, so its absence is not an error.
        if (stream.Position < stream.Length)
        {
            try
            {
                if (Decode(stream, "properties") is ClassRecord properties)
                {
                    document.Properties = ReadProperties(properties);
                }
            }
            catch (SerializationException)
            {
                // Keep the pages we already read rather than failing the import.
            }
        }

        return document;
    }

    /// <summary>Reads a legacy <c>.gumpling</c> file: a single saved group.</summary>
    public static GroupElement ImportGumpling(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        using FileStream stream = File.OpenRead(path);

        if (Decode(stream, "gumpling") is not ClassRecord record)
        {
            throw new InvalidDataException("The file does not contain a gumpling.");
        }

        GroupElement group = new() { Name = ReadString(record, "Name") ?? "Gumpling" };

        group.Location = ReadPoint(record, "Location");

        foreach (Element element in ReadChildren(record))
        {
            group.Add(element);
        }

        return group;
    }

    private static SerializationRecord Decode(Stream stream, string what)
    {
        try
        {
            return NrbfDecoder.Decode(stream, leaveOpen: true);
        }
        catch (SerializationException ex)
        {
            throw new InvalidDataException(
                $"This does not look like a GumpStudio 1.8 file: the {what} could not be read.", ex);
        }
    }

    /// <summary>Yields the items of a legacy <c>ArrayList</c> or array record.</summary>
    private static IEnumerable<SerializationRecord?> EnumerateList(SerializationRecord record)
    {
        switch (record)
        {
            case SZArrayRecord<SerializationRecord?> array:
                foreach (SerializationRecord? item in array.GetArray())
                {
                    yield return item;
                }

                break;

            case ClassRecord classRecord:
                // ArrayList serialises as _items (an array) plus _size, and the
                // array is longer than the live count.
                int size = ReadInt(classRecord, "_size") ?? int.MaxValue;

                if (classRecord.GetRawValue("_items") is SZArrayRecord<SerializationRecord?> items)
                {
                    int index = 0;

                    foreach (SerializationRecord? item in items.GetArray())
                    {
                        if (index++ >= size)
                        {
                            break;
                        }

                        yield return item;
                    }
                }

                break;

            default:
                break;
        }
    }

    private static GumpProperties ReadProperties(ClassRecord record) => new()
    {
        Location = ReadPoint(record, "Location"),
        Movable = ReadBool(record, "Moveable") ?? true,
        Closable = ReadBool(record, "Closeable") ?? true,
        Disposable = ReadBool(record, "Disposeable") ?? true,
        TypeId = ReadInt(record, "Type") ?? 0,
    };

    private static IEnumerable<Element> ReadChildren(ClassRecord group)
    {
        if (group.GetRawValue("Elements") is not SerializationRecord raw)
        {
            yield break;
        }

        foreach (SerializationRecord? child in EnumerateList(raw))
        {
            if (child is ClassRecord record && ReadElement(record) is { } element)
            {
                yield return element;
            }
        }
    }

    /// <summary>Maps one legacy record onto a new element, or null if unrecognised.</summary>
    private static Element? ReadElement(ClassRecord record)
    {
        // The legacy type name is the only discriminator available.
        string typeName = record.TypeName.Name;
        string shortName = typeName[(typeName.LastIndexOf('.') + 1)..];

        Element? element = shortName switch
        {
            "GroupElement" => ReadGroup(record),
            "AlphaElement" => new AlphaElement(),
            "BackgroundElement" => new BackgroundElement { GumpId = ReadInt(record, "GumpID") ?? 9200 },
            "TiledElement" => new TiledElement
            {
                GumpId = ReadInt(record, "GumpID") ?? 0,
                Hue = ReadHue(record),
            },
            "ImageElement" => new ImageElement
            {
                GumpId = ReadInt(record, "GumpID") ?? 0,
                Hue = ReadHue(record),
            },
            "ItemElement" => new ItemElement
            {
                ItemId = ReadInt(record, "ItemID") ?? 0,
                Hue = ReadHue(record),
            },
            "ButtonElement" => new ButtonElement
            {
                NormalId = ReadInt(record, "NormalID") ?? 0,
                PressedId = ReadInt(record, "PressedID") ?? 0,
                Kind = (ButtonKind)(ReadInt(record, "Type") ?? 1),
                Param = ReadInt(record, "Param") ?? 0,
                CodeBehind = ReadString(record, "CodeBehind") ?? string.Empty,
            },
            "RadioElement" => ReadRadio(record),
            "CheckboxElement" => ReadCheckbox(record, new CheckboxElement()),
            "HTMLElement" or "HtmlElement" => new HtmlElement
            {
                Html = ReadString(record, "HTML") ?? string.Empty,
                ClilocId = ReadInt(record, "ClilocID") ?? 0,
                ShowScrollbar = ReadBool(record, "Scrollbar") ?? false,
                ShowBackground = ReadBool(record, "Background") ?? false,
                ContentKind = (HtmlContentKind)(ReadInt(record, "TextType") ?? 0),
            },
            "LabelElement" => new LabelElement
            {
                Text = ReadString(record, "Text") ?? string.Empty,
                Hue = ReadHue(record),
                FontIndex = ReadInt(record, "FontIndex") ?? 0,
                Cropped = ReadBool(record, "Cropped") ?? false,
            },
            "TextEntryElement" => new TextEntryElement
            {
                InitialText = ReadString(record, "Text") ?? string.Empty,
                Hue = ReadHue(record),
                EntryId = ReadInt(record, "ID") ?? 0,
                MaxLength = ReadInt(record, "MaxLength") ?? 0,
            },
            _ => null,
        };

        if (element is null)
        {
            return null;
        }

        element.Name = ReadString(record, "Name") ?? element.TypeName;
        element.Comment = ReadString(record, "Comment") ?? string.Empty;
        element.Location = ReadPoint(record, "Location");

        if (element.IsResizable)
        {
            element.Size = ReadSize(record, "Size");
        }

        return element;
    }

    private static GroupElement ReadGroup(ClassRecord record)
    {
        GroupElement group = new();

        foreach (Element child in ReadChildren(record))
        {
            group.Add(child);
        }

        return group;
    }

    private static RadioElement ReadRadio(ClassRecord record)
    {
        RadioElement radio = new() { Value = ReadInt(record, "Value") ?? 0 };

        ReadCheckbox(record, radio);

        return radio;
    }

    private static CheckboxElement ReadCheckbox(ClassRecord record, CheckboxElement checkbox)
    {
        checkbox.CheckedId = ReadInt(record, "CheckedID") ?? 0;
        checkbox.UncheckedId = ReadInt(record, "UncheckedID") ?? 0;
        checkbox.GroupId = ReadInt(record, "GroupID") ?? 0;
        checkbox.IsChecked = ReadBool(record, "Checked") ?? false;

        return checkbox;
    }

    /// <summary>
    /// Reads the stored hue index and converts it to the one-based convention.
    /// </summary>
    /// <remarks>
    /// The old format saved <c>Hue.Index</c>, which is zero-based, while elements
    /// and scripts use one-based hues with 0 meaning "none".
    /// </remarks>
    private static int ReadHue(ClassRecord record)
    {
        int index = ReadInt(record, "HueIndex") ?? 0;

        return index <= 0 ? 0 : index + 1;
    }

    private static GumpPoint ReadPoint(ClassRecord record, string member) =>
        record.GetRawValue(member) is ClassRecord point
            ? new GumpPoint(ReadInt(point, "x") ?? 0, ReadInt(point, "y") ?? 0)
            : default;

    private static GumpSize ReadSize(ClassRecord record, string member) =>
        record.GetRawValue(member) is ClassRecord size
            ? new GumpSize(ReadInt(size, "width") ?? 0, ReadInt(size, "height") ?? 0)
            : default;

    private static int? ReadInt(ClassRecord record, string member) =>
        record.HasMember(member) ? record.GetRawValue(member) as int? : null;

    private static bool? ReadBool(ClassRecord record, string member) =>
        record.HasMember(member) ? record.GetRawValue(member) as bool? : null;

    private static string? ReadString(ClassRecord record, string member) =>
        record.HasMember(member) ? record.GetRawValue(member) as string : null;
}
