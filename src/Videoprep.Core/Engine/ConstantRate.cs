using System.Globalization;
using Videoprep.Core.Media;

namespace Videoprep.Core.Engine;

/// <summary>
/// What F8 decided for one downloaded video: it has a variable frame rate and becomes constant at <see cref="Target"/>.
/// <see cref="SourceSeconds"/> is the length of the original video; whole-file output is limited to it (without that limit
/// ffmpeg's constant-rate mode added 5 frames at the end of a test clip and the video outlasted the audio).
/// </summary>
public sealed record CfrPlan(Rational Target, double SourceSeconds, FrameRateAnalysis Analysis, StreamPackets? Source = null)
{
    public double Fps => Target.Value;
}

/// <summary>Frame times of the video stream and the ends of the audio stream of a finished file (for the F8 checks).</summary>
public sealed record StreamPackets(IReadOnlyList<double> VideoTimes, double? AudioFirst, double? AudioEnd)
{
    public int VideoCount => VideoTimes.Count;
    public double VideoFirst => VideoTimes.Count > 0 ? VideoTimes.Min() : 0;
    public double VideoLast => VideoTimes.Count > 0 ? VideoTimes.Max() : 0;
}

/// <summary>Reads packet times with ffprobe, without decoding anything.</summary>
public static class PacketProbe
{
    public const int StartWindow = 600;
    public const int MiddleWindow = 300;

    private static string Sec(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary><c>START%+#N</c>: N packets from START seconds (a start of 0 reads from the beginning).</summary>
    public static IReadOnlyList<string> WindowArgs(string file, double startSeconds, int packets) =>
        ["-v", "error", "-select_streams", "v:0", "-read_intervals", $"{Sec(startSeconds)}%+#{packets}",
         "-show_entries", "packet=pts_time", "-of", "csv=p=0", file];

    public static IReadOnlyList<string> AllArgs(string file, string stream) =>
        ["-v", "error", "-select_streams", stream, "-show_entries", "packet=pts_time,duration_time", "-of", "csv=p=0", file];

    /// <summary>"0.033000,0.033000" or "0.033000," or noise: the time and the duration when they are numbers.</summary>
    public static (double? Time, double? Duration) ParseLine(string line)
    {
        var parts = line.Split(',');
        double? Num(int i) => i < parts.Length && double.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
        return (Num(0), Num(1));
    }

    private static async Task<List<(double Time, double? Duration)>> ReadAsync(IProcessRunner runner, string ffprobe, IReadOnlyList<string> args, CancellationToken ct)
    {
        var rows = new List<(double, double?)>();
        var r = await runner.RunAsync(new ProcessSpec(ffprobe, args), line =>
        {
            var (t, d) = ParseLine(line);
            if (t is { } time) lock (rows) rows.Add((time, d));
        }, ct).ConfigureAwait(false);
        return r.ExitCode == 0 ? rows : [];
    }

    /// <summary>The start of the video and, for a longer one, the middle: separate runs of frame times.</summary>
    public static async Task<IReadOnlyList<IReadOnlyList<double>>> SampleVideoAsync(IProcessRunner runner, string ffprobe, string file,
                                                                                  double? seconds, CancellationToken ct)
    {
        var windows = new List<IReadOnlyList<double>>();
        var start = await ReadAsync(runner, ffprobe, WindowArgs(file, 0, StartWindow), ct).ConfigureAwait(false);
        windows.Add(start.Select(p => p.Time).ToList());
        // A video of more than ~20 s is also looked at in its middle (the start alone may be regular).
        if (seconds is > 20)
        {
            var mid = await ReadAsync(runner, ffprobe, WindowArgs(file, seconds.Value / 2, MiddleWindow), ct).ConfigureAwait(false);
            windows.Add(mid.Select(p => p.Time).ToList());
        }
        return windows;
    }

    /// <summary>Every video frame time and the start and end of the audio, of a finished file.</summary>
    public static async Task<StreamPackets> StatsAsync(IProcessRunner runner, string ffprobe, string file, CancellationToken ct)
    {
        var video = await ReadAsync(runner, ffprobe, AllArgs(file, "v:0"), ct).ConfigureAwait(false);
        var audio = await ReadAsync(runner, ffprobe, AllArgs(file, "a:0"), ct).ConfigureAwait(false);
        double? first = null, end = null;
        if (audio.Count > 0)
        {
            first = audio.Min(a => a.Time);
            var last = audio.MaxBy(a => a.Time);
            end = last.Time + (last.Duration ?? 0);
        }
        return new StreamPackets(video.Select(v => v.Time).ToList(), first, end);
    }
}

/// <summary>
/// The F8 checks on a file made constant, BEFORE it replaces anything: right number of frames, same length (one frame at
/// most), audio still in step with the video as it was in the original, and really constant now. Any failure sends the row
/// to "Revisar" with the original untouched. The audio is compared with the ORIGINAL's audio, not with an ideal: a file that
/// came 50 ms out of step stays 50 ms out of step; what must not happen is that the conversion moves it.
/// </summary>
public static class CfrVerifier
{
    /// <summary>
    /// Where the audio starts, compared with the original: audio codecs start a little early (AAC 21 ms, Opus 7 ms, PCM 0),
    /// so it may differ by this much or one frame.
    /// </summary>
    public const double AudioSlack = 0.03;

