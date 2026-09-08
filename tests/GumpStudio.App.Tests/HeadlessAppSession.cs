using Avalonia;
using Avalonia.Headless;

namespace GumpStudio.App.Tests;

/// <summary>
/// A headless Avalonia application, shared by every test that needs real
/// controls.
/// </summary>
/// <remarks>
/// The real <see cref="App"/> is used rather than a bare
/// <see cref="Application"/>, because the window under test is built from Dock's
/// controls and those need the themes <c>App.axaml</c> loads. Its
/// <c>OnFrameworkInitializationCompleted</c> only creates a main window for a
/// classic desktop lifetime, which a headless session does not provide, so
/// nothing there touches the real settings file.
///
/// One session for the whole assembly: it owns a dispatcher thread, and Avalonia
/// allows a single application per process.
/// </remarks>
internal static class HeadlessAppSession
{
    private static readonly Lock Sync = new();
    private static HeadlessUnitTestSession? _session;

    private static HeadlessUnitTestSession Session
    {
        get
        {
            lock (Sync)
            {
                return _session ??= HeadlessUnitTestSession.StartNew(typeof(App));
            }
        }
    }

    /// <summary>Runs an action on the UI thread of the headless application.</summary>
    public static void Run(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        Session.Dispatch(action, CancellationToken.None).GetAwaiter().GetResult();
    }
}
