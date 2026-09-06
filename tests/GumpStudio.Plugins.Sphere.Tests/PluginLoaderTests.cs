using GumpStudio.Core.Export;
using GumpStudio.Plugins;

using Xunit;

namespace GumpStudio.Plugins.Sphere.Tests;

/// <summary>
/// Loads the plugin the way the application does.
/// </summary>
/// <remarks>
/// Exercises the thing most likely to break for a newly added plugin: loading it
/// into its own <see cref="System.Runtime.Loader.AssemblyLoadContext"/> while
/// still resolving the shared contract types to the host's copies. Get that
/// wrong and the plugin implements an <c>IGumpStudioPlugin</c> the host does not
/// recognise, and it silently never appears in the export menu.
/// </remarks>
public class PluginLoaderTests
{
    private static string PluginAssemblyPath =>
        Path.Combine(AppContext.BaseDirectory, "GumpStudio.Plugins.Sphere.dll");

    [Fact]
    public void LoadsFromDiskAndRegistersItsExporters()
    {
        using PluginLoader loader = new();

        DiscoveredPlugin plugin = Assert.Single(loader.Load(PluginAssemblyPath));

        Assert.True(plugin.IsUsable, plugin.Error);

        RecordingHost host = new();

        plugin.Instance!.Initialize(host);

        Assert.Equal(2, host.Exporters.Count);

        // Producing output proves the contract types resolved to the host's
        // copies rather than duplicates loaded into the plugin's own context.
        foreach (IGumpExporter exporter in host.Exporters)
        {
            Assert.NotEmpty(exporter.Export(
                new Core.Document.GumpDocument(),
                new GumpExportOptions { GumpName = "Test" }));
        }
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
