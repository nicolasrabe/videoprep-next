namespace Videoprep.Core.Media;

/// <summary>
/// Frame rate and length (in frames) of a downloaded file, used to turn timecodes into frames.
/// Port of Get-SourceTiming (video) and Get-AudioTiming (audio only, using the video's fps for the timecodes).
/// Rounding is banker's rounding (Math.Round default), the same as PowerShell [math]::Round in the legacy script.
/// </summary>
public sealed record SourceTiming(bool Ok, string Error, int Num, int Den, int FpsBase, bool Ntsc, long TotalFrames,
                                  string VideoCodec, string AudioCodec, bool VariableFrameRate)
{
    public double Fps => Den > 0 ? (double)Num / Den : 0;

    private static SourceTiming Fail(string error, string vcodec = "", string acodec = "") =>
        new(false, error, 0, 1, 0, false, 0, vcodec, acodec, false);

    public static SourceTiming FromVideo(MediaInfo info)
    {
        var v = info.Video;
        var acodec = info.Audio?.Codec ?? "";
        if (v is null) return Fail("could not read the frame rate", "", acodec);
        var rate = v.RFrameRate;
        if (!rate.IsValid) return Fail("could not read the frame rate", v.Codec, acodec);

        var fpsBase = (int)Math.Round((double)rate.Num / rate.Den);
        long total;
        if (v.FrameCount is { } n)
        {
            total = n;
        }
        else
        {
            var dur = v.Duration ?? info.FormatDuration;
            if (dur is null) return Fail("could not read the video length", v.Codec, acodec);
            total = (long)Math.Round(dur.Value * rate.Num / rate.Den);
        }
        if (total < 1) return Fail("the video has no frames", v.Codec, acodec);
        return new SourceTiming(true, "", rate.Num, rate.Den, fpsBase, rate.Den == 1001, total, v.Codec, acodec,
                                info.LooksVariableFrameRate);
    }

    /// <summary>
    /// Audio-only file: timecodes are typed against the video's frame rate (from the preview).
    /// <paramref name="fps"/> null or 0 means unknown; 30 is used and <see cref="FpsWasGuessed"/> tells the caller (AUDIT X4).
    /// </summary>
    public static SourceTiming FromAudio(MediaInfo info, double? fps)
    {
        var f = fps is > 0 ? fps.Value : 30.0;
        int num, den = 1, fpsBase;
        var ntsc = false;
        if (Math.Abs(f - 29.97) < 0.02) { num = 30000; den = 1001; fpsBase = 30; ntsc = true; }
        else if (Math.Abs(f - 59.94) < 0.02) { num = 60000; den = 1001; fpsBase = 60; ntsc = true; }
        else if (Math.Abs(f - 23.976) < 0.02) { num = 24000; den = 1001; fpsBase = 24; ntsc = true; }
        else { num = (int)Math.Round(f); fpsBase = num; }

        var acodec = info.Audio?.Codec ?? "";
        var dur = info.FormatDuration;
        if (dur is null) return Fail("could not read the audio length", "", acodec);
        var total = (long)Math.Floor(dur.Value * num / den);
        if (total < 1) return Fail("the file has no length", "", acodec);
        return new SourceTiming(true, "", num, den, fpsBase, ntsc, total, "", acodec, false);
    }

    public static bool FpsWasGuessed(double? fps) => fps is not > 0;
}
