using Avalonia.Headless.XUnit;
using Videoprep.App.Cli;
using Videoprep.App.Services;
using Videoprep.App.ViewModels;
using Videoprep.Core.Engine;
using Videoprep.Core.Logging;
using Videoprep.Core.Scraping;
using Videoprep.Core.Storage;

namespace Videoprep.App.Tests;

/// <summary>The tests that change the fake yt-dlp's environment variables run one at a time.</summary>
[CollectionDefinition("fake yt-dlp environment", DisableParallelization = true)]
public sealed class FakeYtDlpEnvironment;

/// <summary>Scraping > Suscripciones and <c>videoprep --check</c>, over saved listings and the fake yt-dlp (no internet).</summary>
[Collection("fake yt-dlp environment")]
public sealed class SubscriptionsTests : IDisposable
{
    private readonly TestRig _rig = new(tools: true);
    private readonly string _listing;
    private readonly string _calls;

    public SubscriptionsTests()
    {
        Loc.Instance.Language = "es";
        _listing = Path.Combine(_rig.Dir, "listing");
        Directory.CreateDirectory(_listing);
        foreach (var tab in new[] { "videos", "streams" })
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "listing", $"youtube-{tab}.jsonl"), Path.Combine(_listing, tab + ".jsonl"));
        _calls = Path.Combine(_rig.Dir, "calls.txt");
        Environment.SetEnvironmentVariable("FAKE_YTDLP_LISTING", _listing);
        Environment.SetEnvironmentVariable("FAKE_YTDLP_CALLS", _calls);
        Environment.SetEnvironmentVariable("FAKE_YTDLP_SCENARIO", "ok");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("FAKE_YTDLP_LISTING", null);
        Environment.SetEnvironmentVariable("FAKE_YTDLP_CALLS", null);
        Environment.SetEnvironmentVariable("FAKE_YTDLP_SCENARIO", null);
        _rig.Dispose();
    }

    private int Downloads() => File.Exists(_calls)
        ? File.ReadAllLines(_calls).Count(l => !l.Contains("--flat-playlist") && !l.Contains("--skip-download") && l.Contains("watch?v="))
        : 0;

    private string Out => Path.Combine(_rig.Dir, "subs-out");

    private SubscriptionsViewModel Subs(MainViewModel vm) => vm.Scraping.Subscriptions;

    [AvaloniaFact]
    public async Task Subscribe_from_explore_then_two_checks_download_each_video_once()
    {
        var vm = _rig.Create();
        var ex = vm.Scraping.Explore;
        ex.SourceText = "@fake";
        ex.TabStreams = true;
        ex.Filter.MinText = "1:30";                                 // drops the 82 s teaser
        ex.Format.SelectedFormat = ex.Format.FormatChoices.First(f => f.Key == "movh");
        await ex.SearchCommand.ExecuteAsync(null);
        _rig.Ui.FillSubscription = d => { d.Folder = Out; d.Backfill = true; };
        await vm.Scraping.SubscribeFromExploreCommand.ExecuteAsync(null);

        Assert.True(vm.Scraping.ShowSubscriptions);
        var row = Assert.Single(Subs(vm).Rows);
        Assert.Equal("@fake", row.Name);
        Assert.Equal("Videos + Directos", row.What);
        Assert.Equal("MOV 1080p", row.FormatText);
        Assert.Equal("nunca", row.LastText);
        var saved = Assert.Single(SubscriptionStore.Load(_rig.Paths.SubscriptionsFile, out _).Subscriptions);
        Assert.Equal(90, saved.Filters.MinSec);
        Assert.Equal(["videos", "streams"], saved.Tabs);
        Assert.Equal(Subscription.FirstRunBackfill, saved.FirstRun);

        var r1 = await Subs(vm).CheckAsync(null, interactive: true);
        Assert.NotNull(r1);
        Assert.False(r1.Blocked);
        var downloaded = Downloads();
        Assert.True(downloaded >= 2, $"downloads: {downloaded}");
        Assert.Equal(downloaded.ToString(), Subs(vm).Rows[0].NewText);
        Assert.NotEqual("nunca", Subs(vm).Rows[0].LastText);
        Assert.Contains("videos nuevos de @fake", Subs(vm).StatusLine);
        Assert.False(Subs(vm).IsChecking);

        var r2 = await Subs(vm).CheckAsync(null, interactive: true);
        Assert.Equal(0, r2!.NewTotal);
        Assert.Equal(downloaded, Downloads());                      // nothing downloaded twice
        Assert.Equal("Revisión terminada: no hay videos nuevos.", Subs(vm).StatusLine);
    }

    [AvaloniaFact]
    public async Task A_new_subscription_by_default_only_takes_what_comes_later()
    {
        var vm = _rig.Create();
        _rig.Ui.FillSubscription = d => { d.Url = "https://www.youtube.com/@fake"; d.Folder = Out; };
        await Subs(vm).AddCommand.ExecuteAsync(null);
        Assert.True(Assert.Single(Subs(vm).Rows).Sub.FirstRun == Subscription.FirstRunMarkSeen);
        await Subs(vm).CheckAllCommand.ExecuteAsync(null);
        Assert.Equal(0, Downloads());
        Assert.Contains("3 videos marcados como vistos", Subs(vm).StatusLine);
    }

    [AvaloniaFact]
    public async Task The_dialog_refuses_bad_data_and_explains()
    {
        var vm = _rig.Create();
        _rig.Ui.FillSubscription = d => { d.Url = "hola"; d.Folder = Out; };
        await Subs(vm).AddCommand.ExecuteAsync(null);
        Assert.Contains(Loc.Instance["sc_bad_url"], _rig.Ui.Messages);
        _rig.Ui.FillSubscription = d => { d.Url = "https://x.com/NASA"; d.Folder = Out; };
        await Subs(vm).AddCommand.ExecuteAsync(null);
        Assert.Contains(_rig.Ui.Messages, m => m.Contains("perfil de X"));
        _rig.Ui.FillSubscription = d => { d.Url = "@fake"; d.Folder = ""; };
        await Subs(vm).AddCommand.ExecuteAsync(null);
        Assert.Contains(Loc.Instance["sc_need_folder"], _rig.Ui.Messages);
        Assert.Empty(Subs(vm).Rows);
        Assert.False(File.Exists(_rig.Paths.SubscriptionsFile));
    }

    [AvaloniaFact]
    public async Task Edit_remove_and_switch_off_are_saved()
    {
        var vm = _rig.Create();
        _rig.Ui.FillSubscription = d => { d.Url = "@fake"; d.Folder = Out; };
        await Subs(vm).AddCommand.ExecuteAsync(null);
        await Subs(vm).AddCommand.ExecuteAsync(null);
        Assert.Equal(2, Subs(vm).Rows.Count);

        Subs(vm).Selected = Subs(vm).Rows[0];
        _rig.Ui.FillSubscription = d => { d.Name = "Mi canal"; d.Filter.IncludeText = "blender"; };
        await Subs(vm).EditCommand.ExecuteAsync(null);
        Subs(vm).Rows[1].Enabled = false;

        var vm2 = _rig.Create();                                    // as after closing and opening videoprep
        Assert.Equal("Mi canal", Subs(vm2).Rows[0].Name);
        Assert.Equal("+blender", Subs(vm2).Rows[0].FiltersText);
        Assert.False(Subs(vm2).Rows[1].Enabled);

        Subs(vm2).Selected = Subs(vm2).Rows[0];
        _rig.Ui.Answers.Enqueue(true);
        await Subs(vm2).RemoveCommand.ExecuteAsync(null);
        Assert.Single(SubscriptionStore.Load(_rig.Paths.SubscriptionsFile, out _).Subscriptions);
    }

    [AvaloniaFact]
    public async Task Only_one_run_at_a_time()
    {
        var vm = _rig.Create();
        _rig.Ui.FillSubscription = d => { d.Url = "@fake"; d.Folder = Out; };
        await Subs(vm).AddCommand.ExecuteAsync(null);
        using (RunLock.TryAcquire(_rig.Paths.RunLockFile))         // e.g. the scheduled check is running
        {
            Assert.Null(await Subs(vm).CheckAsync(null, interactive: true));
            Assert.Contains(Loc.Instance["msg_busy"], _rig.Ui.Messages);
            Assert.Equal(CheckCommand.ExitBusy, await CheckCommand.RunAsync(_rig.Paths, new Log(), _rig.Services.Runner,
                _rig.Services.Tools, () => [], TestContext.Current.CancellationToken));
        }
        Assert.NotNull(await Subs(vm).CheckAsync(null, interactive: true));
    }

    [AvaloniaFact]
    public async Task Check_command_works_without_the_window_and_logs_to_a_file()
    {
        SubscriptionStore.Save(_rig.Paths.SubscriptionsFile, new SubscriptionFile
        {
            Subscriptions = [new Subscription { Id = "sub_x", Name = "@fake", Url = "https://www.youtube.com/@fake", Folder = Out, FirstRun = Subscription.FirstRunBackfill }],
        });
        var logDir = Path.Combine(_rig.Dir, "logs");
        var log = new Log(logDir, filePrefix: "check");
        var code = await CheckCommand.RunAsync(_rig.Paths, log, _rig.Services.Runner, _rig.Services.Tools, () => [], TestContext.Current.CancellationToken);
        Assert.Contains(code, new[] { CheckResult.ExitOk, CheckResult.ExitSomeFailed });
        Assert.Equal(3, Downloads());
        var file = Assert.Single(Directory.GetFiles(logDir, "check-*.log"));
        Assert.Contains("=== Subscriptions: checking 1 ===", File.ReadAllText(file));
        Assert.Equal(3, File.ReadAllLines(Path.Combine(_rig.Paths.ArchivesDir, "sub_x.txt")).Length);

        Environment.SetEnvironmentVariable("FAKE_YTDLP_SCENARIO", "bot");
        File.Delete(Path.Combine(_rig.Paths.ArchivesDir, "sub_x.txt"));
        Assert.Equal(CheckResult.ExitBlocked, await CheckCommand.RunAsync(_rig.Paths, log, _rig.Services.Runner, _rig.Services.Tools, () => [],
                                                                         TestContext.Current.CancellationToken));
    }

    [AvaloniaFact]
    public async Task Scheduled_task_shows_the_command_first_and_never_runs_without_yes()
    {
        if (!ScheduledTask.Supported) return;                       // Windows only for now
        var vm = _rig.Create();
        _rig.Ui.Answers.Enqueue(false);
        await Subs(vm).ConfigureTaskCommand.ExecuteAsync(null);
        Assert.Empty(_rig.SchTaskCalls);
        Assert.Contains("schtasks /Create /TN videoprep-check /TR", _rig.Ui.Questions.Single());

        _rig.Ui.Answers.Enqueue(true);
        await Subs(vm).ConfigureTaskCommand.ExecuteAsync(null);
        var args = Assert.Single(_rig.SchTaskCalls);
        Assert.Equal(["/SC", "HOURLY", "/MO", "6"], args.Skip(5).Take(4));
        Assert.EndsWith("\" --check", args[4]);
        Assert.True(Subs(vm).TaskInstalled);

        await Subs(vm).RemoveTaskCommand.ExecuteAsync(null);
        Assert.Equal(["/Delete", "/TN", "videoprep-check", "/F"], _rig.SchTaskCalls[1]);
        Assert.False(Subs(vm).TaskInstalled);
    }

    [AvaloniaFact]   // the class constructor changes Loc, which updates live controls: UI thread only
    public void Scheduled_task_every_24_hours_is_daily()
    {
        Assert.Equal(["/SC", "DAILY", "/MO", "1"], ScheduledTask.CreateArgs(@"C:\vp\videoprep.exe", 24).Skip(5).Take(4));
        Assert.Equal(["/SC", "HOURLY", "/MO", "1"], ScheduledTask.CreateArgs(@"C:\vp\videoprep.exe", 0).Skip(5).Take(4));
        Assert.Equal("\"C:\\vp\\videoprep.exe\" --check", ScheduledTask.CreateArgs(@"C:\vp\videoprep.exe", 6)[4]);
    }
}
