using System.IO.Compression;
using System.Net;
using Videoprep.Core.Engine;
using Videoprep.Core.Formats;
using Videoprep.Core.Logging;
using Videoprep.Core.Media;
using Videoprep.Core.Models;
using Videoprep.Core.Storage;
using Videoprep.Core.Tools;

namespace Videoprep.Core.Tests;

public class OutputVerifierTests
{
    private static MediaInfo Info(double? videoSeconds, long? frames, double? formatSeconds, bool audio = true, bool video = true) => new(
        video ? new VideoStreamInfo("h264", 64, 64, new Rational(30000, 1001), new Rational(30000, 1001), frames, videoSeconds) : null,
        audio ? new AudioStreamInfo("aac", 48000, 2, formatSeconds) : null,
        formatSeconds);

    private static readonly SourceTiming Timing = new(true, "", 30000, 1001, 30, true, 300, "h264", "aac", false);
    private static readonly CutPlan Plan31 = CutPlanner.Plan("00:00:01:00", "00:00:02:00", Timing);   // 31 frames = 1.034 s

    [Fact]
    public void Exact_clip_passes() =>
        Assert.True(OutputVerifier.Check(Info(1.034, 31, 1.04), MediaKind.Video, Plan31, 10, true).Ok);

    [Fact]
    public void Wrong_frame_count_fails()
    {
        var r = OutputVerifier.Check(Info(1.034, 30, 1.04), MediaKind.Video, Plan31, 10, true);
        Assert.False(r.Ok);
        Assert.Equal("the result has 30 frames, expected 31", r.Reason);
    }

    [Fact]
    public void Wrong_length_fails_when_frames_are_unknown()
    {
        var r = OutputVerifier.Check(Info(null, null, 3.0), MediaKind.Video, Plan31, 10, true);
        Assert.False(r.Ok);
        Assert.StartsWith("the result lasts 3 s, expected 1.034 s", r.Reason);
    }

    [Fact]
    public void Missing_streams_fail()
    {
        Assert.Equal("the result has no video stream", OutputVerifier.Check(Info(1, 31, 1, video: false), MediaKind.Video, Plan31, 10, true).Reason);
        Assert.Equal("the result has no audio stream", OutputVerifier.Check(Info(1.034, 31, 1.04, audio: false), MediaKind.Video, Plan31, 10, true).Reason);
        Assert.True(OutputVerifier.Check(Info(1.034, 31, 1.04, audio: false), MediaKind.Video, Plan31, 10, sourceHadAudio: false).Ok);
    }

    [Fact]
    public void Whole_file_conversion_compares_with_the_source_length()
    {
        Assert.True(OutputVerifier.Check(Info(599.8, null, 600), MediaKind.Video, null, 600, true).Ok);
        Assert.False(OutputVerifier.Check(Info(300, null, 300), MediaKind.Video, null, 600, true).Ok);
    }
}

public class VideoGroupTests
{
    [Fact]
    public void Full_file_is_deleted_only_after_the_last_clip_when_nothing_went_wrong()
    {
        var g = new VideoGroup(2, hasClips: true);
        g.SetDownloaded(Path.Combine("d", "Title__full.mp4"), preExisting: false);
        Assert.Equal("Title", g.Stem);
        g.OutputFinished();
        Assert.False(g.ShouldDeleteFull(keepFullOption: false));
        g.OutputFinished();
        Assert.True(g.ShouldDeleteFull(keepFullOption: false));
        Assert.False(g.ShouldDeleteFull(keepFullOption: true));
    }

    [Fact]
    public void Never_deletes_a_file_that_existed_before_AUDIT_X6()
    {
        var g = new VideoGroup(1, hasClips: true);
        g.SetDownloaded("x__full.mp4", preExisting: true);
        g.OutputFinished();
        Assert.False(g.ShouldDeleteFull(false));
    }

    [Fact]
    public void Keeps_the_full_file_after_a_problem()
    {
        var g = new VideoGroup(1, hasClips: true);
        g.SetDownloaded("x__full.mp4", false);
        g.MarkKeep();
        g.OutputFinished();
        Assert.False(g.ShouldDeleteFull(false));
    }
}

public class CookieStrategyTests
{
    private static Settings S(string source, string? preferred = null, string file = "") =>
        new() { CookieSource = source, PreferredBrowser = preferred, CookieFile = file };

    [Fact]
    public void Auto_tries_installed_browsers_then_no_cookies_preferred_first()
    {
        var chain = CookieStrategy.Chain(S("auto", "edge"), ["firefox", "edge"]);
        Assert.Equal(["edge cookies", "firefox cookies", "no login cookies"], chain.Select(c => c.Label));
        Assert.Equal(["firefox cookies", "no login cookies"], CookieStrategy.Chain(S("auto", "brave"), ["firefox"]).Select(c => c.Label));
    }

