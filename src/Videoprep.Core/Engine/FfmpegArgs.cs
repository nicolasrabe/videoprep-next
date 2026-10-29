using System.Globalization;
using Videoprep.Core.Formats;
using Videoprep.Core.Media;
using Videoprep.Core.Models;

namespace Videoprep.Core.Engine;

/// <summary>
/// ffmpeg arguments for a clip and/or a conversion (port of Get-EncodeArgs).
/// AUDIT M3 (approved in Phase 0): the start is sought on the input side (fast, and exact when re-encoding),
/// with the same T0/Dur math; for video, <c>-frames:v N</c> guards the exact frame count.
/// The phase 3 integration test that counts frames confirms it (else: hybrid seek).
/// </summary>
public static class FfmpegArgs
{
    private static string Sec(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

    /// <param name="cfr">F8: make the video constant-rate in this same pass (the cut, if any, counts on the new frame grid).</param>
    public static IReadOnlyList<string> Encode(Job job, CutPlan? plan, SourceTiming timing, int crf, string src, string tmp,
                                               CfrPlan? cfr = null)
    {
        var isRange = plan is not null;
        var a = new List<string> { "-hide_banner", "-loglevel", "error", "-nostats", "-y" };
        if (isRange) a.AddRange(["-ss", Sec(plan!.T0)]);
        a.AddRange(["-i", src]);
        if (isRange) a.AddRange(["-t", Sec(plan!.Duration)]);
        // Whole video made constant: never longer than the original (ffmpeg's constant-rate mode adds frames at the end).
        else if (cfr is not null && job.Kind == MediaKind.Video) a.AddRange(["-t", Sec(cfr.SourceSeconds)]);

        if (job.Kind == MediaKind.Audio)
        {
            a.AddRange(["-vn", "-map", "0:a:0"]);
            a.AddRange(job.Format switch
            {
                "m4a" => ["-c:a", "aac", "-b:a", "256k"],
                "mp3" => ["-c:a", "libmp3lame", "-b:a", "320k"],
                "wav" => ["-c:a", "pcm_s16le"],
                "flac" => ["-c:a", "flac"],
                "opus" => ["-c:a", "libopus", "-b:a", "160k"],
                _ => (string[])[],
            });
        }
        else
        {
            a.AddRange(["-map", "0:v:0", "-map", "0:a:0?"]);
            if (isRange) a.AddRange(["-frames:v", plan!.Frames.ToString(CultureInfo.InvariantCulture)]);
            // -fps_mode, not the deprecated -vsync. It forces a re-encode of the video (no stream copy below).
            if (cfr is not null) a.AddRange(["-fps_mode", "cfr", "-r", FrameRates.ForFfmpeg(cfr.Target)]);
            switch (job.Format)
            {
                case "movp":
                    a.AddRange(["-c:v", "prores_ks", "-profile:v", "3", "-vendor", "apl0", "-pix_fmt", "yuv422p10le",
                                "-c:a", "pcm_s24le", "-ar", "48000"]);
                    break;
                case "webm":
                    a.AddRange(["-c:v", "libvpx-vp9", "-crf", "24", "-b:v", "0", "-row-mt", "1", "-deadline", "good",
                                "-cpu-used", "2", "-pix_fmt", "yuv420p", "-c:a", "libopus", "-b:a", "192k"]);
                    break;
                default:
                    if (!isRange && cfr is null && timing.VideoCodec == "h264") a.AddRange(["-c:v", "copy"]);
                    else a.AddRange(["-c:v", "libx264", "-preset", "medium", "-crf", crf.ToString(CultureInfo.InvariantCulture),
                                     "-pix_fmt", "yuv420p"]);
                    if (!isRange && timing.AudioCodec == "aac") a.AddRange(["-c:a", "copy"]);
                    else a.AddRange(["-c:a", "aac", "-b:a", "256k"]);
                    break;
            }
            if (job.Format is "mp4" or "movh" or "movp") a.AddRange(["-movflags", "+faststart"]);
            // NTSC rates (30000/1001...) are exact in MP4/MOV only when the track clock is the numerator (30000 ticks a second).
            if (cfr is { Target.Den: 1001 } && job.Format is "mp4" or "movh" or "movp")
                a.AddRange(["-video_track_timescale", cfr.Target.Num.ToString(CultureInfo.InvariantCulture)]);
        }
        a.Add(tmp);
        return a;
    }

    /// <summary>One ffprobe call that gives everything (AUDIT M2), parsed by <see cref="MediaInfo.ParseFfprobeJson"/>.</summary>
    public static IReadOnlyList<string> Probe(string path) =>
        ["-v", "error", "-show_streams", "-show_format", "-of", "json", path];
}
