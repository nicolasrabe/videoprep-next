using System.Collections.Concurrent;
using System.Text.Json;
using Videoprep.Core.Logging;

namespace Videoprep.Core.Preview;

/// <summary>
/// RONDA 4 (spec C): the best picture of a video, found without yt-dlp when possible and kept (memory + disk) so the next
/// time it shows at once. YouTube: maxresdefault, then sddefault, then hqdefault when the better one answers 404.
/// Other sites: the largest picture the metadata lists. The disk cache is capped (200 MB by default): the oldest files go first.
/// </summary>
public sealed class ThumbnailService(HttpClient http, string cacheDir, Log? log = null)
{
    public const long DefaultMaxCacheBytes = 200L * 1024 * 1024;
    private const int MaxPictureBytes = 8 * 1024 * 1024;
    private readonly ConcurrentDictionary<string, byte[]> _memory = new();
    private readonly object _purgeGate = new();
    private int _downloads;

    /// <summary>Disk cache size limit; tests make it small.</summary>
    public long MaxCacheBytes { get; init; } = DefaultMaxCacheBytes;

    /// <summary>For tests: pictures that were downloaded from the network (a cache hit does not count).</summary>
    public int Downloads => _downloads;

    /// <summary>The addresses to try for a YouTube video, best first.</summary>
    public static IReadOnlyList<string> YoutubeCandidates(string id) =>
        [$"https://i.ytimg.com/vi/{id}/maxresdefault.jpg", $"https://i.ytimg.com/vi/{id}/sddefault.jpg", $"https://i.ytimg.com/vi/{id}/hqdefault.jpg"];

    /// <summary>The cache name of a link (letters, digits, dash and underscore only).</summary>
    public static string CacheName(string key)
    {
        var name = new string(key.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());
        if (name.Length > 80) name = name[..60] + "_" + Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(key)))[..10];
        return name;
    }

    private string FileOf(string key) => Path.Combine(cacheDir, "thumbs", CacheName(key) + ".img");

    /// <summary>The picture if it is already in memory or on disk; null otherwise. Never touches the network.</summary>
    public byte[]? TryGet(string key)
    {
        if (_memory.TryGetValue(key, out var m)) return m;
        try
        {
            var f = FileOf(key);
            if (!File.Exists(f)) return null;
            var bytes = File.ReadAllBytes(f);
            File.SetLastAccessTimeUtc(f, DateTime.UtcNow);
            _memory[key] = bytes;
            return bytes;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Drops the picture from memory and disk, so the next request downloads it again ("Actualizar datos").</summary>
    public void Forget(string key)
    {
        _memory.TryRemove(key, out _);
        try { File.Delete(FileOf(key)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>The picture of a YouTube video: cached, or the best of the three addresses.</summary>
    public Task<byte[]?> GetYoutubeAsync(string id, CancellationToken ct = default) => GetAsync(id, YoutubeCandidates(id), ct);

    /// <summary>The picture for <paramref name="key"/>: cached, or the first of <paramref name="candidates"/> (best first) that downloads.</summary>
    public async Task<byte[]?> GetAsync(string key, IEnumerable<string> candidates, CancellationToken ct = default)
    {
        if (TryGet(key) is { } cached) return cached;
        foreach (var url in candidates)
        {
            // Only web links: never file:// or other schemes coming from a site's data.
            if (!IsWeb(url)) continue;
            var bytes = await DownloadAsync(new Uri(url), ct).ConfigureAwait(false);
            if (bytes is null) continue;
            Interlocked.Increment(ref _downloads);
            Store(key, bytes);
            return bytes;
        }
        return null;
    }

    private async Task<byte[]?> DownloadAsync(Uri url, CancellationToken ct)
    {
        try
        {
            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode || resp.Content.Headers.ContentLength > MaxPictureBytes) return null;   // 404: try the next, smaller one
            await using var s = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var ms = new MemoryStream();
            var buffer = new byte[81920];
            int n;
            while ((n = await s.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                ms.Write(buffer, 0, n);
                if (ms.Length > MaxPictureBytes) return null;
            }
            return ms.Length > 0 ? ms.ToArray() : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            log?.Write($"Thumbnail: could not read {url.Host}{url.AbsolutePath} ({ex.GetType().Name})");
            return null;
        }
    }

    private void Store(string key, byte[] bytes)
    {
        _memory[key] = bytes;
        try
        {
            var f = FileOf(key);
            Directory.CreateDirectory(Path.GetDirectoryName(f)!);
            var tmp = f + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            File.Move(tmp, f, overwrite: true);
            Purge();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not saved: it still lives in memory.
        }
    }

    /// <summary>Deletes the oldest pictures until the folder is within the limit.</summary>
    public void Purge()
    {
        lock (_purgeGate)
        {
            var dir = new DirectoryInfo(Path.Combine(cacheDir, "thumbs"));
            if (!dir.Exists) return;
            var files = dir.GetFiles("*.img").OrderBy(f => f.LastAccessTimeUtc > f.LastWriteTimeUtc ? f.LastAccessTimeUtc : f.LastWriteTimeUtc).ToList();
            var total = files.Sum(f => f.Length);
            foreach (var f in files)
            {
                if (total <= MaxCacheBytes) break;
                try { var size = f.Length; f.Delete(); total -= size; }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    /// <summary>Total size of the picture cache on disk.</summary>
    public long DiskBytes()
    {
        var dir = new DirectoryInfo(Path.Combine(cacheDir, "thumbs"));
        return dir.Exists ? dir.GetFiles("*.img").Sum(f => f.Length) : 0;
    }

    /// <summary>
    /// The largest picture listed in a yt-dlp info json ("thumbnails": the biggest by width x height; without sizes, the last, which
    /// yt-dlp sorts best-last), or the plain "thumbnail". Only web addresses. Null when there is none.
    /// </summary>
    public static string? BestFromMetadata(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string? best = null;
            long bestArea = -1;
            if (root.TryGetProperty("thumbnails", out var list) && list.ValueKind == JsonValueKind.Array)
                foreach (var t in list.EnumerateArray())
                {
                    if (t.ValueKind != JsonValueKind.Object || !t.TryGetProperty("url", out var u) || u.ValueKind != JsonValueKind.String) continue;
                    var url = u.GetString();
                    if (!IsWeb(url)) continue;
                    long w = t.TryGetProperty("width", out var wv) && wv.ValueKind == JsonValueKind.Number ? wv.GetInt64() : 0;
                    long h = t.TryGetProperty("height", out var hv) && hv.ValueKind == JsonValueKind.Number ? hv.GetInt64() : 0;
                    var area = w * h;
                    if (area >= bestArea) (best, bestArea) = (url, area);   // ">=": on a tie (or no sizes) the later one wins
                }
            if (best is not null) return best;
            if (root.TryGetProperty("thumbnail", out var one) && one.ValueKind == JsonValueKind.String && IsWeb(one.GetString())) return one.GetString();
        }
        catch (JsonException) { }
        return null;
    }

    private static bool IsWeb(string? url) =>
        url is not null && Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp);
}
