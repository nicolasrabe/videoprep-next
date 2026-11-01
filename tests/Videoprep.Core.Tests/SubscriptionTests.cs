using Videoprep.Core.Engine;
using Videoprep.Core.Scraping;
using Videoprep.Core.Storage;

namespace Videoprep.Core.Tests;

public class SubscriptionStoreTests
{
    [Fact]
    public void Round_trip_keeps_every_field()
    {
        var dir = Directory.CreateTempSubdirectory("videoprep-subs-").FullName;
        var path = Path.Combine(dir, "subscriptions.json");
        var file = new SubscriptionFile
        {
            Settings = new SubscriptionSettings { CheckOnStart = false, EveryHours = 6, MaxNewPerCheck = 10 },
            Subscriptions =
            [
                new Subscription
                {
                    Id = "sub_a1b2c3", Name = "@canal", Url = "https://www.youtube.com/@canal", Tabs = ["videos", "streams"],
                    Filters = new SubscriptionFilters { From = "2026-09-01", MinSec = 120, MaxSec = 2700, Include = ["entrevista"], Exclude = ["short"], MaxCount = 50 },
                    Profile = new SubscriptionProfile { Format = "movp", Cap = 720, Naming = "series", Prefix = "Canal" },
                    Folder = @"D:\Videos\canal", FirstRun = Subscription.FirstRunBackfill, LastNew = 2,
                },
            ],
        };
        SubscriptionStore.Save(path, file);
        var back = SubscriptionStore.Load(path, out var problem);
        Assert.Null(problem);
        Assert.Equal(file.Settings.EveryHours, back.Settings.EveryHours);
        Assert.Equal(file.Settings.PauseBetweenSubsSec, back.Settings.PauseBetweenSubsSec);
        var s = Assert.Single(back.Subscriptions);
        Assert.Equal(file.Subscriptions[0].Filters.Include, s.Filters.Include);
        Assert.Equal(file.Subscriptions[0] with { Tabs = s.Tabs, Filters = s.Filters }, s);
        Assert.Equal(["videos", "streams"], s.Tabs);
        Assert.Contains("\"kind\": \"video\"", File.ReadAllText(path));
        var f = s.Filters.ToFilterSet();
        Assert.Equal(new DateOnly(2026, 9, 1), f.From);
        Assert.True(f.SkipDownloaded);
        Assert.Equal(s.Filters with { }, SubscriptionFilters.FromFilterSet(f) with { Include = s.Filters.Include, Exclude = s.Filters.Exclude });
    }

    [Fact]
    public void A_broken_file_is_kept_aside_not_lost()
    {
        var dir = Directory.CreateTempSubdirectory("videoprep-subs-").FullName;
        var path = Path.Combine(dir, "subscriptions.json");
        File.WriteAllText(path, "{ not json");
        var file = SubscriptionStore.Load(path, out var problem);
        Assert.Empty(file.Subscriptions);
        Assert.NotNull(problem);
        Assert.Single(Directory.GetFiles(dir, "subscriptions.json.unreadable-*"));
    }
}

/// <summary>CheckRunner over saved listings and the fake yt-dlp (no internet).</summary>
public sealed class CheckRunnerTests : IDisposable
{
    private readonly FakeYtDlpRig _rig = new();
    private readonly string _listing;
    private readonly AppPaths _paths;
    private readonly List<TimeSpan> _pauses = [];

