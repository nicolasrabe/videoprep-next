using Avalonia.Headless.XUnit;
using Videoprep.App.Services;
using Videoprep.App.ViewModels;
using Videoprep.Core.Scraping;

namespace Videoprep.App.Tests;

/// <summary>Scraping > Explorar driven like a user, over saved listings and the fake yt-dlp (no internet).</summary>
public sealed class ExploreTests : IDisposable
{
    private readonly TestRig _rig = new(tools: true);
    private readonly string _listing;
    private readonly string _calls;

    public ExploreTests()
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
        Environment.SetEnvironmentVariable("FAKE_YTDLP_SCENARIO", null);
        _rig.Dispose();
    }

    private int CallCount() => File.Exists(_calls) ? File.ReadAllLines(_calls).Length : 0;

    private async Task<ExploreViewModel> Searched(string source = "@fake")
    {
        var vm = _rig.Create();
        var ex = vm.Scraping.Explore;
        ex.SourceText = source;
        await ex.SearchCommand.ExecuteAsync(null);
        return ex;
    }

    [AvaloniaFact]
    public async Task Lists_a_channel_and_checks_the_new_videos()
    {
        var ex = await Searched();
        Assert.Equal(3, ex.Rows.Count);
        Assert.All(ex.Rows, r => Assert.True(r.Selected));
        Assert.All(ex.Rows, r => Assert.Equal("Nuevo", r.Status));
        Assert.Equal("Raising the Steaks - Blender Studio Log 32", ex.Rows[0].Title);
        Assert.Equal("14:15", ex.Rows[0].DurationText);
        Assert.StartsWith("~", ex.Rows[0].DateText);      // the quick listing only gives approximate dates
        Assert.True(ex.HasApproxDates);
        Assert.Equal("3 de 3 seleccionados", ex.SelectedText);
        Assert.False(ex.IsSearching);
        // The legal notice comes once, on first use.
        Assert.Single(_rig.Ui.Messages, m => m == Loc.Instance["sc_legal"]);
        await ex.SearchCommand.ExecuteAsync(null);
        Assert.Single(_rig.Ui.Messages, m => m == Loc.Instance["sc_legal"]);
    }

    [AvaloniaFact]
    public async Task Apply_filters_works_on_the_saved_list_without_asking_again()
    {
        var ex = await Searched();
        var calls = CallCount();
        ex.IncludeText = "overgrown";
        ex.ExcludeText = "teaser";
        await ex.ApplyFiltersCommand.ExecuteAsync(null);
        var row = Assert.Single(ex.Rows);
        Assert.Equal("685eur9lMGc", row.Entry.Id);
        ex.IncludeText = ex.ExcludeText = "";
        ex.MinText = "5:00";
        await ex.ApplyFiltersCommand.ExecuteAsync(null);
        Assert.Equal("Q1fMMYyt0I4", Assert.Single(ex.Rows).Entry.Id);
        Assert.Equal(calls, CallCount());
    }

    [AvaloniaFact]
    public async Task A_bad_filter_says_why_and_does_not_list()
    {
        var vm = _rig.Create();
        var ex = vm.Scraping.Explore;
        ex.SourceText = "@fake";
        ex.FromText = "31/10/2026";
        Assert.True(ex.FromBad);
        await ex.SearchCommand.ExecuteAsync(null);
        Assert.Contains(_rig.Ui.Messages, m => m.Contains("31/10/2026"));
        Assert.Empty(ex.Rows);
        Assert.Equal(0, CallCount());
    }

    [AvaloniaFact]
    public async Task Text_that_is_not_a_link_is_refused()
    {
        var ex = await Searched("hola");
        Assert.Contains(Loc.Instance["sc_bad_url"], _rig.Ui.Messages);
        Assert.Equal(0, CallCount());
        Assert.Empty(ex.Rows);
    }

    [AvaloniaFact]
    public async Task An_instagram_profile_gets_an_explanation_not_an_error()
    {
        var ex = await Searched("https://www.instagram.com/nasa/");
        Assert.Contains(_rig.Ui.Messages, m => m.StartsWith("Todavía no se pueden buscar los videos de un perfil de Instagram", StringComparison.Ordinal));
        Assert.Equal(0, CallCount());
        Assert.Empty(ex.Rows);
    }

    [AvaloniaFact]
    public async Task Checked_videos_go_to_the_queue_with_their_titles()
    {
        var vm = _rig.Create();
        var ex = vm.Scraping.Explore;
        ex.SourceText = "@fake";
        await ex.SearchCommand.ExecuteAsync(null);
        ex.Rows[1].Selected = false;
        ex.SelectedFormat = ex.FormatChoices.First(f => f.Key == "movp");
        ex.SelectedResolution = "720p";
        await ex.SendToQueueCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Items.Count);
        Assert.All(vm.Items, i => Assert.Equal("movp", i.Format));
        Assert.All(vm.Items, i => Assert.Equal(720, i.Cap));
        Assert.Equal("Raising the Steaks - Blender Studio Log 32", vm.QueueRows[0].Name);
        Assert.Equal("En cola", ex.Rows[0].Status);
        Assert.Equal("Nuevo", ex.Rows[1].Status);

        // Searching again: the queued ones are marked and left unchecked; sending twice adds nothing.
        await ex.SearchCommand.ExecuteAsync(null);
        Assert.Equal(["En cola", "Nuevo", "En cola"], ex.Rows.Select(r => r.Status));
        Assert.Equal([false, true, false], ex.Rows.Select(r => r.Selected));
        ex.SelectAllCommand.Execute(null);
        await ex.SendToQueueCommand.ExecuteAsync(null);
        Assert.Equal(3, vm.Items.Count);
    }

    [AvaloniaFact]
    public async Task Already_downloaded_videos_are_hidden_or_marked()
    {
        File.WriteAllLines(Path.Combine(_rig.Paths.Roaming, "downloaded.txt"), ["youtube Q1fMMYyt0I4"]);
        var ex = await Searched();
        Assert.Equal(2, ex.Rows.Count);                    // "skip already downloaded" is on by default
        ex.SkipDone = false;
        await ex.ApplyFiltersCommand.ExecuteAsync(null);
        var done = ex.Rows.Single(r => r.Entry.Id == "Q1fMMYyt0I4");
        Assert.Equal("Ya descargado", done.Status);
        Assert.False(done.Selected);
    }

    [AvaloniaFact]
    public async Task Playlists_tab_rows_expand_into_their_videos()
    {
        var vm = _rig.Create();
        var ex = vm.Scraping.Explore;
        ex.SourceText = "@fake";
        ex.TabVideos = false;
        ex.TabPlaylists = true;
        await ex.SearchCommand.ExecuteAsync(null);
        Assert.All(ex.Rows, r => Assert.True(r.IsPlaylist && r.CanExpand && !r.CanSelect));
        var pl = ex.Rows[0];
        var id = SourceUrlListId(pl.Entry.Url);
        File.Copy(Path.Combine(_listing, "videos.jsonl"), Path.Combine(_listing, $"list-{id}.jsonl"));

        await ex.ExpandCommand.ExecuteAsync(pl);
        Assert.False(ex.Rows.Single(r => r.Entry.Id == pl.Entry.Id).CanExpand);
        Assert.Equal(3, ex.Rows.Count(r => r.CanSelect));
    }

    [AvaloniaFact]
    public async Task A_bot_check_stops_and_says_so()
    {
        Environment.SetEnvironmentVariable("FAKE_YTDLP_SCENARIO", "bot");
        var vm = _rig.Create();
        var ex = vm.Scraping.Explore;
        ex.SourceText = "@fake";
        ex.TabShorts = true;
        await ex.SearchCommand.ExecuteAsync(null);
        Assert.Empty(ex.Rows);
        Assert.Contains(Loc.Instance["sc_blocked"], ex.StatusLine);
        Assert.Equal(1, CallCount());                      // the second tab is not even tried
    }

    private static string SourceUrlListId(string url) => System.Web.HttpUtility.ParseQueryString(new Uri(url).Query)["list"]!;
}
