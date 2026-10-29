using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Videoprep.Core.Storage;

namespace Videoprep.Core.Sources;

/// <summary>One line of the list of sources: one output file (a clip counts as its own line).</summary>
public sealed record SourceRow(string Title, string Channel, string Url, string FileName, string Path,
                               string Format, string Date, string Status);

/// <summary>
/// "Lista de fuentes" (F7): where each saved file came from, as CSV (opens in Excel) or TXT. Pure text building, no
/// UI: the app passes the rows and the column names in the user's language.
/// Safety: cookies never get here; links are cleaned of anything that looks like session data; cell text that a
/// spreadsheet would run as a formula ("=", "+", "-", "@") gets an apostrophe in front.
/// </summary>
public static partial class SourceList
{
    /// <summary>Parameter names that carry session data, never written.</summary>
    [GeneratedRegex(@"(token|sig|signature|session|sid|auth|secret|pass|pwd|key|jwt|cookie|credential|policy|expire|hash|ticket|bearer|oauth|login|hmac|nonce|^code$|^st$|^e$)",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretName();

    /// <summary>A long run of letters and digits (a key) is not an ID of a video.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9+/_=.~-]{24,}$")]
    private static partial Regex LooksLikeKey();

    /// <summary>The link as written in the list: web links only, without user, password, "#..." or session-like parameters.</summary>
    public static string CleanUrl(string? url)
    {
        if (!Uri.TryCreate((url ?? "").Trim(), UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps)) return "";
        var kept = new List<string>();
        foreach (var pair in u.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            var name = Uri.UnescapeDataString(eq < 0 ? pair : pair[..eq]);
            var value = eq < 0 ? "" : Uri.UnescapeDataString(pair[(eq + 1)..]);
            // "v" (YouTube video) and "list" are never secrets; the video ID is 11 characters anyway.
            if (name is not ("v" or "list") && (SecretName().IsMatch(name) || LooksLikeKey().IsMatch(value))) continue;
            kept.Add(pair);
        }
        var port = u.IsDefaultPort ? "" : ":" + u.Port;
        return $"{u.Scheme}://{u.Host}{port}{u.AbsolutePath}{(kept.Count > 0 ? "?" + string.Join('&', kept) : "")}";
    }

    /// <summary>Spreadsheets run text that starts with = + - @ (or a tab / return) as a formula: an apostrophe stops that.</summary>
    public static string Guard(string? cell)
    {
        var c = cell ?? "";
        return c.Length > 0 && (c[0] is '=' or '+' or '-' or '@' or '\t' or '\r') ? "'" + c : c;
    }

    /// <summary>The separator Excel expects: the list separator of Windows' language (";" in Spanish), "," otherwise.</summary>
    public static char SeparatorFor(CultureInfo culture)
    {
        var s = culture.TextInfo.ListSeparator.Trim();
        return s.Length == 1 && s[0] is ',' or ';' ? s[0] : ',';
    }

    private static string[] Cells(SourceRow r) =>
        [Guard(r.Title), Guard(r.Channel), CleanUrl(r.Url), Guard(r.FileName), Guard(r.Path), Guard(r.Format), r.Date, Guard(r.Status)];

    /// <summary>CSV with quotes where needed and CRLF line ends (the BOM is added when the file is written).</summary>
    public static string Csv(IEnumerable<SourceRow> rows, IReadOnlyList<string> headers, char separator)
    {
        var sb = new StringBuilder();
        void Line(IEnumerable<string> cells) => sb.Append(string.Join(separator, cells.Select(c => Quote(c, separator)))).Append("\r\n");
        Line(headers);
        foreach (var r in rows) Line(Cells(r));
        return sb.ToString();
    }

    private static string Quote(string cell, char separator)
    {
        var needs = cell.Contains(separator) || cell.Contains('"') || cell.Contains('\n') || cell.Contains('\r');
        return needs ? "\"" + cell.Replace("\"", "\"\"") + "\"" : cell;
    }

    /// <summary>TXT: one line per file with the same columns separated by tabs (line breaks inside a cell become spaces).</summary>
    public static string Txt(IEnumerable<SourceRow> rows, IReadOnlyList<string> headers)
    {
        var sb = new StringBuilder();
        static string One(string c) => Regex.Replace(c, @"[\t\r\n]+", " ");
        sb.Append(string.Join('\t', headers.Select(One))).Append("\r\n");
        foreach (var r in rows) sb.Append(string.Join('\t', Cells(r).Select(One))).Append("\r\n");
        return sb.ToString();
    }

    /// <summary>"fuentes-20261029-1405.csv"</summary>
    public static string FileName(DateTime now, string format) =>
        $"fuentes-{now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture)}.{(format == "txt" ? "txt" : "csv")}";

    /// <summary>
    /// Writes the list into <paramref name="folder"/> under a name nobody has (never replaces a list). CSV and TXT go out
    /// as UTF-8 with BOM so Excel and old Notepad read the accents. Returns the path.
    /// </summary>
    public static string Write(string folder, DateTime now, string format, IReadOnlyList<SourceRow> rows,
                               IReadOnlyList<string> headers, CultureInfo culture)
    {
        Directory.CreateDirectory(folder);
        var ext = format == "txt" ? "txt" : "csv";
        var path = FileNames.UniquePath(folder, System.IO.Path.GetFileNameWithoutExtension(FileName(now, format)), "." + ext);
        WriteTo(path, format, rows, headers, culture);
        return path;
    }

    /// <summary>Writes to an exact path the user chose (manual export).</summary>
    public static void WriteTo(string path, string format, IReadOnlyList<SourceRow> rows, IReadOnlyList<string> headers, CultureInfo culture)
    {
        var text = format == "txt" ? Txt(rows, headers) : Csv(rows, headers, SeparatorFor(culture));
        File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }
}
