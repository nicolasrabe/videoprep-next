namespace Videoprep.Core.Formats;

public enum MediaKind { Video, Audio }

/// <summary>Expected codecs after a job, checked with ffprobe (port of Get-ExpectedCodecs).</summary>
public sealed record ExpectedCodecs(string Video, string Audio);

/// <summary>One output format: key (also used in settings/subscriptions), label, extension, kind.</summary>
public sealed record OutputFormat(string Key, string Label, string Extension, MediaKind Kind, ExpectedCodecs? Expected)
{
    /// <summary>Formats that must be H.264 from YouTube (cap 1080p, strict selector): MP4 and MOV H.264.</summary>
    public bool IsH264 => Key is "mp4" or "movh";

    /// <summary>i18n key of the one-line hint under the format box.</summary>
    public string HintKey => "h_" + Key;
}

/// <summary>Formats and resolutions from the legacy script ($FormatKeys, $FormatLabels, $FormatExt, $ResMap).</summary>
public static class FormatCatalog
{
    private static readonly ExpectedCodecs H264Aac = new("h264", "aac");

    public static IReadOnlyList<OutputFormat> All { get; } =
    [
        new("mp4", "MP4 - H.264 + AAC", ".mp4", MediaKind.Video, H264Aac),
        new("movh", "MOV - H.264 + AAC", ".mov", MediaKind.Video, H264Aac),
        new("movp", "MOV - ProRes 422 HQ", ".mov", MediaKind.Video, new("prores", "pcm_s24le")),
        new("webm", "WebM - VP9 + Opus", ".webm", MediaKind.Video, new("vp9", "opus")),
        new("mkv", "MKV", ".mkv", MediaKind.Video, null),
        new("m4a", "M4A (AAC)", ".m4a", MediaKind.Audio, null),
        new("mp3", "MP3", ".mp3", MediaKind.Audio, null),
        new("wav", "WAV", ".wav", MediaKind.Audio, null),
        new("flac", "FLAC", ".flac", MediaKind.Audio, null),
        new("opus", "Opus", ".opus", MediaKind.Audio, null),
    ];

    private static readonly Dictionary<string, OutputFormat> ByKey = All.ToDictionary(f => f.Key);

    /// <summary>Unknown keys fall back to MP4, like Get-ExtFor.</summary>
    public static OutputFormat Get(string? key) => key is not null && ByKey.TryGetValue(key, out var f) ? f : ByKey["mp4"];

    public static bool IsKnown(string? key) => key is not null && ByKey.ContainsKey(key);

    public static IEnumerable<OutputFormat> OfKind(MediaKind kind) => All.Where(f => f.Kind == kind);

    /// <summary>Resolution labels and their height cap, as in $ResMap.</summary>
    public static IReadOnlyDictionary<string, int> Resolutions { get; } = new Dictionary<string, int>
    {
        ["4K"] = 2160, ["1440p"] = 1440, ["1080p"] = 1080, ["720p"] = 720, ["480p"] = 480,
    };

    public const int DefaultCap = 1080;

    /// <summary>Port of Update-ResList: H.264 (YouTube max 1080p) and audio get 1080p/720p/480p, the rest up to 4K.</summary>
    public static IReadOnlyList<string> ResolutionChoices(string formatKey)
    {
        var f = Get(formatKey);
        return f.IsH264 || f.Kind == MediaKind.Audio
            ? ["1080p", "720p", "480p"]
            : ["4K", "1440p", "1080p", "720p"];
    }

    public static string ResolutionLabel(int cap) =>
        Resolutions.FirstOrDefault(kv => kv.Value == cap).Key ?? "1080p";
}
