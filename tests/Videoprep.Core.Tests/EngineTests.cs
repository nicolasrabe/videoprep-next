using System.Diagnostics;
using Videoprep.Core.Engine;
using Videoprep.Core.Logging;
using Videoprep.Core.Models;

namespace Videoprep.Core.Tests;

/// <summary>Runs the real ProcessRunner against the fake yt-dlp (tests/FakeYtDlp). No network, no ffmpeg needed.</summary>
internal sealed class FakeYtDlpRig : IDisposable
{
    public string Dir { get; } = Directory.CreateTempSubdirectory("videoprep-engine-").FullName;
    public string Folder => Path.Combine(Dir, "out");
    public string CallsFile => Path.Combine(Dir, "calls.txt");
    public Log Log { get; } = new();
    public List<EngineEvent> Events { get; } = [];
    public JobEngine Engine { get; }

    public static string FakeExe
    {
        get
        {
            var p = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "fake-yt-dlp.exe" : "fake-yt-dlp");
            // MSBuild copies may drop the execute bit on macOS/Linux.
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(p, File.GetUnixFileMode(p) | UnixFileMode.UserExecute);
            return p;
        }
    }

    public FakeYtDlpRig(string? source = null)
    {
        Directory.CreateDirectory(Folder);
        Environment.SetEnvironmentVariable("FAKE_YTDLP_CALLS", CallsFile);
        Environment.SetEnvironmentVariable("FAKE_YTDLP_SOURCE", source);
        Engine = new JobEngine(new ProcessRunner(), Log);
        Engine.Event += e => { lock (Events) Events.Add(e); };
    }

    public EngineContext Context(IReadOnlyList<CookieSource>? cookies = null, ToolPaths? tools = null) => new()
    {
        Tools = tools ?? new ToolPaths(FakeExe, null, null, null),
        Options = new DownloadOptions(),
        Folder = Folder,
        Cookies = cookies ?? [CookieSource.NoCookies],
    };

    public static Job Job(string scenario, string id = "abc", int group = 1, int index = 0, int count = 1, bool download = true,
                          string start = "", string end = "") => new()
    {
        GroupId = group, Index = index, Count = count, Url = $"https://fake.test/{scenario}/{id}", Site = Site.Other,
        Format = "mp4", Cap = 1080, Download = download, Start = start, End = end,
    };

    /// <summary>Each yt-dlp call's arguments, in order.</summary>
    public List<string[]> Calls() => File.Exists(CallsFile)
        ? File.ReadAllLines(CallsFile).Select(l => l.Split('\u001f')).ToList()
        : [];

    public string LogText => string.Join("\n", Log.Recent());

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("FAKE_YTDLP_CALLS", null);
        Environment.SetEnvironmentVariable("FAKE_YTDLP_SOURCE", null);
        try { Directory.Delete(Dir, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

// Environment variables are process-wide: these tests must not run in parallel with each other.
[CollectionDefinition("engine", DisableParallelization = true)]
public sealed class EngineCollection;

[Collection("engine")]
public sealed class EngineTests : IDisposable
{
    private readonly FakeYtDlpRig _rig = new();

    public void Dispose() => _rig.Dispose();

    private Task<BatchResult> Run(IReadOnlyList<Job> jobs, EngineContext? ctx = null) =>
        _rig.Engine.RunAsync(jobs, ctx ?? _rig.Context(), TestContext.Current.CancellationToken);

    [Fact]
    public async Task Success_finds_the_file_from_after_move_and_reports_progress()
    {
        var r = await Run([FakeYtDlpRig.Job("ok")]);
        Assert.Equal((1, 0, 0), (r.Ok, r.Warn, r.Fail));
        Assert.Equal(JobStatus.OK, r.Statuses["1|0"]);
        Assert.Equal("Fake video abc.mp4", r.Files["1|0"]);
        Assert.True(File.Exists(Path.Combine(_rig.Folder, "Fake video abc.mp4")));
        Assert.Contains(_rig.Events.OfType<ProgressEvent>(), e => e.Progress.Percent == 41.5);
        Assert.Contains("=== Done: 1 OK, 0 to review, 0 failed ===", _rig.LogText);

        var call = Assert.Single(_rig.Calls());
        Assert.Contains("--progress", call);
        Assert.Equal("after_move:filepath", call[Array.IndexOf(call, "--print") + 1]);
    }

    [Fact]
    public async Task Exit_code_101_is_not_an_error()
    {
        var r = await Run([FakeYtDlpRig.Job("ok101")]);
        Assert.Equal(JobStatus.OK, r.Statuses["1|0"]);
        Assert.Equal(0, r.Fail);
    }

    [Fact]
    public async Task Error_fails_the_job_and_every_clip_of_that_video()
    {
        var r = await Run(
        [
            FakeYtDlpRig.Job("error", count: 2, start: "1:00"),
            FakeYtDlpRig.Job("error", index: 1, count: 2, download: false, start: "2:00"),
            FakeYtDlpRig.Job("ok", id: "other", group: 2),
        ]);
        Assert.Equal((1, 0, 2), (r.Ok, r.Warn, r.Fail));
        Assert.Equal(JobStatus.ERROR, r.Statuses["1|0"]);
        Assert.Equal(JobStatus.ERROR, r.Statuses["1|1"]);
        Assert.Equal(JobStatus.OK, r.Statuses["2|0"]);
        Assert.Contains("ERROR  no file produced (exit code 1).", _rig.LogText);
        Assert.Contains("skipped: the download of this video failed", _rig.LogText);
        Assert.Equal(2, r.FailedItems.Count);
    }

    [Fact]
    public async Task File_written_but_exit_code_1_is_review()
    {
        var r = await Run([FakeYtDlpRig.Job("partial")]);
        Assert.Equal(JobStatus.REVIEW, r.Statuses["1|0"]);
        Assert.Equal(1, r.Warn);
        Assert.Equal("yt-dlp reported an error after writing the file", r.Reasons["1|0"]);
        Assert.Contains(_rig.Events.OfType<StatusEvent>(), e => e.Status == JobStatus.REVIEW && e.Reason.Length > 0);
    }

    [Fact]
    public async Task Bot_check_tries_the_next_cookie_source_and_remembers_the_one_that_worked()
    {
        IReadOnlyList<CookieSource> chain =
            [CookieSource.FromBrowser("firefox"), CookieSource.FromBrowser("chrome"), CookieSource.NoCookies];
        var r = await Run([FakeYtDlpRig.Job("bot-unless-chrome", "v1"), FakeYtDlpRig.Job("bot-unless-chrome", "v2", group: 2)],
                          _rig.Context(chain));
        Assert.Equal(2, r.Ok);
        Assert.False(r.Blocked);
        Assert.Equal("chrome", _rig.Engine.PreferredBrowser);

        var browsers = _rig.Calls().Select(c => c[Array.IndexOf(c, "--cookies-from-browser") + 1]).ToList();
        // First video: firefox fails, chrome works. Second video: chrome is tried first.
        Assert.Equal(["firefox", "chrome", "chrome"], browsers);
        Assert.Contains("\"firefox cookies\" did not work - trying \"chrome cookies\"...", _rig.LogText);
    }

    [Fact]
    public async Task Bot_check_everywhere_fails_with_a_hint_and_marks_the_batch_blocked()
    {
        var r = await Run([FakeYtDlpRig.Job("bot")], _rig.Context([CookieSource.FromBrowser("firefox"), CookieSource.NoCookies]));
        Assert.Equal(JobStatus.ERROR, r.Statuses["1|0"]);
        Assert.True(r.Blocked);
        Assert.Equal(2, _rig.Calls().Count);
        Assert.Contains("Hint: log into YouTube in Firefox", _rig.LogText);
    }

    [Fact]
    public async Task Stop_when_blocked_leaves_the_rest_queued()
    {
        var ctx = _rig.Context() with { StopWhenBlocked = true };
        var r = await Run([FakeYtDlpRig.Job("bot", "a"), FakeYtDlpRig.Job("ok", "b", group: 2)], ctx);
        Assert.True(r.Blocked);
        Assert.Equal(JobStatus.Queued, r.Statuses["2|0"]);
        Assert.Single(_rig.Calls());
    }

    [Fact]
    public async Task Video_in_the_archive_is_skipped_with_its_clips()
    {
        var archive = Path.Combine(_rig.Dir, "sub.txt");
        File.WriteAllText(archive, "fake seen\n");
        var ctx = _rig.Context() with { ArchiveFile = archive, ForceArchive = true };
        var r = await Run([FakeYtDlpRig.Job("ok", "seen", count: 2, start: "1"), FakeYtDlpRig.Job("ok", "seen", index: 1, count: 2, download: false, start: "2")], ctx);
        Assert.Equal(JobStatus.Skipped, r.Statuses["1|0"]);
        Assert.Equal(JobStatus.Skipped, r.Statuses["1|1"]);
        Assert.Equal((0, 2, 0), (r.Ok, r.Warn, r.Fail));
    }

    [Fact]
    public async Task Stop_kills_yt_dlp_and_its_children()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        _rig.Engine.Event += e => { if (e is ProgressEvent) cts.Cancel(); };
        var sw = Stopwatch.StartNew();
        var r = await _rig.Engine.RunAsync([FakeYtDlpRig.Job("slow"), FakeYtDlpRig.Job("ok", "next", group: 2)], _rig.Context(), cts.Token);
        Assert.True(r.Cancelled);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), $"took {sw.Elapsed}");
        Assert.Equal(JobStatus.Queued, r.Statuses["2|0"]);
        Assert.Contains("=== Stopped:", _rig.LogText);

        var pidFile = Path.Combine(_rig.Folder, "fake-child.pid");
        Assert.True(File.Exists(pidFile));
        var pid = int.Parse(File.ReadAllText(pidFile));
        await Task.Delay(500, TestContext.Current.CancellationToken);
        Assert.False(IsRunning(pid), "the child process of yt-dlp is still running");
    }

    private static bool IsRunning(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    [Fact]
    public async Task Session_cookie_copy_is_written_then_read_first_AUDIT_M5()
    {
        var jar = Path.Combine(_rig.Dir, "session-cookies.txt");
        var ctx = _rig.Context([CookieSource.FromBrowser("firefox"), CookieSource.NoCookies]) with { SessionCookieFile = jar };
        var r = await Run([FakeYtDlpRig.Job("ok", "v1"), FakeYtDlpRig.Job("ok", "v2", group: 2)], ctx);
        Assert.Equal(2, r.Ok);
        var calls = _rig.Calls();
        Assert.Equal("firefox", calls[0][Array.IndexOf(calls[0], "--cookies-from-browser") + 1]);
        Assert.Equal(jar, calls[0][Array.IndexOf(calls[0], "--cookies") + 1]);
        Assert.DoesNotContain("--cookies-from-browser", calls[1]);
        Assert.Equal(jar, calls[1][Array.IndexOf(calls[1], "--cookies") + 1]);
    }

    [Fact]
    public async Task Missing_program_is_an_error_not_a_crash()
    {
        var ctx = _rig.Context(tools: new ToolPaths(Path.Combine(_rig.Dir, "nope.exe"), null, null, null));
        var r = await Run([FakeYtDlpRig.Job("ok")], ctx);
        Assert.Equal(JobStatus.ERROR, r.Statuses["1|0"]);
        Assert.Contains("ERROR: could not start yt-dlp", _rig.LogText);
    }

    [Fact]
    public void Retry_takes_only_failed_jobs_and_downloads_once_per_video()
    {
        IReadOnlyList<Job> jobs =
        [
            FakeYtDlpRig.Job("a", count: 3), FakeYtDlpRig.Job("a", index: 1, count: 3, download: false),
            FakeYtDlpRig.Job("a", index: 2, count: 3, download: false), FakeYtDlpRig.Job("b", group: 2),
        ];
        var statuses = new Dictionary<string, JobStatus>
        {
            ["1|0"] = JobStatus.OK, ["1|1"] = JobStatus.ERROR, ["1|2"] = JobStatus.ERROR, ["2|0"] = JobStatus.ERROR,
        };
        var retry = JobEngine.RetryJobs(jobs, statuses);
        Assert.Equal(["1|1", "1|2", "2|0"], retry.Select(j => j.Key));
        Assert.Equal([true, false, true], retry.Select(j => j.Download));
    }
}
