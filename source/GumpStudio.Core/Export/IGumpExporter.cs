using GumpStudio.Core.Document;

namespace GumpStudio.Core.Export;

/// <summary>Turns a document into server-side script.</summary>
/// <remarks>
/// Implementations walk the document with an <see cref="Elements.IElementVisitor"/>
/// and must read positions through <see cref="Elements.Element.GetAbsolutePosition"/>.
/// </remarks>
public interface IGumpExporter
{
    /// <summary>Stable identifier, used to persist which exporter the user picked.</summary>
    string Id { get; }

    /// <summary>Name shown in the export menu.</summary>
    string DisplayName { get; }

    /// <summary>Default file extension, including the leading dot.</summary>
    string FileExtension { get; }

    /// <summary>Produces the script for a document.</summary>
    string Export(GumpDocument document, GumpExportOptions options);
}

/// <summary>
/// Settings common to every exporter.
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
}
