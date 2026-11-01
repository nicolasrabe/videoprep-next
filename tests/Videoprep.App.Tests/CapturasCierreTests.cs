using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Videoprep.App.Services;
using Videoprep.App.ViewModels;
using Videoprep.App.Views;
using Videoprep.Core.Profiles;
using Videoprep.Core.Scraping;
using Videoprep.Core.Storage;
using Videoprep.Core.Themes;

namespace Videoprep.App.Tests;

/// <summary>
/// Closing of 0.3.1 (spec 3.2): a picture of every screen that changed in the version, in the 6 themes and both languages
/// (<c>docs/capturas/0.3.1/</c>). Run by hand, not in the CI (it makes 120 pictures: 10 screens x 6 themes x 2 languages):
/// <c>dotnet test tests/Videoprep.App.Tests -c Release --filter "Capturas=si"</c> (set VIDEOPREP_CAPTURES_DIR to choose the folder).
/// One test goes through all the combinations (no new test per combination, so nothing is set up and torn down 12 times).
/// Besides the pictures it checks every screen for texts that were never translated.
/// </summary>
[Collection("fake yt-dlp environment")]
[Trait("Category", "Integration")]
[Trait("Capturas", "si")]
public sealed class CapturasCierreTests : IDisposable
{
    private static readonly string[] Themes = ["light", "dark", "beige", "gray", "slate", "custom"];
    private static readonly string[] Langs = ["es", "en"];
    private const string Bunny = "https://www.youtube.com/watch?v=aqz-KE-bpKQ";
    private const string Zoo = "https://www.youtube.com/watch?v=jNQXAC9IVRw";

    private readonly string _listing = Path.Combine(Path.GetTempPath(), "videoprep-capturas-" + Guid.NewGuid().ToString("N")[..8]);

    public CapturasCierreTests()
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

    private static string OutDir()
    {
        var dir = Environment.GetEnvironmentVariable("VIDEOPREP_CAPTURES_DIR") is { Length: > 0 } d ? d : Path.Combine(AppContext.BaseDirectory, "screenshots", "0.3.1");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Save(TopLevel window, string path, List<string> problems)
    {
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame();
        if (frame is null) { problems.Add("no picture: " + Path.GetFileName(path)); return; }
        frame.Save(path, PngBitmapEncoderOptions.Default);
        foreach (var t in window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? ""))
            if (t.Contains("{0}", StringComparison.Ordinal) || System.Text.RegularExpressions.Regex.IsMatch(t, "^[a-z]{1,8}_[a-zA-Z0-9_]+$"))
                problems.Add($"{Path.GetFileName(path)}: untranslated text \"{t}\"");
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
    public async Task Every_changed_screen_in_every_theme_and_language()
    {
        var dir = OutDir();
        var problems = new List<string>();
        var made = new List<string>();
        foreach (var lang in Langs)
            foreach (var theme in Themes)
            {
                using var rig = new TestRig(tools: true);
                Loc.Instance.Language = lang;
                SettingsStore.Save(rig.Paths.SettingsFile, new Settings { Lang = lang });
                var store = ProfileStore.Load(rig.Paths.ProfilesFile, out _);
                store.Add(new FormatProfile { Name = lang == "es" ? "Entrevistas HD" : "HD interviews", Format = "mp4", Cap = 1080 });
                SubscriptionStore.Save(rig.Paths.SubscriptionsFile, new SubscriptionFile
                {
                    Subscriptions =
                    [
                        new Subscription { Id = "sub_1", Name = "@BlenderStudio", Url = "https://www.youtube.com/@BlenderStudio", Folder = "D:\\Videos",
                                           LastCheck = "2026-10-30T09:14:00-03:00", LastNew = 2, FirstRun = Subscription.FirstRunSinceDate, FirstRunSince = "2026-10-23" },
                    ],
                });
                var window = new MainWindow { Width = 1100, Height = 780 };
                var vm = new MainViewModel(rig.Services, window, a => Dispatcher.UIThread.Post(a));
                vm.Settings.CustomColor = "#3a2f4a";
                vm.Settings.Theme = theme;
                vm.Settings.ScrapingLegalShown = true;
                vm.Folder = rig.OutFolder;
                ThemeService.Apply(Application.Current!, Palette.For(theme, "#3a2f4a"));
                window.DataContext = vm;
                window.Show();
                Dispatcher.UIThread.RunJobs();
                string P(string screen)
                {
                    var path = Path.Combine(dir, $"{lang}-{theme}-{screen}.png");
                    made.Add(path);
                    return path;
                }

                // Prep: without and with fragments
                vm.SmartPaste(Zoo);
                Save(window, P("prep-sin-fragmentos"), problems);
                vm.AddClipCommand.Execute(null);
                vm.AddClipCommand.Execute(null);
                vm.Clips[0].Start = "00:00:10:00";
                vm.Clips[0].End = "00:00:20:00";
                vm.Clips[0].Name = lang == "es" ? "Entrada" : "Intro";
                Save(window, P("prep-con-fragmentos"), problems);
                vm.Clips.Clear();

                // Queue with one row open (the card shared with Scraping)
                vm.SmartPaste(Bunny + "\n" + Zoo);
                vm.SelectedTab = MainViewModel.TabQueue;
                Dispatcher.UIThread.RunJobs();
                vm.ToggleRow(vm.QueueRows[0]);
                Save(window, P("cola-fila-abierta"), problems);

                // Scraping: link, filters, results with an open row
                vm.SelectedTab = MainViewModel.TabScraping;
                vm.Scraping.ShowExploreView();
                var ex = vm.Scraping.Explore;
                ex.Today = () => new DateOnly(2026, 10, 26);
                ex.LoadDelay = TimeSpan.Zero;
                Save(window, P("scraping-1-enlace"), problems);
                ex.SourceText = "@fake";
                await ex.LoadTask;
                await Until(() => ex.HasSource);
                ex.Filter.FromText = "2026-10-01";
                ex.Filter.ToText = "2026-10-31";
                Save(window, P("scraping-2-filtros"), problems);
                await ex.SearchCommand.ExecuteAsync(null);
                await Until(() => !ex.IsSearching);
                if (ex.Rows.Count > 0) ex.OpenRow(ex.Rows[0]);
                Save(window, P("scraping-3-resultados"), problems);

                // Subscriptions and its dialog (the first-check choices)
                vm.Scraping.ShowSubscriptionsView();
                Save(window, P("suscripciones"), problems);
                var edit = new SubscriptionEditViewModel(null, _ => Task.FromResult<string?>(null), vm.ProfileList()) { Url = "https://www.youtube.com/@fake", Folder = "D:\\Videos" };
                var dialog = new Window { Width = 780, Height = 780, Content = new ScrollViewer { Content = new SubscriptionEditView { DataContext = edit } } };
                dialog.Show();
                Save(dialog, P("suscripcion-dialogo"), problems);
                dialog.Close();

                // The small windows
                vm.SelectedTab = MainViewModel.TabDownload;
                var mini = new MiniWindow { DataContext = vm };
                vm.ToggleMiniCommand.Execute(null);
                mini.ApplyLevel(MiniLevel.Mini);
                mini.Show();
                vm.MiniHasFragment = true;
                Save(mini, P("mini-ficha"), problems);
                vm.DemoteMiniCommand.Execute(null);
                mini.ApplyLevel(MiniLevel.Micro);
                Save(mini, P("mini-micro"), problems);
                mini.CloseForReal();
                vm.CloseMiniCommand.Execute(null);
                window.Close();
            }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.Equal(120, made.Count);
        Assert.All(made, f => Assert.True(File.Exists(f), f));
    }
}
