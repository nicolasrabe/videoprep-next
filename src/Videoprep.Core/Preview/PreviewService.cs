using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Videoprep.Core.Engine;
using Videoprep.Core.Logging;
using Videoprep.Core.Text;

namespace Videoprep.Core.Preview;

/// <summary>Title/author/thumbnail of a link. YouTube: oEmbed (fast). Other sites: from the yt-dlp details run.</summary>
public enum MetaState { Loading, Ready, NotFound }

/// <summary>The yt-dlp "quality check" (legacy DetState). Every state is explicit, so the panel never stays on "Loading" (AUDIT M11).</summary>
public enum DetailState { None, Loading, Done, NoFormat, Failed }

/// <summary>An immutable snapshot of what is known about one link; replaced as a whole on every change.</summary>
public sealed record PreviewInfo
{
    public required string Key { get; init; }
    public required string Url { get; init; }
    public bool IsYoutube => UrlParser.IsYoutubeKey(Key);
    public MetaState Meta { get; init; } = MetaState.Loading;
    public string Title { get; init; } = "";
    public string Author { get; init; } = "";
    public byte[]? Thumbnail { get; init; }
    public DetailState Details { get; init; } = DetailState.None;
    public PreviewDetails? Detail { get; init; }
    /// <summary>Why the details failed: "timeout" or the last yt-dlp error line (English).</summary>
    public string FailReason { get; init; } = "";
    /// <summary>The format group the details were checked for ("h264" or "any"); a change re-runs the check.</summary>
    public string DetailGroup { get; init; } = "";
    /// <summary>The thumbnail was asked for already (titles found in the background come without one).</summary>
    public bool ThumbTried { get; init; }
}

/// <summary>What the details run needs from the Download tab.</summary>
public sealed record DetailRequest(string FormatKey, int Cap, ToolPaths Tools, IReadOnlyList<CookieSource> Cookies);

/// <summary>
/// Port of the legacy preview (Request-Preview, Ensure-Meta, Start-DetailRun, Finish-Detail, Invoke-PreviewTick) as an
/// async service with no UI. AUDIT M11: explicit states, yt-dlp errors go to the log, a failed or slow run is retried
/// once without cookies, and a time limit ends every run.
/// </summary>
public sealed class PreviewService(IProcessRunner runner, HttpClient http, Log log)
{
    private readonly ConcurrentDictionary<string, PreviewInfo> _cache = new();
    private readonly object _gate = new();
    private CancellationTokenSource? _detailRun;

    /// <summary>Raised (from a background thread) with the key of the link that changed.</summary>
    public event Action<string>? Changed;

    /// <summary>Legacy limit: 45 seconds per yt-dlp run.</summary>
    public TimeSpan DetailTimeout { get; init; } = TimeSpan.FromSeconds(45);

    public PreviewInfo? Get(string key) => _cache.GetValueOrDefault(key);

    /// <summary>"h264" for MP4/MOV H.264 and audio (strict selector), "any" for the rest (legacy Get-FmtGroup).</summary>
    public static string GroupOf(string formatKey)
    {
        var f = Formats.FormatCatalog.Get(formatKey);
        return f.IsH264 || f.Kind == Formats.MediaKind.Audio ? "h264" : "any";
    }

    /// <summary>
    /// Starts (or reuses) the preview of a link and, when <paramref name="details"/> is given, the quality check.
    /// Returns the current snapshot right away.
    /// </summary>
    public PreviewInfo Request(string url, DetailRequest? details)
    {
        var key = UrlParser.LinkKey(url);
        var created = false;
        var info = _cache.GetOrAdd(key, k =>
        {
            created = true;
            return new PreviewInfo { Key = k, Url = url };
        });
        if (created && info.IsYoutube)
        {
            _ = FetchYoutubeMetaAsync(key, details);
            return info;
        }
        // A title found in the background (or from Scraping) has no picture yet: ask for it now that it is shown.
        if (info.IsYoutube && info.Thumbnail is null && !info.ThumbTried)
        {
            Update(key, i => i with { ThumbTried = true });
            _ = FetchThumbnailAsync(key, $"https://i.ytimg.com/vi/{key}/mqdefault.jpg");
        }
        if (details is not null) EnsureDetails(key, details);
        return info;
    }

