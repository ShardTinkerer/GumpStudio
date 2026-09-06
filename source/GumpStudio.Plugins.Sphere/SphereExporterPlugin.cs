using GumpStudio.Core.Document;
using GumpStudio.Core.Export;

namespace GumpStudio.Plugins.Sphere;

/// <summary>The Sphere exporter, exposed through the exporter contract.</summary>
public sealed class SphereExporter(SphereDialect dialect = SphereDialect.Revision) : IGumpExporter
{
    public string Id => dialect == SphereDialect.Revision ? "sphere-056" : "sphere-099";

    public string DisplayName => dialect == SphereDialect.Revision
        ? "Sphere script (0.56 / Revisions)"
        : "Sphere script (0.99 / 1.0)";

    public string FileExtension => ".scp";

    public string Export(GumpDocument document, GumpExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        return SphereScriptBuilder.Build(document, new SphereExportOptions
        {
            Dialect = dialect,
            DialogName = options.GumpName,
            IncludeComments = options.IncludeComments,
        });
    }
}

/// <summary>
/// Plugin entry point for the Sphere exporter.
/// </summary>
/// <remarks>
/// Registers both dialects as separate exporters. The original asked which one to
/// use in a modal dialog the plugin built itself; the shell owns the menu now, so
/// the two forms are two entries.
/// </remarks>
public sealed class SphereExporterPlugin : IGumpStudioPlugin
{
    public PluginInfo Info => new(
        Id: "gumpstudio.exporters.sphere",
        Name: "Sphere gump exporter",
        Version: "2.0",
        Author: "Francesco Furiani",
        Description: "Exports a gump as a Sphere dialog script, in either the 0.56 or the 0.99 dialect.");

    public void Initialize(IPluginHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        host.RegisterExporter(new SphereExporter(SphereDialect.Revision));
        host.RegisterExporter(new SphereExporter(SphereDialect.Modern));
    }
}
