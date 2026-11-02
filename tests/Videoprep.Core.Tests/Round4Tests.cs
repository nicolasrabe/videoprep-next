using System.Net;
using Videoprep.Core.Engine;
using Videoprep.Core.Logging;
using Videoprep.Core.Models;
using Videoprep.Core.Preview;

namespace Videoprep.Core.Tests;

/// <summary>An HTTP door that answers from a table of addresses (404 for the rest) and counts the calls.</summary>
internal sealed class StubWeb(Dictionary<string, byte[]> answers) : HttpMessageHandler
{
    public List<string> Asked { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        lock (Asked) Asked.Add(url);
        return Task.FromResult(answers.TryGetValue(url, out var bytes)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
            : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

/// <summary>RONDA 4 (spec C): the thumbnail service.</summary>
public sealed class ThumbnailServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("videoprep-thumbs-").FullName;
    private static readonly byte[] Jpg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4];

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public async Task Youtube_takes_the_biggest_picture_and_falls_back_on_404()
    {
        var web = new StubWeb(new()
        {
            ["https://i.ytimg.com/vi/abc/sddefault.jpg"] = Jpg,
            ["https://i.ytimg.com/vi/abc/hqdefault.jpg"] = [9, 9, 9],
        });
        var svc = new ThumbnailService(new HttpClient(web), _dir);
        var bytes = await svc.GetYoutubeAsync("abc", TestContext.Current.CancellationToken);
        Assert.Equal(Jpg, bytes);                                       // maxres answered 404, sd was the best
        Assert.Equal(["https://i.ytimg.com/vi/abc/maxresdefault.jpg", "https://i.ytimg.com/vi/abc/sddefault.jpg"], web.Asked);
    }

    [Fact]
    public async Task A_picture_already_seen_comes_from_memory_or_disk_without_the_network()
    {
        var web = new StubWeb(new() { ["https://i.ytimg.com/vi/abc/maxresdefault.jpg"] = Jpg });
        var first = new ThumbnailService(new HttpClient(web), _dir);
        await first.GetYoutubeAsync("abc", TestContext.Current.CancellationToken);
        Assert.Equal(1, first.Downloads);
        Assert.Equal(Jpg, first.TryGet("abc"));                          // memory

        var second = new ThumbnailService(new HttpClient(web), _dir);    // a new run of the program: from disk
        Assert.Equal(Jpg, second.TryGet("abc"));
        Assert.Equal(Jpg, await second.GetYoutubeAsync("abc", TestContext.Current.CancellationToken));
        Assert.Equal(0, second.Downloads);
        Assert.Single(web.Asked);
    }

    [Fact]
    public async Task The_disk_cache_stays_under_its_limit_and_the_oldest_picture_goes_first()
    {
        var big = new byte[4000];
        big[0] = 0xFF;
        var web = new StubWeb(Enumerable.Range(1, 4).ToDictionary(i => $"https://x.test/{i}.jpg", _ => big));
        var svc = new ThumbnailService(new HttpClient(web), _dir) { MaxCacheBytes = 9000 };
        for (var i = 1; i <= 4; i++)
        {
            await svc.GetAsync("v" + i, [$"https://x.test/{i}.jpg"], TestContext.Current.CancellationToken);
            // Make the order of age clear for the file system's clock.
            File.SetLastWriteTimeUtc(Path.Combine(_dir, "thumbs", $"v{i}.img"), DateTime.UtcNow.AddMinutes(i - 10));
            File.SetLastAccessTimeUtc(Path.Combine(_dir, "thumbs", $"v{i}.img"), DateTime.UtcNow.AddMinutes(i - 10));
        }
        svc.Purge();
        Assert.True(svc.DiskBytes() <= 9000, svc.DiskBytes().ToString());
        Assert.False(File.Exists(Path.Combine(_dir, "thumbs", "v1.img")));   // the oldest was deleted
        Assert.True(File.Exists(Path.Combine(_dir, "thumbs", "v4.img")));    // the newest stays
    }

    [Fact]
    public void Other_sites_use_the_biggest_picture_of_their_metadata_and_only_web_addresses()
    {
        const string json = """{"thumbnail":"https://c.test/default.jpg","thumbnails":[{"url":"https://c.test/small.jpg","width":120,"height":90},{"url":"https://c.test/big.jpg","width":1920,"height":1080},{"url":"file:///etc/passwd","width":9999,"height":9999},{"url":"https://c.test/mid.jpg","width":640,"height":360}]}""";
        Assert.Equal("https://c.test/big.jpg", ThumbnailService.BestFromMetadata(json));
        Assert.Equal("https://c.test/last.jpg", ThumbnailService.BestFromMetadata("""{"thumbnails":[{"url":"https://c.test/first.jpg"},{"url":"https://c.test/last.jpg"}]}"""));
        Assert.Equal("https://c.test/one.jpg", ThumbnailService.BestFromMetadata("""{"thumbnail":"https://c.test/one.jpg"}"""));
        Assert.Null(ThumbnailService.BestFromMetadata("""{"thumbnail":"file:///c:/x.jpg"}"""));
        Assert.Null(ThumbnailService.BestFromMetadata("not json"));
    }

    [Fact]
    public async Task Addresses_that_are_not_web_links_are_never_asked_for()
    {
        var web = new StubWeb([]);
        var svc = new ThumbnailService(new HttpClient(web), _dir);
        Assert.Null(await svc.GetAsync("k", ["file:///c:/secret.jpg", "ftp://x/y.jpg"], TestContext.Current.CancellationToken));
        Assert.Empty(web.Asked);
    }
}

/// <summary>RONDA 4 (spec D): saving a thumbnail as JPG or PNG.</summary>
public sealed class ThumbnailExporterTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("videoprep-thumbsave-").FullName;
    private static readonly byte[] Jpg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public void The_format_is_read_from_the_first_bytes_and_the_chosen_name_decides_what_is_asked()
    {
        Assert.Equal(ThumbFormat.Jpg, ThumbnailExporter.Detect(Jpg));
        Assert.Equal(ThumbFormat.Png, ThumbnailExporter.Detect(Png));
        Assert.Null(ThumbnailExporter.Detect("RIFF\0\0\0\0WEBP"u8));
        Assert.Equal(ThumbFormat.Png, ThumbnailExporter.FormatOf(@"C:\x\Foto.PNG"));
        Assert.Equal(ThumbFormat.Jpg, ThumbnailExporter.FormatOf(@"C:\x\Foto.jpg"));
        Assert.Equal(ThumbFormat.Jpg, ThumbnailExporter.FormatOf(@"C:\x\Foto"));
    }

