using GumpStudio.Core.Commands;
using GumpStudio.Core.Document;
using GumpStudio.Core.Export;
using GumpStudio.Plugins;
using GumpStudio.TestSupport;

using Xunit;

namespace GumpStudio.Plugins.Pol.Tests;

/// <summary>
/// Tests the on-disk plugin discovery path.
/// </summary>
/// <remarks>
/// Uses the real POL plugin assembly from the build output, so it exercises the
/// thing most likely to break: loading a plugin into its own
/// <see cref="System.Runtime.Loader.AssemblyLoadContext"/> while still resolving
/// the shared contract types to the host's copies. Get that wrong and the
/// loaded plugin implements an <c>IGumpStudioPlugin</c> the host does not
/// recognise.
/// </remarks>
public class PluginLoaderTests
{
    private static string PluginAssemblyPath =>
        Path.Combine(AppContext.BaseDirectory, "GumpStudio.Plugins.Pol.dll");

    [Fact]
    public void LoadsThePolPluginFromDisk()
    {
        using PluginLoader loader = new();

        IReadOnlyList<DiscoveredPlugin> found = loader.Load(PluginAssemblyPath);

        DiscoveredPlugin plugin = Assert.Single(found);

        Assert.True(plugin.IsUsable, plugin.Error);
        Assert.Equal("gumpstudio.exporters.pol", plugin.Info.Id);
    }

    [Fact]
    public void ALoadedPluginRegistersThroughTheSharedContractTypes()
    {
        using PluginLoader loader = new();

        DiscoveredPlugin plugin = loader.Load(PluginAssemblyPath).Single(p => p.IsUsable);
        RecordingHost host = new();

        plugin.Instance!.Initialize(host);

        IGumpExporter exporter = host.Exporters[0];

        // Producing output proves the contract types resolved to the host's copies
        // rather than duplicates loaded into the plugin's own context.
        Assert.Contains(
            "endprogram",
            exporter.Export(new GumpDocument(), new GumpExportOptions()),
            StringComparison.Ordinal);
    }

    [Fact]
    public void DiscoveringAWholeDirectoryFindsThePlugin()
    {
        using PluginLoader loader = new();

        IReadOnlyList<DiscoveredPlugin> found = loader.Discover(AppContext.BaseDirectory);

        Assert.Contains(found, p => p.Info.Id == "gumpstudio.exporters.pol");
    }

    [Fact]
    public void AMissingDirectoryYieldsNothingRatherThanThrowing()
    {
        using PluginLoader loader = new();

        Assert.Empty(loader.Discover(Path.Combine(AppContext.BaseDirectory, "no-such-folder")));
    }

    /// <summary>
    /// A file that is not a managed assembly must be skipped quietly. Native
    /// libraries sit alongside plugins all the time — SkiaSharp ships one.
    /// </summary>
    [Fact]
    public void ANonAssemblyFileIsSkipped()
    {
        using TempDirectory dir = new();

        string path = Path.Combine(dir.Path, "not-an-assembly.dll");

        File.WriteAllText(path, "this is not a PE file");

        using PluginLoader loader = new();

        Assert.Empty(loader.Load(path));
    }

    /// <summary>
    /// One unusable file must not stop the rest of a directory being scanned.
    /// The original enumerated types outside its try block, so a single bad
    /// assembly aborted discovery for everything after it.
    /// </summary>
    [Fact]
    public void OneBadFileDoesNotAbortDirectoryScanning()
    {
        using TempDirectory dir = new();

        File.WriteAllText(Path.Combine(dir.Path, "aaa-broken.dll"), "garbage");
        File.Copy(PluginAssemblyPath, Path.Combine(dir.Path, "GumpStudio.Plugins.Pol.dll"));

        // The plugin's own dependencies must resolve, so copy the host assemblies
        // it references next to it.
        foreach (string dependency in Directory.EnumerateFiles(AppContext.BaseDirectory, "GumpStudio.*.dll"))
        {
            string target = Path.Combine(dir.Path, Path.GetFileName(dependency));

            if (!File.Exists(target))
            {
                File.Copy(dependency, target);
            }
        }

        using PluginLoader loader = new();

        IReadOnlyList<DiscoveredPlugin> found = loader.Discover(dir.Path);

        Assert.Contains(found, p => p.Info.Id == "gumpstudio.exporters.pol");
    }

    private sealed class RecordingHost : IPluginHost
    {
        public List<IGumpExporter> Exporters { get; } = [];

        public IGumpDocumentSession Session => throw new NotSupportedException();

        public void RegisterExporter(IGumpExporter exporter) => Exporters.Add(exporter);

        public void RegisterMenuCommand(MenuCommandDescriptor descriptor)
        {
        }

        public void Notify(PluginNotificationLevel level, string message)
        {
        }
    }
}
