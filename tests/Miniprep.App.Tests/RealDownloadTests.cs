using Miniprep.App.Logic;
using Miniprep.App.Services;
using Videoprep.Core.Engine;
using Videoprep.Core.Logging;
using Videoprep.Core.Models;
using Videoprep.Core.Preview;
using Videoprep.Core.Tools;

namespace Miniprep.App.Tests;

/// <summary>
/// The real thing: the tools in .tools/ and the internet ("Me at the zoo", 19 seconds). Not part of the normal run
/// (<c>--filter "Category=Integration"</c>); skipped by itself when the tools or the connection are not there.
/// </summary>
[Trait("Category", "Integration")]
public sealed class RealDownloadTests : IDisposable
{
    private const string Link = "https://www.youtube.com/watch?v=jNQXAC9IVRw";
    private readonly string _dir = Directory.CreateTempSubdirectory("miniprep-real-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* temp */ }
    }

    private static string? ToolsFolder()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "videoprep.slnx")) && Directory.Exists(Path.Combine(d.FullName, ".tools"))) return Path.Combine(d.FullName, ".tools");
        return null;
    }

    private (MiniServices Svc, ToolsGate Gate)? Build()
    {
        if (ToolsFolder() is not { } tools || !File.Exists(Path.Combine(tools, "yt-dlp.exe"))) return null;
        var paths = MiniPaths.For(Path.Combine(_dir, "miniprep"), tools);
        var http = new HttpClient();
        var log = new Log();
        var runner = new ProcessRunner();
        var thumbs = new ThumbnailService(http, paths.CacheDir, log);
        var svc = new MiniServices
        {
            Paths = paths, Log = log, Runner = runner, Thumbs = thumbs,
            Tools = new ToolManager(paths.ToolsDir, http, exeDir: _dir, pathVariable: ""),
            Info = new VideoInfoService(runner, http, log, thumbs, paths.CacheDir),
        };
        return (svc, new ToolsGate(svc.Tools));
    }

    [Fact]
    public async Task The_data_of_a_real_link_arrives_and_both_formats_download_with_the_chosen_name()
    {
        if (Build() is not { } rig) return;
        var (svc, gate) = rig;
        var ct = TestContext.Current.CancellationToken;
        var paths = await gate.GetAsync(ct);
        Assert.NotNull(paths);

        // Prep: the card of the link
        var info = svc.Info.Request(Link, new InfoRequest(paths, [CookieSource.NoCookies]));
        for (var i = 0; i < 600 && info.State == InfoState.Loading; i++) { await Task.Delay(100, ct); info = svc.Info.Get(info.Key)!; }
        if (info.State == InfoState.Failed) return;   // no connection or the site complains: nothing to judge
        Assert.Equal("Me at the zoo", info.Title);
        Assert.NotEmpty(info.Duration);

        var downloader = new Downloader(svc, gate);
        var folder = Path.Combine(_dir, "salida");
        foreach (var (format, ext) in new[] { ("mp4", ".mp4"), ("mp3", ".mp3") })
        {
            var item = new QueueItem { Id = 0, Url = Link, Format = format, Cap = 720, Naming = NamingMode.Custom, Custom = "Mi elefante " + format };
            var percents = new List<int>();
            var result = await downloader.RunAsync(new DownloadRequest(item, folder, false), new Progress<DownloadProgress>(p => { lock (percents) percents.Add(p.Percent); }), ct);
            Assert.True(result.Ok, result.Reason);
            Assert.Equal(Path.Combine(folder, "Mi elefante " + format + ext), result.File);
            Assert.True(File.Exists(result.File));
            Assert.True(new FileInfo(result.File).Length > 10_000);
            Assert.NotEmpty(percents);
        }
    }
}
