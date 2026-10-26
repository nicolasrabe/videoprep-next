using System.Text.RegularExpressions;

namespace Videoprep.Core.Scraping;

/// <summary>
/// Turns what the user pasted in Explorar into the pages to list (SCRAPING_SPEC 1):
/// a YouTube channel (@handle, /channel/ID, /c/name, /user/name) gives one page per checked tab;
/// a playlist (list=) or any other site gives that single page (best effort).
/// </summary>
public static partial class SourceUrl
{
    [GeneratedRegex(@"^(?:https?://)?(?:www\.|m\.)?youtube\.com/(@[^/?#\s]+|channel/[^/?#\s]+|c/[^/?#\s]+|user/[^/?#\s]+)", RegexOptions.IgnoreCase)]
    private static partial Regex Channel();

    [GeneratedRegex(@"^@[A-Za-z0-9._-]{3,}$")]
    private static partial Regex BareHandle();

    [GeneratedRegex(@"[?&]list=([A-Za-z0-9_-]+)")]
    private static partial Regex ListParam();

    // Profiles yt-dlp cannot list today (docs/VERIFY.md phase 5): instagram:user is marked broken, X has no profile
    // extractor at all, tiktok:user fails ("Unable to extract secondary user ID"). Single posts still download fine.
    [GeneratedRegex(@"^(?:https?://)?(?:www\.|m\.)?instagram\.com/(?!(?:p|reels?|tv|stories|explore)/)[A-Za-z0-9._]+/?(?:[?#].*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex InstagramProfile();

    [GeneratedRegex(@"^(?:https?://)?(?:www\.|mobile\.)?(?:x|twitter)\.com/(?!i/|home|search|explore)[A-Za-z0-9_]+(?:/(?:media|with_replies|highlights))?/?(?:[?#].*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex XProfile();

    [GeneratedRegex(@"^(?:https?://)?(?:www\.|m\.)?tiktok\.com/@[^/?#\s]+/?(?:[?#].*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex TikTokProfile();

    /// <summary>"Instagram", "X" or "TikTok" when the text is a profile page of a site that cannot be listed; else null.</summary>
    public static string? UnsupportedProfile(string input)
    {
        var t = input.Trim();
        if (InstagramProfile().IsMatch(t)) return "Instagram";
        if (XProfile().IsMatch(t)) return "X";
        if (TikTokProfile().IsMatch(t)) return "TikTok";
        return null;
    }

    public sealed record Target(SourceTab Tab, string Url);

    public static string TabPath(SourceTab tab) => tab switch
    {
        SourceTab.Shorts => "shorts",
        SourceTab.Streams => "streams",
        SourceTab.Playlists => "playlists",
        _ => "videos",
    };

    /// <summary>The channel's base URL ("https://www.youtube.com/@name"), or null when the text is not a YouTube channel.</summary>
    public static string? ChannelBase(string input)
    {
        var t = input.Trim();
        if (BareHandle().IsMatch(t)) return "https://www.youtube.com/" + t;
        var m = Channel().Match(t);
        return m.Success ? "https://www.youtube.com/" + m.Groups[1].Value : null;
    }

    /// <summary>Pages to list, in the order of <paramref name="tabs"/>. Empty when the text is not a link.</summary>
    public static IReadOnlyList<Target> Targets(string input, IReadOnlyCollection<SourceTab> tabs)
    {
        var t = input.Trim();
        if (t.Length == 0) return [];
        if (ChannelBase(t) is { } channel)
        {
            var wanted = tabs.Where(x => x != SourceTab.Single).Distinct().ToList();
            if (wanted.Count == 0) wanted = [SourceTab.Videos];
            return wanted.Select(x => new Target(x, $"{channel}/{TabPath(x)}")).ToList();
        }
        var list = ListParam().Match(t);
        var isYoutube = t.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) || t.Contains("youtu.be", StringComparison.OrdinalIgnoreCase);
        if (list.Success && isYoutube)
            return [new Target(SourceTab.Single, "https://www.youtube.com/playlist?list=" + list.Groups[1].Value)];
        if (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || t.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return [new Target(SourceTab.Single, t)];
        return [];
    }
}
