using GumpStudio.Core.Export;
using GumpStudio.Core.Layout;

namespace GumpStudio.Converters;

/// <summary>The converters that ship with the editor.</summary>
/// <remarks>
/// One list, referenced normally. The three script converters used to be plugin
/// assemblies discovered from a <c>Plugins</c> folder, which meant a NativeAOT
/// build — which cannot load an assembly at all — needed a second registration
/// path behind an <c>#if</c>, and the CLI a third hard-coded one. A new converter
/// added to two of the three went silently missing from the other.
/// </remarks>
public static class GumpConverters
{
    /// <summary>Every converter, in the order the export menu shows them.</summary>
    public static IReadOnlyList<IGumpConverter> All { get; } =
    [
        new LayoutConverter(),
        new PolConverter(),
        new RunUoConverter(),
        new SphereConverter(),
    ];

    /// <summary>Finds a converter by its id, or null.</summary>
    public static IGumpConverter? Find(string? id)
    {
        foreach (IGumpConverter converter in All)
        {
            if (string.Equals(converter.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return converter;
            }
        }

        return null;
    }
}

/// <summary>The client's own layout text, with no server wrapper.</summary>
public sealed class LayoutConverter : IGumpConverter
{
    public string Id => "layout";

    public string DisplayName => "Client layout (raw)";

    public string FileExtension => ".txt";

    /// <summary>None: there is only one client grammar.</summary>
    public IReadOnlyList<ConverterDialect> Dialects => [];

    public string Convert(GumpLayout layout, GumpExportOptions options) =>
        LayoutScriptBuilder.Build(layout);
}

/// <summary>POL, in either the distro gump package or raw layout strings.</summary>
public sealed class PolConverter : IGumpConverter
{
    /// <summary>The dialect id for the <c>GF*</c> gump-package form.</summary>
    public const string GumpPackage = "gump-package";

    /// <summary>The dialect id for the raw layout-string array.</summary>
    public const string LayoutStrings = "layout-strings";

    public string Id => "pol";

    public string DisplayName => "POL script";

    public string FileExtension => ".src";

    public IReadOnlyList<ConverterDialect> Dialects =>
    [
        new(GumpPackage, "Gump package"),
        new(LayoutStrings, "Layout strings"),
    ];

    public string Convert(GumpLayout layout, GumpExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return PolScriptBuilder.Build(layout, options.GumpName, new PolExportOptions
        {
            Style = this.ResolveDialect(options) == LayoutStrings
                ? PolScriptStyle.LayoutStrings
                : PolScriptStyle.GumpPackage,
            IncludeComments = options.IncludeComments,
            IncludeNames = options.IncludeComments,
        });
    }
}

/// <summary>RunUO and ServUO, with named or numeric response ids.</summary>
public sealed class RunUoConverter : IGumpConverter
{
    /// <summary>The dialect id for a generated <c>Buttons</c> enum.</summary>
    public const string Named = "named";

    /// <summary>The dialect id for plain integer response ids.</summary>
    public const string Numeric = "numeric";

    public string Id => "runuo";

    public string DisplayName => "RunUO C# gump";

    public string FileExtension => ".cs";

    public IReadOnlyList<ConverterDialect> Dialects =>
    [
        new(Named, "Named button ids"),
        new(Numeric, "Numeric button ids"),
    ];

    public string Convert(GumpLayout layout, GumpExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return RunUoScriptBuilder.Build(layout, new RunUoExportOptions
        {
            ButtonIdStyle = this.ResolveDialect(options) == Numeric
                ? RunUoButtonIdStyle.Numeric
                : RunUoButtonIdStyle.Named,
            ClassName = options.GumpName,
            Namespace = options.Namespace,
            IncludeComments = options.IncludeComments,
        });
    }
}

/// <summary>Sphere, in the 0.56 or the 0.99 dialect.</summary>
public sealed class SphereConverter : IGumpConverter
{
    /// <summary>The dialect id for 0.56 and the Revision builds.</summary>
    public const string Revision = "056";

    /// <summary>The dialect id for 0.99 and 1.0.</summary>
    public const string Modern = "099";

    public string Id => "sphere";

    public string DisplayName => "Sphere script";

    public string FileExtension => ".scp";

    public IReadOnlyList<ConverterDialect> Dialects =>
    [
        new(Revision, "0.56 / Revisions"),
        new(Modern, "0.99 / 1.0"),
    ];

    public string Convert(GumpLayout layout, GumpExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return SphereScriptBuilder.Build(layout, new SphereExportOptions
        {
            Dialect = this.ResolveDialect(options) == Modern
                ? SphereDialect.Modern
                : SphereDialect.Revision,
            DialogName = options.GumpName,
            IncludeComments = options.IncludeComments,
        });
    }
}
