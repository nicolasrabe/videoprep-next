using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Videoprep.Core.Engine;
using Videoprep.Core.Logging;

namespace Videoprep.Core.Scraping;

/// <summary>How one page (tab) of a listing ended.</summary>
public enum ListOutcome { Ok, NoSuchTab, Failed, Blocked, Cancelled }

public sealed record TabResult(SourceUrl.Target Target, ListOutcome Outcome, int Count, string Error = "")
{
    /// <summary>The caller's stop condition ended the listing early (not an error); more entries may exist after <see cref="LastIndex"/>.</summary>
    public bool Stopped { get; init; }

    /// <summary>Position (1-based) of the last entry read in the tab; the next read can start at <c>LastIndex + 1</c>.</summary>
    public int LastIndex { get; init; }
}

/// <summary>What a link is, from one light request that brings no video (Scraping step 1).</summary>
public sealed record SourceProbe(ListOutcome Outcome, string Name, string Handle, string? AvatarUrl, int? TotalCount, string Error = "");

/// <summary>
/// Lists channels and playlists with the quick yt-dlp listing (one JSON line per video, docs/VERIFY.md phase 5) and,
/// when a filter needs it, fetches exact data for a capped number of videos. Gentle with the site: pauses between
/// requests, stops at a bot check or HTTP 429 instead of retrying (SCRAPING_SPEC 2.4).
/// </summary>
public sealed partial class SourceLister(IProcessRunner runner, Log log)
{
    /// <summary>Upper bound per tab so a huge channel does not mean thousands of rows (and requests) by accident. 0 = no bound.</summary>
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

