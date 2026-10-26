using System.Globalization;
using Videoprep.Core.Engine;
using Videoprep.Core.Logging;
using Videoprep.Core.Models;
using Videoprep.Core.Storage;
using Videoprep.Core.Text;

namespace Videoprep.Core.Scraping;

/// <summary>Fixed inputs of one subscription check.</summary>
public sealed record CheckOptions
{
    public required ToolPaths Tools { get; init; }
    /// <summary>Cookies for the listings (none unless the user chose one browser or a file).</summary>
    public CookieSource ListingCookies { get; init; } = CookieSource.NoCookies;
    /// <summary>Cookie sources for the downloads, in order (same as the queue).</summary>
    public IReadOnlyList<CookieSource> DownloadCookies { get; init; } = [CookieSource.NoCookies];
    public DownloadOptions Options { get; init; } = new();
    public string? SessionCookieFile { get; init; }
}

/// <summary>What happened with one subscription.</summary>
public sealed record SubCheckResult(string Id, string Name, int New, int Ok, int Failed, int MarkedSeen, int Deferred,
                                    string? Error, bool Blocked);

public sealed record CheckResult(IReadOnlyList<SubCheckResult> Subs, bool Blocked, bool Cancelled)
{
    public const int ExitOk = 0, ExitSomeFailed = 1, ExitBlocked = 2;

    public int NewTotal => Subs.Sum(s => s.Ok);

    /// <summary>SCRAPING_SPEC 2.5: 0 ok, 1 some failed, 2 blocked / rate-limited (3 = busy is decided by the caller).</summary>
    public int ExitCode => Blocked ? ExitBlocked : Subs.Any(s => s.Error is not null || s.Failed > 0) ? ExitSomeFailed : ExitOk;
}

/// <summary>
/// "Revisar ahora" / on start / every N hours / <c>videoprep --check</c> (SCRAPING_SPEC 2): for each enabled subscription,
/// one at a time, list the newest videos, apply its filters, drop what its history already has, and download the rest
/// with the normal engine. Gentle: pauses between subscriptions and between downloads, stops everything at a bot check
/// or HTTP 429 and never retries in a loop. Each subscription's result is saved as soon as it is done.
/// </summary>
public sealed class CheckRunner(IProcessRunner runner, Log log, AppPaths paths)
{
    /// <summary>Newest entries listed per tab on a check (the channel lists newest first; older ones were seen before).</summary>
    public int ListCap { get; init; } = 100;

    /// <summary>Pause between subscriptions; tests replace it.</summary>
    public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;

    public Func<DateTimeOffset> Now { get; init; } = () => DateTimeOffset.Now;

    public Random Random { get; init; } = Random.Shared;

    /// <summary>Engine events of the downloads (the window shows progress with them).</summary>
    public event Action<EngineEvent>? EngineEvent;

    /// <summary>A subscription starts: (name, number, total).</summary>
    public event Action<string, int, int>? SubscriptionStarted;

