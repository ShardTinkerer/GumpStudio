using GumpStudio.Core.Document;
using GumpStudio.Core.Export;

namespace GumpStudio.Plugins.RunUo;

/// <summary>The RunUO exporter, exposed through the exporter contract.</summary>
public sealed class RunUoExporter(RunUoButtonIdStyle buttonIdStyle = RunUoButtonIdStyle.Named)
    : IGumpExporter
{
    public string Id => buttonIdStyle == RunUoButtonIdStyle.Named ? "runuo" : "runuo-numeric";

    public string DisplayName => buttonIdStyle == RunUoButtonIdStyle.Named
        ? "RunUO C# gump"
        : "RunUO C# gump (numeric ids)";

    public string FileExtension => ".cs";

    public string Export(GumpDocument document, GumpExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        return RunUoScriptBuilder.Build(document, new RunUoExportOptions
        {
            ButtonIdStyle = buttonIdStyle,
            ClassName = options.GumpName,
            Namespace = options.Namespace,
            IncludeComments = options.IncludeComments,
        });
    }
}

/// <summary>
/// Plugin entry point for the RunUO exporter.
/// </summary>
/// <remarks>
/// Registers both id styles as separate exporters. The original put the choice in
/// a modal options dialog the plugin built itself; the shell owns the menu now,
/// and there is no options dialog to hang a radio button off, so the two forms
/// are two entries.
/// </remarks>
public sealed class RunUoExporterPlugin : IGumpStudioPlugin
{
    public PluginInfo Info => new(
        Id: "gumpstudio.exporters.runuo",
        Name: "RunUO gump exporter",
        Version: "2.0",
        Author: "roadmaster / Mark Sweetman, after Daegon / Eric Brown",
        Description: "Exports a gump as a C# Gump subclass for RunUO and ServUO cores.");

    public void Initialize(IPluginHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        host.RegisterExporter(new RunUoExporter(RunUoButtonIdStyle.Named));
        host.RegisterExporter(new RunUoExporter(RunUoButtonIdStyle.Numeric));
    }
}
