using GumpStudio.Core.Document;
using GumpStudio.TestSupport;

using Xunit;

namespace GumpStudio.Converters.Tests;

/// <summary>
/// Byte-exact snapshots of every export format.
/// </summary>
/// <remarks>
/// <para>
/// The per-exporter tests assert with substring matching, which cannot see a
/// reordering, a lost blank line or a command that moved to a different page.
/// These do. They exist to make the layout-IR refactor provably
/// behaviour-preserving: the goldens are generated before it starts and must not
/// move until a change is deliberately made to them.
/// </para>
/// <para>
/// Every builder takes an injectable timestamp for exactly this reason, so the
/// output is reproducible and a diff means something.
/// </para>
/// <para>
/// Set <c>GUMPSTUDIO_UPDATE_GOLDENS=1</c> to rewrite them, and read the diff
/// before committing it.
/// </para>
/// </remarks>
public class GoldenTests
{
    private static readonly DateTimeOffset Stamp = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    public static TheoryData<string> Formats =>
    [
        "layout",
        "pol",
        "pol-layout",
        "runuo",
        "runuo-numeric",
        "sphere-056",
        "sphere-099",
        "uox3",
    ];

    [Theory]
    [MemberData(nameof(Formats))]
    public void OutputMatchesTheGolden(string format)
    {
        string actual = Normalise(Export(format, SampleDocuments.Full()));
        string path = Path.Combine(GoldenDirectory(), format + ".golden.txt");

        if (Environment.GetEnvironmentVariable("GUMPSTUDIO_UPDATE_GOLDENS") == "1")
        {
            File.WriteAllText(path, actual);

            return;
        }

        Assert.True(File.Exists(path), $"No golden for '{format}'. Run with GUMPSTUDIO_UPDATE_GOLDENS=1.");

        Assert.Equal(Normalise(File.ReadAllText(path)), actual);
    }

    /// <summary>Every format is reproducible, which is what makes a golden meaningful.</summary>
    [Theory]
    [MemberData(nameof(Formats))]
    public void OutputIsDeterministic(string format) =>
        Assert.Equal(Export(format, SampleDocuments.Full()), Export(format, SampleDocuments.Full()));

    internal static string Export(string format, GumpDocument document) => format switch
    {
        "layout" => LayoutScriptBuilder.Build(document, Stamp),
        "pol" => PolScriptBuilder.Build(
            document, "MyGump", new PolExportOptions { Style = PolScriptStyle.GumpPackage }, Stamp),
        "pol-layout" => PolScriptBuilder.Build(
            document, "MyGump", new PolExportOptions { Style = PolScriptStyle.LayoutStrings }, Stamp),
        "runuo" => RunUoScriptBuilder.Build(
            document, new RunUoExportOptions { ButtonIdStyle = RunUoButtonIdStyle.Named }, Stamp),
        "runuo-numeric" => RunUoScriptBuilder.Build(
            document, new RunUoExportOptions { ButtonIdStyle = RunUoButtonIdStyle.Numeric }, Stamp),
        "sphere-056" => SphereScriptBuilder.Build(
            document, new SphereExportOptions { Dialect = SphereDialect.Revision }, Stamp),
        "sphere-099" => SphereScriptBuilder.Build(
            document, new SphereExportOptions { Dialect = SphereDialect.Modern }, Stamp),
        "uox3" => UoxScriptBuilder.Build(document, new UoxExportOptions(), Stamp),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown format."),
    };

    /// <summary>Line endings only, so a checkout on either platform compares equal.</summary>
    private static string Normalise(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>
    /// The goldens in the source tree, not a copy beside the test binary.
    /// </summary>
    /// <remarks>
    /// They have to be writable in place for the update switch to be any use, so
    /// the directory is found by walking up to the solution file rather than by
    /// copying the files to the output directory.
    /// </remarks>
    private static string GoldenDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GumpStudio.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.True(directory is not null, "Could not find GumpStudio.slnx above the test binary.");

        string goldens = Path.Combine(
            directory!.FullName, "tests", "GumpStudio.Converters.Tests", "Goldens");

        Directory.CreateDirectory(goldens);

        return goldens;
    }
}
