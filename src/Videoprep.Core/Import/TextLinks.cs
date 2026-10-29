using System.Text.RegularExpressions;
using Videoprep.Core.Text;

namespace Videoprep.Core.Import;

/// <summary>
/// Links typed in free text: "https://...", "www....", "youtu.be/..." and the sites the link parser already knows
/// written without "https://". Punctuation around a link ("(https://x).", "«www.x.com»,") is not part of it.
/// </summary>
public static partial class TextLinks
{
    /// <summary>One link inside a text: the URL to use and where its text was.</summary>
    public readonly record struct Match(string Url, int Index, int Length);

    [GeneratedRegex(@"\S+")]
    private static partial Regex Token();

    [GeneratedRegex(@"^www\.[a-z0-9-]+(\.[a-z0-9-]+)+(?:[/?#]\S*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex Www();

    private const string Leading = "([{<\"'«“‘¿¡*_";
    private const string Trailing = ".,;:!?)]}>\"'»”’…*_";

    /// <summary>
    /// Every link in <paramref name="text"/>, in order. <paramref name="allowBareIdLines"/>: a line that is only an
    /// 11-character YouTube ID counts (the .txt rule of 0.2; never inside a sentence, AUDIT M10).
    /// </summary>
    public static IReadOnlyList<Match> Find(string text, bool allowBareIdLines = false)
    {
        var found = new List<Match>();
        var lineStart = 0;
        while (lineStart <= text.Length)
        {
            var end = text.IndexOf('\n', lineStart);
            if (end < 0) end = text.Length;
            var line = text[lineStart..end];
            var tokens = Token().Matches(line);
            foreach (System.Text.RegularExpressions.Match t in tokens)
            {
                var (start, length) = Trim(t.Value);
                if (length <= 0) continue;
                var tok = t.Value.Substring(start, length);
                // "mirá:https://..." - the link starts at "http" even when glued to a word.
                var http = SchemeIndex(tok);
                if (http > 0)
                {
                    tok = tok[http..];
                    start += http;
                    length -= http;
                }
                var url = ToUrl(tok, allowBareIdLines && tokens.Count == 1);
                if (url is not null) found.Add(new Match(url, lineStart + t.Index + start, length));
            }
            lineStart = end + 1;
        }
        return found;
    }

    /// <summary>One cleaned-up token to a link, or null.</summary>
    public static string? ToUrl(string token, bool allowBareId = false)
    {
        if (Www().IsMatch(token)) return "https://" + token;
        var u = UrlParser.ToAnyUrl(token, allowBareId);
        if (u is null) return null;
        // "https://" alone or "https://x" (no dot) is not a link.
        return Uri.TryCreate(u, UriKind.Absolute, out var uri) && uri.Host.Contains('.') ? u : null;
    }

    private static int SchemeIndex(string tok)
    {
        var a = tok.IndexOf("https://", StringComparison.OrdinalIgnoreCase);
        var b = tok.IndexOf("http://", StringComparison.OrdinalIgnoreCase);
        return a < 0 ? b : b < 0 ? a : Math.Min(a, b);
    }

    private static (int Start, int Length) Trim(string tok)
    {
        int s = 0, e = tok.Length;
        while (s < e && Leading.Contains(tok[s])) s++;
        while (e > s && Trailing.Contains(tok[e - 1]))
        {
            // Keep a closing ")" that belongs to the link (Wikipedia-style "..._(film)").
            if (tok[e - 1] == ')' && Count(tok, s, e, '(') >= Count(tok, s, e, ')')) break;
            e--;
        }
        return (s, e - s);
    }

    private static int Count(string s, int from, int to, char c)
    {
        var n = 0;
        for (var i = from; i < to; i++) if (s[i] == c) n++;
        return n;
    }

    /// <summary>
    /// A short piece of the text around a link (about 40 characters each side, cut at a space, "…" where cut), with
    /// the link itself replaced by "…". "" when the link was alone.
    /// </summary>
    public static string Snippet(string text, int index, int length, int side = 40)
    {
        var lineStart = text.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;
        if (index == 0) lineStart = 0;
        var lineEnd = text.IndexOf('\n', index + length);
        if (lineEnd < 0) lineEnd = text.Length;

        // Punctuation that only hugged the link ("(", ")." ...) is not worth showing.
        var before = Squash(text[lineStart..index]).TrimEnd().TrimEnd(Leading.ToCharArray());
        var after = Squash(text[(index + length)..lineEnd]).TrimStart().TrimStart(Trailing.ToCharArray());
        if (before.Length > side)
        {
            var cut = before.IndexOf(' ', before.Length - side);
            before = "…" + (cut >= 0 ? before[(cut + 1)..] : before[^side..]);
        }
        if (after.Length > side)
        {
            var cut = after.LastIndexOf(' ', side);
            after = (cut > 0 ? after[..cut] : after[..side]) + "…";
        }
        before = before.Trim();
        after = after.Trim();
        if (before.Length == 0 && after.Length == 0) return "";
        if (after.Length == 0) return before;
        if (before.Length == 0) return after;
        return before + " … " + after;
    }

    /// <summary>Whitespace runs to one space.</summary>
    public static string Squash(string s) => Spaces().Replace(s, " ");

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    /// <summary>All typed links of a text as <see cref="FoundLink"/>s with their snippet.</summary>
    public static IEnumerable<FoundLink> Links(string text, bool allowBareIdLines = false) =>
        Find(text, allowBareIdLines).Select(m => new FoundLink(m.Url, Snippet(text, m.Index, m.Length), LinkHow.Text));
}
