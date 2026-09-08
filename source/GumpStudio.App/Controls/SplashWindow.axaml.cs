using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace GumpStudio.App.Controls;

/// <summary>
/// The 1.8 splash graphic, shown while the editor starts.
/// </summary>
/// <remarks>
/// <para>
/// Borderless, centred, always on top and gone after a couple of seconds, which
/// is what the original did. It is nostalgia rather than progress reporting —
/// the editor does not wait for it, and closing it early costs nothing.
/// </para>
/// <para>
/// The original ran its splash on a second thread and pumped
/// <c>Application.DoEvents()</c> in a sleep loop to keep it painting. Here it is
/// an ordinary window on the UI thread with a timer, because Avalonia keeps
/// drawing it while the main window is being built.
/// </para>
/// </remarks>
public sealed partial class SplashWindow : Window
{
    /// <summary>How long the splash stays up, matching the original's two seconds.</summary>
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(2);

    private readonly DispatcherTimer _life;

    private bool _closing;

    public SplashWindow()
    {
        AvaloniaXamlLoader.Load(this);

        this.FindControl<Image>("Graphic")!.Source = Artwork.Splash();

        _life = new DispatcherTimer { Interval = Duration };
        _life.Tick += (_, _) => Dismiss();

        // Clicking it away was the original's only interaction, and it is the
        // right one: nobody wants to look at a splash screen twice.
        PointerPressed += (_, _) => Dismiss();
        KeyDown += (_, _) => Dismiss();
    }

    /// <summary>Shows the splash and starts its countdown.</summary>
    public void Begin()
    {
        Show();

        _life.Start();
    }

    /// <summary>
    /// Closes the splash, once.
    /// </summary>
    /// <remarks>
    /// Guarded because the timer and a click can both arrive, and closing a
    /// window twice is not free — the second call raises the closing events
    /// again on a window that is already gone.
    /// </remarks>
    public void Dismiss()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;

        _life.Stop();
        Close();
    }
}
