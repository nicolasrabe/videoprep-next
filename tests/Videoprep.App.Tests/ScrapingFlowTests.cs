using System.Globalization;
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
using Videoprep.Core.Scraping;
using Videoprep.Core.Storage;
using Videoprep.Core.Themes;

namespace Videoprep.App.Tests;

/// <summary>
/// Scraping > Explorar in three steps (RONDA 1), driven like a user over saved listings and the fake yt-dlp (no internet):
/// the light first request, the strict date rule, the questions at 50 videos, Reiniciar, the selection group, the open rows.
/// </summary>
[Collection("fake yt-dlp environment")]
public sealed class ScrapingFlowTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 26);

    private readonly TestRig _rig = new(tools: true);
    private readonly string _listing;
    private readonly string _calls;

    public ScrapingFlowTests()
    {
        Loc.Instance.Language = "es";
        _listing = Path.Combine(_rig.Dir, "listing");
        Directory.CreateDirectory(_listing);
        foreach (var tab in new[] { "videos", "shorts", "streams", "playlists" })
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "listing", $"youtube-{tab}.jsonl"), Path.Combine(_listing, tab + ".jsonl"));
        _calls = Path.Combine(_rig.Dir, "calls.txt");
        Environment.SetEnvironmentVariable("FAKE_YTDLP_LISTING", _listing);
        Environment.SetEnvironmentVariable("FAKE_YTDLP_CALLS", _calls);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("FAKE_YTDLP_LISTING", null);
        Environment.SetEnvironmentVariable("FAKE_YTDLP_CALLS", null);
        Environment.SetEnvironmentVariable("FAKE_YTDLP_TOTAL", null);
        _rig.Dispose();
    }

    private string[] Calls() => File.Exists(_calls) ? File.ReadAllLines(_calls) : [];

    private static bool IsListingCall(string call) => call.Contains("--lazy-playlist", StringComparison.Ordinal);
    private static bool IsProbeCall(string call) => call.Contains("\u001f-J\u001f", StringComparison.Ordinal);
    private static bool IsExactCall(string call) => call.Contains("EX|", StringComparison.Ordinal);

    private MainViewModel _vm = null!;

    private ExploreViewModel NewExplore()
    {
        _vm = _rig.Create();
        var ex = _vm.Scraping.Explore;
        ex.Today = () => Today;
        ex.LoadDelay = TimeSpan.Zero;
        return ex;
    }

    private static async Task<ExploreViewModel> Found(ExploreViewModel ex, string source = "@fake")
    {
        ex.SourceText = source;
        await ex.LoadTask;
        await ex.SearchCommand.ExecuteAsync(null);
        return ex;
    }

    // ---- ① the link: one light request, no table yet

    [AvaloniaFact]
    public async Task Pasting_a_link_identifies_it_with_one_light_request_and_no_video()
    {
        var ex = NewExplore();
        ex.SourceText = "@fake";
        await ex.LoadTask;

        Assert.True(ex.HasSource);
        Assert.Equal(("Fake channel", "@fake"), (ex.SourceName, ex.SourceHandle));
        Assert.Empty(ex.Rows);
        Assert.False(ex.HasSearched);
        var call = Assert.Single(Calls());
        Assert.True(IsProbeCall(call) && !IsListingCall(call), call);
        Assert.Contains("\u001f-I\u001f0\u001f", call);   // asks for no video at all
        Assert.StartsWith("Videos de @fake", ex.SummaryText, StringComparison.Ordinal);
        Assert.Contains("sin límite de cantidad", ex.SummaryText);
    }

    [AvaloniaFact]
    public async Task The_Cargar_button_repeats_the_identification()
    {
        var ex = NewExplore();
        ex.SourceText = "@fake";
        await ex.LoadTask;
        await ex.LoadCommand.ExecuteAsync(null);
        Assert.Equal(2, Calls().Count(IsProbeCall));
    }

    [AvaloniaFact]
    public async Task Searching_after_loading_does_not_identify_again()
    {
        var ex = await Found(NewExplore());
        Assert.Equal(1, Calls().Count(IsProbeCall));
        Assert.Equal(3, ex.Rows.Count);
        Assert.True(ex.HasSearched);
        Assert.Equal("3 videos pasan los filtros (se revisaron 3)", ex.ResultsHeader);
    }

    // ---- ② filters

    [AvaloniaFact]
    public async Task Quick_buttons_fill_the_two_dates_and_the_summary_says_what_will_be_searched()
    {
        var ex = NewExplore();
        ex.SourceText = "@fake";
        await ex.LoadTask;

        ex.QuickDatesCommand.Execute("7d");
        Assert.Equal(("2026-10-19", "2026-10-26"), (ex.Filter.FromText, ex.Filter.ToText));
        Assert.True(ex.Preset7d);
        ex.QuickDatesCommand.Execute("24h");
        Assert.Equal(("2026-10-25", "2026-10-26"), (ex.Filter.FromText, ex.Filter.ToText));
        ex.QuickDatesCommand.Execute("month");
        Assert.Equal("2026-10-01", ex.Filter.FromText);
        Assert.True(ex.PresetMonth);

        ex.Filter.FromText = "2026-10-23";
        ex.Filter.ToText = "2026-10-27";
        Assert.True(ex.PresetBetween);
        Assert.Contains("publicados del 23 de octubre de 2026 al 27 de octubre de 2026", ex.SummaryText);
        ex.Filter.MaxCountText = "20";
        Assert.Contains("hasta 20 videos", ex.SummaryText);
        Assert.Contains("Sin cuenta ni cookies.", ex.EstimateText);
    }

    [AvaloniaFact]
    public async Task The_estimate_follows_the_filters()
    {
        var ex = NewExplore();
        ex.SourceText = "@fake";
        await ex.LoadTask;
        Assert.StartsWith("Pedidos estimados: 1 a 2.", ex.EstimateText, StringComparison.Ordinal);
        ex.Filter.MaxCountText = "10";
        Assert.StartsWith("Pedidos estimados: 1.", ex.EstimateText, StringComparison.Ordinal);
        ex.Filter.FromText = "2026-10-20";
        Assert.Contains("1 a 151", ex.EstimateText);   // one listing page plus the cap of date confirmations
    }

    // ---- ③ results: the strict date rule

    [AvaloniaFact]
    public async Task A_date_range_shows_only_confirmed_dates_and_lists_the_rest_apart()
    {
        // Shorts have no date. The exact step says: 22 Oct (inside), 16 Oct (outside), and the third has no date to give.
        File.WriteAllText(Path.Combine(_listing, "exact.txt"),
            "Qqmj29bOcLA|20261022|43|not_live|public\nWPSSn367NIA|20261016|48|not_live|public\nt3_O2dahOqw|NA|40|not_live|public\n");
        var ex = NewExplore();
        ex.TabShorts = true;
        ex.SourceText = "@fake";
        await ex.LoadTask;
        ex.Filter.FromText = "2026-10-20";
        ex.Filter.ToText = "2026-10-27";
        await ex.SearchCommand.ExecuteAsync(null);

        var row = Assert.Single(ex.Rows);
        Assert.Equal(("Qqmj29bOcLA", "2026-10-22"), (row.Entry.Id, row.DateText));   // no "~": confirmed
        Assert.Equal("t3_O2dahOqw", Assert.Single(ex.UnconfirmedRows).Entry.Id);
        Assert.False(ex.UnconfirmedRows[0].Selected);
        Assert.True(ex.HasUnconfirmed);
        Assert.Equal("1 video sin fecha confirmada", ex.UnconfirmedText);
        Assert.Equal("Fechas confirmadas: 1 de 2", ex.DatesText);
        Assert.Equal("1 video pasa los filtros (se revisaron 5)", ex.ResultsHeader);

        Assert.False(ex.ShowUnconfirmed);
        ex.ToggleUnconfirmedCommand.Execute(null);
        Assert.True(ex.ShowUnconfirmed);
        Assert.Equal("Ocultar", ex.UnconfirmedToggleText);

        // Sending to the queue never takes the unconfirmed one unless the person checks it.
        await ex.SendToQueueCommand.ExecuteAsync(null);
        Assert.EndsWith("Qqmj29bOcLA", Assert.Single(_vm.Items).Url, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task No_row_is_outside_the_range()
    {
        File.WriteAllText(Path.Combine(_listing, "exact.txt"), "Q1fMMYyt0I4|20261012|855|not_live|public\n");
        var ex = NewExplore();
        ex.Filter.FromText = "2026-10-10";
        ex.Filter.ToText = "2026-10-13";
        await Found(ex);
        var row = Assert.Single(ex.Rows);
        Assert.Equal("Q1fMMYyt0I4", row.Entry.Id);
        Assert.All(ex.Rows, r => Assert.InRange(r.Entry.Date!.Value, new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 13)));
    }

    [AvaloniaFact]
    public async Task Changing_a_filter_and_searching_again_makes_no_new_request()
    {
        var ex = await Found(NewExplore());
        var calls = Calls().Length;
        ex.Filter.MinText = "5:00";
        await ex.SearchCommand.ExecuteAsync(null);
        Assert.Equal(calls, Calls().Length);
        Assert.Equal("Q1fMMYyt0I4", Assert.Single(ex.Rows).Entry.Id);   // 685eur9lMGc lasts 4:42
    }

    // ---- more than 50 videos

    private void BigListing(int n)
    {
        var lines = Enumerable.Range(0, n).Select(i =>
            $"{{\"id\":\"vid{i:000}\",\"title\":\"Video {i:000}\",\"url\":\"https://www.youtube.com/watch?v=vid{i:000}\",\"ie_key\":\"Youtube\",\"duration\":600," +
            $"\"upload_date\":\"{Today.AddDays(-i):yyyyMMdd}\"}}");
        File.WriteAllLines(Path.Combine(_listing, "videos.jsonl"), lines);
    }

    [AvaloniaFact]
    public async Task At_fifty_videos_it_asks_once_and_the_person_can_stop_there()
    {
        BigListing(120);
        _rig.Ui.ThreeAnswers.Enqueue(1);   // "Dejar hasta acá"
        var ex = await Found(NewExplore());
        Assert.Equal("Ya van 50 videos y siguen apareciendo. ¿Seguir buscando?", Assert.Single(_rig.Ui.ThreeWay));
        Assert.Equal(50, ex.Rows.Count);
        Assert.Contains(Loc.Instance["sc_truncated"], ex.StatusLine);
    }

    [AvaloniaFact]
    public async Task At_fifty_videos_Seguir_reads_everything_and_does_not_ask_again()
    {
        BigListing(120);
        _rig.Ui.ThreeAnswers.Enqueue(0);   // "Seguir"
        var ex = await Found(NewExplore());
        Assert.Single(_rig.Ui.ThreeWay);
        Assert.Equal(120, ex.Rows.Count);
    }

    [AvaloniaFact]
    public async Task A_known_total_asks_before_searching_and_Cancelar_asks_for_no_video()
    {
        Environment.SetEnvironmentVariable("FAKE_YTDLP_TOTAL", "200");
        _rig.Ui.ThreeAnswers.Enqueue(2);   // Cancelar
        var ex = NewExplore();
        ex.SourceText = "@fake";
        await ex.LoadTask;
        await ex.SearchCommand.ExecuteAsync(null);
        Assert.Equal("Este canal tiene unos 200 videos. Buscarlos todos son unos 7 pedidos de listado.", Assert.Single(_rig.Ui.ThreeWay));
        Assert.Equal(0, Calls().Count(IsListingCall));
        Assert.Empty(ex.Rows);
    }

    [AvaloniaFact]
    public async Task Fifty_or_fewer_never_asks()
    {
        await Found(NewExplore());
        Assert.Empty(_rig.Ui.ThreeWay);
    }

    // ---- Reiniciar, selection

    [AvaloniaFact]
    public async Task Reiniciar_asks_only_when_something_is_checked_and_goes_back_to_step_one()
    {
        var ex = await Found(NewExplore());
        Assert.All(ex.Rows, r => Assert.True(r.Selected));

        _rig.Ui.ChoiceAnswers.Enqueue(false);   // the person says no
        await ex.ResetCommand.ExecuteAsync(null);
        Assert.Equal(3, ex.Rows.Count);
        Assert.Contains("videos marcados", Assert.Single(_rig.Ui.Choices));

        _rig.Ui.ChoiceAnswers.Enqueue(true);
        await ex.ResetCommand.ExecuteAsync(null);
        Assert.Empty(ex.Rows);
        Assert.False(ex.HasSearched);
        Assert.False(ex.HasSource);
        Assert.Equal("", ex.SourceText);
        Assert.Equal("", ex.Filter.FromText);
        Assert.True(ex.TabVideos);

        // Nothing checked and nothing running: no question.
        ex = await Found(ex);
        ex.SelectNoneCommand.Execute(null);
        var asked = _rig.Ui.Choices.Count;
        await ex.ResetCommand.ExecuteAsync(null);
        Assert.Equal(asked, _rig.Ui.Choices.Count);
        Assert.Empty(ex.Rows);
    }

    [AvaloniaFact]
    public async Task Reiniciar_forgets_what_was_read()
    {
        var ex = await Found(NewExplore());
        _rig.Ui.ChoiceAnswers.Enqueue(true);
        await ex.ResetCommand.ExecuteAsync(null);
        var before = Calls().Count(IsListingCall);
        await Found(ex);
        Assert.Equal(before + 1, Calls().Count(IsListingCall));
    }

    [AvaloniaFact]
    public async Task Selection_All_None_Invert()
    {
        var ex = await Found(NewExplore());
        ex.SelectNoneCommand.Execute(null);
        Assert.Equal("0 de 3 seleccionados", ex.SelectedText);
        ex.SelectAllCommand.Execute(null);
        Assert.Equal("3 de 3 seleccionados", ex.SelectedText);
        ex.Rows[0].Selected = false;
        ex.InvertCommand.Execute(null);
        Assert.Equal([true, false, false], ex.Rows.Select(r => r.Selected));
    }

    // ---- open rows: the same card as the Queue

    [AvaloniaFact]
    public async Task One_row_opens_at_a_time_with_the_shared_card()
    {
        var ex = await Found(NewExplore());
        ex.OpenRow(ex.Rows[0]);
        Assert.True(ex.Rows[0].Expanded);
        Assert.NotNull(ex.Rows[0].Card);
        Assert.Equal(ex.Rows[0].Entry.Title, ex.Rows[0].Card!.Title);   // from the listing until the full data arrives
        Assert.Contains("MP4", ex.Rows[0].Card!.Will);

        ex.OpenRow(ex.Rows[1]);
        Assert.False(ex.Rows[0].Expanded);
        Assert.True(ex.Rows[1].Expanded);
        Assert.Single(ex.Rows, r => r.Expanded);

        ex.ToggleRow(ex.Rows[1]);
        Assert.DoesNotContain(ex.Rows, r => r.Expanded);
    }

    // ---- the screen

    [AvaloniaFact]
    public async Task The_three_steps_render_in_the_real_window()
    {
        Loc.Instance.Language = "es";
        SettingsStore.Save(_rig.Paths.SettingsFile, new Settings { Lang = "es" });
        var window = new MainWindow { Width = 1100, Height = 820 };
        var vm = new MainViewModel(_rig.Services, window, a => Dispatcher.UIThread.Post(a));
        vm.Settings.Theme = "dark";
        vm.Settings.ScrapingLegalShown = true;   // the notice is a real dialog: nobody would close it here
        vm.Folder = _rig.OutFolder;
        ThemeService.Apply(Application.Current!, Palette.For("dark", "#3a2f4a"));
        window.DataContext = vm;
        window.Show();
        vm.SelectedTab = MainViewModel.TabScraping;
        Dispatcher.UIThread.RunJobs();

        var ex = vm.Scraping.Explore;
        ex.Today = () => Today;
        ex.LoadDelay = TimeSpan.Zero;
        // Step 1: only the link card.
        Assert.False(Find(window, "FiltersCard")!.IsVisible);
        Save(window, "v031r1-scraping-1-enlace");

        ex.SourceText = "@fake";
        await ex.LoadTask;
        await Until(() => ex.HasSource);
        Assert.True(Find(window, "FiltersCard")!.IsVisible);
        Save(window, "v031r1-scraping-2-filtros");

        ex.Filter.FromText = "2026-10-01";
        ex.Filter.ToText = "2026-10-31";
        File.WriteAllText(Path.Combine(_listing, "exact.txt"), "Q1fMMYyt0I4|20261012|855|not_live|public\n");
        await ex.SearchCommand.ExecuteAsync(null);
        await Until(() => !ex.IsSearching);
        ex.OpenRow(ex.Rows[0]);
        Dispatcher.UIThread.RunJobs();
        Assert.NotEmpty(ex.Rows);
        Assert.True(Find(window, "SelectionGroup")!.IsEffectivelyVisible);
        Assert.True(Find(window, "ResetButton")!.IsEffectivelyVisible);
        Save(window, "v031r1-scraping-3-resultados");

        var texts = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "").ToList();
        Assert.DoesNotContain(texts, t => System.Text.RegularExpressions.Regex.IsMatch(t, "^[a-z]{1,8}_[a-zA-Z0-9_]+$"));
        Assert.DoesNotContain(texts, t => t.Contains("{0}", StringComparison.Ordinal));
        Assert.Contains("Selección", texts);
        window.Close();
    }

    private static Control? Find(Visual root, string name) => root.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Name == name);

    private static void Save(TopLevel window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(dir);
        frame!.Save(Path.Combine(dir, name + ".png"), PngBitmapEncoderOptions.Default);
    }

    private static async Task Until(Func<bool> done)
    {
        for (var i = 0; i < 300 && !done(); i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(30);
        }
        Dispatcher.UIThread.RunJobs();
        Assert.True(done(), "timed out");
    }
}
