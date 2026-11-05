using System.Net;
using Miniprep.App.Services;
using Miniprep.App.ViewModels;
using Videoprep.Core.Engine;
using Videoprep.Core.Logging;
using Videoprep.Core.Preview;
using Videoprep.Core.Tests;
using Videoprep.Core.Tools;

namespace Miniprep.App.Tests;

/// <summary>A person that answers the dialogs the way the test says, and remembers what was shown.</summary>
internal sealed class FakeMiniUi : IMiniUi
{
    public string? SavePath { get; set; }
    public string? FolderToPick { get; set; }
    public string? ThumbPath { get; set; }
    public Queue<string?> Texts { get; } = new();
    public List<string> SaveSuggestions { get; } = [];
    public List<string> Messages { get; } = [];
    public List<string> Opened { get; } = [];
    public string Clipboard { get; set; } = "";

    public Task<string?> PickSaveFile(string suggestedName, string? startFolder) { SaveSuggestions.Add(suggestedName); return Task.FromResult(SavePath); }
    public Task<string?> PickFolder(string? start) => Task.FromResult(FolderToPick);
    public Task<string?> AskText(string title, string prompt, string initial) => Task.FromResult(Texts.Count > 0 ? Texts.Dequeue() : null);
    public Task<string?> PickSaveThumbnail(string suggestedName) => Task.FromResult(ThumbPath);
    public Task SetClipboardText(string text) { Clipboard = text; return Task.CompletedTask; }
    public Task<string?> GetClipboardText() => Task.FromResult<string?>(Clipboard);
    public void OpenUrl(string url) => Opened.Add(url);
    public Task ShowMessage(string text) { Messages.Add(text); return Task.CompletedTask; }
}

/// <summary>A downloader that never starts a process: the test decides how each download ends.</summary>
internal sealed class FakeDownloader : IDownloader
{
    public List<DownloadRequest> Requests { get; } = [];
    public Func<DownloadRequest, CancellationToken, Task<DownloadResult>> Behaviour { get; set; } =
        (r, _) => Task.FromResult(new DownloadResult(true, false, Path.Combine(r.Folder, r.Item.Custom + ".mp4"), ""));

    public async Task<DownloadResult> RunAsync(DownloadRequest request, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        lock (Requests) Requests.Add(request);
        progress?.Report(new DownloadProgress(37, "2,1 MB/s"));
        return await Behaviour(request, ct);
    }
}

internal sealed class NoWeb : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
}

/// <summary>miniprep over temp folders: its own settings and queue, dummy tools, an in-memory yt-dlp and a fake downloader.</summary>
internal sealed class MiniRig : IDisposable
{
    public string Dir { get; } = Directory.CreateTempSubdirectory("miniprep-test-").FullName;
    public MiniPaths Paths { get; }
    public FakeMiniUi Ui { get; } = new();
    public FakeDownloader Downloader { get; } = new();
    public ScriptedRunner Runner { get; }
    public MiniServices Services { get; }
    public Func<IReadOnlyList<string>, IEnumerable<string>> Script { get; set; }

    public const string InfoJson =
        "{\"title\":\"Entrevista completa\",\"channel\":\"Canal de ejemplo\",\"upload_date\":\"20261021\",\"view_count\":41000,\"duration\":2292," +
        "\"duration_string\":\"38:12\",\"description\":\"En este episodio charlamos con nuestro invitado.\"}";

    public MiniRig(Func<IReadOnlyList<string>, IEnumerable<string>>? script = null, int exitCode = 0, IReadOnlyList<string>? browsers = null)
    {
        Script = script ?? (_ => ["INFO|" + InfoJson]);
        Paths = MiniPaths.For(Path.Combine(Dir, "miniprep"), null);
        Directory.CreateDirectory(Paths.ToolsDir);
        foreach (var tool in new[] { "yt-dlp.exe", "ffmpeg.exe", "ffprobe.exe", "deno.exe" }) File.WriteAllText(Path.Combine(Paths.ToolsDir, tool), "");
        Runner = new ScriptedRunner(a => Script(a), exitCode);
        var log = new Log();
        var http = new HttpClient(new NoWeb());
        var thumbs = new ThumbnailService(http, Paths.CacheDir, log);
        Services = new MiniServices
        {
            Paths = Paths, Log = log, Runner = Runner, Thumbs = thumbs,
            Tools = new ToolManager(Paths.ToolsDir, http, exeDir: Dir, pathVariable: ""),
            Info = new VideoInfoService(Runner, http, log, thumbs, Paths.CacheDir) { Debounce = TimeSpan.Zero, Delay = (_, _) => Task.CompletedTask },
            InstalledBrowsers = () => browsers ?? [],
        };
    }

    /// <summary>The window's view model with everything instant and on the calling thread.</summary>
    public MiniMainViewModel NewViewModel() =>
        new(Services, Ui, Downloader, tools: null, post: a => { lock (UiLock) a(); }, delay: (_, _) => Task.CompletedTask);

    /// <summary>The real program runs everything for the window on one thread; the tests run the same actions one at a time.</summary>
    private static readonly object UiLock = new();

    public void Dispose()
    {
        try { Directory.Delete(Dir, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* temp folder: the system cleans it */ }
    }
}
