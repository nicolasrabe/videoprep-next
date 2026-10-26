using Videoprep.Core.Text;

namespace Videoprep.Core.Media;

/// <summary>
/// Frames to keep and the ffmpeg times for one clip. <see cref="StartFrame"/> and <see cref="EndFrame"/> are both included.
/// <see cref="T0"/>/<see cref="Duration"/> carry a quarter-frame guard so the exact frames survive rounding.
/// </summary>
public sealed record CutPlan(bool Ok, string Error, long StartFrame, long EndFrame, double T0, double Duration,
                             long Frames, double Fps, string Note)
{
    internal static CutPlan Fail(string error) => new(false, error, 0, 0, 0, 0, 0, 0, "");
}

/// <summary>Port of Get-CutPlan: same formulas and same (English, log-only) error messages.</summary>
public static class CutPlanner
{
    public static CutPlan Plan(string? start, string? end, SourceTiming timing)
    {
        var fpsBase = timing.FpsBase;
        var stc = Timecode.Parse(start);
        var etc = Timecode.Parse(end);

        foreach (var tc in new[] { stc, etc })
        {
            if (tc.Blank) continue;
            if (!tc.Valid) return CutPlan.Fail("invalid timecode");
            if (tc.F >= fpsBase) return CutPlan.Fail($"frames must be 0-{fpsBase - 1} for this video ({fpsBase} fps)");
        }
        if (!etc.Blank && etc.Negative) return CutPlan.Fail("End cannot start with a minus sign");

        var total = timing.TotalFrames;
        long s = 0;
        if (!stc.Blank)
        {
            var sf = stc.ToFrameIndex(fpsBase, timing.Ntsc);
            s = stc.Negative ? total - sf : sf;
        }
        var e = total - 1;
        if (!etc.Blank) e = etc.ToFrameIndex(fpsBase, timing.Ntsc);

        var note = "";
        if (s < 0) return CutPlan.Fail("Start is before the beginning of the video");
        if (s >= total) return CutPlan.Fail($"Start is after the end of the video ({total} frames)");
        if (e >= total)
        {
            e = total - 1;
            note = "End was past the last frame - used the last frame";
        }
        if (e < s) return CutPlan.Fail("End is before Start");

        var fd = (double)timing.Den / timing.Num;
        var t0 = s > 0 ? (s - 0.25) * fd : 0.0;
        var dur = ((e + 0.75) * fd) - t0;
        return new CutPlan(true, "", s, e, t0, dur, e - s + 1, (double)timing.Num / timing.Den, note);
    }
}
