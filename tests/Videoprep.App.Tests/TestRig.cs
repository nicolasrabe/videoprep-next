using System.Net;
using Videoprep.App.Services;
using Videoprep.App.ViewModels;
using Videoprep.Core.Engine;
using Videoprep.Core.Logging;
using Videoprep.Core.Preview;
using Videoprep.Core.Storage;
using Videoprep.Core.Tools;

namespace Videoprep.App.Tests;

/// <summary>A user that answers dialogs the way the test says, and remembers what was shown.</summary>
internal sealed class FakeUi : IUiServices
{
    public List<string> Messages { get; } = [];
    public List<string> Questions { get; } = [];
    public Queue<bool> Answers { get; } = new();
    public string? FolderToPick { get; set; }
    public string? FileToPick { get; set; }
    public string? ColorToPick { get; set; }
    public string Clipboard { get; private set; } = "";
    public int Sounds { get; private set; }
    public List<string> OpenedFolders { get; } = [];

    public Task ShowMessage(string text, bool warning = false) { Messages.Add(text); return Task.CompletedTask; }
    public Task<bool> AskYesNo(string title, string text) { Questions.Add(text); return Task.FromResult(Answers.Count > 0 && Answers.Dequeue()); }
    public Task<string?> PickFolder(string? start) => Task.FromResult(FolderToPick);
    public Task<string?> PickTextFile() => Task.FromResult(FileToPick);
    public Task<string?> PickSaveFile(string suggestedName) => Task.FromResult(FileToPick);
    public Task<string?> PickColor(string current) => Task.FromResult(ColorToPick);
    public Task<bool> ShowOptions(OptionsViewModel options) => Task.FromResult(false);
    public Task SetClipboardText(string text) { Clipboard = text; return Task.CompletedTask; }
    public void OpenFolder(string path) => OpenedFolders.Add(path);
    public void PlayDoneSound() => Sounds++;
}

/// <summary>No internet: oEmbed answers for one video, everything else is 404.</summary>
internal sealed class FakeWeb : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var url = request.RequestUri!.ToString();
        if (url.StartsWith("https://www.youtube.com/oembed", StringComparison.Ordinal))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"title":"Me at the zoo","author_name":"jawed"}"""),
            });
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

/// <summary>
/// A MainViewModel over temp folders. With <c>tools: true</c> the tools folder gets the fake yt-dlp (as "yt-dlp") and
/// placeholder ffmpeg/ffprobe/deno files, so Download runs end to end without the internet.
/// </summary>
internal sealed class TestRig : IDisposable
{
    public string Dir { get; } = Directory.CreateTempSubdirectory("videoprep-ui-").FullName;
    public AppPaths Paths { get; }
    public FakeUi Ui { get; } = new();
    public Log Log { get; } = new();
    public AppServices Services { get; }
    public string OutFolder => Path.Combine(Dir, "out");

    public TestRig(bool tools = false)
    {
        Paths = new AppPaths(Path.Combine(Dir, "roaming"), Path.Combine(Dir, "local"));
        if (tools) InstallFakeTools(Paths.ToolsDir);
        var runner = new ProcessRunner();
        var http = new HttpClient(new FakeWeb());
        Services = new AppServices
        {
            Paths = Paths, Log = Log, Runner = runner, Http = http,
            Tools = new ToolManager(Paths.ToolsDir, http, exeDir: Path.Combine(Dir, "noexe"), pathVariable: ""),
            Preview = new PreviewService(runner, http, Log),
            InstalledBrowsers = () => [],
        };
    }

    public MainViewModel Create() => new(Services, Ui, a => a());

    private static string Exe(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;

    private static void InstallFakeTools(string dir)
    {
        Directory.CreateDirectory(dir);
        var bin = AppContext.BaseDirectory;
        foreach (var f in Directory.GetFiles(bin, "fake-yt-dlp*")) File.Copy(f, Path.Combine(dir, Path.GetFileName(f)));
        var exe = Path.Combine(dir, Exe("yt-dlp"));
        File.Copy(Path.Combine(bin, Exe("fake-yt-dlp")), exe);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(exe, File.GetUnixFileMode(exe) | UnixFileMode.UserExecute);
        // Placeholders: found by the tools check, but they cannot run (ffprobe "fails", so nothing is converted).
        foreach (var t in new[] { "ffmpeg", "ffprobe", "deno" }) File.WriteAllText(Path.Combine(dir, Exe(t)), "");
    }

    public void Dispose()
    {
        try { Directory.Delete(Dir, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
