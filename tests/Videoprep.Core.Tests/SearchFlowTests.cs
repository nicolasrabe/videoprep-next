using Videoprep.Core.Engine;
using Videoprep.Core.Logging;
using Videoprep.Core.Scraping;

namespace Videoprep.Core.Tests;

/// <summary>RONDA 1 (spec 1.2 to 1.4): early cut, questions at 50, cap of confirmations, session cache, request estimate.</summary>
public class SearchFlowTests
{
    private static readonly DateOnly Today = DateRangeTests.Today;

    /// <summary>n videos, one per day, newest first, starting two days before today (approximate dates, like the quick listing).</summary>
    private static ScriptedRunner Daily(int n, Func<int, string?>? title = null, Func<int, int?>? duration = null) => new(args =>
    {
        if (args.Contains("--print"))
            return args.Where(a => a.StartsWith("https://", StringComparison.Ordinal)).Select(u => u[(u.LastIndexOf('=') + 1)..])
                       .Select(id => Lines.Exact(id, Today.AddDays(-2 - int.Parse(id[1..])).ToString("yyyyMMdd"))).ToList();
        // like yt-dlp: "-I 51:" starts at the 51st entry
        var from = args.ToList().IndexOf("-I") is var at and >= 0 ? int.Parse(args[at + 1].TrimEnd(':').Split(':')[0]) : 1;
        return Enumerable.Range(from - 1, Math.Max(0, n - (from - 1))).Select(i =>
            Lines.Entry("v" + i, Today.AddDays(-2 - i).ToString("yyyyMMdd"), title?.Invoke(i) ?? "t" + i, duration is null ? 600 : duration(i))).ToList();
    });

    private static FilterSet None => new() { SkipDownloaded = false };

    private static SearchRequest Req(FilterSet f, int? total = null, string source = "@fake") => DateRangeTests.Req(f, source, total);

    private static Task<SearchResult> Run(ScrapeSearch s, SearchRequest r, FakePrompts p) => s.RunAsync(r, p, null, TestContext.Current.CancellationToken);

    // ---- early cut (1.4.1)

    [Fact]
    public async Task The_listing_is_cut_when_it_passes_the_from_date()
    {
        var runner = Daily(100);
        var f = None with { From = Today.AddDays(-12), To = Today };
        var r = await Run(new ScrapeSearch(runner, new Log()), Req(f), new FakePrompts());

        Assert.True(r.Eval.Reviewed < 25, $"read {r.Eval.Reviewed} of 100 entries");   // about 10 days of videos plus the cut
        Assert.Equal(1, runner.Calls.Count(c => c.Contains("--flat-playlist") && !c.Contains("--print")));   // ONE listing per tab
        Assert.All(r.Eval.Shown, e => Assert.InRange(e.Date!.Value, f.From!.Value, f.To!.Value));
    }

    [Fact]
    public async Task Without_a_from_date_the_whole_listing_is_read()
    {
        var runner = Daily(40);
        var r = await Run(new ScrapeSearch(runner, new Log()), Req(None), new FakePrompts());
        Assert.Equal(40, r.Eval.Shown.Count);
        Assert.Equal(40, runner.LinesDelivered);
    }

    // ---- 50 videos (1.3)

    [Fact]
    public async Task Fifty_or_fewer_never_asks()
    {
        var prompts = new FakePrompts();
        var r = await Run(new ScrapeSearch(Daily(49), new Log()), Req(None), prompts);
        Assert.Empty(prompts.Asked);
        Assert.Equal(49, r.Eval.Shown.Count);
    }

    [Fact]
    public async Task At_fifty_it_stops_and_asks_once_and_goes_on_when_told()
    {
        var runner = Daily(120);
        var prompts = new FakePrompts { At50 = Reached50Choice.Continue };
        var r = await Run(new ScrapeSearch(runner, new Log()), Req(None), prompts);

        Assert.Equal(["50:50"], prompts.Asked);            // asked once; after "Seguir" it never stops again
        Assert.Equal(120, r.Eval.Shown.Count);
        Assert.Equal(SearchStatus.Done, r.Status);
        Assert.False(r.Truncated);
        Assert.Equal(2, runner.Calls.Count);               // the second reading starts at entry 51
        Assert.Contains("51:", runner.Calls[1]);
    }

