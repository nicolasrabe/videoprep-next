using System.Text.RegularExpressions;

namespace Videoprep.Core.Text;

/// <summary>
/// Premiere-style timecode typed by the user (port of ConvertFrom-TimecodeText).
/// Accepts HH:MM:SS:FF and shorter forms, digit entry that fills from the right (1523 = 00:00:15:23),
/// a leading '-' (count from the end of the video) and ';' separators (drop-frame).
/// The frame number is not checked here: it depends on the video's frame rate (see CutPlanner).
/// </summary>
public readonly partial record struct Timecode(bool Blank, bool Valid, bool Negative, int H, int M, int S, int F, bool DropFrame)
{
    [GeneratedRegex(@"^\d{1,8}$")]
    private static partial Regex DigitsOnly();

    [GeneratedRegex(@"^\d{1,2}(:\d{1,2}){1,3}$")]
    private static partial Regex Separated();

    public static Timecode Parse(string? text)
    {
        var t = (text ?? "").Trim();
        if (t.Length == 0) return new Timecode { Blank = true };

        var neg = false;
        if (t.StartsWith('-'))
        {
            neg = true;
            t = t[1..].Trim();
        }
        var drop = false;
        if (t.Contains(';'))
        {
            drop = true;
            t = t.Replace(';', ':');
        }
        var invalid = new Timecode { Negative = neg, DropFrame = drop };

        string[] parts;
        if (DigitsOnly().IsMatch(t))
        {
            t = t.PadLeft(8, '0');
            parts = [t[..2], t[2..4], t[4..6], t[6..8]];
        }
        else if (Separated().IsMatch(t))
        {
            var p = t.Split(':').ToList();
            while (p.Count < 4) p.Insert(0, "0");
            parts = [.. p];
        }
        else
        {
            return invalid;
        }

        int h = int.Parse(parts[0]), m = int.Parse(parts[1]), s = int.Parse(parts[2]), f = int.Parse(parts[3]);
        if (m >= 60 || s >= 60) return invalid with { H = h, M = m, S = s, F = f };
        return new Timecode(false, true, neg, h, m, s, f, drop);
    }

    /// <summary>
    /// Frame index for a frame-rate base (30 for 29.97, 60 for 59.94...). Drop-frame is only applied to NTSC 30/60.
    /// Port of ConvertTo-FrameIndex.
    /// </summary>
    public long ToFrameIndex(int fpsBase, bool ntsc)
    {
        long total = ((((long)H * 60 + M) * 60 + S) * fpsBase) + F;
        if (DropFrame && ntsc && (fpsBase == 30 || fpsBase == 60))
        {
            var df = fpsBase == 60 ? 4 : 2;
            long mins = (long)H * 60 + M;
            total -= df * (mins - mins / 10);
        }
        return total;
    }
}
