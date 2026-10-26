using System.Globalization;
using System.Text.RegularExpressions;

namespace Videoprep.Core.Engine;

/// <summary>
/// A progress line from <see cref="YtDlpArgs.ProgressTemplate"/>, e.g. "VP| 41.5%|   7.16MiB/s|00:00" (AUDIT M6).
/// Fields come padded with spaces; speed can be "Unknown B/s" and ETA "NA" (docs/VERIFY.md).
/// </summary>
public sealed record ProgressLine(double Percent, string Speed, string Eta)
{
    public static ProgressLine? TryParse(string line)
    {
        if (!line.StartsWith("VP|", StringComparison.Ordinal)) return null;
        var f = line.Split('|');
        if (f.Length < 4) return null;
        var pctText = f[1].Trim().TrimEnd('%');
        if (!double.TryParse(pctText, NumberStyles.Float, CultureInfo.InvariantCulture, out var pct)) return null;
        static string Clean(string s)
        {
            s = s.Trim();
            return s is "NA" or "Unknown B/s" or "Unknown" ? "" : s;
        }
        return new ProgressLine(Math.Clamp(pct, 0, 100), Clean(f[2]), Clean(f[3]));
    }
}

/// <summary>
/// The preview line from <see cref="YtDlpArgs.DetailTemplate"/> (port of Finish-Detail).
/// Labels like "1080p30", "H.264", "AAC" are what the preview panel shows.
/// </summary>
public sealed partial record PreviewDetails(string Duration, string Resolution, double? Fps, string VideoLabel,
                                            string AudioLabel, string Date, string Uploader, string Thumbnail, string Title)
{
    [GeneratedRegex(@"^\d+$")]
    private static partial Regex Digits();

    [GeneratedRegex(@"^\d+(\.\d+)?$")]
    private static partial Regex Number();

    [GeneratedRegex(@"^(\d{4})(\d{2})(\d{2})$")]
    private static partial Regex YyyyMmDd();

    public static PreviewDetails? TryParse(string line)
    {
        if (!line.StartsWith("DET|", StringComparison.Ordinal)) return null;
        var f = line.Split('|', 10);
        string F(int i) => i < f.Length ? f[i] : "";
        static string Na(string s) => s is "NA" or "none" ? "" : s;

        // duration_string is "19" for a 19-second video, "4:05" or "1:02:03" otherwise (docs/VERIFY.md); shown as is.
        var dur = Na(F(1));
        var h = F(2);
        var fpsText = F(3);
        double? fps = null;
        var res = "";
        if (Digits().IsMatch(h))
        {
            res = h + "p";
            if (Number().IsMatch(fpsText))
            {
                fps = double.Parse(fpsText, CultureInfo.InvariantCulture);
                res += Math.Round(fps.Value).ToString(CultureInfo.InvariantCulture);
            }
        }
        var vc = F(4);
        var vl = vc.StartsWith("avc1", StringComparison.OrdinalIgnoreCase) || vc == "h264" ? "H.264"
            : vc.StartsWith("vp0", StringComparison.OrdinalIgnoreCase) || vc.StartsWith("vp9", StringComparison.OrdinalIgnoreCase) ? "VP9"
            : vc.StartsWith("av01", StringComparison.OrdinalIgnoreCase) ? "AV1"
            : Na(vc);
        var ac = F(5);
        var al = ac.StartsWith("mp4a", StringComparison.OrdinalIgnoreCase) || ac == "aac" ? "AAC"
            : ac.StartsWith("opus", StringComparison.OrdinalIgnoreCase) ? "Opus"
            : Na(ac);
        var m = YyyyMmDd().Match(F(6));
        var date = m.Success ? $"{m.Groups[1].Value}-{m.Groups[2].Value}-{m.Groups[3].Value}" : "";
        var thumb = F(8).StartsWith("http", StringComparison.OrdinalIgnoreCase) ? F(8) : "";
        return new PreviewDetails(dur, res, fps, vl, al, date, Na(F(7)), thumb, Na(F(9)));
    }
}
