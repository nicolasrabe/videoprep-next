using System.Text.RegularExpressions;
using Videoprep.Core.Text;

namespace Videoprep.Core.Import;

/// <summary>What a link most likely is, judged only from its text (no network).</summary>
public enum LinkKind
{
    /// <summary>A single video on a site we know.</summary>
    Video,
    /// <summary>A channel or profile page (many videos; the Scraping tab is for those).</summary>
    Channel,
    /// <summary>A playlist.</summary>
    Playlist,
    /// <summary>A document, picture or web page that is not a video (".pdf", ".jpg", a known site's home page...).</summary>
    NotVideo,
    /// <summary>A site we do not know: it may or may not be a video.</summary>
    Unknown,
}

public sealed record LinkClass(LinkKind Kind, string Site);

/// <summary>Sorts links found in a file so the review window can pre-check only the likely videos.</summary>
public static partial class LinkClassifier
{
    private sealed record Rule(string Site, Regex Host, Regex Video, Regex Channel);

    private const RegexOptions I = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly Rule[] Rules =
    [
        new("Twitch", new(@"^(twitch\.tv|clips\.twitch\.tv|go\.twitch\.tv)$", I),
            new(@"^/(videos/\d+|[^/]+/clip/[^/]+|[^/]+/v/\d+)/?$", I), new(@"^/[A-Za-z0-9_]+(/(videos|clips|about|schedule))?/?$", I)),
        new("Kick", new(@"^kick\.com$", I), new(@"^/[^/]+/(videos|clips)/[^/]+/?$|^/video/[^/]+/?$", I), new(@"^/[A-Za-z0-9_-]+/?$", I)),
        new("X", new(@"^(x\.com|twitter\.com)$", I), new(@"^/[^/]+/status/\d+", I), new(@"^/[A-Za-z0-9_]+/?$", I)),
        new("Facebook", new(@"^(facebook\.com|fb\.watch)$", I), new(@"^/(watch|reel/|[^/]+/videos/|share/v/|share/r/)", I), new(@"^/[A-Za-z0-9.]+/?$", I)),
        new("Instagram", new(@"^instagram\.com$", I), new(@"^/(p|reels?|tv)/[^/]+", I), new(@"^/[A-Za-z0-9._]+/?$", I)),
        new("TikTok", new(@"^(tiktok\.com|vm\.tiktok\.com|vt\.tiktok\.com)$", I), new(@"^/@[^/]+/video/\d+|^/v/\d+|^/t/", I), new(@"^/@[^/]+/?$", I)),
        new("Vimeo", new(@"^(vimeo\.com|player\.vimeo\.com)$", I), new(@"^/(video/)?\d+(/[0-9a-f]+)?/?$|^/channels/[^/]+/\d+/?$", I), new(@"^/[A-Za-z][^/]*/?$", I)),
        new("Reddit", new(@"^(reddit\.com|old\.reddit\.com|v\.redd\.it)$", I), new(@"^/r/[^/]+/comments/", I), new(@"^/(r|u|user)/[^/]+/?$", I)),
        new("Dailymotion", new(@"^(dailymotion\.com|dai\.ly)$", I), new(@"^/video/[^/]+", I), new(@"^/[A-Za-z0-9_-]+/?$", I)),
    ];

    [GeneratedRegex(@"\.(pdf|docx?|xlsx?|pptx?|txt|rtf|odt|csv|zip|rar|7z|jpe?g|png|gif|webp|svg|bmp|psd|html?|php)$", I)]
    private static partial Regex DocumentExtension();

    [GeneratedRegex(@"\.(mp4|mov|m4v|webm|mkv|avi|mp3|m4a|wav|flac|ogg|opus)$", I)]
    private static partial Regex MediaExtension();

    [GeneratedRegex(@"^/(@[^/]+|channel/[^/]+|c/[^/]+|user/[^/]+)(/(videos|shorts|streams|featured|playlists|about))?/?$", I)]
    private static partial Regex YoutubeChannel();

    public static LinkClass Classify(string url)
    {
        if (UrlParser.VideoId(url) is not null || UrlParser.ToYoutubeUrl(url, allowBareId: false) is not null)
            return new(LinkKind.Video, "YouTube");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return new(LinkKind.NotVideo, "");
        var host = LinkNormalizer.BareHost(u.Host.ToLowerInvariant());
        var path = Uri.UnescapeDataString(u.AbsolutePath);

        if (host is "youtube.com" or "youtube-nocookie.com" or "youtu.be")
        {
            if (path.StartsWith("/playlist", StringComparison.OrdinalIgnoreCase) || u.Query.Contains("list=", StringComparison.Ordinal))
                return new(LinkKind.Playlist, "YouTube");
            return YoutubeChannel().IsMatch(path) ? new(LinkKind.Channel, "YouTube") : new(LinkKind.NotVideo, "YouTube");
        }
        foreach (var r in Rules)
        {
            if (!r.Host.IsMatch(host)) continue;
            // Short-link hosts are always a single video.
            if (host is "clips.twitch.tv" or "fb.watch" or "vm.tiktok.com" or "vt.tiktok.com" or "v.redd.it" or "dai.ly" or "player.vimeo.com")
                return new(path.Trim('/').Length > 0 ? LinkKind.Video : LinkKind.NotVideo, r.Site);
            if (r.Video.IsMatch(path)) return new(LinkKind.Video, r.Site);
            if (path.Trim('/').Length == 0) return new(LinkKind.NotVideo, r.Site);
            return new(r.Channel.IsMatch(path) ? LinkKind.Channel : LinkKind.NotVideo, r.Site);
        }
        if (MediaExtension().IsMatch(path)) return new(LinkKind.Video, host);
        if (DocumentExtension().IsMatch(path)) return new(LinkKind.NotVideo, host);
        return new(LinkKind.Unknown, host);
    }
}
