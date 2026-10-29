using System.Web;
using Videoprep.Core.Text;

namespace Videoprep.Core.Import;

/// <summary>
/// Cleans links found in files, without any network access:
/// unwraps redirect links (Google Docs "google.com/url?q=", Outlook SafeLinks, YouTube and Facebook redirects),
/// turns every YouTube form into the canonical watch link (so youtu.be/ID = watch?v=ID = shorts/ID, and "t=" or
/// "list=" are dropped: always the whole single video), and removes tracking parameters.
/// </summary>
public static class LinkNormalizer
{
    private static readonly HashSet<string> Tracking = new(StringComparer.OrdinalIgnoreCase)
    {
        "si", "feature", "fbclid", "gclid", "dclid", "msclkid", "igshid", "igsh", "mc_cid", "mc_eid", "ref_src", "ref_url",
        "_hsenc", "_hsmi", "yclid", "share_id", "is_from_webapp", "sender_device",
    };

    /// <summary>Hosts whose "s"/"t" parameters are share tokens, not a time (X/Twitter).</summary>
    private static readonly HashSet<string> ShareTokenHosts = new(StringComparer.OrdinalIgnoreCase) { "x.com", "twitter.com" };

    /// <summary>The link that a redirect wrapper points to (up to three levels); the link itself when it is not one.</summary>
    public static string Unwrap(string url)
    {
        for (var i = 0; i < 3; i++)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return url;
            var host = u.Host.ToLowerInvariant();
            var q = HttpUtility.ParseQueryString(u.Query);
            string? inner = null;
            if ((host.StartsWith("google.", StringComparison.Ordinal) || host.StartsWith("www.google.", StringComparison.Ordinal)) && u.AbsolutePath == "/url")
                inner = q["q"] ?? q["url"];
            else if (host.EndsWith(".safelinks.protection.outlook.com", StringComparison.Ordinal))
                inner = q["url"];
            else if (host is "www.youtube.com" or "youtube.com" or "m.youtube.com" && u.AbsolutePath == "/redirect")
                inner = q["q"];
            else if (host is "l.facebook.com" or "lm.facebook.com" && u.AbsolutePath == "/l.php")
                inner = q["u"];
            if (inner is null || !(inner.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || inner.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                return url;
            url = inner;
        }
        return url;
    }

    /// <summary>
    /// The link as it goes to the queue: unwrapped, YouTube canonical, tracking parameters and "#..." removed.
    /// Null when it is not an http/https link (mailto:, file:, ftp:... are never used).
    /// </summary>
    public static string? ForQueue(string url)
    {
        url = Unwrap(url.Trim());
        if (UrlParser.ToYoutubeUrl(url, allowBareId: false) is { } yt) return yt;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps)) return null;
        if (u.UserInfo.Length > 0) return null;   // "https://user:pass@host": never pass credentials along
        var host = u.Host.ToLowerInvariant();
        var bare = BareHost(host);
        var kept = new List<string>();
        foreach (var pair in u.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var name = Uri.UnescapeDataString(pair.Split('=')[0]);
            if (Tracking.Contains(name) || name.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)) continue;
            if (ShareTokenHosts.Contains(bare) && name is "s" or "t") continue;
            kept.Add(pair);
        }
        var port = u.IsDefaultPort ? "" : ":" + u.Port;
        return $"{u.Scheme}://{host}{port}{u.AbsolutePath}{(kept.Count > 0 ? "?" + string.Join('&', kept) : "")}";
    }

    /// <summary>
    /// Key to compare two links: the same video gives the same key (YouTube ID; host without www./m., x.com = twitter.com,
    /// path without the final "/", parameters in any order).
    /// </summary>
    public static string CompareKey(string url)
    {
        var q = ForQueue(url) ?? url.Trim();
        if (UrlParser.VideoId(q) is { } id) return "youtube:" + id;
        if (!Uri.TryCreate(q, UriKind.Absolute, out var u)) return q.ToLowerInvariant();
        var host = BareHost(u.Host.ToLowerInvariant());
        if (host == "twitter.com") host = "x.com";
        var query = u.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal);
        return host + u.AbsolutePath.TrimEnd('/') + "?" + string.Join('&', query);
    }

    public static string BareHost(string host)
    {
        foreach (var p in new[] { "www.", "m.", "mobile.", "music." })
            if (host.StartsWith(p, StringComparison.Ordinal)) return host[p.Length..];
        return host;
    }

    /// <summary>The link without "https://www." for a narrow column.</summary>
    public static string Short(string url)
    {
        var s = url;
        foreach (var p in new[] { "https://", "http://" })
            if (s.StartsWith(p, StringComparison.OrdinalIgnoreCase)) { s = s[p.Length..]; break; }
        return s.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? s[4..] : s;
    }
}
