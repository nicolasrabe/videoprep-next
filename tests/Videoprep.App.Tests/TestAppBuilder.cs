using Avalonia;
using Avalonia.Headless;
using Videoprep.App.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]
// One headless application for the whole run. The default (a new one per test) sometimes failed while setting the next one up
// ("Test Case Cleanup Failure ... a different thread owns it", inside Avalonia.Headless, about 1 run in 3 on the screen tests).
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerAssembly)]
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
