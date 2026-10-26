using Avalonia;
using Videoprep.App.Cli;
using Videoprep.Core.Engine;
using Videoprep.Core.Logging;
using Videoprep.Core.Storage;
using Videoprep.Core.Tools;

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
        if (args.Length > 0 && args[0] == "--check")
        {
            ConsoleHost.Attach();
            var paths = AppPaths.ForCurrentUser();
            var checkLog = new Log(paths.LogsDir, filePrefix: "check");
            checkLog.LineAdded += line => { try { Console.WriteLine(line); } catch (IOException) { } };
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                return CheckCommand.RunAsync(paths, checkLog, new ProcessRunner(), new ToolManager(paths.ToolsDir, http),
                                             () => CookieStrategy.InstalledBrowsers(), cts.Token).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                var file = checkLog.Crash(ex, "--check");
                checkLog.Write("Unexpected error: " + ex.Message + (file is not null ? " - details in " + file : ""));
                return 1;
            }
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Also used by the designer and by the headless tests.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();   // UI errors: App.OnFrameworkInitializationCompleted logs them and shows a friendly message.
}
