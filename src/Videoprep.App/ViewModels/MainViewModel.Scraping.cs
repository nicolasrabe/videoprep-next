using System.Net.Http;
using Videoprep.App.Services;
using Videoprep.Core;
using Videoprep.Core.Engine;
using Videoprep.Core.Models;
using Videoprep.Core.Preview;
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

    /// <summary>The Scraping search (listing, cache, questions): one per Explorar, so what was read is kept while the program is open.</summary>
    internal ScrapeSearch NewSearch() => new(_svc.Runner, _svc.Log);

    /// <summary>Raised (on the UI thread) when the info service has new data for a video; Explorar refreshes its open row.</summary>
    internal event Action<string>? VideoInfoChanged;

    /// <summary>A row of the results was opened: ask for its description, one request at a time. No cookies unless the user chose them for listings.</summary>
    internal void RequestVideoInfo(string url)
    {
        RefreshTools();
        _svc.Info.Request(url, new InfoRequest(_tools, [ListingCookies()]));
    }

    internal void RetryVideoInfo(string url) => _svc.Info.Retry(url, new InfoRequest(_tools, [ListingCookies()]));

    /// <summary>A small picture from a static address (the channel avatar); null when it cannot be read.</summary>
    internal async Task<byte[]?> FetchBytes(string url)
    {
        try
        {
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return null;
            return await _svc.Http.GetByteArrayAsync(url).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>"Se descargará: formato · nombre" for a video that has not been queued yet (same naming as AddToQueue).</summary>
    internal string WillText(string formatKey, int cap, string title)
    {
        var it = new QueueItem { Id = 0, Url = "", Format = formatKey, Cap = cap };
        var ext = Core.Formats.FormatCatalog.Get(formatKey).Extension;
        var name = NameSeries
            ? SafeName.Make(SeriesText.Trim().Length > 0 ? SeriesText.Trim() : "Video", "Video") + " NN"
            : title.Length > 0 ? SafeName.Make(title) : "(" + L["det_name_title"] + ")";
        return L.Format("det_will", $"{FormatText(it)}  ·  {name}{ext}");
    }

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