    [Fact]
    public async Task At_fifty_the_person_can_stop_there()
    {
        var prompts = new FakePrompts { At50 = Reached50Choice.StopHere };
        var r = await Run(new ScrapeSearch(Daily(120), new Log()), Req(None), prompts);
        Assert.Equal(50, r.Eval.Shown.Count);
        Assert.True(r.Truncated);
        Assert.Equal(SearchStatus.Done, r.Status);
    }

    [Fact]
    public async Task At_fifty_cancel_keeps_what_was_already_read()
    {
        var prompts = new FakePrompts { At50 = Reached50Choice.Cancel };
        var r = await Run(new ScrapeSearch(Daily(120), new Log()), Req(None), prompts);
        Assert.Equal(SearchStatus.Cancelled, r.Status);
        Assert.Equal(50, r.Eval.Shown.Count);
    }

    [Fact]
    public async Task A_date_filter_also_stops_at_fifty_candidates()
    {
        var prompts = new FakePrompts { At50 = Reached50Choice.StopHere };
        var f = None with { From = Today.AddDays(-200), To = Today };
        var r = await Run(new ScrapeSearch(Daily(120), new Log()), Req(f, total: 500), prompts);
        Assert.DoesNotContain(prompts.Asked, a => a.StartsWith("many:", StringComparison.Ordinal));   // total is not used with dates
        Assert.Contains("50:50", prompts.Asked);
    }

    [Fact]
    public async Task A_limit_in_Mostrar_hasta_never_asks_and_reads_only_what_it_needs()
    {
        var runner = Daily(120);
        var prompts = new FakePrompts();
        var r = await Run(new ScrapeSearch(runner, new Log()), Req(None with { MaxCount = 20 }), prompts);
        Assert.Empty(prompts.Asked);
        Assert.Equal(20, r.Eval.Shown.Count);
        Assert.Equal(20, runner.LinesDelivered);
    }

    [Fact]
    public async Task A_known_total_over_fifty_asks_before_the_first_request()
    {
        var runner = Daily(5);
        var prompts = new FakePrompts { Many = ManyKnownChoice.Cancel };
        var r = await Run(new ScrapeSearch(runner, new Log()), Req(None, total: 200), prompts);
        Assert.Equal(["many:200:7"], prompts.Asked);       // about 7 listing requests (200 / 30)
        Assert.Equal(0, runner.CallCount);
        Assert.Equal(SearchStatus.Cancelled, r.Status);
    }

    [Fact]
    public async Task Known_total_limit_to_fifty_stops_at_fifty_without_asking_again()
    {
        var prompts = new FakePrompts { Many = ManyKnownChoice.LimitTo50 };
        var r = await Run(new ScrapeSearch(Daily(120), new Log()), Req(None, total: 200), prompts);
        Assert.Equal(["many:200:7"], prompts.Asked);
        Assert.Equal(50, r.Eval.Shown.Count);
        Assert.Equal(50, r.Filters.MaxCount);
    }

    [Fact]
    public async Task Known_total_search_all_goes_on_without_the_second_question()
    {
        var prompts = new FakePrompts { Many = ManyKnownChoice.SearchAll };
        var r = await Run(new ScrapeSearch(Daily(120), new Log()), Req(None, total: 120), prompts);
        Assert.Equal(["many:120:4"], prompts.Asked);
        Assert.Equal(120, r.Eval.Shown.Count);
    }

    [Fact]
    public async Task A_known_total_of_fifty_or_less_does_not_ask()
    {
        var prompts = new FakePrompts();
        await Run(new ScrapeSearch(Daily(30), new Log()), Req(None, total: 30), prompts);
        Assert.Empty(prompts.Asked);
    }

    // ---- session cache (1.4.3)

