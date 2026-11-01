using Videoprep.Core.Engine;
using Videoprep.Core.Logging;
using Videoprep.Core.Scraping;

namespace Videoprep.Core.Tests;

/// <summary>Answers the questions of a search the way a test says; remembers what was asked.</summary>
internal sealed class FakePrompts : ISearchPrompts
{
    public ManyKnownChoice Many { get; set; } = ManyKnownChoice.SearchAll;
    public Reached50Choice At50 { get; set; } = Reached50Choice.Continue;
    public bool MoreConfirm { get; set; }
    public List<string> Asked { get; } = [];

    public Task<ManyKnownChoice> AskManyKnown(int total, int requests) { Asked.Add($"many:{total}:{requests}"); return Task.FromResult(Many); }
    public Task<Reached50Choice> AskReached50(int found) { Asked.Add($"50:{found}"); return Task.FromResult(At50); }
    public Task<bool> AskConfirmMore(int missing) { Asked.Add($"more:{missing}"); return Task.FromResult(MoreConfirm); }
}

/// <summary>
/// Nico's bug (Sparkles, filter 2026-10-23 to 2026-10-27 showed videos outside the range): entries with no date or with an
/// approximate date went through the filter. RONDA 1 spec 1.1: a video is shown only if its date is CONFIRMED and inside the range.
/// </summary>
public class DateRangeTests
{
    internal static readonly DateOnly Today = new(2026, 10, 30);
    internal static readonly ToolPaths Tools = new("yt-dlp", null, null, null);

    private static readonly FilterSet Range = new()
    {
        From = new DateOnly(2026, 10, 23), To = new DateOnly(2026, 10, 27), SkipDownloaded = false,
    };

    internal static SearchRequest Req(FilterSet f, string source = "@fake", int? total = null) =>
        new(source, [SourceTab.Videos], f, _ => false, Today, Tools, CookieSource.NoCookies, total);

    /// <summary>Listing as YouTube's quick listing gives it (newest first, approximate dates) and what the exact step answers.</summary>
    private static ScriptedRunner Runner() => new(args =>
    {
        if (args.Contains("--print"))
        {
            // Exact step: the exact date of each video; the Short has none.
            var exact = new Dictionary<string, string?>
            {
                ["edge-to"] = "20261027", ["inside"] = "20261026", ["edge-from"] = "20261023", ["just-before"] = "20261022",
                ["short-nodate"] = null, ["inside2"] = "20261024",
            };
            return args.Where(a => a.StartsWith("https://", StringComparison.Ordinal))
                       .Select(u => u[(u.LastIndexOf('=') + 1)..])
                       .Where(exact.ContainsKey).Select(id => Lines.Exact(id, exact[id])).ToList();
        }
        return
        [
            Lines.Entry("after", "20261029"),          // clearly after the range
            Lines.Entry("edge-to", "20261027"),
            Lines.Entry("inside", "20261026"),
            Lines.Entry("edge-from", "20261023"),
            Lines.Entry("just-before", "20261023"),    // approximate says the 23rd, the exact date is the 22nd
            Lines.Entry("short-nodate", null),
            Lines.Entry("inside2", "20261024"),
            Lines.Entry("old", "20260801"),            // clearly before the range
            Lines.Entry("older", "20260701"),
        ];
    });

    [Fact]
    public async Task Only_videos_with_a_confirmed_date_inside_the_range_are_shown()
    {
        var search = new ScrapeSearch(Runner(), new Log());
        var r = await search.RunAsync(Req(Range), new FakePrompts(), null, TestContext.Current.CancellationToken);

        Assert.Equal(SearchStatus.Done, r.Status);
        Assert.Equal(["edge-to", "inside", "edge-from", "inside2"], r.Eval.Shown.OrderBy(e => e.Order).Select(e => e.Id));
        Assert.All(r.Eval.Shown, e =>
        {
            Assert.NotNull(e.Date);
            Assert.False(e.DateIsApprox, $"{e.Id}: its date was never confirmed");
            Assert.InRange(e.Date!.Value, Range.From!.Value, Range.To!.Value);
        });
        // The Short has no date at all: counted apart, never mixed with the results.
        Assert.Equal(["short-nodate"], r.Eval.Unconfirmed.Select(e => e.Id));
        Assert.Equal((4, 5), (r.DatesConfirmed, r.DatesTotal));
    }

