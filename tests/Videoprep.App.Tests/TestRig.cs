using System.Net;
using Videoprep.App.Services;
using Videoprep.App.ViewModels;
using Videoprep.Core.Engine;
using Videoprep.Core.Logging;
using Videoprep.Core.Preview;
using Videoprep.Core.Storage;
using Videoprep.Core.Tools;
using Videoprep.Core.Tests;
using Videoprep.Core.Update;

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
    /// <summary>What the "user" does in Opciones; null = presses Cancelar.</summary>
    public Func<OptionsViewModel, bool>? Options { get; set; }
    public Task<bool> ShowOptions(OptionsViewModel options) => Task.FromResult(Options?.Invoke(options) ?? false);
    /// <summary>What the "user" types in the subscription dialog; null = presses Cancelar.</summary>
    public Action<SubscriptionEditViewModel>? FillSubscription { get; set; }
    public Task<bool> EditSubscription(SubscriptionEditViewModel subscription)
    {
        if (FillSubscription is null) return Task.FromResult(false);
        FillSubscription(subscription);
        return Task.FromResult(subscription.Build(out var problem) is not null || Fail(problem));
    }
    private bool Fail(string? problem) { Messages.Add(problem ?? ""); return false; }
    public Task SetClipboardText(string text) { Clipboard = text; UserCopies(text); return Task.CompletedTask; }
    public void OpenFolder(string path) => OpenedFolders.Add(path);
    public Task<string?> PickLinkFile() => Task.FromResult(FileToPick);
    /// <summary>What the "user" does in "Enlaces encontrados"; null = presses Cancelar.</summary>
    public Func<ImportReviewViewModel, bool>? Review { get; set; }
    public ImportReviewViewModel? LastReview { get; private set; }
    public Task<bool> ReviewImport(ImportReviewViewModel review) { LastReview = review; return Task.FromResult(Review?.Invoke(review) ?? false); }
    /// <summary>What the "user" does in "Cambiar N elementos"; null = presses Cancelar.</summary>
    public Func<BatchEditViewModel, bool>? Batch { get; set; }
    public Task<bool> EditBatch(BatchEditViewModel edit) => Task.FromResult(Batch?.Invoke(edit) ?? false);
    public List<string> ShownFiles { get; } = [];
    public void ShowInFolder(string file) => ShownFiles.Add(file);
    public List<string> OpenedUrls { get; } = [];
    public void OpenUrl(string url) => OpenedUrls.Add(url);
    public void PlayDoneSound() => Sounds++;

    // ---- 0.3 round 2
    public List<string> TextAnswers { get; } = [];
    public Queue<string?> Texts { get; } = new();
    public Task<string?> AskText(string title, string prompt, string initial) { TextAnswers.Add(prompt); return Task.FromResult(Texts.Count > 0 ? Texts.Dequeue() : null); }
    public List<string> Choices { get; } = [];
    public Queue<bool> ChoiceAnswers { get; } = new();
    public Task<bool> AskChoice(string title, string text, string yes, string no)
    {
        Choices.Add(text);
        return Task.FromResult(ChoiceAnswers.Count > 0 && ChoiceAnswers.Dequeue());
    }
    public string? SavePath { get; set; }
    public string? OpenPath { get; set; }
    public Task<string?> PickSaveFileAs(string suggestedName, string label, IReadOnlyList<string> patterns) => Task.FromResult(SavePath);
    public Task<string?> PickOpenFileAs(string label, IReadOnlyList<string> patterns) => Task.FromResult(OpenPath);
    public bool MiniShown { get; private set; }
    public int MiniShows { get; private set; }
    public int AppClosed { get; private set; }
    public bool MainActive { get; set; } = true;
    public bool IsMainActive => MainActive && !MiniShown;
    public void ShowMini() { MiniShown = true; MiniShows++; }
    public void CloseMini() => MiniShown = false;
    public void CloseApp() => AppClosed++;
    // The "user" copies something: the text and the change number go together.
    public long ClipboardSequence { get; private set; } = 1;
    private string? _copied;
    public void UserCopies(string text) { _copied = text; ClipboardSequence++; }
    public int ClipboardReads { get; private set; }
    public Task<string?> ReadClipboardTextAsync() { ClipboardReads++; return Task.FromResult(_copied); }
}

/// <summary>No internet: oEmbed answers for one video, everything else is 404.</summary>
internal sealed class FakeWeb : HttpMessageHandler
{
    /// <summary>Every URL asked for, in order (tests check that nothing is asked for without a reason).</summary>
    public List<string> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var url = request.RequestUri!.ToString();
        lock (Requests) Requests.Add(url);
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
    public List<IReadOnlyList<string>> SchTaskCalls { get; } = [];
    public int SchTaskExit { get; set; }
    public FakeWeb Web { get; } = new();
    /// <summary>Pauses asked for by the app (title lookups, scraping); tests do not wait.</summary>
    public List<TimeSpan> Pauses { get; } = [];

    /// <summary>F9: an in-memory GitHub; the updater only exists when a test asks for it (<c>updates: true</c>).</summary>
    public FakeGitHub Hub { get; } = new();
    /// <summary>Where the "installed" videoprep.exe is (a real file of the right name inside the temp folder).</summary>
    public string InstalledExe => Path.Combine(Dir, "install", "videoprep.exe");
    public List<(string Exe, IReadOnlyList<string> Args)> Started { get; } = [];
    public bool StartWorks { get; set; } = true;
    public string ExeVersion { get; set; } = "0.3.0";

    public TestRig(bool tools = false, bool updates = false, string installedVersion = "0.2.0", bool selfUpdate = true)
    {
        Paths = new AppPaths(Path.Combine(Dir, "roaming"), Path.Combine(Dir, "local"));
        // Start in Spanish whatever the language of the machine (CI runners are English).
        SettingsStore.Save(Paths.SettingsFile, new Settings { Lang = "es" });
        if (tools) InstallFakeTools(Paths.ToolsDir);
        var runner = new ProcessRunner();
        var http = new HttpClient(Web);
        Services = new AppServices
        {
            Paths = Paths, Log = Log, Runner = runner, Http = http,
            Tools = new ToolManager(Paths.ToolsDir, http, exeDir: Path.Combine(Dir, "noexe"), pathVariable: ""),
            Preview = new PreviewService(runner, http, Log),
            Info = new VideoInfoService(runner, http, Log) { Debounce = TimeSpan.Zero },
            InstalledBrowsers = () => [],
            ClipboardTimer = false,
            Delay = (t, _) => { lock (Pauses) Pauses.Add(t); return Task.CompletedTask; },
            SchTasks = args => { SchTaskCalls.Add(args); return Task.FromResult((SchTaskExit, "fake schtasks")); },
            Updates = updates ? Hub.Client() : null,
            CurrentVersion = SemVer.Parse(installedVersion),
            CurrentExe = InstalledExe,
            CanSelfUpdate = selfUpdate,
            Rid = "win-x64",   // the fake release has the Windows zip whatever system runs the tests
            StartProcess = (exe, args) => { Started.Add((exe, args)); return Task.FromResult(StartWorks); },
            VersionOf = _ => ExeVersion,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(InstalledExe)!);
        File.WriteAllBytes(InstalledExe, UpdateKit.FakeExe("installed"));
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
