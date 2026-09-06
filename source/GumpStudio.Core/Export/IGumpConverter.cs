using GumpStudio.Core.Document;
using GumpStudio.Core.Layout;

namespace GumpStudio.Core.Export;

/// <summary>
/// Turns a gump layout into script for one server.
/// </summary>
/// <remarks>
/// <para>
/// A converter reads <see cref="GumpLayout"/>, not the document. What a gump
/// means is settled once by <see cref="GumpLayoutBuilder"/>; what is left here is
/// how one target spells it. That is the whole of the split, and it is why adding
/// a server is one file with no document-model knowledge in it.
/// </para>
/// <para>
/// This replaced a plugin contract. The three shipped converters used to arrive
/// as external assemblies through an <c>AssemblyLoadContext</c>, which a NativeAOT
/// image cannot do at all — so that build had a second registration path guarded
/// by <c>#if</c>, and a new converter had to be added in three places or it went
/// missing from one of them.
/// </para>
/// </remarks>
public interface IGumpConverter
{
    /// <summary>Stable identifier, used by the CLI and to persist the user's choice.</summary>
    string Id { get; }

    /// <summary>Name shown in the export menu.</summary>
    string DisplayName { get; }

    /// <summary>Default file extension, including the leading dot.</summary>
    string FileExtension { get; }

    /// <summary>
    /// The dialects this converter can emit, most common first.
    /// </summary>
    /// <remarks>
    /// Empty when there is only one form. Each of these used to be a separate
    /// menu entry, which made the export list read as six unrelated formats
    /// rather than four servers.
    /// </remarks>
    IReadOnlyList<ConverterDialect> Dialects { get; }

    /// <summary>Produces the script for a layout.</summary>
    string Convert(GumpLayout layout, GumpExportOptions options);
}

/// <summary>One of a converter's output dialects.</summary>
public readonly record struct ConverterDialect(string Id, string DisplayName);

/// <summary>Convenience for callers that hold a document rather than a layout.</summary>
public static class GumpConverterExtensions
{
    /// <summary>Builds the layout for a document and converts it.</summary>
    public static string Export(
        this IGumpConverter converter, GumpDocument document, GumpExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(converter);

        return converter.Convert(GumpLayoutBuilder.Build(document), options);
    }

    /// <summary>
    /// The dialect the options select, or the converter's default.
    /// </summary>
    /// <remarks>
    /// An unknown id falls back to the default rather than throwing: the choice
    /// is persisted between sessions, and a converter can stop offering a dialect.
    /// </remarks>
    public static string ResolveDialect(this IGumpConverter converter, GumpExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(converter);
        ArgumentNullException.ThrowIfNull(options);

        if (converter.Dialects.Count == 0)
        {
            return string.Empty;
        }

        foreach (ConverterDialect dialect in converter.Dialects)
        {
            if (string.Equals(dialect.Id, options.Dialect, StringComparison.Ordinal))
            {
                return dialect.Id;
            }
        }

        return converter.Dialects[0].Id;
    }
}

/// <summary>
/// Settings common to every converter.
/// </summary>
/// <remarks>
/// A record class, not a struct: defaulted primary-constructor parameters on a
/// struct are skipped by <c>default</c>, which would leave the name null.
/// </remarks>
public sealed record GumpExportOptions
{
    /// <summary>Identifier used for the generated gump or function.</summary>
    public string GumpName { get; init; } = "MyGump";

    /// <summary>Namespace or scope, where the target language has one.</summary>
    public string Namespace { get; init; } = "Gumps";

    /// <summary>Whether to emit element comments into the output.</summary>
    public bool IncludeComments { get; init; } = true;

    /// <summary>
    /// Which of the converter's dialects to emit, or null for its default.
    /// </summary>
    public string? Dialect { get; init; }
}
