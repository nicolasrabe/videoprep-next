using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Videoprep.Core.Import;

/// <summary>Links in Markdown, HTML and CSV text. Only parsed: nothing is fetched, run or opened.</summary>
public static partial class MarkupLinks
{
    // A time limit on every pattern: the text comes from an untrusted file.
    private const int RegexMs = 2000;

    // ---------------- Markdown ----------------

    [GeneratedRegex(@"!?\[([^\]\n]*)\]\(\s*<?([^)\s>]+)>?(?:\s+""[^""]*"")?\s*\)", RegexOptions.None, RegexMs)]
    private static partial Regex MdInline();

    [GeneratedRegex(@"^[ \t]{0,3}\[([^\]\n]+)\]:[ \t]*<?(\S+?)>?(?:[ \t]+.*)?$", RegexOptions.Multiline, RegexMs)]
    private static partial Regex MdReference();

    /// <summary>"[text](url)" and reference links as hyperlinks; "&lt;url&gt;" and bare URLs as typed links.</summary>
    public static IReadOnlyList<FoundLink> Markdown(string text)
    {
        var found = new List<(int Pos, FoundLink Link)>();
        var rest = new StringBuilder(text);
        void Take(System.Text.RegularExpressions.Match m, FoundLink? link)
        {
            if (link is not null) found.Add((m.Index, link));
            for (var i = m.Index; i < m.Index + m.Length; i++) if (rest[i] != '\n') rest[i] = ' ';
        }
        foreach (System.Text.RegularExpressions.Match m in MdInline().Matches(text))
            Take(m, TextLinks.ToUrl(m.Groups[2].Value) is { } u ? new FoundLink(u, Clean(m.Groups[1].Value), LinkHow.Hyperlink) : null);
        foreach (System.Text.RegularExpressions.Match m in MdReference().Matches(rest.ToString()))
            Take(m, TextLinks.ToUrl(m.Groups[2].Value) is { } u ? new FoundLink(u, Clean(m.Groups[1].Value), LinkHow.Hyperlink) : null);
        var plain = rest.ToString();
        foreach (var m in TextLinks.Find(plain)) found.Add((m.Index, new FoundLink(m.Url, TextLinks.Snippet(plain, m.Index, m.Length), LinkHow.Text)));
        return found.OrderBy(f => f.Pos).Select(f => f.Link).ToList();
    }

    // ---------------- HTML ----------------

