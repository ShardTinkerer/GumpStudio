using Avalonia;

namespace GumpStudio.App;

internal static class Program
{
    // Avalonia needs to be initialised before any of its types are referenced,
    // so keep this free of anything that touches the UI.
    [STAThread]
    public static int Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // Referenced by name by the Avalonia XAML previewer and designer tooling.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
