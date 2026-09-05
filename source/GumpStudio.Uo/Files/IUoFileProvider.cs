namespace GumpStudio.Uo.Files;

/// <summary>
/// Random access to an indexed client data container.
/// </summary>
/// <remarks>
/// Implemented once for legacy <c>.idx</c>/<c>.mul</c> pairs and once for
/// <c>.uop</c> packages. Consumers such as the art and gump decoders are written
/// against this interface and do not know which container backs them.
/// </remarks>
public interface IUoFileProvider : IDisposable
{
    /// <summary>Number of index slots, including empty ones.</summary>
    int Count { get; }

    /// <summary>Describes the slot at <paramref name="index"/> without reading its data.</summary>
    /// <remarks>
    /// Art browsers use this to show dimensions for thousands of entries without
    /// decoding any of them.
    /// </remarks>
    UoFileEntry GetEntry(int index);

    /// <summary>
    /// Reads the raw bytes of a record.
    /// </summary>
    /// <returns>
    /// The record's bytes, or an empty span when the slot is empty or the index
    /// is out of range. Never throws for a missing entry.
    /// </returns>
    ReadOnlyMemory<byte> Read(int index);
}