    [GeneratedRegex(@"<!--.*?-->|<(script|style|template|noscript)\b.*?</\1\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase, RegexMs)]
    private static partial Regex HtmlHidden();

    [GeneratedRegex(@"<a\b([^>]*)>(.*?)</a\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase, RegexMs)]
    private static partial Regex HtmlAnchor();

    [GeneratedRegex(@"<(?:iframe|embed|video|source)\b([^>]*)>", RegexOptions.IgnoreCase, RegexMs)]
    private static partial Regex HtmlEmbed();

    [GeneratedRegex(@"\b(href|src)\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))", RegexOptions.IgnoreCase, RegexMs)]
    private static partial Regex HtmlAttr();

    [GeneratedRegex(@"</?(?:p|div|br|li|tr|td|th|h[1-6]|table|ul|ol|section|article|blockquote|pre)\b[^>]*>", RegexOptions.IgnoreCase, RegexMs)]
    private static partial Regex HtmlBlock();

    [GeneratedRegex(@"<[^>]*>", RegexOptions.None, RegexMs)]
    private static partial Regex HtmlTag();

    /// <summary>
    /// &lt;a href&gt; links with their visible text, embedded videos (&lt;iframe src&gt;), and URLs typed in the page text.
    /// Scripts, styles and comments are ignored.
    /// </summary>
    public static IReadOnlyList<FoundLink> Html(string html)
    {
        var found = new List<(int Pos, FoundLink Link)>();
        var visible = new StringBuilder(HtmlHidden().Replace(html, m => new string(' ', m.Length)));
        var source = visible.ToString();
        foreach (System.Text.RegularExpressions.Match m in HtmlAnchor().Matches(source))
        {
            var href = Attr(m.Groups[1].Value, "href");
            if (href is not null && TextLinks.ToUrl(WebUtility.HtmlDecode(href).Trim()) is { } u)
                found.Add((m.Index, new FoundLink(u, HtmlText(m.Groups[2].Value), LinkHow.Hyperlink)));
            // The anchor text is shown as the link's text; it is not searched again for typed links.
            for (var i = m.Index; i < m.Index + m.Length; i++) visible[i] = ' ';
        }
        foreach (System.Text.RegularExpressions.Match m in HtmlEmbed().Matches(source))
        {
            var src = Attr(m.Groups[1].Value, "src");
            if (src is not null && TextLinks.ToUrl(WebUtility.HtmlDecode(src).Trim()) is { } u)
                found.Add((m.Index, new FoundLink(u, "", LinkHow.Hyperlink)));
            for (var i = m.Index; i < m.Index + m.Length; i++) visible[i] = ' ';
        }
        // Typed links: tags out (block tags become line breaks, so snippets stay inside their paragraph).
        var withBreaks = HtmlBlock().Replace(visible.ToString(), m => "\n" + new string(' ', m.Length - 1));
        var noTags = HtmlTag().Replace(withBreaks, m => new string(' ', m.Length));
        foreach (var m in TextLinks.Find(noTags))
        {
            var url = TextLinks.ToUrl(WebUtility.HtmlDecode(noTags.Substring(m.Index, m.Length))) ?? m.Url;
            found.Add((m.Index, new FoundLink(url, WebUtility.HtmlDecode(TextLinks.Snippet(noTags, m.Index, m.Length)), LinkHow.Text)));
        }
        return found.OrderBy(f => f.Pos).Select(f => f.Link).ToList();
    }

    private static string? Attr(string attrs, string name)
    {
        foreach (System.Text.RegularExpressions.Match a in HtmlAttr().Matches(attrs))
        {
            if (!a.Groups[1].Value.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            return a.Groups[2].Success ? a.Groups[2].Value : a.Groups[3].Success ? a.Groups[3].Value : a.Groups[4].Value;
        }
        return null;
    }

    private static string HtmlText(string inner) => Clean(WebUtility.HtmlDecode(HtmlTag().Replace(inner, " ")));

    private static string Clean(string s)
    {
        var t = TextLinks.Squash(s).Trim();
        return t.Length > 120 ? t[..119] + "…" : t;
    }

    // ---------------- CSV ----------------

    /// <summary>
    /// Any cell that contains a link. The text shown for it is the other cells of its row (often a scene name or a note).
    /// The separator is guessed from the first line: "," or ";" (Excel in Spanish) or tab.
    /// </summary>
    public static IReadOnlyList<FoundLink> Csv(string text)
    {
        var found = new List<FoundLink>();
        foreach (var row in CsvRows(text))
        {
            for (var c = 0; c < row.Count; c++)
            {
                foreach (var m in TextLinks.Find(row[c]))
                {
                    var others = string.Join(" · ", row.Where((cell, i) => i != c && cell.Trim().Length > 0 && TextLinks.Find(cell).Count == 0)
                                                       .Select(cell => TextLinks.Squash(cell).Trim()));
                    var inCell = TextLinks.Snippet(row[c], m.Index, m.Length);
                    var context = string.Join(" · ", new[] { others, inCell }.Where(s => s.Length > 0));
                    found.Add(new FoundLink(m.Url, Clean(context), LinkHow.Text));
                }
            }
        }
        return found;
    }

    /// <summary>RFC 4180 rows: quoted cells may contain the separator, "" for a quote, and line breaks.</summary>
    public static IEnumerable<List<string>> CsvRows(string text)
    {
        var sep = GuessSeparator(text);
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (quoted)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                    else quoted = false;
                }
                else cell.Append(ch);
            }
            else if (ch == '"' && cell.Length == 0) quoted = true;
            else if (ch == sep) { row.Add(cell.ToString()); cell.Clear(); }
            else if (ch == '\n')
            {
                row.Add(cell.ToString().TrimEnd('\r'));
                cell.Clear();
                yield return row;
                row = [];
            }
            else cell.Append(ch);
        }
        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString().TrimEnd('\r'));
            yield return row;
        }
    }

    private static char GuessSeparator(string text)
    {
        int comma = 0, semi = 0, tab = 0;
        var quoted = false;
        foreach (var ch in text)
        {
            if (ch == '"') quoted = !quoted;
            else if (quoted) continue;
            else if (ch == '\n') break;
            else if (ch == ',') comma++;
            else if (ch == ';') semi++;
            else if (ch == '\t') tab++;
        }
        return tab > comma && tab > semi ? '\t' : semi > comma ? ';' : ',';
    }
}
