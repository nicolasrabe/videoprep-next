using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Videoprep.Core.Text;

/// <summary>
/// Recognizes links pasted by the user. Port of ConvertTo-YoutubeUrl, Get-VideoId, Get-LinkKey, ConvertTo-AnyUrl and
/// Get-LinksFromText (PowerShell -match is case-insensitive, so these regexes are too).
/// AUDIT M10: a bare 11-character video ID is only accepted when it is alone on its line, so a word like
/// "Hello_World" inside a sentence is not taken for a video.
/// </summary>
public static partial class UrlParser
{
    private const string Id = "[A-Za-z0-9_-]{11}";
    private const string NotIdChar = "(?![A-Za-z0-9_-])";

    [GeneratedRegex("^" + Id + "$")]
    private static partial Regex BareId();

    [GeneratedRegex(@"^(?:https?://)?(?:www\.|m\.|music\.)?youtu\.be/(" + Id + ")" + NotIdChar, RegexOptions.IgnoreCase)]
    private static partial Regex ShortLink();

    [GeneratedRegex(@"^(?:https?://)?(?:www\.|m\.|music\.)?youtube(?:-nocookie)?\.com/(?:shorts|live|embed|v)/(" + Id + ")" + NotIdChar, RegexOptions.IgnoreCase)]
    private static partial Regex PathLink();

    [GeneratedRegex(@"^(?:https?://)?(?:www\.|m\.|music\.)?youtube\.com/watch\?(?:[^#\s]*&)?v=(" + Id + ")" + NotIdChar, RegexOptions.IgnoreCase)]
    private static partial Regex WatchLink();

    [GeneratedRegex(@"^https://www\.youtube\.com/watch\?v=(" + Id + ")$")]
    private static partial Regex Canonical();

    [GeneratedRegex(@"^(?:www\.|m\.|mobile\.|clips\.|go\.)?(?:twitch\.tv|kick\.com|x\.com|twitter\.com|facebook\.com|fb\.watch|instagram\.com|tiktok\.com|vimeo\.com|reddit\.com|dailymotion\.com)/", RegexOptions.IgnoreCase)]
    private static partial Regex KnownHosts();

    [GeneratedRegex(@"^https?://", RegexOptions.IgnoreCase)]
    private static partial Regex HttpPrefix();

    public static string CanonicalYoutube(string id) => "https://www.youtube.com/watch?v=" + id;

    /// <summary>YouTube link (any common form) to the canonical watch URL. Bare IDs only when <paramref name="allowBareId"/>.</summary>
    public static string? ToYoutubeUrl(string? text, bool allowBareId = true)
    {
        var t = (text ?? "").Trim();
        if (t.Length == 0) return null;
        if (BareId().IsMatch(t)) return allowBareId ? CanonicalYoutube(t) : null;
        foreach (var rx in new[] { ShortLink(), PathLink(), WatchLink() })
        {
            var m = rx.Match(t);
            if (m.Success) return CanonicalYoutube(m.Groups[1].Value);
        }
        return null;
    }

    /// <summary>The 11-character ID of a canonical YouTube URL, else null.</summary>
    public static string? VideoId(string url)
    {
        var m = Canonical().Match(url);
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>Cache key: the YouTube ID, or "h" + 12 hex chars of the URL's SHA-1 for other sites.</summary>
    public static string LinkKey(string url)
    {
        var id = VideoId(url);
        if (id is not null) return id;
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(url));
        return "h" + Convert.ToHexStringLower(hash)[..12];
    }

    public static bool IsYoutubeKey(string key) => key.Length == 11;

    /// <summary>One token to a URL: YouTube forms, any http(s) link, or a known host typed without "https://".</summary>
    public static string? ToAnyUrl(string token, bool allowBareId = true)
    {
        var yt = ToYoutubeUrl(token, allowBareId);
        if (yt is not null) return yt;
        if (HttpPrefix().IsMatch(token)) return token;
        if (KnownHosts().IsMatch(token)) return "https://" + token;
        return null;
    }

    /// <summary>All links in a pasted text or .txt file, in order.</summary>
    public static IReadOnlyList<string> LinksFromText(string? text)
    {
        var urls = new List<string>();
        if (string.IsNullOrEmpty(text)) return urls;
        foreach (var line in text.Split('\n'))
        {
            var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var alone = tokens.Length == 1;
            foreach (var tok in tokens)
            {
                var u = ToAnyUrl(tok, allowBareId: alone);
                if (u is not null) urls.Add(u);
            }
        }
        return urls;
    }
}
