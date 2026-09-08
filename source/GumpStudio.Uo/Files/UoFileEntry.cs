namespace GumpStudio.Uo.Files;

/// <summary>
/// One indexed record inside a client data container.
/// </summary>
/// <param name="Offset">Byte offset of the record's data, or a negative value when the slot is empty.</param>
/// <param name="Length">Length of the record's data in bytes.</param>
/// <param name="Extra">
/// Format-specific metadata. For gumps this packs the image dimensions: width in
/// the high 16 bits, height in the low 16.
/// </param>
/// <param name="Source">Which container the data actually lives in.</param>
public readonly record struct UoFileEntry(long Offset, int Length, int Extra, UoEntrySource Source)
{
    /// <summary>An absent record.</summary>
    public static UoFileEntry Missing { get; } = new(-1, 0, 0, UoEntrySource.None);

    /// <summary>True when this slot holds data.</summary>
    public bool Exists => Offset >= 0 && Length > 0;

    /// <summary>Gump width, decoded from <see cref="Extra"/>.</summary>
    public int ExtraWidth => (Extra >> 16) & 0xFFFF;

    /// <summary>Gump height, decoded from <see cref="Extra"/>.</summary>
    public int ExtraHeight => Extra & 0xFFFF;
}

/// <summary>Which file an entry's bytes come from.</summary>
public enum UoEntrySource
{
    /// <summary>The slot is empty.</summary>
    None = 0,

    /// <summary>The primary <c>.mul</c> or <c>.uop</c> container.</summary>
    Primary,

    /// <summary>A <c>verdata.mul</c> patch overriding the primary container.</summary>
    Verdata,
}