    /// <param name="onlyIds">Check only these (the "Revisar ahora" of one row); null = every enabled one.</param>
    public async Task<CheckResult> RunAsync(IReadOnlyCollection<string>? onlyIds, CheckOptions o, CancellationToken ct)
    {
        var file = SubscriptionStore.Load(paths.SubscriptionsFile, out var problem);
        if (problem is not null) log.Write("WARN  " + problem);
        var subs = file.Subscriptions.Where(s => onlyIds is null ? s.Enabled : onlyIds.Contains(s.Id)).ToList();
        log.Write("");
        log.Write($"=== Subscriptions: checking {subs.Count} ===");
        var results = new List<SubCheckResult>();
        var budget = Math.Max(1, file.Settings.MaxNewPerCheck);
        var blocked = false;
        for (var i = 0; i < subs.Count; i++)
        {
            if (ct.IsCancellationRequested) break;
            if (i > 0) await PauseAsync(file.Settings, ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested) break;
            SubscriptionStarted?.Invoke(subs[i].Name, i + 1, subs.Count);
            SubCheckResult r;
            try
            {
                r = await CheckOneAsync(subs[i], o, budget, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                r = new SubCheckResult(subs[i].Id, subs[i].Name, 0, 0, 0, 0, 0, ex.Message, false);
            }
            budget -= r.New;
            results.Add(r);
            Save(r);
            log.Write($"Subscription {subs[i].Name}: {Summary(r)}");
            if (r.Blocked)
            {
                blocked = true;
                log.Write("Stopped: the site asked for verification (bot check) or for fewer requests. Try again later.");
                break;
            }
            if (budget <= 0 && i + 1 < subs.Count)
            {
                log.Write($"Reached {file.Settings.MaxNewPerCheck} new videos for this check; the other subscriptions wait for the next one.");
                break;
            }
        }
        var result = new CheckResult(results, blocked, ct.IsCancellationRequested);
        log.Write($"=== Subscriptions: {result.NewTotal} new video(s) downloaded ===");
        return result;
    }

    private static string Summary(SubCheckResult r) =>
        r.Error is not null ? "ERROR " + r.Error
        : r.MarkedSeen > 0 ? $"first check, {r.MarkedSeen} existing video(s) marked as seen (not downloaded)"
        : $"{r.New} new, {r.Ok} downloaded, {r.Failed} failed" + (r.Deferred > 0 ? $", {r.Deferred} wait for the next check" : "");

    private async Task PauseAsync(SubscriptionSettings s, CancellationToken ct)
    {
        var lo = s.PauseBetweenSubsSec.Count > 0 ? Math.Max(0, s.PauseBetweenSubsSec[0]) : 5;
        var hi = s.PauseBetweenSubsSec.Count > 1 ? Math.Max(lo, s.PauseBetweenSubsSec[1]) : lo;
        try { await Delay(TimeSpan.FromSeconds(Random.Next(lo, hi + 1)), ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }

    private async Task<SubCheckResult> CheckOneAsync(Subscription sub, CheckOptions o, int budget, CancellationToken ct)
    {
        SubCheckResult Fail(string error, bool isBlocked = false) => new(sub.Id, sub.Name, 0, 0, 0, 0, 0, error, isBlocked);

        var targets = SourceUrl.Targets(sub.Url, sub.SourceTabs());
        if (targets.Count == 0) return Fail("the link is not valid");
        var lister = new SourceLister(runner, log) { MaxPerTab = ListCap };
        var entries = new List<ListingEntry>();
        var listed = await lister.ListAsync(targets, o.Tools, o.ListingCookies, e => { lock (entries) entries.Add(e); }, ct).ConfigureAwait(false);
        if (listed.Any(r => r.Outcome == ListOutcome.Blocked)) return Fail("the site asked for verification", isBlocked: true);
        if (listed.Any(r => r.Outcome == ListOutcome.Cancelled)) return new SubCheckResult(sub.Id, sub.Name, 0, 0, 0, 0, 0, null, false);
        if (listed.All(r => r.Outcome is ListOutcome.Failed or ListOutcome.NoSuchTab) && listed.Any(r => r.Outcome == ListOutcome.Failed))
            return Fail(listed.First(r => r.Outcome == ListOutcome.Failed).Error);
        var videos = entries.Where(e => !e.IsPlaylist).ToList();

        var archivePath = SubscriptionStore.ArchiveFor(paths, sub);
        var archive = ArchiveStore.Load(archivePath);
        if (!File.Exists(archivePath) && sub.FirstRun == Subscription.FirstRunMarkSeen)
        {
            // "Only new from now": everything listed today counts as seen; nothing is downloaded.
            ArchiveStore.Append(archivePath, videos.Select(v => v.ArchiveKey));
            return new SubCheckResult(sub.Id, sub.Name, 0, 0, 0, videos.Count, 0, null, false);
        }

        var filters = sub.Filters.ToFilterSet();
        var passed = Filters.Apply(videos, filters, archive.Contains);
        var need = Filters.NeedsExact(passed, filters);
        var exactBlocked = false;
        if (need.Count > 0)
        {
            var (infos, b) = await lister.ExactAsync(need, o.Tools, o.ListingCookies, null, ct).ConfigureAwait(false);
            exactBlocked = b;
            var byId = infos.ToDictionary(x => x.Id);
            videos = videos.Select(v => byId.TryGetValue(v.Id, out var x) ? x.ApplyTo(v) : v).ToList();
            passed = Filters.Apply(videos, filters, archive.Contains);
        }
        if (exactBlocked) return Fail("the site asked for verification", isBlocked: true);

        var take = passed.Take(budget).ToList();
        var deferred = passed.Count - take.Count;
        if (take.Count == 0) return new SubCheckResult(sub.Id, sub.Name, 0, 0, 0, 0, 0, null, false);

        var folder = sub.TargetFolder();
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(paths.ArchivesDir);   // yt-dlp appends to the archive but does not create its folder
        var series = sub.Profile.Naming == "series";
        var prefix = SafeName.Make(sub.Profile.Prefix.Trim().Length > 0 ? sub.Profile.Prefix.Trim() : sub.Name, "Video");
        var items = take.Select((v, n) => new QueueItem
        {
            Id = n + 1, Url = v.Url, Format = sub.Profile.Format, Cap = sub.Profile.Cap,
            Naming = series ? NamingMode.Series : NamingMode.Original, Prefix = prefix,
        }).ToList();
        var next = new Dictionary<string, int>();
        if (series)
            next[prefix] = FileNames.HighestSeriesNumber(prefix, Directory.EnumerateFiles(folder).Select(Path.GetFileName).OfType<string>()) + 1;
        var jobs = JobPlanner.Build(items, next);

        var engine = new JobEngine(runner, log);
        engine.Event += e => EngineEvent?.Invoke(e);
        var ctx = new EngineContext
        {
            Tools = o.Tools,
            // SCRAPING_SPEC 2.4: a random 3-8 s pause before each download.
            Options = o.Options with { SleepInterval = 3, MaxSleepInterval = 8, SleepRequests = Math.Max(1, o.Options.SleepRequests) },
            Folder = folder,
            Cookies = o.DownloadCookies,
            ArchiveFile = archivePath,
            ForceArchive = true,
            SessionCookieFile = o.SessionCookieFile,
            StopWhenBlocked = true,
        };
        var r = await engine.RunAsync(jobs, ctx, ct).ConfigureAwait(false);
        return new SubCheckResult(sub.Id, sub.Name, take.Count, r.Ok + r.Warn, r.Fail, 0, deferred,
                                  r.Blocked ? "the site asked for verification" : null, r.Blocked);
    }

    /// <summary>Writes lastCheck/lastNew/lastError of one subscription, re-reading the file so edits made meanwhile are kept.</summary>
    private void Save(SubCheckResult r)
    {
        try
        {
            var file = SubscriptionStore.Load(paths.SubscriptionsFile, out _);
            var now = Now().ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
            var subs = file.Subscriptions.Select(s => s.Id != r.Id ? s : s with
            {
                LastCheck = now, LastNew = r.Error is null ? r.Ok : s.LastNew, LastError = r.Error,
            }).ToList();
            SubscriptionStore.Save(paths.SubscriptionsFile, file with { Subscriptions = subs });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Write("WARN  could not save the subscription results: " + ex.Message);
        }
    }
}