    public CheckRunnerTests()
    {
        _listing = Path.Combine(_rig.Dir, "listing");
        Directory.CreateDirectory(_listing);
        foreach (var tab in new[] { "videos", "streams" })
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "listing", $"youtube-{tab}.jsonl"), Path.Combine(_listing, tab + ".jsonl"));
        Environment.SetEnvironmentVariable("FAKE_YTDLP_LISTING", _listing);
        Environment.SetEnvironmentVariable("FAKE_YTDLP_SCENARIO", "ok");
        _paths = new AppPaths(Path.Combine(_rig.Dir, "roaming"), Path.Combine(_rig.Dir, "local"));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("FAKE_YTDLP_LISTING", null);
        Environment.SetEnvironmentVariable("FAKE_YTDLP_SCENARIO", null);
        _rig.Dispose();
    }

    private CheckRunner Runner() => new(new ProcessRunner(), _rig.Log, _paths)
    {
        Delay = (t, _) => { _pauses.Add(t); return Task.CompletedTask; },
        Now = () => new DateTimeOffset(2026, 10, 26, 9, 14, 0, TimeSpan.FromHours(-3)),
    };

    private static CheckOptions Options => new() { Tools = new ToolPaths(FakeYtDlpRig.FakeExe, null, null, null) };

    private Subscription Sub(string id, string firstRun = Subscription.FirstRunBackfill, SubscriptionFilters? filters = null, bool enabled = true) => new()
    {
        Id = id, Name = "@fake-" + id, Url = "https://www.youtube.com/@fake", Enabled = enabled, FirstRun = firstRun,
        Filters = filters ?? new SubscriptionFilters(), Folder = Path.Combine(_rig.Dir, "out-" + id),
    };

    private void SaveSubs(int maxNew = 25, params Subscription[] subs) =>
        SubscriptionStore.Save(_paths.SubscriptionsFile, new SubscriptionFile { Settings = new SubscriptionSettings { MaxNewPerCheck = maxNew }, Subscriptions = subs });

    private Subscription Stored(string id) => SubscriptionStore.Load(_paths.SubscriptionsFile, out _).Subscriptions.Single(s => s.Id == id);

    private int Downloads() => _rig.Calls().Count(c => !c.Contains("--flat-playlist") && !c.Contains("--skip-download"));

    [Fact]
    public async Task Backfill_downloads_what_passes_and_a_second_check_downloads_nothing()
    {
        SaveSubs(25, Sub("a", filters: new SubscriptionFilters { MinSec = 100 }));   // the 82 s teaser is filtered out
        var r1 = await Runner().RunAsync(null, Options, TestContext.Current.CancellationToken);
        Assert.Equal(CheckResult.ExitOk, r1.ExitCode);
        Assert.Equal(2, r1.NewTotal);
        Assert.Equal(2, Downloads());
        var archive = File.ReadAllLines(Path.Combine(_paths.ArchivesDir, "a.txt"));
        Assert.Equal(["youtube Q1fMMYyt0I4", "youtube 685eur9lMGc"], archive);
        var call = _rig.Calls().First(c => !c.Contains("--flat-playlist"));
        Assert.Equal("3", call[Array.IndexOf(call, "--sleep-interval") + 1]);
        Assert.Equal("8", call[Array.IndexOf(call, "--max-sleep-interval") + 1]);
        Assert.Contains("--download-archive", call);
        var stored = Stored("a");
        Assert.Equal(2, stored.LastNew);
        Assert.Null(stored.LastError);
        Assert.Equal("2026-10-26T09:14:00-03:00", stored.LastCheck);

        var r2 = await Runner().RunAsync(null, Options, TestContext.Current.CancellationToken);
        Assert.Equal(0, r2.NewTotal);
        Assert.Equal(2, Downloads());                                                 // no new download calls
        Assert.Equal(0, Stored("a").LastNew);
    }

    [Fact]
    public async Task A_subscription_that_follows_a_profile_downloads_with_the_profiles_current_values()
    {
        var profiles = Profiles.ProfileStore.Load(_paths.ProfilesFile, out _);
        var p = profiles.Add(new Profiles.FormatProfile { Name = "Voz", Format = "m4a", Cap = 720 });
        var own = new SubscriptionProfile { Format = "mp4", Cap = 1080, ProfileId = p.Id };
        SaveSubs(25, Sub("a") with { Profile = own });
        await Runner().RunAsync(null, Options, TestContext.Current.CancellationToken);
        var call = _rig.Calls().First(c => !c.Contains("--flat-playlist") && !c.Contains("--skip-download"));
        Assert.Contains("m4a", call);                                                 // the profile's format, not the stored mp4
        Assert.DoesNotContain("--merge-output-format", call);

        // The profile is deleted: the values the subscription kept are used again.
        profiles.Remove(p.Id);
        Assert.Equal("mp4", Profiles.ProfileLinks.Effective(own, Profiles.ProfileStore.Load(_paths.ProfilesFile, out _)).Format);
    }

    [Fact]
    public async Task Max_count_means_the_newest_ones_not_two_more_every_check()
    {
        // Nico's report 2026-10-26: backfill with max 2 downloaded two OLDER videos on every check.
        SaveSubs(25, Sub("a", filters: new SubscriptionFilters { MaxCount = 2 }));
        Assert.Equal(2, (await Runner().RunAsync(null, Options, TestContext.Current.CancellationToken)).NewTotal);
        Assert.Equal(0, (await Runner().RunAsync(null, Options, TestContext.Current.CancellationToken)).NewTotal);
        Assert.Equal(0, (await Runner().RunAsync(null, Options, TestContext.Current.CancellationToken)).NewTotal);
        Assert.Equal(2, Downloads());

        // A new video on the channel: it is now one of the 2 newest, so it is downloaded.
        var first = File.ReadLines(Path.Combine(_listing, "videos.jsonl")).First();
        File.WriteAllLines(Path.Combine(_listing, "videos.jsonl"),
            [first.Replace("Q1fMMYyt0I4", "NEWvideo001"), .. File.ReadAllLines(Path.Combine(_listing, "videos.jsonl"))]);
        Assert.Equal(1, (await Runner().RunAsync(null, Options, TestContext.Current.CancellationToken)).NewTotal);
    }

    [Fact]
    public async Task Only_new_from_now_marks_the_current_videos_as_seen()
    {
        SaveSubs(25, Sub("a", Subscription.FirstRunMarkSeen));
        var r = await Runner().RunAsync(null, Options, TestContext.Current.CancellationToken);
        Assert.Equal(3, r.Subs[0].MarkedSeen);
        Assert.Equal(0, Downloads());
        Assert.Equal(3, File.ReadAllLines(Path.Combine(_paths.ArchivesDir, "a.txt")).Length);

        // A new video appears on the channel: the next check downloads only that one.
        var first = File.ReadLines(Path.Combine(_listing, "videos.jsonl")).First();
        File.WriteAllLines(Path.Combine(_listing, "videos.jsonl"),
            [first.Replace("Q1fMMYyt0I4", "NEWvideo001").Replace("Raising the Steaks", "Brand new"), .. File.ReadAllLines(Path.Combine(_listing, "videos.jsonl"))]);
        var r2 = await Runner().RunAsync(null, Options, TestContext.Current.CancellationToken);
        Assert.Equal(1, r2.NewTotal);
        Assert.Equal(1, Downloads());
        Assert.Contains("https://www.youtube.com/watch?v=NEWvideo001", _rig.Calls().Last());
    }

    [Fact]
    public async Task Max_new_per_check_leaves_the_rest_for_next_time_and_disabled_ones_wait()
    {
        SaveSubs(2, Sub("a"), Sub("b"), Sub("c", enabled: false));
        var r = await Runner().RunAsync(null, Options, TestContext.Current.CancellationToken);
        var a = Assert.Single(r.Subs);                                                // budget used up by "a": "b" waits
        Assert.Equal((2, 1), (a.New, a.Deferred));
        Assert.Empty(_pauses);

        var r2 = await Runner().RunAsync(null, Options, TestContext.Current.CancellationToken);
        Assert.Equal(["a", "b"], r2.Subs.Select(s => s.Id));
        Assert.Equal(1, r2.Subs[0].New);
        Assert.Equal(1, r2.Subs[1].New);
        var pause = Assert.Single(_pauses);
        Assert.InRange(pause.TotalSeconds, 5, 15);
        Assert.Null(Stored("c").LastCheck);
    }

    [Fact]
    public async Task Only_the_chosen_subscription_is_checked_even_if_disabled()
    {
        SaveSubs(25, Sub("a"), Sub("b", enabled: false));
        var r = await Runner().RunAsync(["b"], Options, TestContext.Current.CancellationToken);
        Assert.Equal("b", Assert.Single(r.Subs).Id);
    }

    [Fact]
    public async Task A_bot_check_stops_the_whole_run()
    {
        SaveSubs(25, Sub("a"), Sub("b"));
        Environment.SetEnvironmentVariable("FAKE_YTDLP_SCENARIO", "bot");
        var r = await Runner().RunAsync(null, Options, TestContext.Current.CancellationToken);
        Assert.True(r.Blocked);
        Assert.Equal(CheckResult.ExitBlocked, r.ExitCode);
        Assert.Single(r.Subs);
        Assert.Single(_rig.Calls());                                                  // one listing, nothing else
        Assert.NotNull(Stored("a").LastError);
        Assert.Null(Stored("b").LastCheck);
        Assert.False(File.Exists(Path.Combine(_paths.ArchivesDir, "a.txt")));
    }

    [Fact]
    public async Task A_broken_link_is_reported_and_the_others_go_on()
    {
        File.Delete(Path.Combine(_listing, "videos.jsonl"));
        File.Copy(Path.Combine(_listing, "streams.jsonl"), Path.Combine(_listing, "fine.jsonl"));
        var bad = Sub("a") with { Url = "https://example.test/gone" };
        var good = Sub("b") with { Url = "https://example.test/fine" };
        SaveSubs(25, bad, good);
        var r = await Runner().RunAsync(null, Options, TestContext.Current.CancellationToken);
        Assert.NotNull(r.Subs[0].Error);
        Assert.True(r.Subs[1].Ok > 0);
        Assert.Equal(CheckResult.ExitSomeFailed, r.ExitCode);
        Assert.NotNull(Stored("a").LastError);
    }

    // ---- RONDA 3: first check "desde cuándo", the same strict dates and caps as Scraping, a check that stops at what it already knows

    private void ExactDates(params string[] lines) => File.WriteAllLines(Path.Combine(_listing, "exact.txt"), lines);

    [Fact]
    public async Task A_new_subscription_since_a_date_takes_only_what_is_newer_and_marks_the_older_ones_as_seen()
    {
        // 14 days ago (approximate 12 Oct) is in; the two of 26 Sep are before the date: seen, not downloaded.
        ExactDates("Q1fMMYyt0I4|20261012|855|not_live|public");
        SaveSubs(25, Sub("a", Subscription.FirstRunSinceDate) with { FirstRunSince = "2026-10-01" });
        var r = await Runner().RunAsync(null, Options, TestContext.Current.CancellationToken);

        Assert.Equal(1, r.NewTotal);
        Assert.Equal(1, Downloads());
        Assert.Contains("https://www.youtube.com/watch?v=Q1fMMYyt0I4", _rig.Calls().Last());
        var archive = File.ReadAllLines(Path.Combine(_paths.ArchivesDir, "a.txt")).Order().ToArray();
        Assert.Equal(["youtube 39h7ZJbRcsw", "youtube 685eur9lMGc", "youtube Q1fMMYyt0I4"], archive);   // the history is not downloaded, only remembered

        var again = await Runner().RunAsync(null, Options, TestContext.Current.CancellationToken);
        Assert.Equal(0, again.NewTotal);
        Assert.Equal(1, Downloads());
    }

    [Fact]
    public async Task From_today_reads_one_page_only_and_the_next_check_stops_at_the_first_known_video()
    {
        SaveSubs(25, Sub("a", Subscription.FirstRunMarkSeen));
        await Runner().RunAsync(null, Options, TestContext.Current.CancellationToken);
        var firstListing = _rig.Calls()[0];
        Assert.Equal("1:30", firstListing[Array.IndexOf(firstListing, "-I") + 1]);          // one page, not the whole history

        // A new video: the check reads it and the first video it already has, and stops (the rest of the channel is not asked for).
        var first = File.ReadLines(Path.Combine(_listing, "videos.jsonl")).First();
        File.WriteAllLines(Path.Combine(_listing, "videos.jsonl"),
            [first.Replace("Q1fMMYyt0I4", "NEWvideo001"), .. File.ReadAllLines(Path.Combine(_listing, "videos.jsonl"))]);
        var r = await Runner().RunAsync(null, Options, TestContext.Current.CancellationToken);
        Assert.Equal(1, r.NewTotal);
        Assert.Contains("stopped early", _rig.LogText);
    }

    [Fact]
    public async Task A_date_that_cannot_be_confirmed_is_counted_apart_never_downloaded_and_not_asked_again()
    {
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "listing", "youtube-shorts.jsonl"), Path.Combine(_listing, "shorts.jsonl"));
        ExactDates("Qqmj29bOcLA|20261022|43|not_live|public", "WPSSn367NIA|20261016|48|not_live|public", "t3_O2dahOqw|NA|40|not_live|public");
        var filters = new SubscriptionFilters { From = "2026-10-20", To = "2026-10-27" };
        SaveSubs(25, Sub("a", Subscription.FirstRunBackfill, filters) with { Tabs = ["shorts"] });
        var r = await Runner().RunAsync(null, Options, TestContext.Current.CancellationToken);

        var sub = Assert.Single(r.Subs);
        Assert.Equal((1, 1), (sub.Ok, sub.Unconfirmed));
        Assert.Equal(1, Downloads());                                                 // only the one with a confirmed date inside the range
        Assert.Contains("1 without a confirmed date", _rig.LogText);
        Assert.Contains("youtube t3_O2dahOqw", File.ReadAllLines(Path.Combine(_paths.ArchivesDir, "a.txt")));   // asked once, not every check
    }

    [Fact]
    public async Task The_cap_of_date_requests_does_not_ask_and_what_is_left_waits_for_the_next_check()
    {
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "listing", "youtube-shorts.jsonl"), Path.Combine(_listing, "shorts.jsonl"));
        ExactDates("Qqmj29bOcLA|20261022|43|not_live|public", "WPSSn367NIA|20261022|48|not_live|public", "t3_O2dahOqw|20261022|40|not_live|public");
        var filters = new SubscriptionFilters { From = "2026-10-20", To = "2026-10-27" };
        SaveSubs(25, Sub("a", Subscription.FirstRunBackfill, filters) with { Tabs = ["shorts"] });
        var runner = new CheckRunner(new ProcessRunner(), _rig.Log, _paths)
        {
            Delay = (t, _) => Task.CompletedTask, ExactRequestCap = 2, Now = () => new DateTimeOffset(2026, 10, 26, 9, 14, 0, TimeSpan.FromHours(-3)),
        };
        var first = await runner.RunAsync(null, Options, TestContext.Current.CancellationToken);
        Assert.Equal((2, 1), (first.Subs[0].Ok, first.Subs[0].Unconfirmed));          // 2 dates asked (the cap), 1 left over
        // The OLDEST dates are asked first, so the one left over is the newest: the next check reaches it before any video it already knows.
        Assert.DoesNotContain("Qqmj29bOcLA", string.Join("\n", File.ReadAllLines(Path.Combine(_paths.ArchivesDir, "a.txt"))));

        var second = await runner.RunAsync(null, Options, TestContext.Current.CancellationToken);
        Assert.Equal(1, second.Subs[0].Ok);                                           // the next check gets to it
    }

    [Fact]
    public async Task Subscriptions_are_checked_one_after_the_other_with_pauses_between()
    {
        SaveSubs(25, Sub("a", Subscription.FirstRunMarkSeen), Sub("b", Subscription.FirstRunMarkSeen), Sub("c", Subscription.FirstRunMarkSeen));
        var r = await Runner().RunAsync(null, Options, TestContext.Current.CancellationToken);
        Assert.Equal(3, r.Subs.Count);
        Assert.Equal(2, _pauses.Count);                                               // a pause between each pair, never all at once
        Assert.All(_pauses, p => Assert.InRange(p.TotalSeconds, 5, 15));
    }

    [Fact]
    public void The_first_check_choice_is_saved_and_older_files_still_load()
    {
        var sub = Sub("a", Subscription.FirstRunSinceDate) with { FirstRunSince = "2026-10-19" };
        SaveSubs(25, sub);
        var back = Stored("a");
        Assert.Equal((Subscription.FirstRunSinceDate, "2026-10-19"), (back.FirstRun, back.FirstRunSince));
        Assert.DoesNotContain("firstRunSince", System.Text.Json.JsonSerializer.Serialize(Sub("b")));   // not written when not used
    }
}

