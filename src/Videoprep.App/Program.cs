using Avalonia;
using Avalonia.Threading;
using Videoprep.App.Cli;
using Videoprep.Core.Logging;
using Videoprep.Core.Storage;

namespace Videoprep.App;

internal static class Program
{
    /// <summary>Shared engine log (English lines, daily file in %LOCALAPPDATA%\videoprep\logs).</summary>
    public static Log Log { get; } = new(AppPaths.ForCurrentUser().LogsDir);

    [STAThread]
    public static int Main(string[] args)
    {
        // AUDIT H4: nothing may end in the raw .NET error dialog; everything is logged with its stack trace.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) Log.Crash(ex, "unhandled exception");
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Crash(e.Exception, "background task");
            e.SetObserved();
        };

        if (args.Length > 0 && args[0] == "--demo")
        {
            ConsoleHost.Attach();
            try
            {
                return DemoCommand.RunAsync(args[1..]).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                var file = Log.Crash(ex, "--demo");
                Console.WriteLine($"Error inesperado: {ex.Message}");
                if (file is not null) Console.WriteLine($"Detalles en: {file}");
                return 1;
            }
        }
        // `videoprep --check` (headless subscription check) arrives in phase 5.

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Also used by the designer and by the headless tests.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .AfterSetup(_ => Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                // Errors in buttons and other UI handlers: log them and keep the window alive.
                // The friendly message box comes with the phase 4 screens.
                Log.Crash(e.Exception, "user interface");
                e.Handled = true;
            });
}