    private static string LastError(string text) =>
        text.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith("ERROR", StringComparison.Ordinal)) ?? "";

    /// <summary>Lists every target; each entry is passed to <paramref name="onEntry"/> as soon as yt-dlp prints it.</summary>
    public async Task<IReadOnlyList<TabResult>> ListAsync(IReadOnlyList<SourceUrl.Target> targets, ToolPaths tools, CookieSource cookies,
                                                          Action<ListingEntry> onEntry, CancellationToken ct)
    {
        var results = new List<TabResult>();
        var order = 0;
        foreach (var target in targets)
        {
            if (ct.IsCancellationRequested) { results.Add(new TabResult(target, ListOutcome.Cancelled, 0)); break; }
            var result = await ListTargetAsync(target, tools, cookies, 1, order, null, onEntry, ct).ConfigureAwait(false);
            order += result.Count;
            results.Add(result);
            if (result.Outcome is ListOutcome.Blocked or ListOutcome.Cancelled) break;   // never hammer a site that pushed back
        }
        return results;
    }

    /// <summary>
    /// Lists one page (tab) from entry <paramref name="startIndex"/> on. <paramref name="stopAfter"/>, called after each entry is
    /// delivered, can end the listing early (the process is killed: no more pages are asked for). Entries get
    /// <c>Order = orderBase + position</c>. <see cref="MaxPerTab"/> &gt; 0 caps how many entries are read; 0 = no cap.
    /// </summary>
    public async Task<TabResult> ListTargetAsync(SourceUrl.Target target, ToolPaths tools, CookieSource cookies, int startIndex, int orderBase,
                                                 Func<ListingEntry, bool>? stopAfter, Action<ListingEntry> onEntry, CancellationToken ct)
    {
        var args = BaseArgs(tools, cookies).Concat(["--flat-playlist", "-j", "--lazy-playlist"]).ToList();
        if (MaxPerTab > 0) args.AddRange(["-I", $"{startIndex}:{startIndex + MaxPerTab - 1}"]);
        else if (startIndex > 1) args.AddRange(["-I", $"{startIndex}:"]);
        args.AddRange(["--extractor-args", "youtubetab:approximate_date", target.Url]);
        log.Write($"Scraping: listing {target.Url}" + (startIndex > 1 ? $" from #{startIndex}" : ""));
        var errors = new StringBuilder();
        var count = 0;
        var stopped = 0;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var r = await runner.RunAsync(new ProcessSpec(tools.YtDlp!, args), line =>
        {
            if (Volatile.Read(ref stopped) == 1) return;
            var e = ListingEntry.Parse(line, target.Tab, orderBase + startIndex + count);
            if (e is not null)
            {
                count++;
                onEntry(e);
                if (stopAfter?.Invoke(e) == true && Interlocked.Exchange(ref stopped, 1) == 0) linked.Cancel();
            }
            else if (line.Length > 0)
            {
                lock (errors) errors.AppendLine(line);
            }
        }, linked.Token).ConfigureAwait(false);

        var text = errors.ToString();
        var lastError = LastError(text);
        var byCaller = Volatile.Read(ref stopped) == 1 && !ct.IsCancellationRequested;
        TabResult result;
        if (byCaller) result = new TabResult(target, ListOutcome.Ok, count);
        else if (r.Cancelled) result = new TabResult(target, ListOutcome.Cancelled, count);
        else if (CookieStrategy.BlockedOutput().IsMatch(text)) result = new TabResult(target, ListOutcome.Blocked, count, lastError);
        else if (r.ExitCode != 0 && NoTab().IsMatch(text)) result = new TabResult(target, ListOutcome.NoSuchTab, 0);
        else if (r.ExitCode != 0 && count == 0) result = new TabResult(target, ListOutcome.Failed, 0, lastError.Length > 0 ? lastError : r.StartError ?? $"exit code {r.ExitCode}");
        else result = new TabResult(target, ListOutcome.Ok, count);
        result = result with { Stopped = byCaller, LastIndex = startIndex - 1 + count };
        log.Write($"Scraping: {target.Url} -> {result.Outcome}{(byCaller ? " (stopped early)" : "")}, {count} entries{(result.Error.Length > 0 ? " - " + result.Error : "")}");
        return result;
    }

    /// <summary>
    /// Scraping step 1: ONE light request that says what the link is (name, user, avatar, total when the site tells it) without
    /// listing any video (<c>-I 0</c>, docs/VERIFY.md RONDA 1).
    /// </summary>
    public async Task<SourceProbe> ProbeAsync(SourceUrl.Target target, ToolPaths tools, CookieSource cookies, CancellationToken ct)
    {
        var args = BaseArgs(tools, cookies).Concat(["--flat-playlist", "-J", "-I", "0", target.Url]).ToList();
        log.Write($"Scraping: identifying {target.Url}");
        var errors = new StringBuilder();
        string? json = null;
        var r = await runner.RunAsync(new ProcessSpec(tools.YtDlp!, args), line =>
        {
            if (line.Length > 0 && line[0] == '{') json = line;
            else if (line.Length > 0) lock (errors) errors.AppendLine(line);
        }, ct).ConfigureAwait(false);
        var text = errors.ToString();
        var lastError = LastError(text);
        if (r.Cancelled) return new SourceProbe(ListOutcome.Cancelled, "", "", null, null);
        if (CookieStrategy.BlockedOutput().IsMatch(text)) return new SourceProbe(ListOutcome.Blocked, "", "", null, null, lastError);
        if (r.ExitCode != 0 && NoTab().IsMatch(text)) return new SourceProbe(ListOutcome.NoSuchTab, "", "", null, null);
        if (json is null)
            return new SourceProbe(ListOutcome.Failed, "", "", null, null, lastError.Length > 0 ? lastError : r.StartError ?? $"exit code {r.ExitCode}");
        try
        {
            using var doc = JsonDocument.Parse(json);
            var e = doc.RootElement;
            var name = new[] { Str(e, "channel"), Str(e, "uploader"), Str(e, "playlist_title"), Str(e, "title") }.FirstOrDefault(x => x.Length > 0) ?? "";
            var handle = Str(e, "uploader_id");
            int? total = e.TryGetProperty("playlist_count", out var pc) && pc.ValueKind == JsonValueKind.Number ? (int)pc.GetDouble() : null;
            string? avatar = null;
            if (e.TryGetProperty("thumbnails", out var th) && th.ValueKind == JsonValueKind.Array)
            {
                // The channel's avatar is the square picture; the banner is wide.
                foreach (var t in th.EnumerateArray())
                {
                    var w = Num(t, "width");
                    if (w > 0 && w == Num(t, "height") && Str(t, "url") is { Length: > 0 } u && u.StartsWith("https://", StringComparison.Ordinal))
                    {
                        avatar = u;
                        break;
                    }
                }
            }
            return new SourceProbe(ListOutcome.Ok, name, handle, avatar, total);
        }
        catch (JsonException)
        {
            return new SourceProbe(ListOutcome.Failed, "", "", null, null, "the answer was not readable");
        }
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static double Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    /// <summary>
    /// Exact date/length/live/members data for up to <see cref="ExactCap"/> (or <paramref name="cap"/>) videos, in batches, reporting
    /// progress (done, total). Stops at the first bot check. Returns what it got.
    /// </summary>
    public async Task<(IReadOnlyList<ExactInfo> Infos, bool Blocked)> ExactAsync(IReadOnlyList<ListingEntry> entries, ToolPaths tools,
        CookieSource cookies, Action<int, int>? progress, CancellationToken ct, int? cap = null)
    {
        var limit = cap ?? ExactCap;
        var wanted = entries.Where(e => !e.IsPlaylist).Take(limit).ToList();
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
        log.Write($"Scraping: exact data for {infos.Count} of {wanted.Count} video(s)" + (entries.Count > wanted.Count ? $" (cap {limit})" : ""));
        return (infos, blocked);
    }
}