    [Fact]
    public async Task Changing_the_filters_does_not_repeat_the_requests()
    {
        var runner = Daily(40, duration: i => 100 + i * 100);
        var search = new ScrapeSearch(runner, new Log());
        await Run(search, Req(None), new FakePrompts());
        var calls = runner.CallCount;

        var longOnes = None with { MinSec = 1800 };
        Assert.True(search.CanRefilter(Req(longOnes)));
        var local = search.Refilter(Req(longOnes));
        var again = await Run(search, Req(longOnes), new FakePrompts());

        Assert.Equal(calls, runner.CallCount);
        Assert.True(local.FromCache);
        Assert.Equal(local.Eval.Shown.Count, again.Eval.Shown.Count);
        Assert.All(again.Eval.Shown, e => Assert.True(e.DurationSec >= 1800));
    }

    [Fact]
    public async Task A_narrower_range_reuses_a_listing_cut_early_but_an_older_one_asks_again()
    {
        var runner = Daily(100);
        var search = new ScrapeSearch(runner, new Log());
        await Run(search, Req(None with { From = Today.AddDays(-20), To = Today }), new FakePrompts());
        var calls = runner.CallCount;

        // newer start: everything needed is already read
        await Run(search, Req(None with { From = Today.AddDays(-10), To = Today }), new FakePrompts());
        Assert.Equal(calls, runner.CallCount);

        // wider towards older dates than what was read: new listing
        await Run(search, Req(None with { From = Today.AddDays(-60), To = Today }), new FakePrompts());
        Assert.True(runner.CallCount > calls);
    }

    [Fact]
    public async Task Dates_already_confirmed_are_not_asked_again()
    {
        var runner = Daily(60);
        var search = new ScrapeSearch(runner, new Log());
        var f = None with { From = Today.AddDays(-30), To = Today.AddDays(-10) };
        await Run(search, Req(f), new FakePrompts());
        var exact1 = runner.Calls.Count(c => c.Contains("--print"));
        await Run(search, Req(f), new FakePrompts());
        Assert.True(exact1 > 0);
        Assert.Equal(exact1, runner.Calls.Count(c => c.Contains("--print")));
    }

    [Fact]
    public async Task Clear_forgets_what_was_read()
    {
        var runner = Daily(10);
        var search = new ScrapeSearch(runner, new Log());
        await Run(search, Req(None), new FakePrompts());
        search.Clear();
        Assert.False(search.CanRefilter(Req(None)));
    }

    [Fact]
    public async Task What_was_read_expires()
    {
        var now = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
        var search = new ScrapeSearch(Daily(10), new Log(), () => now) { CacheTtl = TimeSpan.FromMinutes(10) };
        await Run(search, Req(None), new FakePrompts());
        Assert.True(search.CanRefilter(Req(None)));
        now = now.AddMinutes(11);
        Assert.False(search.CanRefilter(Req(None)));
    }

    // ---- the estimate (1.4.7)

    [Fact]
    public void The_estimate_comes_from_the_filters_and_the_known_total()
    {
        var search = new ScrapeSearch(Daily(1), new Log());
        Assert.Equal(new RequestEstimate(1, 2, false), search.Estimate(Req(None)));                               // up to 50 videos: 2 pages
        Assert.Equal(new RequestEstimate(1, 1, false), search.Estimate(Req(None with { MaxCount = 10 })));        // 10 videos: 1 page
        Assert.Equal(new RequestEstimate(1, 7, false), search.Estimate(Req(None with { MaxCount = 200 })));
        var dated = search.Estimate(Req(None with { From = Today.AddDays(-5) }));
        Assert.Equal(1, dated.Min);
        Assert.Equal(2 + 150, dated.Max);                                                                         // + the cap of confirmations
    }

    [Fact]
    public async Task The_estimate_is_exact_when_everything_is_already_read()
    {
        var search = new ScrapeSearch(Daily(30), new Log());
        await Run(search, Req(None), new FakePrompts());
        Assert.Equal(new RequestEstimate(0, 0, true), search.Estimate(Req(None with { MinSec = 60 })));
    }

    // ---- careful with the sites (1.4.4, 1.4.5)

