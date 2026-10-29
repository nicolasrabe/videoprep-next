using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Videoprep.Core.Engine;
using Videoprep.Core.Logging;
using Videoprep.Core.Text;

namespace Videoprep.Core.Preview;

public enum InfoState { Loading, Ready, Failed }

/// <summary>What the expanded queue row shows (F3). Replaced as a whole on every change.</summary>
public sealed record VideoInfo
{
    public required string Key { get; init; }
    public required string Url { get; init; }
    public InfoState State { get; init; } = InfoState.Loading;
    public string Title { get; init; } = "";
    public string Channel { get; init; } = "";
    /// <summary>yyyymmdd as yt-dlp gives it, or "".</summary>
    public string UploadDate { get; init; } = "";
    public long? Views { get; init; }
    public string Duration { get; init; } = "";
    public double? DurationSeconds { get; init; }
    public string Description { get; init; } = "";
    public byte[]? Thumbnail { get; init; }
    /// <summary>Why it failed: "timeout", "yt-dlp is not installed" or the last yt-dlp error line (English).</summary>
    public string FailReason { get; init; } = "";
}

public sealed record InfoRequest(ToolPaths Tools, IReadOnlyList<CookieSource> Cookies);

/// <summary>
/// Card data of the queue row the user opens (F3). Never prefetched: one link at a time, only when asked.
/// Rules: one yt-dlp run at a time; while one runs, only the most recent request waits (moving with the arrow keys
/// through ten rows asks for one, not ten); a short pause before each run; results kept for the session; the same
/// cookie chain as the downloads, then one last try without cookies (AUDIT M11); a time limit on every run.
/// </summary>
public sealed class VideoInfoService(IProcessRunner runner, HttpClient http, Log log)
{
    private readonly ConcurrentDictionary<string, VideoInfo> _cache = new();
    private readonly object _gate = new();
    private readonly CancellationTokenSource _life = new();
    private string? _pendingKey;
    private InfoRequest? _pendingRequest;
    private string? _runningKey;
    private bool _working;
    private int _active;

    /// <summary>Raised (from a background thread) with the key of the link that changed.</summary>
    public event Action<string>? Changed;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(45);
    /// <summary>Wait before starting a run, so quick moves through the rows only ask for where the user stopped.</summary>
    public TimeSpan Debounce { get; init; } = TimeSpan.FromMilliseconds(400);
    public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;
    public const int MaxDescription = 4000;
    private const int MaxThumbnailBytes = 5 * 1024 * 1024;

    /// <summary>For tests: yt-dlp runs started, and the most that ever ran at the same time (must stay 1).</summary>
    public int RunsStarted { get; private set; }
    public int MostAtOnce { get; private set; }

    public VideoInfo? Get(string key) => _cache.GetValueOrDefault(key);

    /// <summary>The card of a link: the cached one, or a "loading" one while it is fetched.</summary>
    public VideoInfo Request(string url, InfoRequest req)
    {
        var key = UrlParser.LinkKey(url);
        var info = _cache.GetOrAdd(key, k => new VideoInfo { Key = k, Url = url });
        if (info.State == InfoState.Loading) Enqueue(key, req);
        return info;
    }

    /// <summary>"Reintentar" after a failure.</summary>
    public void Retry(string url, InfoRequest req)
    {
        var key = UrlParser.LinkKey(url);
        _cache[key] = new VideoInfo { Key = key, Url = url };
        Changed?.Invoke(key);
        Enqueue(key, req);
    }

    /// <summary>The app is closing: stop waiting and stop the running yt-dlp.</summary>
    public void Stop() => _life.Cancel();

    private void Enqueue(string key, InfoRequest req)
    {
        lock (_gate)
        {
            if (_runningKey == key) return;
            _pendingKey = key;
            _pendingRequest = req;
            if (_working) return;
            _working = true;
        }
        _ = Task.Run(WorkAsync);
    }

