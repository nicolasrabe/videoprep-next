using Videoprep.Core.Engine;
using Videoprep.Core.Logging;
using Videoprep.Core.Scraping;

namespace Videoprep.Core.Tests;

public class SourceUrlTests
{
    private static readonly SourceTab[] VideosStreams = [SourceTab.Videos, SourceTab.Streams];

    [Theory]
    [InlineData("https://www.youtube.com/@BlenderStudio", "https://www.youtube.com/@BlenderStudio")]
    [InlineData("youtube.com/@BlenderStudio/featured", "https://www.youtube.com/@BlenderStudio")]
    [InlineData("@BlenderStudio", "https://www.youtube.com/@BlenderStudio")]
    [InlineData("https://m.youtube.com/channel/UCz75RVbH8q2jdcfQJ0PHrcQ/videos", "https://www.youtube.com/channel/UCz75RVbH8q2jdcfQJ0PHrcQ")]
    [InlineData("https://www.youtube.com/c/BlenderFoundation", "https://www.youtube.com/c/BlenderFoundation")]
    [InlineData("https://www.youtube.com/user/BlenderFoundation", "https://www.youtube.com/user/BlenderFoundation")]
    public void Channels_become_one_page_per_tab(string input, string channel)
    {
        Assert.Equal(channel, SourceUrl.ChannelBase(input));
        var t = SourceUrl.Targets(input, VideosStreams);
        Assert.Equal([new SourceUrl.Target(SourceTab.Videos, channel + "/videos"), new SourceUrl.Target(SourceTab.Streams, channel + "/streams")], t);
    }

    [Fact]
    public void Playlists_and_other_sites_are_one_page()
    {
        Assert.Equal([new SourceUrl.Target(SourceTab.Single, "https://www.youtube.com/playlist?list=PLabc")],
                     SourceUrl.Targets("https://www.youtube.com/watch?v=x&list=PLabc&index=2", VideosStreams));
        Assert.Equal([new SourceUrl.Target(SourceTab.Single, "https://www.twitch.tv/somebody/clips")],
                     SourceUrl.Targets("https://www.twitch.tv/somebody/clips", VideosStreams));
        Assert.Empty(SourceUrl.Targets("hola", VideosStreams));
        Assert.Single(SourceUrl.Targets("@BlenderStudio", []));   // no tab checked: Videos
    }

    [Theory]
    [InlineData("https://www.instagram.com/nasa/", "Instagram")]
    [InlineData("instagram.com/nasa?igsh=abc", "Instagram")]
    [InlineData("https://x.com/NASA", "X")]
    [InlineData("https://twitter.com/NASA/media", "X")]
    [InlineData("https://www.tiktok.com/@nasa", "TikTok")]
    [InlineData("https://www.instagram.com/p/C1abc/", null)]
    [InlineData("https://www.instagram.com/reel/C1abc/", null)]
    [InlineData("https://x.com/NASA/status/123", null)]
    [InlineData("https://www.tiktok.com/@nasa/video/123", null)]
    [InlineData("https://www.twitch.tv/nasa/videos", null)]
    [InlineData("@BlenderStudio", null)]
    public void Profiles_that_cannot_be_listed_are_recognized(string input, string? site) =>
        Assert.Equal(site, SourceUrl.UnsupportedProfile(input));
}