    [Fact]
    public async Task The_same_format_is_saved_byte_for_byte_without_ffmpeg()
    {
        var path = Path.Combine(_dir, "sub", "a.jpg");
        var error = await ThumbnailExporter.SaveAsync(Jpg, path, ThumbFormat.Jpg, null, new ProcessRunner(), new Log(), TestContext.Current.CancellationToken);
        Assert.Equal("", error);
        Assert.Equal(Jpg, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Another_format_without_ffmpeg_says_why_and_writes_nothing()
    {
        var path = Path.Combine(_dir, "a.png");
        var error = await ThumbnailExporter.SaveAsync(Jpg, path, ThumbFormat.Png, null, new ProcessRunner(), new Log(), TestContext.Current.CancellationToken);
        Assert.Contains("ffmpeg", error);
        Assert.False(File.Exists(path));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_different_format_is_converted_with_the_real_ffmpeg()
    {
        Ffmpeg.SkipIfMissing();
        var src = Path.Combine(_dir, "src.jpg");
        Assert.Equal(0, Ffmpeg.Run("-y", "-f", "lavfi", "-i", "color=c=red:s=64x48", "-frames:v", "1", src));
        var original = await File.ReadAllBytesAsync(src, TestContext.Current.CancellationToken);

        var png = Path.Combine(_dir, "out.png");
        Assert.Equal("", await ThumbnailExporter.SaveAsync(original, png, ThumbFormat.Png, Ffmpeg.Exe, new ProcessRunner(), new Log(), TestContext.Current.CancellationToken));
        Assert.Equal(ThumbFormat.Png, ThumbnailExporter.Detect(await File.ReadAllBytesAsync(png, TestContext.Current.CancellationToken)));

        var jpg = Path.Combine(_dir, "out.jpg");
        var pngBytes = await File.ReadAllBytesAsync(png, TestContext.Current.CancellationToken);
        Assert.Equal("", await ThumbnailExporter.SaveAsync(pngBytes, jpg, ThumbFormat.Jpg, Ffmpeg.Exe, new ProcessRunner(), new Log(), TestContext.Current.CancellationToken));
        Assert.Equal(ThumbFormat.Jpg, ThumbnailExporter.Detect(await File.ReadAllBytesAsync(jpg, TestContext.Current.CancellationToken)));
        Assert.Empty(Directory.GetFiles(Path.GetTempPath(), "videoprep-thumb-*"));   // no temporary files left behind
    }
}

/// <summary>RONDA 4 (spec C): card data kept on disk for 24 h, and the next row asked for in advance.</summary>
public sealed class VideoInfoCacheTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("videoprep-infocache-").FullName;
    private static readonly ToolPaths Tools = new("yt-dlp", null, null, null);
    private static readonly InfoRequest Req = new(Tools, [CookieSource.NoCookies]);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private static string Json(string id, string title = "A title") =>
        "INFO|{\"title\":\"" + title + "\",\"channel\":\"Ch\",\"upload_date\":\"20261001\",\"duration\":60,\"duration_string\":\"1:00\",\"description\":\"D\",\"thumbnail\":\"https://c.test/" + id + ".jpg\"}";

    private VideoInfoService Make(ScriptedRunner runner, StubWeb? web = null, TimeSpan? ttl = null) =>
        new(runner, new HttpClient(web ?? new StubWeb([])), new Log(), new ThumbnailService(new HttpClient(web ?? new StubWeb([])), _dir), _dir)
        {
            Debounce = TimeSpan.Zero, MetaTtl = ttl ?? TimeSpan.FromHours(24),
        };

    private static async Task<VideoInfo> Ready(VideoInfoService svc, string url)
    {
        var key = Videoprep.Core.Text.UrlParser.LinkKey(url);
        for (var i = 0; i < 200; i++)
        {
            if (svc.Get(key) is { State: not InfoState.Loading } info) return info;
            await Task.Delay(25);
        }
        throw new TimeoutException(key);
    }

    [Fact]
    public async Task A_card_read_less_than_24_hours_ago_comes_from_disk_with_no_yt_dlp_run()
    {
        var first = new ScriptedRunner(_ => [Json("v1")]);
        var svc = Make(first);
        svc.Request("https://vimeo.com/111", Req);
        await Ready(svc, "https://vimeo.com/111");
        Assert.Equal(1, first.CallCount);

        var second = new ScriptedRunner(_ => [Json("v1", "should not be asked")]);
        var again = Make(second);                                           // another run of the program
        var info = again.Request("https://vimeo.com/111", Req);
        Assert.Equal((InfoState.Ready, "A title"), (info.State, info.Title));
        Assert.Equal(0, second.CallCount);
    }

    [Fact]
    public async Task A_card_older_than_the_limit_is_asked_for_again()
    {
        var first = new ScriptedRunner(_ => [Json("v1")]);
        var svc = Make(first);
        svc.Request("https://vimeo.com/222", Req);
        await Ready(svc, "https://vimeo.com/222");
        foreach (var f in Directory.GetFiles(Path.Combine(_dir, "meta"))) File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddHours(-25));

        var second = new ScriptedRunner(_ => [Json("v1", "New title")]);
        var again = Make(second);
        again.Request("https://vimeo.com/222", Req);
        var info = await Ready(again, "https://vimeo.com/222");
        Assert.Equal(("New title", 1), (info.Title, second.CallCount));
    }

    [Fact]
    public async Task Prefetch_asks_only_when_idle_one_at_a_time_and_never_after_a_failure()
    {
        var runner = new ScriptedRunner(args => args.Contains("https://vimeo.com/bad") ? ["ERROR: nope"] : [Json("x")], exitCode: 0);
        var svc = Make(runner);
        Assert.True(svc.Prefetch("https://vimeo.com/1", Req));
        Assert.False(svc.Prefetch("https://vimeo.com/2", Req));              // the first is still pending: nothing else is started
        await Ready(svc, "https://vimeo.com/1");
        Assert.True(runner.CallCount <= 1);
        Assert.False(svc.Prefetch("https://vimeo.com/1", Req));              // already known

        // A failed read: the next prefetch stays quiet.
        svc.Request("https://vimeo.com/bad", Req);
        await Ready(svc, "https://vimeo.com/bad");
        Assert.Equal(InfoState.Failed, svc.Get(Videoprep.Core.Text.UrlParser.LinkKey("https://vimeo.com/bad"))!.State);
        Assert.False(svc.Prefetch("https://vimeo.com/3", Req));
        Assert.Equal(1, svc.MostAtOnce);
    }
}

/// <summary>RONDA 4 (spec E): dragging rows in the queue, and "Pausar".</summary>
[Collection("engine")]
public sealed class QueueOrderAndPauseTests : IDisposable
{
    private readonly FakeYtDlpRig _rig = new();

