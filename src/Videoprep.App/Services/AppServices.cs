using Videoprep.Core;
using Videoprep.Core.Engine;
using Videoprep.Core.Logging;
using Videoprep.Core.Preview;
using Videoprep.Core.Storage;
using Videoprep.Core.Tools;
using Videoprep.Core.Update;

namespace Videoprep.App.Services;

/// <summary>The engine-side objects the window uses. Tests build one with temp folders and the fake yt-dlp.</summary>
public sealed class AppServices
{
    public required AppPaths Paths { get; init; }
    public required Log Log { get; init; }
    public required IProcessRunner Runner { get; init; }
    public required HttpClient Http { get; init; }
    public required ToolManager Tools { get; init; }
    public required PreviewService Preview { get; init; }
    /// <summary>Card data of the queue row the user opens (F3).</summary>
    public required VideoInfoService Info { get; init; }
    /// <summary>Browsers with a profile folder on this PC (only the folder is checked; cookies are not read).</summary>
    public Func<IReadOnlyList<string>> InstalledBrowsers { get; init; } = () => CookieStrategy.InstalledBrowsers();

    /// <summary>F9: the updater's own HTTP door (null = no update checks, e.g. in tests that do not need them).</summary>
    public UpdateClient? Updates { get; init; }
    /// <summary>The version that runs (tests pretend to be older).</summary>
    public SemVer CurrentVersion { get; init; } = SemVer.ParseOrZero(AppInfo.Version);
    /// <summary>Where the running program lives (null or not videoprep.exe = it was started by something else, e.g. during development).</summary>
    public string? CurrentExe { get; init; } = Environment.ProcessPath;
    /// <summary>Only Windows installs the update by itself; elsewhere the button opens the downloads page.</summary>
    public bool CanSelfUpdate { get; init; } = OperatingSystem.IsWindows();
    /// <summary>Starts the new program with its arguments (ArgumentList, never a command string); false when it could not start.</summary>
    public Func<string, IReadOnlyList<string>, bool> StartProcess { get; init; } = StartUpdater;
    /// <summary>Reads the version a downloaded program says it has (tests answer without a real file).</summary>
    public Func<string, string?>? VersionOf { get; init; }

    private static bool StartUpdater(string exe, IReadOnlyList<string> args)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) ?? "" };
            foreach (var a in args) psi.ArgumentList.Add(a);
            return System.Diagnostics.Process.Start(psi) is not null;
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException) { return false; }
    }

    /// <summary>Run the clipboard timer (tests turn it off and ask for each check themselves).</summary>
    public bool ClipboardTimer { get; init; } = true;

    /// <summary>Pause between subscriptions in a check; tests make it instant.</summary>
    public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;
    /// <summary>Runs schtasks.exe; tests never touch the real Task Scheduler.</summary>
    public Func<IReadOnlyList<string>, Task<(int Code, string Output)>> SchTasks { get; init; } = ScheduledTask.RunAsync;

    public static AppServices CreateDefault(Log log)
    {
        var paths = AppPaths.ForCurrentUser();
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"videoprep/{AppInfo.Version}");
        var runner = new ProcessRunner();
        return new AppServices
        {
            Paths = paths,
            Log = log,
            Runner = runner,
            Http = http,
            Tools = new ToolManager(paths.ToolsDir, http),
            Preview = new PreviewService(runner, http, log),
            Info = new VideoInfoService(runner, http, log),
            Updates = new UpdateClient(UpdateClient.CreateHandler(), $"videoprep/{AppInfo.Version}"),
        };
    }
}
