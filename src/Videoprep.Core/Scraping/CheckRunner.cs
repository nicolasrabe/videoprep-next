using System.Globalization;
using Videoprep.Core.Engine;
using Videoprep.Core.Logging;
using Videoprep.Core.Models;
using Videoprep.Core.Profiles;
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
                                    string? Error, bool Blocked)
{
    /// <summary>Videos that passed the filters but whose date could not be confirmed (RONDA 3): never downloaded, counted apart.</summary>
    public int Unconfirmed { get; init; }
}

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
    /// <summary>Entries listed to mark a new subscription's existing videos as seen ("Desde hoy"): one page is enough, later checks stop at them.</summary>
    public int ListCap { get; init; } = 30;

    /// <summary>
    /// Exact-date requests per subscription and check (RONDA 3, same cap as Scraping). A check runs by itself, so past the cap it does not
    /// ask: what is left waits for the next check.
    /// </summary>
    public int ExactRequestCap { get; init; } = 150;

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
        : $"{r.New} new, {r.Ok} downloaded, {r.Failed} failed" + (r.Deferred > 0 ? $", {r.Deferred} wait for the next check" : "")
          + (r.Unconfirmed > 0 ? $", {r.Unconfirmed} without a confirmed date" : "");

    private async Task PauseAsync(SubscriptionSettings s, CancellationToken ct)
    {
        var lo = s.PauseBetweenSubsSec.Count > 0 ? Math.Max(0, s.PauseBetweenSubsSec[0]) : 5;
        var hi = s.PauseBetweenSubsSec.Count > 1 ? Math.Max(lo, s.PauseBetweenSubsSec[1]) : lo;
        try { await Delay(TimeSpan.FromSeconds(Random.Next(lo, hi + 1)), ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }

    /// <summary>The questions of a search, answered by nobody: a check runs by itself (at start, every N hours, with the program closed).</summary>
    private sealed class NobodyToAsk : ISearchPrompts
    {
        public Task<ManyKnownChoice> AskManyKnown(int total, int requests) => Task.FromResult(ManyKnownChoice.LimitTo50);
        public Task<Reached50Choice> AskReached50(int found) => Task.FromResult(Reached50Choice.StopHere);
        public Task<bool> AskConfirmMore(int missing) => Task.FromResult(false);
    }

    private async Task<SubCheckResult> CheckOneAsync(Subscription sub, CheckOptions o, int budget, CancellationToken ct)
    {
        SubCheckResult Fail(string error, bool isBlocked = false) => new(sub.Id, sub.Name, 0, 0, 0, 0, 0, error, isBlocked);

        var targets = SourceUrl.Targets(sub.Url, sub.SourceTabs());
        if (targets.Count == 0) return Fail("the link is not valid");
        var archivePath = SubscriptionStore.ArchiveFor(paths, sub);
        var firstRun = !File.Exists(archivePath);
        var archive = ArchiveStore.Load(archivePath);
        var today = DateOnly.FromDateTime(Now().DateTime);

        if (firstRun && sub.FirstRun == Subscription.FirstRunMarkSeen)
        {
            // "Desde hoy": what exists now counts as seen; one page of the newest videos is enough, because later checks stop at the first one they know.
            var lister = new SourceLister(runner, log) { MaxPerTab = ListCap };
            var seen = new List<ListingEntry>();
            var listed = await lister.ListAsync(targets, o.Tools, o.ListingCookies, e => { lock (seen) seen.Add(e); }, ct).ConfigureAwait(false);
            if (listed.Any(r => r.Outcome == ListOutcome.Blocked)) return Fail("the site asked for verification", isBlocked: true);
            if (listed.Any(r => r.Outcome == ListOutcome.Cancelled)) return new SubCheckResult(sub.Id, sub.Name, 0, 0, 0, 0, 0, null, false);
            if (listed.All(r => r.Outcome is ListOutcome.Failed or ListOutcome.NoSuchTab) && listed.Any(r => r.Outcome == ListOutcome.Failed))
                return Fail(listed.First(r => r.Outcome == ListOutcome.Failed).Error);
            var videos = seen.Where(e => !e.IsPlaylist).ToList();
            ArchiveStore.Append(archivePath, videos.Select(v => v.ArchiveKey));
            return new SubCheckResult(sub.Id, sub.Name, 0, 0, 0, videos.Count, 0, null, false);
        }

        // The same search as Scraping (RONDA 1): one listing per tab from the newest back, cut early, strict dates, at most ExactRequestCap
        // date requests. Plus: it stops at the first video this subscription's history already has, so a check costs almost nothing.
        var filters = sub.Filters.ToFilterSet();
        if (firstRun && sub.FirstRun == Subscription.FirstRunSinceDate && Filters.TryParseDate(sub.FirstRunSince, out var since) && since is { } sinceDate)
            filters = filters with { From = filters.From is { } f0 && f0 > sinceDate ? f0 : sinceDate };
        var search = new ScrapeSearch(runner, log) { ExactRequestCap = ExactRequestCap, ConfirmOldestFirst = true };
        var req = new SearchRequest(sub.Url, sub.SourceTabs(), filters, archive.Contains, today, o.Tools, o.ListingCookies, null, archive.Contains);
        var res = await search.RunAsync(req, new NobodyToAsk(), null, ct).ConfigureAwait(false);
        if (res.Status == SearchStatus.Blocked) return Fail("the site asked for verification", isBlocked: true);
        if (res.Status == SearchStatus.Cancelled) return new SubCheckResult(sub.Id, sub.Name, 0, 0, 0, 0, 0, null, false);
        if (res.Status == SearchStatus.Failed) return Fail(res.Error.Length > 0 ? res.Error : "the link could not be listed");

        var passed = res.Eval.Shown.Where(e => !e.IsPlaylist).ToList();
        var unconfirmed = res.Eval.Unconfirmed;
        var all = search.AllEntries(targets);
        if (firstRun && sub.FirstRun == Subscription.FirstRunSinceDate)
        {
            // The older videos that were looked at count as seen, so the next check stops at them and never walks back through the history.
            var passedIds = passed.Select(p => p.Id).ToHashSet();
            ArchiveStore.Append(archivePath, all.Where(e => !e.IsPlaylist && !passedIds.Contains(e.Id)).Select(e => e.ArchiveKey));
        }
        else
        {
            // A date that was asked for and still did not come: it will never be a confirmed date, so it is not asked again every check.
            var passedIds2 = passed.Select(p => p.Id).ToHashSet();
            var unconfirmedIds = unconfirmed.Select(u => u.Id).ToHashSet();
            var settled = unconfirmed.Where(e => search.WasAttempted(e.Id))
                // ... and a video whose exact date came back OUTSIDE the range (or that the exact data filtered out): it will not change either.
                .Concat(all.Where(e => !e.IsPlaylist && search.WasAttempted(e.Id) && !passedIds2.Contains(e.Id) && !unconfirmedIds.Contains(e.Id)))
                .Select(e => e.ArchiveKey).Distinct().ToList();
            if (settled.Count > 0) ArchiveStore.Append(archivePath, settled);
        }
        if (unconfirmed.Count > 0) log.Write($"Subscription {sub.Name}: {unconfirmed.Count} video(s) without a confirmed date were not downloaded");

        // More than the budget: the OLDEST ones go first. The next check lists from the newest back and stops at the first known video, so
        // the ones left (the newest) are found again; taking the newest first would hide the rest behind them.
        var take = passed.Count > budget
            ? passed.OrderBy(e => e.Date ?? DateOnly.MaxValue).ThenByDescending(e => e.Order).Take(budget).ToList()
            : passed;
        var deferred = passed.Count - take.Count;
        if (take.Count == 0) return new SubCheckResult(sub.Id, sub.Name, 0, 0, 0, 0, 0, null, false) { Unconfirmed = unconfirmed.Count };

        var folder = sub.TargetFolder();
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(paths.ArchivesDir);   // yt-dlp appends to the archive but does not create its folder
        // F6: a subscription that follows a profile downloads with the profile's current values (read now, so a profile
        // edited a minute ago counts); if the profile is gone it uses the values it kept.
        var profile = ProfileLinks.Effective(sub.Profile, ProfileStore.Load(paths.ProfilesFile, out _));
        var series = profile.Naming == "series";
        var prefix = SafeName.Make(profile.Prefix.Trim().Length > 0 ? profile.Prefix.Trim() : sub.Name, "Video");
        var items = take.Select((v, n) => new QueueItem
        {
            Id = n + 1, Url = v.Url, Format = profile.Format, Cap = profile.Cap, ConstantRate = profile.ConstantRate,
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
                                  r.Blocked ? "the site asked for verification" : null, r.Blocked) { Unconfirmed = unconfirmed.Count };
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