    [Fact]
    public void Explicit_choices()
    {
        Assert.Equal([CookieSource.FromBrowser("chrome")], CookieStrategy.Chain(S("chrome"), ["firefox"]));
        Assert.Equal([CookieSource.NoCookies], CookieStrategy.Chain(S("none"), ["firefox"]));
        Assert.Equal([CookieSource.FromFile("c.txt")], CookieStrategy.Chain(S("file", file: "c.txt"), [], _ => true));
        Assert.Equal([CookieSource.NoCookies], CookieStrategy.Chain(S("file", file: "c.txt"), [], _ => false));
    }

    [Fact]
    public void Installed_browsers_from_profile_folders()
    {
        var installed = CookieStrategy.InstalledBrowsers(p => p.Contains("Mozilla") || p.Contains("Vivaldi"));
        Assert.Equal(["firefox", "vivaldi"], installed);
    }

    [Theory]
    [InlineData("ERROR: [youtube] x: Sign in to confirm you’re not a bot", true, true)]
    [InlineData("ERROR: Could not copy Chrome cookie database", true, false)]
    [InlineData("ERROR: Failed to decrypt with DPAPI", true, false)]
    [InlineData("ERROR: HTTP Error 429: Too Many Requests", false, true)]
    [InlineData("ERROR: HTTP Error 404: Not Found", false, false)]
    public void Output_classification(string text, bool retry, bool blocked)
    {
        Assert.Equal(retry, CookieStrategy.RetryOutput().IsMatch(text));
        Assert.Equal(blocked, CookieStrategy.BlockedOutput().IsMatch(text));
    }
}

public sealed class StorageTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("videoprep-storage-").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void Queue_round_trip()
    {
        var path = Path.Combine(_dir, "queue.json");
        QueueItem[] items =
        [
            new() { Id = 1, Url = "https://www.youtube.com/watch?v=jNQXAC9IVRw", Format = "movp", Cap = 2160, Naming = NamingMode.Series,
                    Prefix = "Toma", Clips = [new("00:00:01:00", ""), new("-00:00:00:15", "")], Fps = 29.97 },
            new() { Id = 2, Url = "https://twitch.tv/videos/1", Format = "mp3", Naming = NamingMode.Custom, Custom = "Entrevista" },
        ];
        QueueStore.Save(path, items);
        Assert.Contains("\"naming\": \"series\"", File.ReadAllText(path));
        Assert.DoesNotContain("videoKey", File.ReadAllText(path));
        var back = QueueStore.Load(path, out var problem);
        Assert.Null(problem);
        Assert.Equal(2, back.Count);
        Assert.Equal(items[0] with { Clips = back[0].Clips }, back[0]);
        Assert.Equal(items[0].Clips, back[0].Clips);
        Assert.Equal(Site.Other, back[1].Site);
    }

    [Fact]
    public void Broken_queue_items_are_dropped_not_fatal()
    {
        var path = Path.Combine(_dir, "queue.json");
        File.WriteAllText(path, """{"version":1,"items":[{"id":1,"url":"https://youtu.be/x","format":"xyz"},{"id":2,"url":"https://vimeo.com/1","format":"mp4"}]}""");
        var back = QueueStore.Load(path, out var problem);
        Assert.Single(back);
        Assert.Equal("1 queue item(s) could not be read and were left out", problem);
        File.WriteAllText(path, "garbage");
        Assert.Empty(QueueStore.Load(path, out problem));
        Assert.NotNull(problem);
    }

    [Fact]
    public void Run_lock_is_exclusive_and_released()
    {
        var path = Path.Combine(_dir, "run.lock");
        using (var first = RunLock.TryAcquire(path))
        {
            Assert.NotNull(first);
            Assert.Null(RunLock.TryAcquire(path));
        }
        using var again = RunLock.TryAcquire(path);
        Assert.NotNull(again);
        Assert.Equal(3, RunLock.ExitCodeBusy);
    }

    [Fact]
    public void Log_keeps_recent_lines_and_writes_a_crash_file()
    {
        var log = new Log(_dir, keepLines: 3);
        var seen = new List<string>();
        log.LineAdded += seen.Add;
        foreach (var l in new[] { "a", "b", "c", "d" }) log.Write(l);
        Assert.Equal(["b", "c", "d"], log.Recent());
        Assert.Equal(4, seen.Count);
        Assert.True(File.Exists(log.FilePath));

        var crash = log.Crash(new InvalidOperationException("boom"), "test");
        Assert.NotNull(crash);
        Assert.Contains("boom", File.ReadAllText(crash!));
        Assert.Contains(log.Recent(50), l => l.StartsWith("INTERNAL ERROR in test", StringComparison.Ordinal));
    }
}