    private async Task WorkAsync()
    {
        while (true)
        {
            try { await Delay(Debounce, _life.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                lock (_gate) _working = false;
                return;
            }
            string key;
            InfoRequest req;
            lock (_gate)
            {
                if (_pendingKey is null || _life.IsCancellationRequested)
                {
                    _working = false;
                    return;
                }
                key = _pendingKey;
                req = _pendingRequest!;
                _pendingKey = null;
                _runningKey = key;
            }
            try
            {
                await FetchAsync(key, req, _life.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                log.Write($"Card: could not read {key} ({ex.GetType().Name}: {ex.Message})");
                Update(key, i => i with { State = InfoState.Failed, FailReason = ex.Message });
            }
            finally
            {
                lock (_gate) _runningKey = null;
            }
        }
    }

    private async Task FetchAsync(string key, InfoRequest req, CancellationToken ct)
    {
        var info = _cache[key];
        if (req.Tools.YtDlp is null)
        {
            Update(key, i => i with { State = InfoState.Failed, FailReason = "yt-dlp is not installed" });
            return;
        }
        var chain = req.Cookies.Count > 0 ? req.Cookies : [CookieSource.NoCookies];
        var lastError = "";
        var i = 0;
        var current = chain[0];
        while (!ct.IsCancellationRequested)
        {
            var (json, error, tryNext) = await AttemptAsync(info.Url, req.Tools, current, ct).ConfigureAwait(false);
            if (json is not null)
            {
                var parsed = Parse(key, info.Url, json);
                Update(key, _ => parsed);
                await FetchThumbnailAsync(key, parsed, json, ct).ConfigureAwait(false);
                return;
            }
            if (ct.IsCancellationRequested) return;
            lastError = error;
            if (tryNext && i + 1 < chain.Count)
            {
                current = chain[++i];
                continue;
            }
            if (current.Kind != CookieKind.None)
            {
                current = CookieSource.NoCookies;
                i = chain.Count;
                continue;
            }
            break;
        }
        Update(key, x => x with { State = InfoState.Failed, FailReason = lastError });
    }

    private async Task<(string? Json, string Error, bool TryNext)> AttemptAsync(string url, ToolPaths tools, CookieSource src, CancellationToken ct)
    {
        var output = new StringBuilder();
        string? json = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        RunsStarted++;
        var now = Interlocked.Increment(ref _active);
        if (now > MostAtOnce) MostAtOnce = now;
        ProcessResult r;
        try
        {
            r = await runner.RunAsync(new ProcessSpec(tools.YtDlp!, YtDlpArgs.Info(url, tools, src)), line =>
            {
                if (line.StartsWith("INFO|", StringComparison.Ordinal)) json = line[5..];
                else lock (output) output.AppendLine(line);
            }, timeout.Token).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref _active);
        }
        if (json is not null) return (json, "", false);
        if (ct.IsCancellationRequested) return (null, "", false);
        if (r.Cancelled)
        {
            log.Write($"Card: yt-dlp took more than {Timeout.TotalSeconds:0} s ({src.Label}) - stopped");
            return (null, "timeout", false);
        }
        var text = output.ToString();
        var error = text.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith("ERROR", StringComparison.Ordinal))
                    ?? (r.StartError ?? $"yt-dlp exit code {r.ExitCode}");
        log.Write($"Card: yt-dlp failed ({src.Label}): {error}");
        return (null, error, CookieStrategy.RetryOutput().IsMatch(text));
    }

    /// <summary>The INFO json to a card. Unknown or missing fields stay empty; the description is capped.</summary>
    public static VideoInfo Parse(string key, string url, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        string S(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        double? N(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
        var desc = S("description").Replace("\r\n", "\n");
        if (desc.Length > MaxDescription) desc = desc[..MaxDescription].TrimEnd() + "…";
        var channel = S("channel");
        return new VideoInfo
        {
            Key = key, Url = url, State = InfoState.Ready,
            Title = S("title"),
            Channel = channel.Length > 0 ? channel : S("uploader"),
            UploadDate = S("upload_date"),
            Views = N("view_count") is { } v ? (long)v : null,
            Duration = S("duration_string"),
            DurationSeconds = N("duration"),
            Description = desc,
        };
    }

    private async Task FetchThumbnailAsync(string key, VideoInfo info, string json, CancellationToken ct)
    {
        string? url = null;
        if (UrlParser.IsYoutubeKey(key)) url = $"https://i.ytimg.com/vi/{key}/hqdefault.jpg";
        else
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("thumbnail", out var t) && t.ValueKind == JsonValueKind.String) url = t.GetString();
        }
        // Only web links: never file:// or other schemes coming from a site's data.
        if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeHttp)) return;
        try
        {
            using var resp = await http.GetAsync(u, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode || resp.Content.Headers.ContentLength > MaxThumbnailBytes) return;
            await using var s = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var ms = new MemoryStream();
            var buffer = new byte[81920];
            int n;
            while ((n = await s.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                ms.Write(buffer, 0, n);
                if (ms.Length > MaxThumbnailBytes) return;
            }
            var bytes = ms.ToArray();
            Update(key, x => x with { Thumbnail = bytes });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            // No picture is fine.
        }
    }

    private void Update(string key, Func<VideoInfo, VideoInfo> change)
    {
        _cache.AddOrUpdate(key, _ => throw new InvalidOperationException("unknown card " + key), (_, old) => change(old));
        Changed?.Invoke(key);
    }
}
