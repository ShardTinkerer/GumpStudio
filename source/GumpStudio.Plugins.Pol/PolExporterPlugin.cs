using GumpStudio.Core.Document;
using GumpStudio.Core.Export;

namespace GumpStudio.Plugins.Pol;

/// <summary>The POL exporter, exposed through the exporter contract.</summary>
public sealed class PolExporter : IGumpExporter
{
    /// <summary>POL-specific settings, set by the export dialog.</summary>
    public PolExportOptions Options { get; set; } = new();

    public string Id => "pol";

    public string DisplayName => "POL script";

    public string FileExtension => ".src";

    public string Export(GumpDocument document, GumpExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);

        return PolScriptBuilder.Build(
            document,
            options.GumpName,
            Options with
            {
                IncludeComments = options.IncludeComments,
                IncludeNames = options.IncludeComments,
            });
    }
}

/// <summary>
/// Plugin entry point for the POL exporter.
/// </summary>
/// <remarks>
/// Registers an exporter and nothing else. The original also built its own
/// WinForms menu item and modal dialog; the shell owns both now, driven by the
/// declarative contract.
/// </remarks>
public sealed class PolExporterPlugin : IGumpStudioPlugin
{
    public PluginInfo Info => new(
        Id: "gumpstudio.exporters.pol",
        Name: "POL gump exporter",
        Version: "2.0",
        Author: "Fernando Rozenblit, based on the Sphere exporter by Francesco Furiani and Mark Chandler",
        Description: "Exports a gump as a POL script, in either the gump-package or layout-string dialect.");

    public void Initialize(IPluginHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        host.RegisterExporter(new PolExporter());
    }
}
