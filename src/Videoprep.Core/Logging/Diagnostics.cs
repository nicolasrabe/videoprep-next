using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Videoprep.Core.Logging;

/// <summary>
/// Text for the "Copy diagnostics" button (SCRAPING_SPEC 3): versions, tools, browsers and the last log lines, safe to
/// paste in a bug report. Links lose their query strings (signatures, tokens) except a YouTube video ID; lines that
/// look like cookie data are dropped.
/// </summary>
public static partial class Diagnostics
{
    [GeneratedRegex(@"https?://[^\s""'<>]+", RegexOptions.IgnoreCase)]
    private static partial Regex Url();

    [GeneratedRegex(@"[?&]v=([A-Za-z0-9_-]{11})")]
    private static partial Regex YoutubeId();

    [GeneratedRegex(@"\t(TRUE|FALSE)\t/|cookie:\s|set-cookie|__Secure-|SAPISID|LOGIN_INFO", RegexOptions.IgnoreCase)]
    private static partial Regex CookieLike();

    public static string SanitizeLine(string line)
    {
        if (CookieLike().IsMatch(line)) return "[line removed: looked like cookie data]";
        return Url().Replace(line, m =>
        {
            var u = m.Value;
            var q = u.IndexOfAny(['?', '#']);
            if (q < 0) return u;
            var id = YoutubeId().Match(u);
            return u[..q] + (id.Success ? "?v=" + id.Groups[1].Value : "?…");
        });
    }

    public static string Build(string appVersion, IReadOnlyList<(string Name, string? Path, string Version)> tools,
                               IReadOnlyList<string> browsers, IReadOnlyList<string> logLines)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"videoprep {appVersion}");
        sb.AppendLine($"OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}), .NET {Environment.Version}");
        foreach (var (name, path, version) in tools)
            sb.AppendLine(path is null ? $"{name}: MISSING" : $"{name}: {version}  ({AnonymizeUser(path)})");
        sb.AppendLine("Browsers with a profile: " + (browsers.Count > 0 ? string.Join(", ", browsers) : "none found"));
        sb.AppendLine();
        sb.AppendLine($"--- last {logLines.Count} log lines ---");
        foreach (var l in logLines) sb.AppendLine(AnonymizeUser(SanitizeLine(l)));
        return sb.ToString();
    }

    /// <summary>C:\Users\nico\... -> C:\Users\[user]\... (the user name is personal data).</summary>
    public static string AnonymizeUser(string text)
    {
        var name = Environment.UserName;
        return name.Length < 2 ? text : text.Replace(@"\" + name + @"\", @"\[user]\", StringComparison.OrdinalIgnoreCase)
                                             .Replace("/" + name + "/", "/[user]/", StringComparison.OrdinalIgnoreCase);
    }
}