public class ListingEntryTests
{
    private static List<ListingEntry> Read(string name, SourceTab tab) =>
        File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "listing", name))
            .Select((l, i) => ListingEntry.Parse(l, tab, i)).OfType<ListingEntry>().ToList();

    [Fact]
    public void Videos_tab_has_length_views_and_an_approximate_date()
    {
        var v = Read("youtube-videos.jsonl", SourceTab.Videos);
        Assert.Equal(3, v.Count);
        var first = v[0];
        Assert.Equal(("Q1fMMYyt0I4", "youtube", 855), (first.Id, first.Extractor, first.DurationSec));
        Assert.Equal(new DateOnly(2026, 10, 12), first.Date);
        Assert.True(first.DateIsApprox);
        Assert.Equal("youtube Q1fMMYyt0I4", first.ArchiveKey);
        Assert.False(first.IsPlaylist);
    }

    [Fact]
    public void Shorts_have_no_date_or_length()
    {
        var s = Read("youtube-shorts.jsonl", SourceTab.Shorts);
        Assert.All(s, e => Assert.Null(e.Date));
        Assert.All(s, e => Assert.Null(e.DurationSec));
        Assert.StartsWith("https://www.youtube.com/shorts/", s[0].Url);
    }

    [Fact]
    public void Past_streams_are_not_live_now()
    {
        var s = Read("youtube-streams.jsonl", SourceTab.Streams);
        Assert.All(s, e => Assert.Equal("was_live", e.LiveStatus));
        Assert.All(s, e => Assert.False(e.IsLiveNow));
    }

    [Fact]
    public void Playlists_tab_rows_are_playlists()
    {
        var p = Read("youtube-playlists.jsonl", SourceTab.Playlists);
        Assert.All(p, e => Assert.True(e.IsPlaylist));
        Assert.StartsWith("https://www.youtube.com/playlist?list=", p[0].Url);
    }

    [Fact]
    public void Exact_line()
    {
        var x = ExactInfo.Parse("EX|Qqmj29bOcLA|20261022|43|not_live|public")!;
        Assert.Equal((new DateOnly(2026, 10, 22), 43, "not_live", "public"), (x.Date!.Value, x.DurationSec!.Value, x.LiveStatus, x.Availability));
        var e = new ListingEntry { Id = "Qqmj29bOcLA", Title = "t", Url = "u" };
        var merged = x.ApplyTo(e with { Date = new DateOnly(2026, 10, 1), DateIsApprox = true });
        Assert.Equal(new DateOnly(2026, 10, 22), merged.Date);
        Assert.False(merged.DateIsApprox);
        Assert.Null(ExactInfo.Parse("EX|id|NA|NA|NA|NA")!.Date);
        Assert.Null(ExactInfo.Parse("[youtube] x: Downloading webpage"));
    }

    [Fact]
    public void Garbage_lines_are_ignored()
    {
        Assert.Null(ListingEntry.Parse("WARNING: something", SourceTab.Videos, 0));
        Assert.Null(ListingEntry.Parse("{not json", SourceTab.Videos, 0));
        Assert.Null(ListingEntry.Parse("{\"title\":\"no id\"}", SourceTab.Videos, 0));
    }
}

public class FiltersTests
{
    private static readonly DateOnly Today = new(2026, 10, 26);

