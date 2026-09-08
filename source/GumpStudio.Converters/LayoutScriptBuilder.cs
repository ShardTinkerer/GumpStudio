using System.Globalization;
using System.Text;

using GumpStudio.Core.Document;
using GumpStudio.Core.Layout;
using GumpStudio.Core.Primitives;

namespace GumpStudio.Converters;

/// <summary>
/// Writes a gump as the client's own layout text.
/// </summary>
/// <remarks>
/// <para>
/// The interchange form: the command list the client's parser reads, followed by
/// the data array the commands index into. It is what shard authors already paste
/// between tools and into forum posts, and it is the one output that belongs to
/// no particular server — every emulator ultimately sends the client this.
/// </para>
/// <para>
/// It is also the plainest view of what the editor thinks a gump is, which makes
/// it the quickest way to check a suspected export defect without reading
/// generated C# or POL.
/// </para>
/// </remarks>
public static class LayoutScriptBuilder
{
    /// <summary>
    /// The grammar as the client defines it, with nothing bent to suit a server.
    /// </summary>
    /// <remarks>
    /// Lowercase <c>group</c> and a closing <c>endgroup</c>: the client accepts
    /// both, and closing the group is what stops a radio placed after the last
    /// group on a page silently joining it.
    /// </remarks>
    private static readonly LayoutStringOptions Options = new();

    /// <summary>Builds the layout text for a document.</summary>
    /// <param name="document">The gump to export.</param>
    /// <param name="timestamp">Header timestamp; injectable for reproducible output.</param>
    public static string Build(GumpDocument document, DateTimeOffset? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        return Build(GumpLayoutBuilder.Build(document), timestamp);
    }

    /// <summary>Builds the layout text for a layout that has already been produced.</summary>
    /// <param name="layout">The gump to export.</param>
    /// <param name="timestamp">Header timestamp; injectable for reproducible output.</param>
    public static string Build(GumpLayout layout, DateTimeOffset? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(layout);

        StringBuilder script = new();
        string stamp = (timestamp ?? DateTimeOffset.Now).ToString("u", CultureInfo.InvariantCulture);

        script.AppendLine(CultureInfo.InvariantCulture, $"// Created {stamp}, with Gump Studio.");
        script.AppendLine("// Raw client layout: the commands, then the strings they index.");
        script.AppendLine();

        GumpPoint at = layout.Properties.Location;

        script.AppendLine(CultureInfo.InvariantCulture, $"// Opens at {at.X},{at.Y}");

        // The client has no command for these three; they travel in the packet
        // that delivers the gump, so they are written as a note rather than as
        // layout the parser would reject.
        script.AppendLine(CultureInfo.InvariantCulture,
            $"// movable={Flag(layout.Properties.Movable)} closable={Flag(layout.Properties.Closable)} disposable={Flag(layout.Properties.Disposable)}");
        script.AppendLine();

        script.AppendLine("[layout]");

        foreach (string token in LayoutStringWriter.GumpLevelTokens(layout.Properties))
        {
            script.AppendLine("{ " + token + " }");
        }

        foreach (string line in LayoutStringWriter.Write(layout, Options, LayoutStringWriter.ByIndex))
        {
            script.AppendLine("{ " + line + " }");
        }

        script.AppendLine();
        script.AppendLine("[data]");

        for (int i = 0; i < layout.Texts.Count; i++)
        {
            script.AppendLine(CultureInfo.InvariantCulture, $"{i}\t{Sanitise(layout.Texts[i].Value)}");
        }

        return script.ToString();
    }

    /// <summary>
    /// Keeps one command on one line.
    /// </summary>
    /// <remarks>
    /// A layout string is line-based, so a newline inside a data entry would read
    /// as the start of the next one.
    /// </remarks>
    private static string Sanitise(string value) =>
        value
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

    private static string Flag(bool value) => value ? "1" : "0";
}