public sealed class ToolManagerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("videoprep-tools-").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private static string Exe(string n) => OperatingSystem.IsWindows() ? n + ".exe" : n;

    /// <summary>Serves fake downloads: yt-dlp.exe bytes, and zips shaped like the real FFmpeg/Deno releases.</summary>
    private sealed class FakeServer : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            Requests.Add(url);
            byte[] body = url switch
            {
                ToolManager.YtDlpUrl => [1, 2, 3],
                ToolManager.FfmpegUrl => Zip(("ffmpeg-master-latest-win64-gpl/bin/ffmpeg.exe", "F"), ("ffmpeg-master-latest-win64-gpl/bin/ffprobe.exe", "P"),
                                             ("ffmpeg-master-latest-win64-gpl/LICENSE.txt", "L")),
                ToolManager.DenoUrl => Zip(("deno.exe", "D")),
                _ => [],
            };
            var resp = new HttpResponseMessage(body.Length > 0 ? HttpStatusCode.OK : HttpStatusCode.NotFound) { Content = new ByteArrayContent(body) };
            return Task.FromResult(resp);
        }

        private static byte[] Zip(params (string Name, string Text)[] files)
        {
            using var ms = new MemoryStream();
            using (var z = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
                foreach (var (name, text) in files)
                {
                    using var w = new StreamWriter(z.CreateEntry(name).Open());
                    w.Write(text);
                }
            return ms.ToArray();
        }
    }

    [Fact]
    public async Task Installs_only_the_needed_files_and_reports_progress()
    {
        var server = new FakeServer();
        var tools = Path.Combine(_dir, "tools");
        var tm = new ToolManager(tools, new HttpClient(server), exeDir: _dir, pathVariable: "");
        var progress = new List<ToolProgress>();
        await tm.InstallAsync(["yt-dlp", "ffmpeg", "deno"], new SyncProgress<ToolProgress>(progress.Add), TestContext.Current.CancellationToken);

        Assert.Equal(["deno.exe", "ffmpeg.exe", "ffprobe.exe", "yt-dlp.exe"], Directory.GetFiles(tools).Select(Path.GetFileName).Order());
        Assert.Equal("P", File.ReadAllText(Path.Combine(tools, "ffprobe.exe")));
        Assert.Contains(progress, p => p.Tool == "ffmpeg");
        Assert.Equal(3, server.Requests.Count);
    }

    [Fact]
    public async Task Failed_download_leaves_no_half_file()
    {
        var tools = Path.Combine(_dir, "tools");
        var tm = new ToolManager(tools, new HttpClient(new FailingServer()), exeDir: _dir, pathVariable: "");
        await Assert.ThrowsAsync<HttpRequestException>(() => tm.InstallAsync(["yt-dlp"], null, TestContext.Current.CancellationToken));
        Assert.Empty(Directory.GetFiles(tools));
    }

    private sealed class FailingServer : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
    }

    [Fact]
    public void Resolve_order_tools_folder_then_exe_folder_then_path()
    {
        var tools = Directory.CreateDirectory(Path.Combine(_dir, "tools")).FullName;
        var exeDir = Directory.CreateDirectory(Path.Combine(_dir, "app")).FullName;
        var pathDir = Directory.CreateDirectory(Path.Combine(_dir, "path")).FullName;
        File.WriteAllText(Path.Combine(tools, Exe("yt-dlp")), "");
        File.WriteAllText(Path.Combine(exeDir, Exe("yt-dlp")), "");
        File.WriteAllText(Path.Combine(exeDir, Exe("ffmpeg")), "");
        File.WriteAllText(Path.Combine(pathDir, Exe("ffprobe")), "");

        var t = new ToolManager(tools, new HttpClient(), exeDir, pathDir).Resolve();
        Assert.Equal(Path.Combine(tools, Exe("yt-dlp")), t.YtDlp);
        Assert.Equal(Path.Combine(exeDir, Exe("ffmpeg")), t.Ffmpeg);
        Assert.Equal(Path.Combine(pathDir, Exe("ffprobe")), t.Ffprobe);
        Assert.Null(t.Deno);
        Assert.Equal(["deno"], t.Missing());
        Assert.True(Path.IsPathFullyQualified(t.YtDlp!));
    }

    /// <summary>IProgress that reports on the calling thread (Progress&lt;T&gt; posts to the thread pool).</summary>
    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
