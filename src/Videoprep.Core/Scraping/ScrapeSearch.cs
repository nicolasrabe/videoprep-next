using Videoprep.Core.Engine;
using Videoprep.Core.Logging;

namespace Videoprep.Core.Scraping;

public enum SearchStatus { Done, Cancelled, Blocked, Failed }

/// <summary>Answer to "the channel has about N videos".</summary>
public enum ManyKnownChoice { SearchAll, LimitTo50, Cancel }

/// <summary>Answer to "already 50 videos and more keep appearing".</summary>
public enum Reached50Choice { Continue, StopHere, Cancel }

/// <summary>The questions a search may ask the person (RONDA 1 spec 1.3 and 1.4). The UI implements them with dialogs.</summary>
public interface ISearchPrompts
{
    /// <summary>Before searching, when the total is known beforehand (N videos, about <paramref name="requests"/> listing requests).</summary>
    Task<ManyKnownChoice> AskManyKnown(int total, int requests);

    /// <summary>The search found <paramref name="found"/> videos and more keep appearing. Asked once per search.</summary>
    Task<Reached50Choice> AskReached50(int found);

    /// <summary>The cap of date confirmations was reached; <paramref name="missing"/> dates are still unconfirmed.</summary>
    Task<bool> AskConfirmMore(int missing);
}

/// <summary>Where a search is: what the "Resultados" header and bar show.</summary>
public sealed record SearchProgress(string Phase, int Reviewed, int Passing, int DatesConfirmed, int DatesTotal, int Requests);

public sealed record SearchRequest(string Source, IReadOnlyList<SourceTab> Tabs, FilterSet Filters, Func<ListingEntry, bool> IsDownloaded, DateOnly Today,
                                   ToolPaths Tools, CookieSource Cookies, int? KnownTotal = null);

public sealed record SearchResult(SearchStatus Status, Filters.Evaluation Eval, IReadOnlyList<TabResult> Tabs, FilterSet Filters, int Requests,
                                  int DatesConfirmed, int DatesTotal, string Error = "")
{
    /// <summary>The search stopped before reading everything (the person chose to stop at 50, or the limit was reached).</summary>
    public bool Truncated { get; init; }

    /// <summary>It answered from what was already read, with no request to the site.</summary>
    public bool FromCache { get; init; }
}

/// <summary>"Pedidos estimados: Min a Max" (RONDA 1 spec 1.4 point 7).</summary>
public sealed record RequestEstimate(int Min, int Max, bool FromCache);

/// <summary>
/// The Scraping search (RONDA 1): ONE listing per tab read from the newest video back, cut early when it passes the "from" date;
/// exact dates only for the doubtful ones and at most <see cref="ExactRequestCap"/> per search; a session cache so changing the
/// filters never repeats a request; questions at 50 videos. Requests are made one at a time (the lister sleeps between them).
/// </summary>
public sealed class ScrapeSearch(IProcessRunner runner, Log log, Func<DateTime>? clock = null)
{
    /// <summary>Candidate videos found before the search asks whether to go on.</summary>
    public int AskAt { get; init; } = 50;

    /// <summary>Exact-date requests per search before asking again (one request = one video).</summary>
    public int ExactRequestCap { get; init; } = 150;

