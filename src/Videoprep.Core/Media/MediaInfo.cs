using System.Globalization;
using System.Text.Json;

namespace Videoprep.Core.Media;

/// <summary>A frame rate as ffprobe gives it ("30000/1001").</summary>
public readonly record struct Rational(int Num, int Den)
{
    public bool IsValid => Num > 0 && Den > 0;
    public double Value => IsValid ? (double)Num / Den : 0;

    public static Rational Parse(string? text)
    {
        if (string.IsNullOrEmpty(text)) return default;
        var cut = text.IndexOf('/');
        if (cut < 0) return default;
        return int.TryParse(text[..cut], NumberStyles.None, CultureInfo.InvariantCulture, out var n)
               && int.TryParse(text[(cut + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var d)
            ? new Rational(n, d)
            : default;
    }

    public override string ToString() => $"{Num}/{Den}";
}

public sealed record VideoStreamInfo(string Codec, int Width, int Height, Rational RFrameRate, Rational AvgFrameRate,
                                     long? FrameCount, double? Duration);

public sealed record AudioStreamInfo(string Codec, int SampleRate, int Channels, double? Duration);

/// <summary>
/// Everything videoprep needs from one file, read with a single
/// <c>ffprobe -v error -show_streams -show_format -of json</c> call (AUDIT M2).
/// </summary>
public sealed record MediaInfo(VideoStreamInfo? Video, AudioStreamInfo? Audio, double? FormatDuration)
{
    /// <summary>avg_frame_rate differs from r_frame_rate by more than 1%: probably variable frame rate (AUDIT M4).</summary>
    public bool LooksVariableFrameRate =>
        Video is { } v && v.RFrameRate.IsValid && v.AvgFrameRate.IsValid
        && Math.Abs(v.AvgFrameRate.Value - v.RFrameRate.Value) / v.RFrameRate.Value > 0.01;

    /// <summary>Short text for the log, like the legacy one: "video h264 1920x1080 29.97 fps | audio aac".</summary>
    public string Summary
    {
        get
        {
            var vc = Video?.Codec ?? "";
            var size = Video is { Width: > 0 } v ? $"{v.Width}x{v.Height}" : "";
            var fps = Video is { RFrameRate.IsValid: true } vv
                ? vv.RFrameRate.Value.ToString("0.##", CultureInfo.InvariantCulture) + " fps"
                : "";
            var text = string.Join(' ', new[] { "video", vc, size, fps }.Where(s => s.Length > 0));
            return $"{text} | audio {Audio?.Codec ?? ""}".TrimEnd();
        }
    }

    public static MediaInfo ParseFfprobeJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        VideoStreamInfo? video = null;
        AudioStreamInfo? audio = null;

        if (root.TryGetProperty("streams", out var streams) && streams.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in streams.EnumerateArray())
            {
                var type = Str(s, "codec_type");
                // Cover art in audio files shows up as a video stream; skip it.
                if (type == "video" && video is null && !IsAttachedPicture(s))
                {
                    video = new VideoStreamInfo(Str(s, "codec_name"), Int(s, "width"), Int(s, "height"),
                        Rational.Parse(Str(s, "r_frame_rate")), Rational.Parse(Str(s, "avg_frame_rate")),
                        Long(s, "nb_frames"), Dbl(s, "duration"));
                }
                else if (type == "audio" && audio is null)
                {
                    audio = new AudioStreamInfo(Str(s, "codec_name"), (int)(Long(s, "sample_rate") ?? 0),
                        Int(s, "channels"), Dbl(s, "duration"));
                }
            }
        }
        double? formatDuration = root.TryGetProperty("format", out var fmt) ? Dbl(fmt, "duration") : null;
        return new MediaInfo(video, audio, formatDuration);
    }

    private static bool IsAttachedPicture(JsonElement s) =>
        s.TryGetProperty("disposition", out var d) && d.TryGetProperty("attached_pic", out var a)
        && a.ValueKind == JsonValueKind.Number && a.GetInt32() == 1;

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static int Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 0;

    // ffprobe writes some numbers as strings ("nb_frames": "573", "duration": "19.063").
    private static long? Long(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)) return n;
        if (v.ValueKind == JsonValueKind.String
            && long.TryParse(v.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var p)) return p;
        return null;
    }

    private static double? Dbl(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
        if (v.ValueKind == JsonValueKind.String
            && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var p)) return p;
        return null;
    }
}
