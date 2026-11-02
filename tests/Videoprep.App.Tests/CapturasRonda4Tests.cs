using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Videoprep.App.Services;
using Videoprep.App.ViewModels;
using Videoprep.App.Views;
using Videoprep.Core.Profiles;
using Videoprep.Core.Scraping;
using Videoprep.Core.Storage;
using Videoprep.Core.Themes;

namespace Videoprep.App.Tests;

/// <summary>
/// Round 4: one picture (dark theme, Spanish) of every screen that changed, in <c>docs/capturas/0.3.1/ronda-4/</c>. By hand, not in the CI:
/// <c>dotnet test tests/Videoprep.App.Tests -c Release --filter "Capturas=ronda4"</c> (VIDEOPREP_CAPTURES_DIR chooses the folder).
/// </summary>
[Collection("fake yt-dlp environment")]
[Trait("Category", "Integration")]
[Trait("Capturas", "ronda4")]
public sealed class CapturasRonda4Tests : IDisposable
{
    private const string Zoo = "https://www.youtube.com/watch?v=jNQXAC9IVRw";
    private const string Bunny = "https://www.youtube.com/watch?v=aqz-KE-bpKQ";
    private readonly string _listing = Path.Combine(Path.GetTempPath(), "videoprep-cap4-" + Guid.NewGuid().ToString("N")[..8]);

    public CapturasRonda4Tests()
    {
        Directory.CreateDirectory(_listing);
        foreach (var tab in new[] { "videos", "shorts", "streams", "playlists" })
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "listing", $"youtube-{tab}.jsonl"), Path.Combine(_listing, tab + ".jsonl"));
        File.WriteAllText(Path.Combine(_listing, "exact.txt"), "Q1fMMYyt0I4|20261012|855|not_live|public\n");
        Environment.SetEnvironmentVariable("FAKE_YTDLP_LISTING", _listing);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("FAKE_YTDLP_LISTING", null);
        try { Directory.Delete(_listing, true); } catch (IOException) { }
    }

    private static async Task Until(Func<bool> done)
    {
        for (var i = 0; i < 300 && !done(); i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(30);
        }
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task One_picture_of_each_changed_screen()
    {
        var dir = Environment.GetEnvironmentVariable("VIDEOPREP_CAPTURES_DIR") is { Length: > 0 } d ? d : Path.Combine(AppContext.BaseDirectory, "screenshots", "ronda-4");
        Directory.CreateDirectory(dir);
        using var rig = new TestRig(tools: true);
        Loc.Instance.Language = "es";
        SettingsStore.Save(rig.Paths.SettingsFile, new Settings { Lang = "es" });
        SubscriptionStore.Save(rig.Paths.SubscriptionsFile, new SubscriptionFile
        {
            Subscriptions =
            [
                new Subscription { Id = "s1", Name = "@BlenderStudio", Url = "https://www.youtube.com/@BlenderStudio", Folder = "D:\\Videos", LastCheck = "2026-10-30T09:14:00-03:00", LastNew = 2 },
                new Subscription { Id = "s2", Name = "@AgerGames", Url = "https://www.youtube.com/@AgerGames", Folder = "D:\\Videos", Enabled = false },
            ],
        });
        var window = new MainWindow { Width = 1100, Height = 780 };
        var vm = new MainViewModel(rig.Services, window, a => Dispatcher.UIThread.Post(a));
        vm.Settings.Theme = "dark";
        vm.Settings.ScrapingLegalShown = true;
        vm.Folder = rig.OutFolder;
        ThemeService.Apply(Application.Current!, Palette.For("dark", "#3a2f4a"));
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var made = new List<string>();
        void Save(string name)
        {
            Dispatcher.UIThread.RunJobs();
            var path = Path.Combine(dir, name + ".png");
            window.CaptureRenderedFrame()!.Save(path, PngBitmapEncoderOptions.Default);
            made.Add(path);
        }

        // Prep: the preview while it loads (no internet in the test: the title never arrives)
        vm.SmartPaste(Zoo);
        vm.PreviewLoading = true;
        vm.PreviewTitle = "";
        Save("prep-cargando");

        // Queue: the new bar with the counter and the grip column, with one row open
        vm.SmartPaste(Bunny + "\n" + Zoo);
        vm.SelectedTab = MainViewModel.TabQueue;
        Dispatcher.UIThread.RunJobs();
        Save("cola-barra-y-agarre");

        // Scraping: link, then filters folded + results taking the height
        vm.SelectedTab = MainViewModel.TabScraping;
        vm.Scraping.ShowExploreView();
        var ex = vm.Scraping.Explore;
        ex.Today = () => new DateOnly(2026, 10, 26);
        ex.LoadDelay = TimeSpan.Zero;
        ex.SourceText = "@fake";
        await ex.LoadTask;
        await Until(() => ex.HasSource);
        ex.Filter.FromText = "2026-10-01";
        ex.Filter.ToText = "2026-10-31";
        Save("scraping-filtros-desplegados");
        await ex.SearchCommand.ExecuteAsync(null);
        await Until(() => !ex.IsSearching);
        if (ex.Rows.Count > 0) ex.OpenRow(ex.Rows[0]);
        Save("scraping-filtros-plegados-resultados");
        ex.LinkExpanded = false;
        Save("scraping-enlace-plegado");

        vm.Scraping.ShowSubscriptionsView();
        Save("suscripciones");

        vm.SelectedTab = MainViewModel.TabSettings;
        Save("ajustes-grupos-plegables");
        vm.SelectedTab = MainViewModel.TabLog;
        for (var i = 1; i <= 40; i++) rig.Log.Write($"[{i:00}/40] ejemplo de linea del registro");
        Dispatcher.UIThread.RunJobs();
        Save("registro-compacto");
        window.Close();
        Assert.All(made, f => Assert.True(File.Exists(f), f));
        Assert.Equal(8, made.Count);
    }
}
