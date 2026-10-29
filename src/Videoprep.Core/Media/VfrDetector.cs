using System.Globalization;

namespace Videoprep.Core.Media;

/// <summary>The standard frame rates a variable-rate video can be turned into (F8).</summary>
public static class FrameRates
{
    /// <summary>23.976, 24, 25, 29.97, 30, 50, 59.94, 60; the NTSC ones as exact fractions.</summary>
    public static readonly IReadOnlyList<Rational> Standard =
    [
        new(24000, 1001), new(24, 1), new(25, 1), new(30000, 1001), new(30, 1), new(50, 1), new(60000, 1001), new(60, 1),
    ];

    /// <summary>The standard rate nearest to <paramref name="fps"/> (nearest in ratio, so 31 snaps to 30 and 27 to 25).</summary>
    public static Rational Snap(double fps)
    {
        if (fps <= 0 || double.IsNaN(fps)) return new Rational(30, 1);
        return Standard.MinBy(r => Math.Abs(Math.Log(fps / r.Value)));
    }

    /// <summary>"29.97", "30": for the log and the queue.</summary>
    public static string Label(Rational r) => r.Value.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>The value ffmpeg's <c>-r</c> takes: "30000/1001" or "30".</summary>
    public static string ForFfmpeg(Rational r) => r.Den == 1 ? r.Num.ToString(CultureInfo.InvariantCulture) : r.ToString();
}

/// <summary>What the frame-rate check found in a downloaded video (F8).</summary>
public sealed record FrameRateAnalysis(bool IsVariable, string Reason, double AverageFps, double NominalFps,
                                       int Intervals, int Irregular, Rational Target)
{
    /// <summary>English line for the log.</summary>
    public string LogText => IsVariable
        ? string.Create(CultureInfo.InvariantCulture,
            $"frame rate: VARIABLE ({Reason}) -> will be made constant at {FrameRates.Label(Target)} fps (the video is re-encoded)")
        : $"frame rate: constant ({Reason}) -> left as it is";
}

/// <summary>
/// Decides whether a video really has a variable frame rate (F8: only those are ever touched). It looks at the time of
/// the frames: in a constant-rate file every interval between frames is the same (give or take the container's rounding,
/// 1 ms for MKV/WebM); in a variable one some intervals are twice, three times... as long. ffprobe's two frame rates
/// (<c>r_frame_rate</c> and <c>avg_frame_rate</c>) back it up but never decide alone.
/// </summary>
public static class VfrDetector
{
    /// <summary>Fewer frames than this cannot tell: the video is left alone.</summary>
    public const int MinFrames = 12;

    /// <summary>An interval differs from the usual one by more than this: container rounding (1 ms) or 2 %, whichever is bigger.</summary>
    public static double Tolerance(double usualInterval) => Math.Max(0.0015, 0.02 * usualInterval);

    /// <summary>Intervals and irregular intervals of one run of frame times (any order).</summary>
    public static (int Intervals, int Irregular, double Usual) Irregularity(IReadOnlyList<double> times)
    {
        if (times.Count < 2) return (0, 0, 0);
        var t = times.Order().ToArray();
        var deltas = new double[t.Length - 1];
        for (var i = 0; i < deltas.Length; i++) deltas[i] = t[i + 1] - t[i];
        var usual = deltas.Order().ElementAt(deltas.Length / 2);   // the median
        var tol = Tolerance(usual);
        return (deltas.Length, deltas.Count(d => Math.Abs(d - usual) > tol), usual);
    }

    /// <param name="windows">Frame times of the samples taken (the start of the video and the middle), each a separate run.</param>
    public static FrameRateAnalysis Analyze(MediaInfo info, IReadOnlyList<IReadOnlyList<double>> windows)
    {
        var nominal = info.Video?.RFrameRate.Value ?? 0;
        var average = info.Video?.AvgFrameRate.Value ?? 0;
        var frames = windows.Sum(w => w.Count);
        var intervals = 0;
        var irregular = 0;
        var longest = windows.OrderByDescending(w => w.Count).FirstOrDefault();
        foreach (var w in windows)
        {
            var (n, bad, _) = Irregularity(w);
            intervals += n;
            irregular += bad;
        }
        if (average <= 0 && longest is { Count: >= 2 })
            average = (longest.Count - 1) / (longest.Max() - longest.Min());
        var target = FrameRates.Snap(average > 0 ? average : nominal);
        // A screen recording that stands still for seconds averages far below its real speed: a video that says it is 30 fps or
        // slower never needed more, so keep that speed (nothing is dropped) instead of the average's nearest one.
        if (nominal is > 0 and <= 30.01 && nominal > target.Value && FrameRates.Snap(nominal).Value is var snapped && Math.Abs(snapped - nominal) < 0.01)
            target = FrameRates.Snap(nominal);

        if (frames < MinFrames)
            return new FrameRateAnalysis(false, $"only {frames} frames to look at", average, nominal, intervals, irregular, target);

        // Variable: at least 3 irregular intervals and 1 % of them, or the two ffprobe rates disagree and at least one is irregular.
        var variable = (irregular >= 3 && irregular >= 0.01 * intervals) || (info.LooksVariableFrameRate && irregular >= 1);
        var reason = string.Create(CultureInfo.InvariantCulture,
            $"{irregular} of {intervals} intervals between frames are irregular; average {average:0.##} fps, nominal {nominal:0.##} fps");
        return new FrameRateAnalysis(variable, reason, average, nominal, intervals, irregular, target);
    }
}
