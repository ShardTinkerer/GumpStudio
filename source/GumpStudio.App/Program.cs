using Avalonia;

namespace GumpStudio.App;

internal static class Program
{
    // Avalonia needs to be initialised before any of its types are referenced,
    // so keep this free of anything that touches the UI.
    /// <summary>A document path passed on the command line, opened at startup.</summary>
    public static string? StartupDocument { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        StartupDocument = args.FirstOrDefault(a => !a.StartsWith('-') && File.Exists(a));

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Referenced by name by the Avalonia XAML previewer and designer tooling.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
