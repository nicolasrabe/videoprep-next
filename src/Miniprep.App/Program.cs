using Avalonia;
using Miniprep.App.Services;
using Videoprep.Core.Logging;

namespace Miniprep.App;

internal static class Program
{
    /// <summary>Log of miniprep (English lines, a daily file in %LOCALAPPDATA%\miniprep\logs).</summary>
    public static Log Log { get; } = new(MiniPaths.ForCurrentUser().LogsDir, filePrefix: "miniprep");

    [STAThread]
    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) Log.Crash(ex, "unhandled exception");
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Crash(e.Exception, "background task");
            e.SetObserved();
        };
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Also used by the headless tests.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
