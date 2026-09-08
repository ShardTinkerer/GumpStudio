using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace GumpStudio.App.Controls;

/// <summary>
/// The artwork recovered from GumpStudio 1.8.
/// </summary>
/// <remarks>
/// <para>
/// The splash graphic is the JPEG that 1.8 embedded in both its splash form and
/// its about box, kept byte for byte — artwork credited to Melanius in the
/// original's own about text. It is loaded through Avalonia's asset system
/// rather than from a file beside the executable, so a single-file or NativeAOT
/// publish carries it too.
/// </para>
/// <para>
/// Decoded once and held for the life of the process. Both windows that show it
/// are transient, and a <see cref="Bitmap"/> is not owned by the
/// <c>Image</c> displaying it, so caching costs one decode instead of one per
/// window. It is deliberately never disposed: it lives exactly as long as the
/// application does.
/// </para>
/// </remarks>
internal static class Artwork
{
    private const string SplashUri = "avares://GumpStudio.App/Assets/splash.jpg";

    private static Bitmap? _splash;

    /// <summary>The 454x158 "Gump Studio .NET" graphic.</summary>
    public static Bitmap Splash() =>
        _splash ??= new Bitmap(AssetLoader.Open(new Uri(SplashUri)));
}