    public void Dispose() => _rig.Dispose();

    private static List<QueueItem> Items(int n) => Enumerable.Range(1, n).Select(i => new QueueItem { Id = i, Url = "https://v.test/" + i }).ToList();

    private static string Order(IEnumerable<QueueItem> items) => string.Join(",", items.Select(i => i.Id));

    [Theory]
    [InlineData("3", 0, "3,1,2,4,5")]        // to the top
    [InlineData("1", 5, "2,3,4,5,1")]        // to the end
    [InlineData("2", 4, "1,3,4,2,5")]        // down
    [InlineData("4,5", 1, "1,4,5,2,3")]      // two at once keep their order
    [InlineData("2", 2, "1,2,3,4,5")]        // dropped where it already is: nothing changes
    [InlineData("1,3,5", 2, "2,1,3,5,4")]    // spread out items gather at the drop
    public void Dropped_rows_land_before_the_target_in_their_own_order(string ids, int before, string expected)
    {
        var items = Items(5);
        var changed = QueueOrder.Move(items, ids.Split(',').Select(int.Parse).ToList(), before, _ => false);
        Assert.Equal(expected, Order(items));
        Assert.Equal(expected != "1,2,3,4,5", changed);
    }

    [Fact]
    public void Items_that_download_or_are_done_never_move_and_nothing_lands_above_them()
    {
        var items = Items(5);
        bool Locked(QueueItem i) => i.Id <= 2;                                   // 1 and 2 are done / downloading
        Assert.False(QueueOrder.Move(items, [1], 4, Locked));                    // a locked item cannot be picked up
        Assert.Equal("1,2,3,4,5", Order(items));
        Assert.True(QueueOrder.Move(items, [5, 1], 0, Locked));                  // 5 moves, 1 stays; the drop is pushed below the locked ones
        Assert.Equal("1,2,5,3,4", Order(items));
        Assert.True(QueueOrder.Move(items, [4], 1, Locked));
        Assert.Equal("1,2,4,5,3", Order(items));
    }

