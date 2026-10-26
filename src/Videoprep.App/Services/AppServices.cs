using Videoprep.Core;
using Videoprep.Core.Engine;
using Videoprep.Core.Logging;
using Videoprep.Core.Preview;
using Videoprep.Core.Storage;
using Videoprep.Core.Tools;

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
    /// <summary>Browsers with a profile folder on this PC (only the folder is checked; cookies are not read).</summary>
    public Func<IReadOnlyList<string>> InstalledBrowsers { get; init; } = () => CookieStrategy.InstalledBrowsers();

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
        };
    }
}