    [Fact]
    public async Task Both_edges_are_included_and_the_day_before_is_not()
    {
        var search = new ScrapeSearch(Runner(), new Log());
        var r = await search.RunAsync(Req(Range), new FakePrompts(), null, TestContext.Current.CancellationToken);
        var dates = r.Eval.Shown.Select(e => e.Date!.Value.ToString("yyyy-MM-dd")).Order().ToArray();
        Assert.Equal(["2026-10-23", "2026-10-24", "2026-10-26", "2026-10-27"], dates);
        Assert.DoesNotContain(r.Eval.Shown, e => e.Id == "just-before");   // approximate 23rd, exact 22nd
    }

    [Fact]
    public async Task When_the_cap_of_confirmations_is_reached_the_rest_is_listed_apart_not_shown()
    {
        var search = new ScrapeSearch(Runner(), new Log()) { ExactRequestCap = 3 };
        var prompts = new FakePrompts { MoreConfirm = false };
        var r = await search.RunAsync(Req(Range), prompts, null, TestContext.Current.CancellationToken);

        Assert.Equal(["more:3"], prompts.Asked);                    // asked before going past the cap, with how many are missing
        Assert.All(r.Eval.Shown, e => Assert.False(e.DateIsApprox));
        Assert.Equal(3, r.Eval.Unconfirmed.Count);                  // the 3 it did not get to
        Assert.True(r.Truncated);
    }

    [Fact]
    public async Task The_cap_asks_and_goes_on_when_the_person_agrees()
    {
        var search = new ScrapeSearch(Runner(), new Log()) { ExactRequestCap = 3 };
        var prompts = new FakePrompts { MoreConfirm = true };
        var r = await search.RunAsync(Req(Range), prompts, null, TestContext.Current.CancellationToken);

        Assert.Equal(["more:3"], prompts.Asked);
        Assert.Equal(4, r.Eval.Shown.Count);
        Assert.Single(r.Eval.Unconfirmed);
        Assert.Equal(1 + 6, r.Requests);   // one listing + one request per doubtful video
    }

    [Fact]
    public async Task Exact_requests_are_only_for_the_doubtful_ones()
    {
        var runner = Runner();
        var search = new ScrapeSearch(runner, new Log());
        await search.RunAsync(Req(Range), new FakePrompts(), null, TestContext.Current.CancellationToken);

        var exactUrls = runner.Calls.Where(c => c.Contains("--print")).SelectMany(c => c).Where(a => a.StartsWith("https://", StringComparison.Ordinal)).ToList();
        Assert.DoesNotContain(exactUrls, u => u.EndsWith("after") || u.EndsWith("old") || u.EndsWith("older"));   // clearly outside: never asked
        Assert.Equal(6, exactUrls.Count);
    }

    [Theory]
    [InlineData("2026-10-23", true)]
    [InlineData("2026-10-27", true)]
    [InlineData("2026-10-22", false)]
    [InlineData("2026-10-28", false)]
    public void Confirmed_dates_follow_the_range_including_both_ends(string date, bool shown)
    {
        var e = new ListingEntry { Id = "x", Title = "x", Url = "https://x", Date = DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), DateIsApprox = false };
        Assert.Equal(shown ? Filters.Verdict.Show : Filters.Verdict.Drop, Filters.Classify(e, Range, Today));
    }

    [Fact]
    public void An_entry_without_a_date_is_doubtful_when_a_date_filter_is_set_and_shown_when_not()
    {
        var e = new ListingEntry { Id = "x", Title = "x", Url = "https://x" };
        Assert.Equal(Filters.Verdict.Doubtful, Filters.Classify(e, Range, Today));
        Assert.Equal(Filters.Verdict.Show, Filters.Classify(e, new FilterSet(), Today));
    }

    [Fact]
    public async Task Quick_presets_use_the_same_strict_rule()
    {
        // "Últimos 7 días" is just a From/To pair (today - 7 .. today): same engine, same rule.
        var week = new FilterSet { From = Today.AddDays(-7), To = Today, SkipDownloaded = false };
        var search = new ScrapeSearch(Runner(), new Log());
        var r = await search.RunAsync(Req(week), new FakePrompts(), null, TestContext.Current.CancellationToken);
        Assert.All(r.Eval.Shown, e => { Assert.False(e.DateIsApprox); Assert.InRange(e.Date!.Value, week.From!.Value, week.To!.Value); });
        Assert.DoesNotContain(r.Eval.Shown, e => e.Id is "old" or "older");
    }
}
