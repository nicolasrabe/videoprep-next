using Videoprep.Core.Engine;
using Videoprep.Core.Logging;
using Videoprep.Core.Preview;

namespace Videoprep.Core.Tests;

/// <summary>
/// Real requests to YouTube, only with a freely licensed video (Big Buck Bunny, Blender Foundation, CC BY 3.0).
/// Opt-in twice: Category=Integration (not in the main CI job) and VIDEOPREP_NETWORK_TESTS=1, so CI never depends on
/// YouTube. yt-dlp and deno are taken from .tools/ (or VIDEOPREP_TOOLS_DIR).
/// </summary>
[Trait("Category", "Integration")]
public sealed class NetworkTests
{
    private const string Bunny = "https://www.youtube.com/watch?v=aqz-KE-bpKQ";

    private static ToolPaths Tools()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("VIDEOPREP_NETWORK_TESTS") == "1", "set VIDEOPREP_NETWORK_TESTS=1 to ask YouTube for real");
        var dir = Environment.GetEnvironmentVariable("VIDEOPREP_TOOLS_DIR");
        if (string.IsNullOrEmpty(dir))
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d is not null && !Directory.Exists(Path.Combine(d.FullName, ".tools"))) d = d.Parent;
            dir = d is null ? "" : Path.Combine(d.FullName, ".tools");
        }
        string? Find(string name) => new[] { name + ".exe", name }.Select(n => Path.Combine(dir, n)).FirstOrDefault(File.Exists);
        var ytdlp = Find("yt-dlp");
        Assert.SkipWhen(ytdlp is null, "yt-dlp not found in .tools/");
        return new ToolPaths(ytdlp, null, null, Find("deno"));
    }

    [Fact]
    public async Task Card_of_a_free_video_comes_from_one_yt_dlp_run()
    {
        var tools = Tools();
        var log = new Log();
        using var http = new HttpClient();
        var service = new VideoInfoService(new ProcessRunner(), http, log) { Timeout = TimeSpan.FromSeconds(90) };
        service.Request(Bunny, new InfoRequest(tools, [CookieSource.NoCookies]));
        for (var i = 0; i < 600 && service.Get("aqz-KE-bpKQ") is not { State: not InfoState.Loading, Thumbnail: not null }
                                && service.Get("aqz-KE-bpKQ")?.State != InfoState.Failed; i++)
            await Task.Delay(200, TestContext.Current.CancellationToken);
        var info = service.Get("aqz-KE-bpKQ")!;
        Assert.True(info.State == InfoState.Ready, info.FailReason + " " + string.Join(" | ", log.Recent()));
        Assert.Contains("Big Buck Bunny", info.Title);
        Assert.Equal("Blender", info.Channel);
        Assert.Equal("20141110", info.UploadDate);
        Assert.True(info.Views > 1_000_000);
        Assert.InRange(info.DurationSeconds ?? 0, 600, 700);
        Assert.NotEmpty(info.Description);
        Assert.NotNull(info.Thumbnail);
        Assert.Equal(1, service.RunsStarted);
    }

    [Fact]
    public async Task Background_title_uses_oembed_only()
    {
        Tools();
        using var http = new HttpClient();
        var preview = new PreviewService(new ProcessRunner(), http, new Log());
        Assert.True(await preview.LookupTitleAsync("https://youtu.be/aqz-KE-bpKQ", CancellationToken.None));
        Assert.Contains("Big Buck Bunny", preview.Get("aqz-KE-bpKQ")!.Title);
        Assert.Null(preview.Get("aqz-KE-bpKQ")!.Thumbnail);
        Assert.False(await preview.LookupTitleAsync("https://youtu.be/aqz-KE-bpKQ", CancellationToken.None));   // known: not asked again
    }
}
