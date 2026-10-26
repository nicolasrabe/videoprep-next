using Avalonia;

namespace Videoprep.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // `videoprep --check` (headless subscription check) arrives in phase 5.
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Also used by the designer and by the headless tests.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