    private static ListingEntry E(string id, string title, string? date = null, int? dur = null, bool approx = false,
                                  string? live = null, string? avail = null, int order = 0, bool playlist = false) => new()
    {
        Id = id, Title = title, Url = "https://www.youtube.com/watch?v=" + id, Extractor = "youtube",
        Date = date is null ? null : DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), DateIsApprox = approx,
        DurationSec = dur, LiveStatus = live, Availability = avail, Order = order, IsPlaylist = playlist,
    };

    private static IReadOnlyList<string> Ids(IEnumerable<ListingEntry> es) => es.Select(e => e.Id).ToList();

    private static IReadOnlyList<ListingEntry> Apply(IEnumerable<ListingEntry> all, FilterSet f, params string[] downloaded) =>
        Filters.Apply(all, f, e => downloaded.Contains(e.Id), Today);

    [Fact]
    public void Title_includes_any_and_excludes_any_ignoring_case_and_accents()
    {
        ListingEntry[] all = [E("a", "Entrevísta completa"), E("b", "REVIEW del mes"), E("c", "Short: tráiler"), E("d", "Otro video")];
        var f = new FilterSet { Include = Filters.SplitTerms("entrevista, review"), Exclude = Filters.SplitTerms("trailer") };
        Assert.Equal(["a", "b"], Ids(Apply(all, f)));
        Assert.Equal(["a", "b", "d"], Ids(Apply(all, new FilterSet { Exclude = ["TRAILER"] })));
    }

    [Fact]
    public void Exact_dates_are_checked_strictly()
    {
        ListingEntry[] all = [E("in", "x", "2026-09-10"), E("before", "x", "2026-08-31"), E("after", "x", "2026-10-26"), E("edge", "x", "2026-09-01")];
        var f = new FilterSet { From = new DateOnly(2026, 9, 1), To = new DateOnly(2026, 10, 25) };
        Assert.Equal(["in", "edge"], Ids(Apply(all, f)));
    }

    [Fact]
    public void Approximate_dates_only_drop_what_is_clearly_outside()
    {
        // 3 months old, approximate: margin 31 days.
        ListingEntry[] all = [E("near", "x", "2026-08-10", approx: true), E("far", "x", "2026-06-01", approx: true), E("unknown", "x")];
        var f = new FilterSet { From = new DateOnly(2026, 9, 1) };
        var passed = Apply(all, f);
        Assert.Equal(["near", "unknown"], Ids(passed).Order());
        Assert.Equal(["near", "unknown"], Ids(Filters.NeedsExact(passed, f)).Order());
        Assert.Equal(1, Filters.ApproxMarginDays(new DateOnly(2026, 10, 24), Today));
        Assert.Equal(366, Filters.ApproxMarginDays(new DateOnly(2024, 1, 1), Today));
    }

    [Fact]
    public void Length_filter_keeps_unknown_lengths_for_the_exact_step()
    {
        ListingEntry[] all = [E("short", "x", dur: 50), E("ok", "x", dur: 600), E("long", "x", dur: 4000), E("nolen", "x")];
        var f = new FilterSet { MinSec = 120, MaxSec = 2700 };
        Assert.Equal(["ok", "nolen"], Ids(Apply(all, f)));
        Assert.Equal(["nolen"], Ids(Filters.NeedsExact(Apply(all, f), f)));
        Assert.Empty(Filters.NeedsExact(Apply(all, new FilterSet()), new FilterSet()));
    }

    [Fact]
    public void Skip_live_members_and_downloaded()
    {
        ListingEntry[] all =
        [
            E("live", "x", live: "is_live"), E("soon", "x", live: "is_upcoming"), E("past", "x", live: "was_live"),
            E("members", "x", avail: "subscriber_only"), E("done", "x"), E("new", "x"),
        ];
        Assert.Equal(["new", "past"], Ids(Apply(all, new FilterSet(), "done")).Order());
        Assert.Equal(6, Apply(all, new FilterSet { SkipLive = false, SkipMembers = false, SkipDownloaded = false }, "done").Count);
    }

    [Fact]
    public void Order_and_max_count()
    {
        ListingEntry[] all = [E("b", "x", "2026-10-02", order: 2), E("a", "x", "2026-10-03", order: 1), E("c", "x", "2026-09-01", order: 3), E("nd", "x", order: 4)];
        Assert.Equal(["a", "b", "c", "nd"], Ids(Apply(all, new FilterSet())));
        Assert.Equal(["c", "b", "a", "nd"], Ids(Apply(all, new FilterSet { OldestFirst = true })));
        Assert.Equal(["a", "b"], Ids(Apply(all, new FilterSet { MaxCount = 2 })));
    }

    [Fact]
    public void Playlists_are_never_filtered_out()
    {
        ListingEntry[] all = [E("pl", "Lista", playlist: true), E("v", "video", dur: 10)];
        Assert.Equal(["pl"], Ids(Apply(all, new FilterSet { MinSec = 60, Include = ["zzz"] })));
    }

    [Theory]
    [InlineData("", true, null)]
    [InlineData("45", true, 45)]
    [InlineData("2:00", true, 120)]
    [InlineData("1:02:03", true, 3723)]
    [InlineData("1:60", false, null)]
    [InlineData("abc", false, null)]
    public void Duration_text(string text, bool ok, int? seconds)
    {
        Assert.Equal(ok, Filters.TryParseDuration(text, out var s));
        Assert.Equal(seconds, s);
    }

    [Theory]
    [InlineData("2026-09-01", true)]
    [InlineData("", true)]
    [InlineData("01/09/2026", false)]
    [InlineData("2026-13-01", false)]
    public void Date_text(string text, bool ok) => Assert.Equal(ok, Filters.TryParseDate(text, out _));

    [Fact]
    public void Display_texts()
    {
        Assert.Equal("4:05", Filters.FormatDuration(245));
        Assert.Equal("1:02:03", Filters.FormatDuration(3723));
        Assert.Equal("41 mil", Filters.FormatViews(41_000, "es"));
        Assert.Equal("9,3 mil", Filters.FormatViews(9_300, "es"));
        Assert.Equal("9.3K", Filters.FormatViews(9_300, "en"));
        Assert.Equal("1,2 M", Filters.FormatViews(1_200_000, "es"));
        Assert.Equal("850", Filters.FormatViews(850, "en"));
    }
}

