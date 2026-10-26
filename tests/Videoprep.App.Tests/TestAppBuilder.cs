using Avalonia;
using Avalonia.Headless;
using Videoprep.App.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]
// The UI texts (Loc.Instance) and the theme are app-wide: these tests run one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Videoprep.App.Tests;

public static class TestAppBuilder
{
    // Real Skia rendering (not the fake drawing backend) so tests can save screenshots.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
