namespace GumpStudio.Uo.Files;

/// <summary>
/// The <c>verdata.mul</c> file identifiers for the containers this application
/// reads. A patch record names its target container with one of these.
/// </summary>
public enum UoFileKind
{
    Art = 4,
    Gumps = 12,
    TileData = 30,
    Hues = 32,
}