    /// <summary>
    /// A title already known from a listing (Scraping): the queue shows it without asking the site again.
    /// Does nothing when the link is already known.
    /// </summary>
    public void Remember(string url, string title)
    {
        if (title.Length == 0) return;
        var key = UrlParser.LinkKey(url);
        if (_cache.TryAdd(key, new PreviewInfo { Key = key, Url = url, Meta = MetaState.Ready, Title = title })) Changed?.Invoke(key);
    }

    /// <summary>
    /// Title of one YouTube link added from a file, found in the background: oEmbed only (no yt-dlp, no cookies), no
    /// picture. Returns true when it asked the network (the caller pauses between requests); links already known or
    /// not from YouTube return false without asking.
    /// </summary>
    public async Task<bool> LookupTitleAsync(string url, CancellationToken ct)
    {
        var key = UrlParser.LinkKey(UrlParser.ToYoutubeUrl(url, allowBareId: false) ?? url);
        if (!UrlParser.IsYoutubeKey(key) || Get(key) is { Meta: MetaState.Ready }) return false;
        var (title, author) = await OEmbedAsync(key, ct).ConfigureAwait(false);
        if (title.Length == 0) return true;
        var info = new PreviewInfo { Key = key, Url = UrlParser.CanonicalYoutube(key), Meta = MetaState.Ready, Title = title, Author = author };
        _cache.AddOrUpdate(key, info, (_, old) => old.Meta == MetaState.Ready ? old : old with { Meta = MetaState.Ready, Title = title, Author = author });
        Changed?.Invoke(key);
        return true;
    }

