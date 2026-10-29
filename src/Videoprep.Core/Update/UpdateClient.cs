using System.Text;

namespace Videoprep.Core.Update;

/// <summary>
/// The only HTTP door of the updater. It never follows a redirect by itself: every hop is looked at and must be an allowed
/// address (<see cref="UpdateEndpoint.IsAllowed"/>), at most 5 hops; every download has a size limit that is enforced while reading.
/// </summary>
public sealed class UpdateClient
{
    public const int MaxHops = 5;
    public const long MaxListBytes = 2_000_000;
    public const long MaxSumsBytes = 64 * 1024;
    public const long MaxZipBytes = 250L * 1024 * 1024;

    private readonly HttpClient _http;

    /// <param name="handler">Must not follow redirects on its own (use <see cref="CreateHandler"/>); tests pass an in-memory one.</param>
    public UpdateClient(HttpMessageHandler handler, string userAgent)
    {
        _http = new HttpClient(handler, disposeHandler: false) { Timeout = TimeSpan.FromMinutes(15) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
    }

    public static HttpMessageHandler CreateHandler() =>
        new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, ConnectTimeout = TimeSpan.FromSeconds(20) };

    public async Task<string> GetStringAsync(Uri uri, long maxBytes, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await DownloadAsync(uri, ms, maxBytes, null, ct).ConfigureAwait(false);
        return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
    }

    public async Task DownloadAsync(Uri uri, Stream destination, long maxBytes, IProgress<double>? progress, CancellationToken ct)
    {
        using var response = await SendAsync(uri, ct).ConfigureAwait(false);
        var declared = response.Content.Headers.ContentLength;
        if (declared > maxBytes) throw new UpdateException(UpdateFailure.TooBig, $"{uri.Host} announces {declared} bytes, the limit is {maxBytes}");
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var buffer = new byte[81920];
            long total = 0;
            int n;
            while ((n = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                total += n;
                if (total > maxBytes) throw new UpdateException(UpdateFailure.TooBig, $"more than {maxBytes} bytes arrived from {uri.Host}");
                await destination.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                if (declared is > 0) progress?.Report(Math.Min(1.0, (double)total / declared.Value));
            }
        }
        catch (HttpRequestException ex) { throw new UpdateException(UpdateFailure.Network, ex.Message); }
    }

    private async Task<HttpResponseMessage> SendAsync(Uri uri, CancellationToken ct)
    {
        for (var hop = 0; hop <= MaxHops; hop++)
        {
            if (!UpdateEndpoint.IsAllowed(uri)) throw new UpdateException(UpdateFailure.UnsafeAddress, "address not allowed: " + uri.GetLeftPart(UriPartial.Path));
            HttpResponseMessage response;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Accept.ParseAdd("application/vnd.github+json, application/octet-stream, text/plain");
                response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) { throw new UpdateException(UpdateFailure.Network, ex.Message); }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested) { throw new UpdateException(UpdateFailure.Network, "timed out: " + ex.Message); }

            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                var next = response.Headers.Location;
                response.Dispose();
                if (next is null) throw new UpdateException(UpdateFailure.Network, "redirect without a destination");
                uri = next.IsAbsoluteUri ? next : new Uri(uri, next);
                continue;
            }
            if (!response.IsSuccessStatusCode)
            {
                var code = (int)response.StatusCode;
                response.Dispose();
                throw new UpdateException(UpdateFailure.Network, $"{uri.Host} answered {code}");
            }
            return response;
        }
        throw new UpdateException(UpdateFailure.UnsafeAddress, "too many redirects");
    }
}