    [Fact]
    public async Task Pause_finishes_the_current_video_and_leaves_the_rest_queued()
    {
        var asked = 0;
        var ctx = _rig.Context() with { PauseRequested = () => ++asked > 0 };   // "Pausar" was pressed during the first video
        var r = await _rig.Engine.RunAsync([FakeYtDlpRig.Job("ok", "a", group: 1), FakeYtDlpRig.Job("ok", "b", group: 2), FakeYtDlpRig.Job("ok", "c", group: 3)], ctx,
                                           TestContext.Current.CancellationToken);
        Assert.Equal(JobStatus.OK, r.Statuses["1|0"]);                           // the one that was downloading finished
        Assert.Equal(JobStatus.Queued, r.Statuses["2|0"]);
        Assert.Equal(JobStatus.Queued, r.Statuses["3|0"]);
        Assert.False(r.Cancelled);
        Assert.Single(_rig.Calls());
        Assert.Contains("Paused", _rig.LogText);
    }

    [Fact]
    public async Task Without_a_pause_request_everything_runs()
    {
        var r = await _rig.Engine.RunAsync([FakeYtDlpRig.Job("ok", "a", group: 1), FakeYtDlpRig.Job("ok", "b", group: 2)], _rig.Context() with { PauseRequested = () => false },
                                           TestContext.Current.CancellationToken);
        Assert.Equal(2, r.Ok);
    }
}

/// <summary>RONDA 4 (spec A): the full window comes back on the monitor where the small one is now.</summary>
public sealed class FullFromMiniTests
{
    private static readonly ScreenArea Left = new(new(0, 0, 1920, 1080), new(0, 0, 1920, 1040), 1.0);
    private static readonly ScreenArea Right = new(new(1920, 0, 2560, 1440), new(1920, 0, 2560, 1400), 1.5);
    private static readonly ScreenArea[] Both = [Left, Right];

