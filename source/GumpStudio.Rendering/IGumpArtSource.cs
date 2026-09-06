using GumpStudio.Core.Elements;

using SkiaSharp;

namespace GumpStudio.Rendering;

/// <summary>
/// Supplies the client art the renderer draws.
/// </summary>
/// <remarks>
/// An interface rather than a direct dependency on the data layer, so the
/// renderer can be tested against synthetic art with no UO installation present.
/// Returned images are owned by the source and must not be disposed by callers.
/// </remarks>
public interface IGumpArtSource
{
    /// <summary>Gump art, optionally hued.</summary>
    SKImage? GetGump(int gumpId, int hue = 0, bool partialHue = false);

    /// <summary>Static item art, optionally hued.</summary>
    SKImage? GetItem(int itemId, int hue = 0, bool partialHue = false);

    /// <summary>
    /// Renders a run of text in one of the client's fonts.
    /// </summary>
    /// <remarks>
    /// The family defaults to Unicode, which is the only one the renderer used
    /// before the font became selectable.
    /// </remarks>
    SKImage? GetText(
        int fontIndex,
        string text,
        int hue = 0,
        GumpFontFamily family = GumpFontFamily.Unicode);

    /// <summary>Reads a gump's dimensions without decoding its pixels.</summary>
    bool TryGetGumpSize(int gumpId, out int width, out int height);

    /// <summary>
    /// The localised string for a cliloc id, or null when it cannot be resolved.
    /// </summary>
    /// <remarks>
    /// Default so a source that only supplies art keeps compiling. Returning null
    /// is the honest answer when no client is loaded, and the renderer falls back
    /// to showing the bare id.
    /// </remarks>
    string? GetCliloc(int clilocId) => null;
}
