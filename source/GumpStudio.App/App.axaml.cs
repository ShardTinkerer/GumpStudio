using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using GumpStudio.App.Controls;

namespace GumpStudio.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Shows the splash, and builds the editor once it has gone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The editor is deliberately not constructed until the splash closes.
    /// Building it is synchronous and takes long enough to matter, and the
    /// dispatcher cannot paint while it runs — so constructing it first made the
    /// splash appear at the same moment as the window it was supposed to
    /// precede, which is the one thing a splash screen must not do.
    /// </para>
    /// <para>
    /// While only the splash is open there is no main window, so the lifetime
    /// would treat the splash closing as the last window closing and shut the
    /// application down mid-startup. <see cref="ShutdownMode"/> is held at
    /// <c>OnExplicitShutdown</c> until the editor exists, then handed over to
    /// it.
    /// </para>
    /// <para>
    /// Clicking the splash away brings the editor up early, because the same
    /// close event drives both.
    /// </para>
    /// <para>
    /// None of this runs under test: it needs a classic desktop lifetime, and a
    /// headless session has none. The splash and about windows are exercised
    /// directly instead.
    /// </para>
    /// </remarks>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            SplashWindow splash = new();

            splash.Closed += (_, _) => Open(desktop);

            splash.Begin();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Builds the editor and makes it the window the application lives by.</summary>
    private static void Open(IClassicDesktopStyleApplicationLifetime desktop)
    {
        MainWindow editor = new();

        desktop.MainWindow = editor;
        desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;

        editor.Show();
    }
}
