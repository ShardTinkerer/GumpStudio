namespace GumpStudio.TestSupport;

/// <summary>
/// Locates optional real Ultima Online installations for tests that need genuine
/// <c>.mul</c> / <c>.uop</c> data.
/// </summary>
/// <remarks>
/// Client files are not redistributable, so they are never checked in and CI
/// never has them. Tests that need them are skipped rather than failed.
/// <list type="bullet">
/// <item><c>GUMPSTUDIO_TEST_CLIENT</c> — a classic MUL-era install.</item>
/// <item><c>GUMPSTUDIO_TEST_CLIENT_UOP</c> — a UOP-era install.</item>
/// <item>
/// <c>GUMPSTUDIO_TEST_CLIENT_ROOT</c> — a folder holding many client versions,
/// each of which is discovered automatically. This is what gives coverage across
/// the whole 2001-to-current range of file-format changes.
/// </item>
/// </list>
/// </remarks>
public static class UoTestClient
{
    public const string MulPathVariable = "GUMPSTUDIO_TEST_CLIENT";
    public const string UopPathVariable = "GUMPSTUDIO_TEST_CLIENT_UOP";
    public const string RootVariable = "GUMPSTUDIO_TEST_CLIENT_ROOT";

    /// <summary>How deep under the root to look for a client directory.</summary>
    private const int MaxSearchDepth = 2;

    /// <summary>Directory of a classic <c>.mul</c>-era client, or <see langword="null"/>.</summary>
    public static string? MulPath { get; } = ResolveDirectory(MulPathVariable);

    /// <summary>Directory of a <c>.uop</c>-era client, or <see langword="null"/>.</summary>
    public static string? UopPath { get; } = ResolveDirectory(UopPathVariable);

    /// <summary>Every distinct installation the environment points at.</summary>
    public static IReadOnlyList<string> AllClients { get; } = DiscoverAll();

    /// <summary>Referenced by <c>[Fact(SkipUnless = ...)]</c> on tests needing MUL data.</summary>
    public static bool HasMulClient => MulPath is not null;

    /// <summary>Referenced by <c>[Fact(SkipUnless = ...)]</c> on tests needing UOP data.</summary>
    public static bool HasUopClient => UopPath is not null;

    /// <summary>True when both a MUL-era and a UOP-era client are configured.</summary>
    public static bool HasBothClients => HasMulClient && HasUopClient;

    public const string NoMulClientReason =
        $"No UO client configured. Set {MulPathVariable} to a directory containing art.mul to run this test.";

    public const string NoUopClientReason =
        $"No UOP-era UO client configured. Set {UopPathVariable} to run this test.";

    public const string NoBothClientsReason =
        $"This test compares two clients. Set both {MulPathVariable} and {UopPathVariable} to run it.";

    /// <summary>
    /// Returns the configured MUL client directory, or throws if it is absent.
    /// Only call this from a test already gated on <see cref="HasMulClient"/>.
    /// </summary>
    public static string RequireMulPath() =>
        MulPath ?? throw new InvalidOperationException(NoMulClientReason);

    public static string RequireUopPath() =>
        UopPath ?? throw new InvalidOperationException(NoUopClientReason);

    /// <summary>True when a directory holds art in either container format.</summary>
    public static bool IsClientDirectory(string directory) =>
        File.Exists(Path.Combine(directory, "art.mul"))
        || File.Exists(Path.Combine(directory, "artLegacyMUL.uop"));

    private static List<string> DiscoverAll()
    {
        List<string> found = [];

        foreach (string? explicitPath in new[] { MulPath, UopPath })
        {
            if (explicitPath is not null)
            {
                found.Add(explicitPath);
            }
        }

        if (ResolveDirectory(RootVariable) is { } root)
        {
            Search(root, MaxSearchDepth, found);
        }

        return [.. found.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.OrdinalIgnoreCase)];
    }

    private static void Search(string directory, int depth, List<string> found)
    {
        if (IsClientDirectory(directory))
        {
            found.Add(directory);

            // A client directory has no nested clients worth finding.
            return;
        }

        if (depth <= 0)
        {
            return;
        }

        IEnumerable<string> children;

        try
        {
            children = Directory.EnumerateDirectories(directory);
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }
        catch (IOException)
        {
            return;
        }

        foreach (string child in children)
        {
            Search(child, depth - 1, found);
        }
    }

    private static string? ResolveDirectory(string variable)
    {
        string? configured = Environment.GetEnvironmentVariable(variable);

        if (string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }

        // A path that is set but wrong is a configuration mistake worth surfacing
        // loudly, rather than silently degrading into "all tests skipped".
        string full = Path.GetFullPath(configured.Trim());

        return Directory.Exists(full)
            ? full
            : throw new DirectoryNotFoundException(
                $"{variable} is set to '{configured}', which is not an existing directory.");
    }
}
