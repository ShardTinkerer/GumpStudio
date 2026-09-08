using SkiaSharp;

namespace GumpStudio.Rendering;

/// <summary>Shared drawing settings for client art.</summary>
public static class PixelArt
{
    /// <summary>
    /// Nearest-neighbour sampling with no mipmaps.
    /// </summary>
    /// <remarks>
    /// Gump art is low-resolution pixel art meant to be shown at its native size.
    /// Skia's default sampling smooths it, which softens every hard edge and makes
    /// a one-pixel border disappear. Every art draw must pass this.
    /// </remarks>
    public static SKSamplingOptions Sampling { get; } = new(SKFilterMode.Nearest, SKMipmapMode.None);
}
