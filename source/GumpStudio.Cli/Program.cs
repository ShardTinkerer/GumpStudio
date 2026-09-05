using System.Globalization;

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
        string? Output);
}
