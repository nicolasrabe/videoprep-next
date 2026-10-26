using System.Globalization;
using Videoprep.Core.Formats;
using Videoprep.Core.Media;

namespace Videoprep.Core.Engine;

/// <summary>Result of checking an ffmpeg output before it replaces anything (AUDIT H3).</summary>
public sealed record VerifyResult(bool Ok, string Reason)
{
    public static readonly VerifyResult Good = new(true, "");
}

/// <summary>
/// Checks a freshly written file with ffprobe data: it has the streams the format needs and the length the plan asked for.
/// Only after this passes is the file moved to its final name, and only after that is the source deleted.
/// </summary>
public static class OutputVerifier
{
    /// <param name="plan">The clip plan, or null for a whole-file conversion.</param>
    /// <param name="sourceSeconds">Length of the source, used for whole-file conversions.</param>
    public static VerifyResult Check(MediaInfo info, MediaKind kind, CutPlan? plan, double? sourceSeconds, bool sourceHadAudio)
    {
        if (kind == MediaKind.Video && info.Video is null) return new(false, "the result has no video stream");
        if (info.Audio is null && (kind == MediaKind.Audio || sourceHadAudio)) return new(false, "the result has no audio stream");

        var actual = kind == MediaKind.Video ? info.Video!.Duration ?? info.FormatDuration : info.FormatDuration;
        if (actual is null) return new(false, "could not read the length of the result");

        double expected, tolerance;
        if (plan is not null)
        {
            expected = plan.Frames / plan.Fps;
            // Two frames or a tenth of a second, whichever is larger: containers round timestamps.
            tolerance = Math.Max(2.0 / plan.Fps, 0.1);
            // For video the frame count is exact when ffprobe knows it.
            if (kind == MediaKind.Video && info.Video!.FrameCount is { } n && n != plan.Frames)
                return new(false, $"the result has {n} frames, expected {plan.Frames}");
        }
        else if (sourceSeconds is { } src)
        {
            expected = src;
            tolerance = Math.Max(0.5, src * 0.01);
        }
        else
        {
            return VerifyResult.Good;
        }
        if (Math.Abs(actual.Value - expected) > tolerance)
            return new(false, string.Create(CultureInfo.InvariantCulture,
                $"the result lasts {actual.Value:0.###} s, expected {expected:0.###} s"));
        return VerifyResult.Good;
    }
}
