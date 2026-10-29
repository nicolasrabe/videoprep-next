using Videoprep.App.Services;
using Videoprep.Core;
using Videoprep.Core.Engine;
using Videoprep.Core.Models;
using Videoprep.Core.Scraping;
using Videoprep.Core.Storage;
using Videoprep.Core.Text;
using Videoprep.Core.Tools;

namespace Videoprep.App.ViewModels;

/// <summary>What the Scraping tab (ExploreViewModel) needs from the rest of the window.</summary>
public sealed partial class MainViewModel
{
    public ScrapingViewModel Scraping { get; private set; } = null!;

    internal IUiServices Ui => _ui;

    internal void Post(Action action) => _post(action);

    /// <summary>Completes once every action posted before it has run (results of a listing arrive through Post).</summary>
    internal Task FlushPosts()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _post(() => done.SetResult());
        return done.Task;
    }

    internal ToolPaths CurrentTools()
    {
        RefreshTools();
        return _tools;
    }

    internal SourceLister NewLister() => new(_svc.Runner, _svc.Log);

    internal CookieSource ListingCookies() => CookieStrategy.ForListing(Settings, _svc.InstalledBrowsers());

    /// <summary>The archive the downloads write (Settings "skip already downloaded"); Explorar reads it for "Ya descargado".</summary>
    internal string ArchivePath => _svc.Paths.ArchiveFile;

    internal AppPaths Paths => _svc.Paths;

    internal void LogLine(string line) => _svc.Log.Write(line);

    /// <summary>The Download tab folder: the starting folder for a new subscription.</summary>
    internal string CurrentFolder => Folder.Trim('"', ' ');

    internal string ExePath => Environment.ProcessPath ?? "videoprep.exe";

    internal Task<(int Code, string Output)> RunScheduledTask(IReadOnlyList<string> args) => _svc.SchTasks(args);

    internal CheckRunner NewCheckRunner() => new(_svc.Runner, _svc.Log, _svc.Paths) { Delay = _svc.Delay };

    internal CheckOptions CheckOptions(ToolPaths tools) => new()
    {
        Tools = tools,
        ListingCookies = ListingCookies(),
        DownloadCookies = CookieChain(),
        Options = OptionsViewModel.ToDownloadOptions(Settings),
        SessionCookieFile = _svc.Paths.SessionCookieFile,
    };

    internal bool IsInQueue(string url)
    {
        var key = UrlParser.LinkKey(url);
        return Items.Any(i => i.VideoKey == key);
    }

    /// <summary>Adds whole videos to the queue with the naming chosen in Download; titles come from the listing.</summary>
    internal void AddToQueue(IEnumerable<(string Url, string Title)> videos, string format, int cap, bool constantRate = false)
    {
        var prefix = SafeName.Make(SeriesText.Trim().Length > 0 ? SeriesText.Trim() : "Video", "Video");
        foreach (var (url, title) in videos)
        {
            if (IsInQueue(url)) continue;
            _svc.Preview.Remember(url, title);
            Items.Add(new QueueItem
            {
                Id = _nextItemId++, Url = url, Format = format, Cap = cap,
                Naming = NameSeries ? NamingMode.Series : NamingMode.Original, Prefix = prefix,
                ConstantRate = constantRate && Core.Formats.FormatCatalog.Get(format).Kind == Core.Formats.MediaKind.Video,
            });
        }
        SaveQueue();
        RebuildQueueRows();
    }
}
