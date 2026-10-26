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
}