    [Fact]
    public void Same_monitor_the_top_right_corner_of_the_small_window_is_kept()
    {
        var mini = new PixelBox(1500, 200, 340, 296);                               // right edge 1840
        Assert.Equal(new PixelBox(840, 200, 1000, 700), MiniPlacement.FullFromMini(Both, new PixelBox(100, 100, 1000, 700), mini));
    }

    [Fact]
    public void The_other_monitor_gets_the_old_size_in_that_monitors_scale()
    {
        var mini = new PixelBox(3900, 100, 510, 444);                               // on the 150 % monitor, right edge 4410
        Assert.Equal(new PixelBox(4410 - 1500, 100, 1500, 1050), MiniPlacement.FullFromMini(Both, new PixelBox(100, 100, 1000, 700), mini));
    }

    [Fact]
    public void Going_from_a_big_monitor_to_a_small_one_shrinks_the_window_to_fit()
    {
        var onSmall = new PixelBox(1500, 900, 340, 296);                            // bottom right of the 100 % monitor
        var r = MiniPlacement.FullFromMini(Both, new PixelBox(2000, 100, 2400, 1300), onSmall);   // came from the 150 % monitor: 1600x867 dip
        Assert.True(r.X >= 0 && r.Right <= 1920 && r.Y >= 0 && r.Bottom <= 1040, r.ToString());
        Assert.Equal(1600, r.Width);
    }

    [Fact]
    public void A_small_window_on_no_monitor_leaves_the_old_place()
    {
        var old = new PixelBox(100, 100, 1000, 700);
        Assert.Equal(old, MiniPlacement.FullFromMini([], old, new PixelBox(5, 5, 340, 296)));
    }
}

/// <summary>RONDA 4 (spec C): the card asks yt-dlp for the least it needs.</summary>
public sealed class CardArgsTests
{
    [Fact]
    public void The_card_request_skips_the_format_manifests_and_asks_for_the_picture_list()
    {
        var args = YtDlpArgs.Info("https://www.youtube.com/watch?v=aqz-KE-bpKQ", new ToolPaths(null, null, null, null), CookieSource.NoCookies);
        Assert.Equal("youtube:skip=hls,dash,translated_subs", args[args.ToList().IndexOf("--extractor-args") + 1]);
        Assert.Contains("thumbnails", YtDlpArgs.InfoTemplate);
        Assert.Contains("--skip-download", args);
    }
}