    /// <summary>
    /// How long the audio is, compared with the original: each codec adds its own start and its own padding at the end
    /// (AAC from the original, PCM or Opus in the result: up to ~45 ms in all), so this is wider. More than this is a real shift.
    /// </summary>
    public const double AudioLengthSlack = 0.06;

    public static VerifyResult Check(StreamPackets s, CfrPlan cfr, CutPlan? plan)
    {
        var rate = cfr.Fps;
        var frame = 1 / rate;
        if (s.VideoCount < 1) return new(false, "the result has no video frames");

        // 1. Number of frames: exact for a clip, the expected one (one more or less) for a whole video.
        long expected = plan is not null ? plan.Frames : (long)Math.Round(cfr.SourceSeconds * rate);
        var slack = plan is not null ? 0 : 1;
        if (Math.Abs(s.VideoCount - expected) > slack)
            return new(false, $"the result has {s.VideoCount} frames, expected {expected}");

        // 2. Length: at most one frame different from the original (whole video) or from the planned frames (clip).
        var videoSeconds = s.VideoLast - s.VideoFirst + frame;
        var expectedSeconds = plan is not null ? plan.Frames / rate : cfr.SourceSeconds;
        if (Math.Abs(videoSeconds - expectedSeconds) > frame + 0.001)
            return new(false, string.Create(CultureInfo.InvariantCulture,
                $"the video lasts {videoSeconds:0.###} s, expected {expectedSeconds:0.###} s (more than one frame of difference)"));

        // 3. Audio still in step: it starts where it did relative to the video, and it is as long as it was.
        var audio = CheckAudio(s, cfr, plan, frame);
        if (!audio.Ok) return audio;

        // 4. It is constant now: every interval between frames is the frame time (a rounding of 1 ms is not a difference).
        var times = s.VideoTimes.Order().ToArray();
        var intervals = Math.Max(0, times.Length - 1);
        var tolerance = VfrDetector.Tolerance(frame);
        var wrong = 0;
        for (var i = 0; i < intervals; i++)
            if (Math.Abs(times[i + 1] - times[i] - frame) > tolerance) wrong++;
        if (wrong > Math.Max(1, intervals / 500))
            return new(false, $"the result still has an irregular frame rate ({wrong} of {intervals} intervals)");
        return VerifyResult.Good;
    }

    private static VerifyResult CheckAudio(StreamPackets s, CfrPlan cfr, CutPlan? plan, double frame)
    {
        var src = cfr.Source;
        if (s.AudioFirst is not { } af || s.AudioEnd is not { } ae)
        {
            // No audio in the result: only wrong if the original had some.
            return src?.AudioFirst is not null ? new(false, "the result has no audio, the original had") : VerifyResult.Good;
        }
        var tol = Math.Max(frame, AudioSlack);
        var outSeconds = ae - af;

        if (src?.AudioFirst is { } sf && src.AudioEnd is { } se)
        {
            // Start: the gap between audio and video start must be the one the original had.
            var srcGap = sf - src.VideoFirst;
            var outGap = af - s.VideoFirst;
            if (plan is null && Math.Abs(outGap - srcGap) > tol)
                return new(false, string.Create(CultureInfo.InvariantCulture,
                    $"audio and video are out of step: the audio starts {outGap - srcGap:+0.###;-0.###} s away from where it did in the original"));

            // Length: as long as the original audio (a whole video) or as long as the clip needs (a clip, unless the audio ran out).
            double expectedAudio;
            if (plan is null) expectedAudio = se - sf;
            else expectedAudio = Math.Max(0, Math.Min(plan.T0 + plan.Duration, se) - Math.Max(plan.T0, sf));
            if (Math.Abs(outSeconds - expectedAudio) > Math.Max(frame, AudioLengthSlack))
                return new(false, string.Create(CultureInfo.InvariantCulture,
                    $"the audio lasts {outSeconds:0.###} s, expected {expectedAudio:0.###} s (audio and video are out of step)"));
        }
        else
        {
            // The original's audio could not be measured: audio and video must at least start and end together.
            var videoEnd = s.VideoLast + frame;
            if (Math.Abs(af - s.VideoFirst) > tol || Math.Abs(ae - videoEnd) > tol)
                return new(false, string.Create(CultureInfo.InvariantCulture,
                    $"audio and video are out of step (video {s.VideoFirst:0.###}-{videoEnd:0.###} s, audio {af:0.###}-{ae:0.###} s)"));
        }
        return VerifyResult.Good;
    }
}