public sealed class ArchiveStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("videoprep-archive-").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void Reads_and_appends_yt_dlp_archive_lines()
    {
        var path = Path.Combine(_dir, "downloaded.txt");
        File.WriteAllText(path, "youtube jNQXAC9IVRw\nYoutube AbCdEfGhIjK\n\n");
        var a = ArchiveStore.Load(path);
        Assert.Equal(2, a.Count);
        Assert.True(a.Contains("youtube", "AbCdEfGhIjK"));
        Assert.False(a.Contains("youtube", "abcdefghijk"));   // ids are case-sensitive
        ArchiveStore.Append(path, ["youtube jNQXAC9IVRw", "youtube NEWNEWNEW01"]);
        Assert.Equal(["youtube jNQXAC9IVRw", "Youtube AbCdEfGhIjK", "", "youtube NEWNEWNEW01"], File.ReadAllLines(path));
        Assert.Equal(0, ArchiveStore.Load(Path.Combine(_dir, "none.txt")).Count);
    }
}

/// <summary>The lister against the fake yt-dlp serving the saved listings.</summary>
[Collection("engine")]
public sealed class SourceListerTests : IDisposable
{
    private readonly FakeYtDlpRig _rig = new();
    private readonly string _listing;

    public SourceListerTests()
    {
        _listing = Path.Combine(_rig.Dir, "listing");
        Directory.CreateDirectory(_listing);
        foreach (var tab in new[] { "videos", "shorts", "streams", "playlists" })
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "listing", $"youtube-{tab}.jsonl"), Path.Combine(_listing, tab + ".jsonl"));
        File.Copy(Path.Combine(_listing, "videos.jsonl"), Path.Combine(_listing, "list-PLx.jsonl"));
        File.WriteAllText(Path.Combine(_listing, "exact.txt"), "Qqmj29bOcLA|20261022|43|not_live|public\nWPSSn367NIA|20261016|48|not_live|subscriber_only\n");
        Environment.SetEnvironmentVariable("FAKE_YTDLP_LISTING", _listing);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("FAKE_YTDLP_LISTING", null);
        Environment.SetEnvironmentVariable("FAKE_YTDLP_SCENARIO", null);
        _rig.Dispose();
    }

    private static ToolPaths Tools => new(FakeYtDlpRig.FakeExe, null, null, null);

    [Fact]
    public async Task Lists_every_checked_tab_streaming_entries()
    {
        var lister = new SourceLister(new ProcessRunner(), _rig.Log);
        var got = new List<ListingEntry>();
        var targets = SourceUrl.Targets("@fake", [SourceTab.Videos, SourceTab.Shorts, SourceTab.Streams]);
        var r = await lister.ListAsync(targets, Tools, CookieSource.NoCookies, e => { lock (got) got.Add(e); }, TestContext.Current.CancellationToken);
        Assert.All(r, x => Assert.Equal(ListOutcome.Ok, x.Outcome));
        Assert.Equal(9, got.Count);
        Assert.Equal(3, got.Count(e => e.Tab == SourceTab.Shorts));
        Assert.Equal(got.Count, got.Select(e => e.Order).Distinct().Count());
        var call = _rig.Calls()[0];
        Assert.Contains("--lazy-playlist", call);
        Assert.Equal("1:1000", call[Array.IndexOf(call, "-I") + 1]);
        Assert.Equal("1", call[Array.IndexOf(call, "--sleep-requests") + 1]);
        Assert.Equal("youtubetab:approximate_date", call[Array.IndexOf(call, "--extractor-args") + 1]);
    }

    [Fact]
    public async Task A_missing_tab_is_not_a_failure()
    {
        File.Delete(Path.Combine(_listing, "shorts.jsonl"));
        var lister = new SourceLister(new ProcessRunner(), _rig.Log);
        var r = await lister.ListAsync(SourceUrl.Targets("@fake", [SourceTab.Shorts, SourceTab.Videos]), Tools, CookieSource.NoCookies, _ => { },
                                       TestContext.Current.CancellationToken);
        Assert.Equal([ListOutcome.NoSuchTab, ListOutcome.Ok], r.Select(x => x.Outcome));
    }

    [Fact]
    public async Task Bot_check_stops_the_listing_without_retrying()
    {
        Environment.SetEnvironmentVariable("FAKE_YTDLP_SCENARIO", "bot");
        var lister = new SourceLister(new ProcessRunner(), _rig.Log);
        var r = await lister.ListAsync(SourceUrl.Targets("@fake", [SourceTab.Videos, SourceTab.Shorts]), Tools, CookieSource.NoCookies, _ => { },
                                       TestContext.Current.CancellationToken);
        Assert.Equal(ListOutcome.Blocked, Assert.Single(r).Outcome);
        Assert.Single(_rig.Calls());
    }

    [Fact]
    public async Task Max_per_tab_and_playlists()
    {
        var lister = new SourceLister(new ProcessRunner(), _rig.Log) { MaxPerTab = 2 };
        var got = new List<ListingEntry>();
        await lister.ListAsync(SourceUrl.Targets("https://www.youtube.com/playlist?list=PLx", []), Tools, CookieSource.NoCookies,
                               e => { lock (got) got.Add(e); }, TestContext.Current.CancellationToken);
        Assert.Equal(2, got.Count);
        Assert.All(got, e => Assert.Equal(SourceTab.Single, e.Tab));
    }

    [Fact]
    public async Task Exact_data_in_capped_batches()
    {
        var lister = new SourceLister(new ProcessRunner(), _rig.Log) { ExactCap = 3, ExactBatch = 2 };
        var entries = Enumerable.Range(0, 5).Select(i => new ListingEntry
        {
            Id = i == 0 ? "Qqmj29bOcLA" : i == 1 ? "WPSSn367NIA" : "id" + i, Title = "t",
            Url = i < 2 ? "https://www.youtube.com/shorts/" + (i == 0 ? "Qqmj29bOcLA" : "WPSSn367NIA") : "https://www.youtube.com/watch?v=id" + i,
        }).ToList();
        var progress = new List<(int, int)>();
        var (infos, blocked) = await lister.ExactAsync(entries, Tools, CookieSource.NoCookies, (d, t) => progress.Add((d, t)), TestContext.Current.CancellationToken);
        Assert.False(blocked);
        Assert.Equal(3, infos.Count);                     // cap
        Assert.Equal(2, _rig.Calls().Count);              // batches of 2: 2 + 1
        Assert.Equal([(2, 3), (3, 3)], progress);
        Assert.Equal("subscriber_only", infos.Single(x => x.Id == "WPSSn367NIA").Availability);
        Assert.Contains("--skip-download", _rig.Calls()[0]);
    }
}
