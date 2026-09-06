using System.Globalization;

using GumpStudio.Core.Document;
using GumpStudio.Core.Elements;
using GumpStudio.Core.Legacy;
using GumpStudio.Core.Primitives;
using GumpStudio.Core.Serialization;
using GumpStudio.Rendering;
using GumpStudio.Uo;
using GumpStudio.Uo.Primitives;

namespace GumpStudio.Cli;

/// <summary>
/// Headless tooling over the UO data layer.
/// </summary>
/// <remarks>
/// Exists mainly so the client-data pipeline can be exercised without the UI:
/// point it at an installation and it will write real art to PNG.
/// </remarks>
internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();

            return 1;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "dump" => Dump(args[1..]),
                "info" => Info(args[1..]),
                "render" => Render(args[1..]),
                "sample" => Sample(args[1..]),
                "export" => Export(args[1..]),
                "help" or "--help" or "-h" => PrintUsage(),
                _ => Fail($"Unknown command '{args[0]}'."),
            };
        }
        catch (DirectoryNotFoundException ex)
        {
            return Fail(ex.Message);
        }
        catch (FileNotFoundException ex)
        {
            return Fail(ex.Message);
        }
        catch (InvalidDataException ex)
        {
            return Fail(ex.Message);
        }
    }

    /// <summary>Reports what a client installation contains.</summary>
    private static int Info(string[] args)
    {
        if (ParseOptions(args) is not { } options || options.Client is null)
        {
            return Fail("info requires --client <path>.");
        }

        IReadOnlyList<string> missing = UoDataContext.Validate(options.Client);

        if (missing.Count > 0)
        {
            return Fail($"'{options.Client}' is missing: {string.Join(", ", missing)}");
        }

        using UoDataContext context = UoDataContext.Open(options.Client);

        Console.WriteLine($"Client:        {context.ClientPath}");
        Console.WriteLine($"Gump slots:    {context.GumpCount}");
        Console.WriteLine($"Gumps present: {context.EnumerateGumpIds().Count()}");
        Console.WriteLine($"Items present: {context.EnumerateItemIds().Count()}");
        Console.WriteLine($"Hues:          {context.Hues.Count}");
        Console.WriteLine($"Static tiles:  {context.TileData.StaticCount}"
            + $" ({(context.TileData.IsHighSeasFormat ? "High Seas" : "legacy")} tiledata)");
        Console.WriteLine($"Cliloc strings:{context.Clilocs.Count,7}");
        Console.WriteLine($"ASCII fonts:   {context.AsciiFonts.Count}");
        Console.WriteLine($"Unicode fonts: {context.UnicodeFonts.Count}");

        return 0;
    }

    /// <summary>Writes a gump or item tile to a PNG.</summary>
    private static int Dump(string[] args)
    {
        if (ParseOptions(args) is not { } options || options.Client is null)
        {
            return Fail("dump requires --client <path>.");
        }

        using UoDataContext context = UoDataContext.Open(options.Client);

        (UoImage? image, string label) = options switch
        {
            { Gump: { } gump } => (context.GetGump(gump, options.Hue, options.PartialHue), $"gump {gump}"),
            { Item: { } item } => (context.GetStatic(item, options.Hue, options.PartialHue), $"item {item}"),
            { Land: { } land } => (context.GetLand(land), $"land {land}"),
            _ => (null, string.Empty),
        };

        if (label.Length == 0)
        {
            return Fail("dump requires one of --gump, --item or --land.");
        }

        if (image is null)
        {
            return Fail($"No art found for {label}.");
        }

        string output = options.Output ?? $"{label.Replace(' ', '-')}.png";

        image.SavePng(output);

        Console.WriteLine($"Wrote {output} ({image.Width}x{image.Height}) from {label}.");

        return 0;
    }

    /// <summary>Renders a saved document to a PNG using real client art.</summary>
    private static int Render(string[] args)
    {
        if (ParseOptions(args) is not { } options || options.Client is null || options.Input is null)
        {
            return Fail("render requires --client <path> and --in <file.gump>.");
        }

        GumpDocument document = options.Input.EndsWith(".gump", StringComparison.OrdinalIgnoreCase)
            && !IsXml(options.Input)
                ? LegacyGumpImporter.ImportDocument(options.Input)
                : GumpXmlSerializer.Load(options.Input);

        using UoDataContext data = UoDataContext.Open(options.Client);
        using UoArtSource art = new(data);

        GumpRenderer renderer = new(art);
        GumpPage page = document.Pages[Math.Clamp(options.Page, 0, document.PageCount - 1)];

        renderer.MeasureContentSizes(page);

        int width = Math.Max(1, page.Root.Size.Width);
        int height = Math.Max(1, page.Root.Size.Height);

        using SkiaSharp.SKBitmap bitmap = renderer.RenderToBitmap(page, width, height, RenderOptions.Plain);
        using SkiaSharp.SKData encoded = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);

        string output = options.Output ?? "gump.png";

        File.WriteAllBytes(output, encoded.ToArray());

        Console.WriteLine($"Wrote {output} ({width}x{height}) from page {options.Page} of {options.Input}.");

        return 0;
    }

    /// <summary>Writes a small demonstration document, so the pipeline can be exercised.</summary>
    private static int Sample(string[] args)
    {
        Options options = ParseOptions(args) ?? default;
        string output = options.Output ?? "sample.gump";

        GumpDocument document = new();
        GumpPage page = document.Pages[0];

        page.Root.Add(new BackgroundElement
        {
            Name = "Frame",
            Location = new GumpPoint(0, 0),
            Size = new GumpSize(300, 200),
            GumpId = 5054,
        });

        page.Root.Add(new LabelElement
        {
            Location = new GumpPoint(20, 20),
            Text = "GumpStudio",
            Hue = 88,
        });

        page.Root.Add(new ItemElement { Location = new GumpPoint(20, 50), ItemId = 3821 });
        page.Root.Add(new ButtonElement { Location = new GumpPoint(20, 120), NormalId = 247, PressedId = 248 });

        GroupElement group = new() { Name = "Nested", Location = new GumpPoint(150, 60) };

        group.Add(new ImageElement { Location = new GumpPoint(5, 5), GumpId = 1417 });
        page.Root.Add(group);

        GumpXmlSerializer.Save(document, output);

        Console.WriteLine($"Wrote {output}.");

        return 0;
    }

    /// <summary>Exports a document as a POL script.</summary>
    private static int Export(string[] args)
    {
        if (ParseOptions(args) is not { } options || options.Input is null)
        {
            return Fail("export requires --in <file.gump>.");
        }

        GumpDocument document = IsXml(options.Input)
            ? GumpXmlSerializer.Load(options.Input)
            : LegacyGumpImporter.ImportDocument(options.Input);

        GumpStudio.Plugins.Pol.PolExporter exporter = new();

        if (options.Style is { } style)
        {
            exporter.Options = exporter.Options with
            {
                Style = string.Equals(style, "layout", StringComparison.OrdinalIgnoreCase)
                    ? GumpStudio.Plugins.Pol.PolScriptStyle.LayoutStrings
                    : GumpStudio.Plugins.Pol.PolScriptStyle.GumpPackage,
            };
        }

        string script = exporter.Export(
            document,
            new GumpStudio.Core.Export.GumpExportOptions
            {
                GumpName = options.Name ?? "MyGump",
            });

        if (options.Output is null)
        {
            Console.Write(script);
        }
        else
        {
            File.WriteAllText(options.Output, script);
            Console.WriteLine($"Wrote {options.Output}.");
        }

        return 0;
    }

    /// <summary>Distinguishes the new XML format from a legacy binary one.</summary>
    private static bool IsXml(string path)
    {
        using FileStream stream = File.OpenRead(path);

        int first = stream.ReadByte();

        return first is '<' or 0xEF;
    }

    private static Options? ParseOptions(string[] args)
    {
        Options options = new();

        for (int i = 0; i < args.Length; i++)
        {
            string name = args[i];
            string? value = i + 1 < args.Length ? args[i + 1] : null;

            switch (name)
            {
                case "--client" when value is not null:
                    options = options with { Client = value };
                    i++;
                    break;

                case "--gump" when value is not null:
                    options = options with { Gump = ParseId(value) };
                    i++;
                    break;

                case "--item" when value is not null:
                    options = options with { Item = ParseId(value) };
                    i++;
                    break;

                case "--land" when value is not null:
                    options = options with { Land = ParseId(value) };
                    i++;
                    break;

                case "--hue" when value is not null:
                    options = options with { Hue = ParseId(value) };
                    i++;
                    break;

                case "--partial-hue":
                    options = options with { PartialHue = true };
                    break;

                case "--out" when value is not null:
                    options = options with { Output = value };
                    i++;
                    break;

                case "--in" when value is not null:
                    options = options with { Input = value };
                    i++;
                    break;

                case "--style" when value is not null:
                    options = options with { Style = value };
                    i++;
                    break;

                case "--name" when value is not null:
                    options = options with { Name = value };
                    i++;
                    break;

                case "--page" when value is not null:
                    options = options with { Page = ParseId(value) };
                    i++;
                    break;

                default:
                    Console.Error.WriteLine($"Ignoring unrecognised argument '{name}'.");
                    break;
            }
        }

        return options;
    }

    /// <summary>Accepts decimal or 0x-prefixed hexadecimal ids.</summary>
    private static int ParseId(string value) =>
        value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? int.Parse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);

        return 1;
    }

    private static int PrintUsage()
    {
        Console.WriteLine("""
            gumpstudio — headless tooling for Ultima Online client data.

            Usage:
              gumpstudio info --client <path>
              gumpstudio dump --client <path> (--gump <id> | --item <id> | --land <id>)
                              [--hue <n>] [--partial-hue] [--out <file.png>]
              gumpstudio render --client <path> --in <file.gump> [--page <n>] [--out <file.png>]
              gumpstudio sample [--out <file.gump>]
              gumpstudio export --in <file.gump> [--style pkg|layout] [--name <n>] [--out <file.src>]

            Ids accept decimal or 0x-prefixed hexadecimal.
            Hues are one-based, matching the values gump scripts use; 0 means none.
            """);

        return 0;
    }

    private readonly record struct Options(
        string? Client,
        int? Gump,
        int? Item,
        int? Land,
        int Hue,
        bool PartialHue,
        string? Output,
        string? Input = null,
        int Page = 0,
        string? Style = null,
        string? Name = null);
}
