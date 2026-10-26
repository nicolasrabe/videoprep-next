using System.Text;
using System.Text.RegularExpressions;
using Videoprep.Core.Engine;
using Videoprep.Core.Logging;

namespace Videoprep.Core.Scraping;

/// <summary>How one page (tab) of a listing ended.</summary>
public enum ListOutcome { Ok, NoSuchTab, Failed, Blocked, Cancelled }

public sealed record TabResult(SourceUrl.Target Target, ListOutcome Outcome, int Count, string Error = "");

/// <summary>
/// Lists channels and playlists with the quick yt-dlp listing (one JSON line per video, docs/VERIFY.md phase 5) and,
/// when a filter needs it, fetches exact data for a capped number of videos. Gentle with the site: pauses between
/// requests, stops at a bot check or HTTP 429 instead of retrying (SCRAPING_SPEC 2.4).
/// </summary>
public sealed partial class SourceLister(IProcessRunner runner, Log log)
{
    /// <summary>Upper bound per tab so a huge channel does not mean thousands of rows (and requests) by accident.</summary>
    public int MaxPerTab { get; init; } = 1000;

    /// <summary>At most this many videos get the slow exact-data step per search (SCRAPING_SPEC: default 100).</summary>
    public int ExactCap { get; init; } = 100;

    public int ExactBatch { get; init; } = 10;

    [GeneratedRegex(@"does not have an? \w+ tab", RegexOptions.IgnoreCase)]
    private static partial Regex NoTab();

    private static IEnumerable<string> BaseArgs(ToolPaths tools, CookieSource cookies)
    {
        yield return "--ignore-config";
        yield return "--no-warnings";
        yield return "--sleep-requests";
        yield return "1";
        if (tools.Deno is not null)
        {
            yield return "--js-runtimes";
            yield return "deno:" + tools.Deno;
        }
        foreach (var a in cookies.Args()) yield return a;
    }

    /// <summary>Lists every target; each entry is passed to <paramref name="onEntry"/> as soon as yt-dlp prints it.</summary>
    public async Task<IReadOnlyList<TabResult>> ListAsync(IReadOnlyList<SourceUrl.Target> targets, ToolPaths tools, CookieSource cookies,
                                                          Action<ListingEntry> onEntry, CancellationToken ct)
    {
        var results = new List<TabResult>();
        var order = 0;
        foreach (var target in targets)
        {
            if (ct.IsCancellationRequested) { results.Add(new TabResult(target, ListOutcome.Cancelled, 0)); break; }
            var args = BaseArgs(tools, cookies).Concat(
            [
                "--flat-playlist", "-j", "--lazy-playlist", "-I", $"1:{MaxPerTab}",
                "--extractor-args", "youtubetab:approximate_date", target.Url,
            ]).ToList();
            log.Write($"Scraping: listing {target.Url}");
            var errors = new StringBuilder();
            var count = 0;
            var r = await runner.RunAsync(new ProcessSpec(tools.YtDlp!, args), line =>
            {
                var e = ListingEntry.Parse(line, target.Tab, Interlocked.Increment(ref order));
                if (e is not null)
                {
                    Interlocked.Increment(ref count);
                    onEntry(e);
                }
                else if (line.Length > 0)
                {
                    lock (errors) errors.AppendLine(line);
                }
            }, ct).ConfigureAwait(false);

            var text = errors.ToString();
            var lastError = text.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith("ERROR", StringComparison.Ordinal)) ?? "";
            TabResult result;
            if (r.Cancelled) result = new TabResult(target, ListOutcome.Cancelled, count);
            else if (CookieStrategy.BlockedOutput().IsMatch(text)) result = new TabResult(target, ListOutcome.Blocked, count, lastError);
            else if (r.ExitCode != 0 && NoTab().IsMatch(text)) result = new TabResult(target, ListOutcome.NoSuchTab, 0);
            else if (r.ExitCode != 0 && count == 0) result = new TabResult(target, ListOutcome.Failed, 0, lastError.Length > 0 ? lastError : r.StartError ?? $"exit code {r.ExitCode}");
            else result = new TabResult(target, ListOutcome.Ok, count);
            log.Write($"Scraping: {target.Url} -> {result.Outcome}, {count} entries{(result.Error.Length > 0 ? " - " + result.Error : "")}");
            results.Add(result);
            if (result.Outcome is ListOutcome.Blocked or ListOutcome.Cancelled) break;   // never hammer a site that pushed back
        }
        return results;
    }

    /// <summary>
    /// Exact date/length/live/members data for up to <see cref="ExactCap"/> videos, in batches, reporting progress
    /// (done, total). Stops at the first bot check. Returns what it got.
    /// </summary>
    public async Task<(IReadOnlyList<ExactInfo> Infos, bool Blocked)> ExactAsync(IReadOnlyList<ListingEntry> entries, ToolPaths tools,
        CookieSource cookies, Action<int, int>? progress, CancellationToken ct)
    {
        var wanted = entries.Where(e => !e.IsPlaylist).Take(ExactCap).ToList();
        var infos = new List<ExactInfo>();
        var blocked = false;
        for (var i = 0; i < wanted.Count && !ct.IsCancellationRequested; i += ExactBatch)
        {
            var batch = wanted.Skip(i).Take(ExactBatch).ToList();
            var args = BaseArgs(tools, cookies).Concat(["--skip-download", "--no-playlist", "--print", ExactInfo.PrintTemplate])
                                               .Concat(batch.Select(e => e.Url)).ToList();
            var text = new StringBuilder();
            await runner.RunAsync(new ProcessSpec(tools.YtDlp!, args), line =>
            {
                if (ExactInfo.Parse(line) is { } x) lock (infos) infos.Add(x);
                else lock (text) text.AppendLine(line);
            }, ct).ConfigureAwait(false);
            progress?.Invoke(Math.Min(i + batch.Count, wanted.Count), wanted.Count);
            if (CookieStrategy.BlockedOutput().IsMatch(text.ToString()))
            {
                blocked = true;
                log.Write("Scraping: the site asked for verification while confirming dates - stopped.");
                break;
            }
        }
        log.Write($"Scraping: exact data for {infos.Count} of {wanted.Count} video(s)" + (entries.Count > wanted.Count ? $" (cap {ExactCap})" : ""));
        return (infos, blocked);
    }
}