    [Fact]
    public async Task Listing_and_confirmations_never_use_cookies_and_sleep_between_requests()
    {
        var runner = Daily(30);
        await Run(new ScrapeSearch(runner, new Log()), Req(None with { From = Today.AddDays(-20) }), new FakePrompts());
        Assert.All(runner.Calls, c =>
        {
            Assert.DoesNotContain(c, a => a.StartsWith("--cookies", StringComparison.Ordinal));
            Assert.Equal("1", c[c.ToList().IndexOf("--sleep-requests") + 1]);
        });
    }

    [Fact]
    public async Task A_bot_check_stops_the_search_and_keeps_what_was_read()
    {
        var runner = new ScriptedRunner(args => Enumerable.Range(0, 3).Select(i => Lines.Entry("v" + i, "20261028"))
            .Append("ERROR: [youtube:tab] x: Sign in to confirm you’re not a bot.").ToList(), exitCode: 1);
        var r = await Run(new ScrapeSearch(runner, new Log()), Req(None), new FakePrompts());
        Assert.Equal(SearchStatus.Blocked, r.Status);
        Assert.Equal(3, r.Eval.Shown.Count);
        Assert.Equal(1, runner.CallCount);   // no retry
    }

    [Fact]
    public async Task The_light_request_identifies_a_channel_without_listing_videos()
    {
        var runner = new ScriptedRunner(args => args.Contains("-J")
            ? ["{\"_type\":\"playlist\",\"title\":\"Sparkles - Videos\",\"channel\":\"Sparkles\",\"uploader_id\":\"@sparkles\",\"playlist_count\":null," +
               "\"thumbnails\":[{\"url\":\"https://yt3.example/banner\",\"width\":1060,\"height\":175},{\"url\":\"https://yt3.example/avatar\",\"width\":900,\"height\":900}],\"entries\":[]}"]
            : []);
        var probe = await new SourceLister(runner, new Log()).ProbeAsync(new SourceUrl.Target(SourceTab.Videos, "https://www.youtube.com/@sparkles/videos"),
            DateRangeTests.Tools, CookieSource.NoCookies, TestContext.Current.CancellationToken);

        Assert.Equal((ListOutcome.Ok, "Sparkles", "@sparkles", "https://yt3.example/avatar", (int?)null), (probe.Outcome, probe.Name, probe.Handle, probe.AvatarUrl, probe.TotalCount));
        var call = Assert.Single(runner.Calls);
        Assert.Equal("0", call[call.ToList().IndexOf("-I") + 1]);   // asks for no video at all
    }

    [Theory]
    [InlineData("ERROR: [youtube:tab] x: Sign in to confirm you’re not a bot. Use --cookies-from-browser", true)]
    [InlineData("ERROR: [youtube] abc: Private video. Sign in if you've been granted access to this video", true)]
    [InlineData("ERROR: [youtube] abc: This video is available to this channel's members-only", true)]
    [InlineData("ERROR: [youtube] abc: Sign in to confirm your age", true)]
    [InlineData("ERROR: HTTP Error 429: Too Many Requests", false)]
    [InlineData("ERROR: [generic] Unable to download webpage: HTTP Error 404: Not Found", false)]
    [InlineData("", false)]
    public void The_offer_of_cookies_is_only_for_logins_not_for_rate_limits_or_missing_pages(string error, bool offer) =>
        Assert.Equal(offer, CookieStrategy.AsksForLogin(error));

    [Fact]
    public async Task A_search_for_a_subscription_stops_at_the_first_video_the_history_already_has()
    {
        var runner = Daily(100);
        var known = new HashSet<string> { "v6", "v7", "v8" };
        var req = Req(None) with { StopAtKnown = e => known.Contains(e.Id) };
        var r = await Run(new ScrapeSearch(runner, new Log()), req, new FakePrompts());
        Assert.Equal(7, r.Eval.Reviewed);                              // v0 .. v6: the 7th is the first known one
        Assert.Equal(6, r.Eval.Shown.Count(e => !known.Contains(e.Id)));
        Assert.Single(runner.Calls);                                   // one listing, the rest of the channel is never asked for
    }
}

