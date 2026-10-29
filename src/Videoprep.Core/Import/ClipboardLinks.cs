using Videoprep.Core.Text;

namespace Videoprep.Core.Import;

/// <summary>
/// Links worth adding to the queue from copied text (F5). Stricter than a file import on purpose: only links that are
/// clearly one video of a known site. An ordinary web page copied from the address bar, a channel or a list is left
/// alone, so copying things for other reasons never fills the queue.
/// </summary>
public static class ClipboardLinks
{
    /// <summary>Longest text looked at; a bigger copy (a whole document) is ignored.</summary>
    public const int MaxChars = 100_000;

    /// <summary>More links than this in one copy are not added without asking.</summary>
    public const int AskAbove = 20;

    public static IReadOnlyList<string> Find(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxChars) return [];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var links = new List<string>();
        foreach (var m in TextLinks.Find(text))
        {
            var url = LinkNormalizer.ForQueue(m.Url);
            if (url is null || LinkClassifier.Classify(url).Kind != LinkKind.Video) continue;
            // A link to some unknown site with a video file extension is not "clearly" a known video site.
            if (UrlParser.VideoId(url) is null && LinkClassifier.Classify(url).Site is var site && !KnownSites.Contains(site)) continue;
            if (seen.Add(LinkNormalizer.CompareKey(url))) links.Add(url);
        }
        return links;
    }

    private static readonly HashSet<string> KnownSites = new(StringComparer.Ordinal)
    {
        "YouTube", "Twitch", "Kick", "X", "Facebook", "Instagram", "TikTok", "Vimeo", "Reddit", "Dailymotion",
    };
}
