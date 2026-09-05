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

/// <summary>Settings common to every exporter.</summary>
/// <param name="GumpName">Identifier used for the generated gump or function.</param>
/// <param name="Namespace">Namespace or scope, where the target language has one.</param>
/// <param name="IncludeComments">Whether to emit element comments into the output.</param>
public readonly record struct GumpExportOptions(
    string GumpName = "MyGump",
    string Namespace = "Gumps",
    bool IncludeComments = true);
