using Videoprep.App.ViewModels;
using Videoprep.Core;
using Videoprep.Core.Engine;
using Videoprep.Core.Logging;
using Videoprep.Core.Scraping;
using Videoprep.Core.Storage;
using Videoprep.Core.Tools;

namespace Videoprep.App.Cli;

/// <summary>
/// <c>videoprep --check</c>: checks every enabled subscription without opening the window (for the Windows scheduled
/// task, SCRAPING_SPEC 2). Writes %LOCALAPPDATA%\videoprep\logs\check-YYYYMMDD.log and also prints to the terminal.
/// Exit code: 0 ok, 1 something failed (or tools missing), 2 the site blocked us, 3 another check or download is running.
/// </summary>
internal static class CheckCommand
{
    public const int ExitBusy = RunLock.ExitCodeBusy;

    public static async Task<int> RunAsync(AppPaths paths, Log log, IProcessRunner runner, ToolManager tools,
                                           Func<IReadOnlyList<string>> installedBrowsers, CancellationToken ct)
    {
        log.Write($"videoprep {AppInfo.Version} --check");
        using var runLock = RunLock.TryAcquire(paths.RunLockFile);
        if (runLock is null)
        {
            log.Write("Skipped: another check or download is running (videoprep is busy). Nothing was done.");
            return ExitBusy;
        }
        var settings = SettingsStore.Load(paths.SettingsFile, out var problem);
        if (problem is not null) log.Write("WARN  " + problem);
        var found = tools.Resolve();
        if (found.Missing().Count > 0)
        {
            log.Write("Cannot check: missing tools (" + string.Join(", ", found.Missing()) + "). Open videoprep once and press Download to install them.");
            return CheckResult.ExitSomeFailed;
        }
        var browsers = installedBrowsers();
        var checker = new CheckRunner(runner, log, paths);
        var result = await checker.RunAsync(null, new CheckOptions
        {
            Tools = found,
            ListingCookies = CookieStrategy.ForListing(settings, browsers),
            DownloadCookies = CookieStrategy.Chain(settings, browsers),
            Options = OptionsViewModel.ToDownloadOptions(settings),
            SessionCookieFile = paths.SessionCookieFile,
        }, ct).ConfigureAwait(false);
        try
        {
            if (File.Exists(paths.SessionCookieFile)) File.Delete(paths.SessionCookieFile);
        }
        catch (IOException) { }
        log.Write($"Exit code {result.ExitCode}");
        return result.ExitCode;
    }
}
