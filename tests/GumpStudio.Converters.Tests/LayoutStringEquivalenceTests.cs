using GumpStudio.Core.Document;
using GumpStudio.Core.Layout;
using GumpStudio.TestSupport;

using Xunit;

namespace GumpStudio.Converters.Tests;

/// <summary>
/// Proves the shared layout writer reproduces both raw dialects exactly.
/// </summary>
/// <remarks>
/// <para>
/// The client's grammar was hand-written four times: <c>pol-layout</c> and
/// <c>sphere-056</c> emit it, and the gump-package and 0.99 dialects build it too
/// for the notes they leave beside commands they cannot express. Before any of
/// them is switched over to <see cref="LayoutStringWriter"/>, this asserts the
/// writer already agrees with what they produce today.
/// </para>
/// <para>
/// The expected values are read out of the goldens rather than restated here, so
/// there is no third copy of the grammar to drift.
/// </para>
/// </remarks>
public class LayoutStringEquivalenceTests
{
    /// <summary>POL lowercases the group command and closes the group at page end.</summary>
    private static readonly LayoutStringOptions Pol = new();

    /// <summary>Sphere capitalises it and has never emitted an endgroup.</summary>
    private static readonly LayoutStringOptions Sphere =
        new() { GroupKeyword = "Group", EmitEndGroup = false };

    [Fact]
    public void MatchesThePolLayoutDialect() =>
        Assert.Equal(GoldenCommands("pol-layout"), Written(Pol));

    [Fact]
    public void MatchesTheSphereRevisionDialect() =>
        Assert.Equal(GoldenCommands("sphere-056"), Written(Sphere));

    /// <summary>The two dialects differ only in the ways the options describe.</summary>
    [Fact]
    public void TheTwoDialectsDifferOnlyInGroupHandling()
    {
        IEnumerable<string> polOnly = Written(Pol).Except(Written(Sphere), StringComparer.Ordinal);
        IEnumerable<string> sphereOnly = Written(Sphere).Except(Written(Pol), StringComparer.Ordinal);

        Assert.Equal(["group 3", "endgroup"], polOnly);
        Assert.Equal(["Group 3"], sphereOnly);
    }

    private static List<string> Written(LayoutStringOptions options)
    {
        GumpLayout layout = GumpLayoutBuilder.Build(SampleDocuments.Full());

        List<string> lines = [.. LayoutStringWriter.GumpLevelTokens(layout.Properties)];

        lines.AddRange(LayoutStringWriter.Write(layout, options, LayoutStringWriter.ByIndex));

        return lines;
    }

    /// <summary>
    /// The command lines of a golden, from the first gump-level token to the last
    /// element command.
    /// </summary>
    /// <remarks>
    /// Both dialects wrap the same run of commands in their own container: POL in
    /// a quoted array literal, Sphere in a bare block. The movable, closable and
    /// disposable flags ahead of them are excluded, because their spelling and
    /// their order are exactly what the two do not share.
    /// </remarks>
    private static List<string> GoldenCommands(string format)
    {
        string[] lines = File.ReadAllLines(GoldenPath(format));
        List<string> commands = [];
        bool started = false;

        foreach (string raw in lines)
        {
            string line = raw.Trim();

            if (format == "pol-layout")
            {
                if (line == "};")
                {
                    break;
                }

                if (!line.StartsWith('"'))
                {
                    continue;
                }

                line = line.Trim(',').Trim('"');
            }
            else if (line.Length == 0 && started)
            {
                break;
            }

            if (!started && line != "mastergump 7")
            {
                continue;
            }

            started = true;

            commands.Add(line);
        }

        Assert.True(commands.Count > 30, $"Extracted only {commands.Count} commands from {format}.");

        return commands;
    }

    private static string GoldenPath(string format)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GumpStudio.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.True(directory is not null, "Could not find GumpStudio.slnx above the test binary.");

        return Path.Combine(
            directory!.FullName,
            "tests",
            "GumpStudio.Converters.Tests",
            "Goldens",
            format + ".golden.txt");
    }
}