    private async Task<(string Title, string Author)> OEmbedAsync(string key, CancellationToken ct)
    {
        try
        {
            var watch = UrlParser.CanonicalYoutube(key);
            var json = await http.GetStringAsync("https://www.youtube.com/oembed?format=json&url=" + Uri.EscapeDataString(watch), ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var title = doc.RootElement.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
            var author = doc.RootElement.TryGetProperty("author_name", out var a) ? a.GetString() ?? "" : "";
            return (title, author);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            log.Write($"Titles: no oEmbed data for {key} ({ex.GetType().Name}: {ex.Message})");
            return ("", "");
        }
    }

    /// <summary>Format or resolution changed: check again if the format group changed (port of Reset-Details).</summary>
    public void Refresh(string key, DetailRequest details)
    {
        if (!_cache.TryGetValue(key, out var info)) return;
        if (info.Details is DetailState.Done or DetailState.NoFormat && info.DetailGroup == GroupOf(details.FormatKey)) return;
        Update(key, i => i with { Details = DetailState.None, FailReason = "" });
        EnsureDetails(key, details);
    }

    /// <summary>Stops the running quality check (a download is starting, or the link changed).</summary>
    public void CancelDetails()
    {
        lock (_gate)
        {
            _detailRun?.Cancel();
            _detailRun = null;
        }
    }

    private void EnsureDetails(string key, DetailRequest req)
    {
        var info = _cache[key];
        // YouTube: only after oEmbed found the video (a missing title means private/removed).
        if (info.IsYoutube && info.Meta != MetaState.Ready) return;
        if (info.Details is DetailState.Loading) return;
        if (info.Details is DetailState.Done or DetailState.NoFormat && info.DetailGroup == GroupOf(req.FormatKey)) return;
        if (req.Tools.YtDlp is null)
        {
            if (!info.IsYoutube) Update(key, i => i with { Meta = MetaState.NotFound, Details = DetailState.Failed, FailReason = "yt-dlp is not installed" });
            return;
        }
        CancellationTokenSource cts;
        lock (_gate)
        {
            _detailRun?.Cancel();
            cts = _detailRun = new CancellationTokenSource();
        }
        Update(key, i => i with { Details = DetailState.Loading, DetailGroup = GroupOf(req.FormatKey), FailReason = "" });
        _ = RunDetailsAsync(key, req, cts.Token);
    }

    private async Task FetchYoutubeMetaAsync(string key, DetailRequest? details)
    {
        string title = "", author = "";
        byte[]? thumb = null;
        try
        {
            var watch = UrlParser.CanonicalYoutube(key);
            var json = await http.GetStringAsync("https://www.youtube.com/oembed?format=json&url=" + Uri.EscapeDataString(watch)).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            title = doc.RootElement.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
            author = doc.RootElement.TryGetProperty("author_name", out var a) ? a.GetString() ?? "" : "";
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            log.Write($"Preview: no oEmbed data for {key} ({ex.GetType().Name}: {ex.Message})");
        }
        try
        {
            thumb = await http.GetByteArrayAsync($"https://i.ytimg.com/vi/{key}/mqdefault.jpg").ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // No thumbnail is fine.
        }
        Update(key, i => i with { Meta = title.Length > 0 ? MetaState.Ready : MetaState.NotFound, Title = title, Author = author, Thumbnail = thumb, ThumbTried = true });
        if (title.Length > 0 && details is not null) EnsureDetails(key, details);
    }

    private async Task RunDetailsAsync(string key, DetailRequest req, CancellationToken ct)
    {
        var info = _cache[key];
        var chain = req.Cookies.Count > 0 ? req.Cookies : [CookieSource.NoCookies];
        var lastError = "";
        var i = 0;
        var current = chain[0];
        while (true)
        {
            if (ct.IsCancellationRequested) return;
            var outcome = await AttemptAsync(current).ConfigureAwait(false);
            if (outcome is null) return;   // done, no format, or cancelled
            lastError = outcome.Value.Error;
            // Same rule as the downloads: a cookie problem or bot check moves on to the next source.
            if (outcome.Value.TryNext && i + 1 < chain.Count)
            {
                current = chain[++i];
                continue;
            }
            // AUDIT M11: one last try without cookies, unless that was the source that just failed.
            if (current.Kind != CookieKind.None)
            {
                current = CookieSource.NoCookies;
                i = chain.Count;
                continue;
            }
            break;
        }
        Update(key, x => x with
        {
            Details = DetailState.Failed, FailReason = lastError,
            Meta = x.IsYoutube ? x.Meta : MetaState.NotFound,
        });

        // Returns null when finished (success, no format or cancelled), else the error and whether to try the next source.
        async Task<(string Error, bool TryNext)?> AttemptAsync(CookieSource src)
        {
            var args = YtDlpArgs.Preview(info.Url, !info.IsYoutube, req.FormatKey, req.Cap, req.Tools, src);
            var output = new StringBuilder();
            string? det = null;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(DetailTimeout);
            var r = await runner.RunAsync(new ProcessSpec(req.Tools.YtDlp!, args), line =>
            {
                lock (output) output.AppendLine(line);
                if (line.StartsWith("DET|", StringComparison.Ordinal)) det = line;
            }, timeout.Token).ConfigureAwait(false);
            if (ct.IsCancellationRequested) return null;   // replaced by a newer request: nothing to report

            var text = output.ToString();
            if (det is not null && PreviewDetails.TryParse(det) is { } d)
            {
                Update(key, x => x with
                {
                    Details = DetailState.Done, Detail = d, FailReason = "",
                    Meta = x.IsYoutube ? x.Meta : MetaState.Ready,
                    Title = x.IsYoutube ? x.Title : d.Title,
                    Author = x.IsYoutube ? x.Author : d.Uploader,
                });
                if (!info.IsYoutube && d.Thumbnail.Length > 0) _ = FetchThumbnailAsync(key, d.Thumbnail);
                return null;
            }
            if (r.Cancelled)
            {
                log.Write($"Preview: yt-dlp took more than {DetailTimeout.TotalSeconds:0} s ({src.Label}) - stopped");
                return ("timeout", false);
            }
            if (info.IsYoutube && text.Contains("Requested format is not available", StringComparison.OrdinalIgnoreCase))
            {
                Update(key, x => x with { Details = DetailState.NoFormat });
                return null;
            }
            var error = text.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith("ERROR", StringComparison.Ordinal))
                        ?? (r.StartError ?? $"yt-dlp exit code {r.ExitCode}");
            log.Write($"Preview: yt-dlp failed ({src.Label}): {error}");
            return (error, CookieStrategy.RetryOutput().IsMatch(text));
        }
    }

    private async Task FetchThumbnailAsync(string key, string url)
    {
        try
        {
            var bytes = await http.GetByteArrayAsync(url).ConfigureAwait(false);
            Update(key, x => x with { Thumbnail = bytes });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException or InvalidOperationException)
        {
            // No thumbnail is fine.
        }
    }

    private void Update(string key, Func<PreviewInfo, PreviewInfo> change)
    {
        _cache.AddOrUpdate(key, _ => throw new InvalidOperationException("unknown preview " + key), (_, old) => change(old));
        Changed?.Invoke(key);
    }
}