    /// <summary>How long what was read can be reused.</summary>
    public TimeSpan CacheTtl { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Entries per listing request (YouTube pages), only to estimate "unos X pedidos".</summary>
    public int PageSize { get; init; } = 30;

    /// <summary>Older entries in a row (by approximate date) before the listing is cut.</summary>
    public int OlderInARow { get; init; } = 2;

    private readonly Func<DateTime> _clock = clock ?? (() => DateTime.UtcNow);
    private readonly Dictionary<string, Tab> _tabs = [];
    private readonly Dictionary<string, ExactInfo> _exact = [];
    private readonly HashSet<string> _attempted = [];

    private sealed class Tab
    {
        public List<ListingEntry> Entries { get; } = [];
        public bool Complete { get; set; }
        /// <summary>Entries not read are not newer than this date (last read date plus its error margin); null = unknown.</summary>
        public DateOnly? CoveredAfter { get; set; }
        public DateTime At { get; set; }
        public TabResult? Result { get; set; }
    }

    /// <summary>Forgets everything read (the person pressed Reiniciar).</summary>
    public void Clear()
    {
        _tabs.Clear();
        _exact.Clear();
        _attempted.Clear();
    }

    private static string TabKey(SourceUrl.Target t) => t.Url;

    private bool Reusable(Tab? tab, FilterSet f)
    {
        if (tab is null || _clock() - tab.At > CacheTtl) return false;
        if (tab.Complete) return true;
        // A listing cut early still answers every filter whose "from" date is newer than what was left unread.
        return f.From is { } from && tab.CoveredAfter is { } covered && from > covered;
    }

    /// <summary>True when the filters can be answered from what was already read (no request at all).</summary>
    public bool CanRefilter(SearchRequest req)
    {
        var targets = SourceUrl.Targets(req.Source, req.Tabs);
        return targets.Count > 0 && targets.All(t => _tabs.TryGetValue(TabKey(t), out var tab) && Reusable(tab, req.Filters));
    }

    /// <summary>The filters applied again to what was read, no request. Dates already confirmed are kept.</summary>
    public SearchResult Refilter(SearchRequest req)
    {
        var all = AllEntries(SourceUrl.Targets(req.Source, req.Tabs));
        var eval = Filters.Evaluate(all, req.Filters, req.IsDownloaded, req.Today);
        var (a, b) = DateCounts(eval, req.Filters);
        return new SearchResult(SearchStatus.Done, eval, [], req.Filters, 0, a, b) { FromCache = true };
    }

    /// <summary>Everything read for these targets, with the exact data found so far applied.</summary>
    public IReadOnlyList<ListingEntry> AllEntries(IReadOnlyList<SourceUrl.Target> targets)
    {
        var all = new List<ListingEntry>();
        foreach (var t in targets)
            if (_tabs.TryGetValue(TabKey(t), out var tab))
                all.AddRange(tab.Entries.Select(e => _exact.TryGetValue(e.Id, out var x) ? x.ApplyTo(e) : e));
        return all;
    }

    private static (int Confirmed, int Total) DateCounts(Filters.Evaluation eval, FilterSet f) =>
        f.UsesDates ? (eval.Shown.Count(e => !e.IsPlaylist), eval.Shown.Count(e => !e.IsPlaylist) + eval.Unconfirmed.Count) : (0, 0);

    /// <summary>"Pedidos estimados": worked out from the filters and from what is already known, never invented.</summary>
    public RequestEstimate Estimate(SearchRequest req)
    {
        var targets = SourceUrl.Targets(req.Source, req.Tabs);
        if (targets.Count == 0) return new RequestEstimate(0, 0, false);
        var f = req.Filters;
        if (CanRefilter(req))
        {
            var all = AllEntries(targets);
            var eval = Filters.Evaluate(all, f, req.IsDownloaded, req.Today);
            var pending = Filters.NeedsExact(eval.Shown.Concat(eval.Unconfirmed), f).Count(e => !_attempted.Contains(e.Id));
            return new RequestEstimate(0, Math.Min(pending, ExactRequestCap), true);
        }
        // One listing per tab: at least one request; at most the pages needed for the limit, the known total, or the 50 where it asks.
        var wanted = f.MaxCount ?? req.KnownTotal ?? AskAt;
        var pages = Math.Max(1, (int)Math.Ceiling(wanted / (double)PageSize));
        var listMin = targets.Count;
        var listMax = targets.Count * pages;
        var exactMax = f.UsesDates || f.UsesDuration ? ExactRequestCap : 0;
        return new RequestEstimate(listMin, listMax + exactMax, false);
    }

    /// <summary>"Unos X pedidos de listado" for a channel of <paramref name="total"/> videos.</summary>
    public int ListingRequestsFor(int total) => Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));

    public async Task<SearchResult> RunAsync(SearchRequest req, ISearchPrompts prompts, Action<SearchProgress>? progress, CancellationToken ct)
    {
        var targets = SourceUrl.Targets(req.Source, req.Tabs);
        var f = req.Filters;
        var requests = 0;
        var error = "";
        var lister = new SourceLister(runner, log) { MaxPerTab = 0 };

        // 1.3: when the total is known beforehand and nothing limits the search, say it before starting.
        var askAgain = f.MaxCount is null;
        if (f.MaxCount is null && !f.UsesDates && req.KnownTotal is { } total && total > AskAt && !targets.All(t => _tabs.TryGetValue(TabKey(t), out var c) && Reusable(c, f)))
        {
            switch (await prompts.AskManyKnown(total, ListingRequestsFor(total)).ConfigureAwait(false))
            {
                case ManyKnownChoice.Cancel:
                    return Finish(SearchStatus.Cancelled, [], false);
                case ManyKnownChoice.LimitTo50:
                    f = f with { MaxCount = AskAt };
                    break;
            }
            askAgain = false;   // already answered
        }
        req = req with { Filters = f };

        var tabResults = new List<TabResult>();
        var status = SearchStatus.Done;
        var truncated = false;
        var candidates = 0;   // videos that pass everything but may still wait for a date: what "50 videos" counts
        var include = f.Include.Select(Filters.Normalize).Where(s => s.Length > 0).ToList();
        var exclude = f.Exclude.Select(Filters.Normalize).Where(s => s.Length > 0).ToList();
        var order = 0;

        foreach (var target in targets)
        {
            if (ct.IsCancellationRequested) { status = SearchStatus.Cancelled; break; }
            if (_tabs.TryGetValue(TabKey(target), out var cached) && Reusable(cached, f))
            {
                log.Write($"Scraping: {target.Url} answered from what was already read ({cached.Entries.Count} entries)");
                candidates += cached.Entries.Count(e => Filters.Classify(e, f, req.Today, include, exclude) != Filters.Verdict.Drop);
                order += cached.Entries.Count;
                if (cached.Result is { } cr) tabResults.Add(cr);
                continue;
            }

            var tab = new Tab { At = _clock() };
            _tabs[TabKey(target)] = tab;
            var start = 1;
            var olderRun = 0;
            var reason = "";
            while (true)
            {
                reason = "";
                var r = await lister.ListTargetAsync(target, req.Tools, req.Cookies, start, order, e =>
                {
                    tab.Entries.Add(e);
                    var v = Filters.Classify(e, f, req.Today, include, exclude);
                    if (v != Filters.Verdict.Drop) candidates++;
                    if (e.Date is { } d && f.From is { } from && d.AddDays(Filters.ApproxMarginDays(d, req.Today) * (e.DateIsApprox ? 1 : 0)) < from) olderRun++;
                    else olderRun = 0;
                    progress?.Invoke(new SearchProgress("listing", tab.Entries.Count, candidates, 0, 0, requests + 1));
                    if (f.From is not null && olderRun >= OlderInARow) { reason = "older"; return true; }
                    if (!f.UsesDates && !f.OldestFirst && f.MaxCount is { } n && candidates >= n) { reason = "limit"; return true; }
                    if (askAgain && candidates >= AskAt) { reason = "ask"; return true; }
                    return false;
                }, _ => { }, ct).ConfigureAwait(false);
                requests++;
                tab.Result = r;
                if (r.Outcome is ListOutcome.Blocked) { status = SearchStatus.Blocked; error = r.Error; }
                else if (r.Outcome is ListOutcome.Cancelled) status = SearchStatus.Cancelled;
                else if (r.Outcome is ListOutcome.Failed) { status = tabResults.Count == 0 && targets.Count == 1 ? SearchStatus.Failed : status; error = r.Error; }

                if (r.Outcome == ListOutcome.Ok && r.Stopped && reason == "ask")
                {
                    switch (await prompts.AskReached50(candidates).ConfigureAwait(false))
                    {
                        case Reached50Choice.Continue:
                            askAgain = false;
                            start = r.LastIndex + 1;
                            continue;
                        case Reached50Choice.StopHere:
                            askAgain = false;
                            truncated = true;
                            break;
                        default:
                            status = SearchStatus.Cancelled;
                            break;
                    }
                }
                if (r.Outcome == ListOutcome.Ok)
                {
                    tab.Complete = !r.Stopped;
                    if (tab.Entries.LastOrDefault() is { Date: { } last } le)
                        tab.CoveredAfter = last.AddDays(le.DateIsApprox ? Filters.ApproxMarginDays(last, req.Today) : 0);
                    if (r.Stopped && reason == "limit") truncated = true;
                }
                break;
            }
            tabResults.Add(tab.Result!);
            order = tab.Entries.Count + order;
            if (status is SearchStatus.Blocked or SearchStatus.Cancelled) break;
        }

        // Exact dates: only for the doubtful ones, at most ExactRequestCap per search, asking before going past it.
        if (status == SearchStatus.Done && (f.UsesDates || f.UsesDuration))
        {
            var rounds = 0;
            while (!ct.IsCancellationRequested)
            {
                var all = AllEntries(targets);
                var eval = Filters.Evaluate(all, f, req.IsDownloaded, req.Today);
                var need = Filters.NeedsExact(eval.Shown.Concat(eval.Unconfirmed), f).Where(e => !_attempted.Contains(e.Id)).OrderBy(e => e.Order).ToList();
                if (need.Count == 0) break;
                if (rounds > 0 && !await prompts.AskConfirmMore(need.Count).ConfigureAwait(false)) { truncated = true; break; }
                var take = need.Take(ExactRequestCap).ToList();
                var datesTotal = eval.Shown.Count + eval.Unconfirmed.Count;
                var (infos, blocked) = await lister.ExactAsync(take, req.Tools, req.Cookies,
                    (done, tot) => progress?.Invoke(new SearchProgress("confirming", all.Count, eval.Shown.Count, Math.Max(0, datesTotal - need.Count) + done, datesTotal, requests + done)),
                    ct, take.Count).ConfigureAwait(false);
                requests += take.Count;
                foreach (var e in take) _attempted.Add(e.Id);
                foreach (var x in infos) _exact[x.Id] = x;
                rounds++;
                if (blocked) { status = SearchStatus.Blocked; break; }
            }
            if (ct.IsCancellationRequested && status == SearchStatus.Done) status = SearchStatus.Cancelled;
        }
        return Finish(status, tabResults, truncated);

        SearchResult Finish(SearchStatus st, IReadOnlyList<TabResult> results, bool wasTruncated)
        {
            var all = AllEntries(targets);
            var eval = Filters.Evaluate(all, f, req.IsDownloaded, req.Today);
            var (a, b) = DateCounts(eval, f);
            log.Write($"Scraping: search {st}: {all.Count} reviewed, {eval.Shown.Count} shown, {eval.Unconfirmed.Count} without a confirmed date, {requests} request(s)");
            return new SearchResult(st, eval, results, f, requests, a, b, error) { Truncated = wasTruncated };
        }
    }
}
