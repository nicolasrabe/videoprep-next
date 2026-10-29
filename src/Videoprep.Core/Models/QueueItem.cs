using System.Text.Json.Serialization;
using Videoprep.Core.Formats;
using Videoprep.Core.Text;

namespace Videoprep.Core.Models;

public enum NamingMode { Original, Custom, Series }

public enum Site { Youtube, Other }

/// <summary>One clip typed by the user (timecode text as typed; empty End = until the end).</summary>
public sealed record ClipRange(string Start, string End);

/// <summary>
/// One link in the queue with its choices (port of New-Item-Object). Clips empty = whole video.
/// <see cref="Fps"/> comes from the preview; null = not known yet (AUDIT X4).
/// </summary>
public sealed record QueueItem
{
    public int Id { get; init; }
    public required string Url { get; init; }
    [JsonIgnore] public string VideoKey => UrlParser.LinkKey(Url);
    [JsonIgnore] public Site Site => UrlParser.IsYoutubeKey(VideoKey) ? Site.Youtube : Site.Other;
    public string Format { get; init; } = "mp4";
    [JsonIgnore] public MediaKind Kind => FormatCatalog.Get(Format).Kind;
    public int Cap { get; init; } = FormatCatalog.DefaultCap;
    public NamingMode Naming { get; init; } = NamingMode.Original;
    public string Custom { get; init; } = "";
    public string Prefix { get; init; } = "Video";
    public IReadOnlyList<ClipRange> Clips { get; init; } = [];
    public double? Fps { get; init; }
    /// <summary>Folder inside the destination ("" = the destination itself). Set by "Guardar en una subcarpeta" (F1).</summary>
    public string SubFolder { get; init; } = "";
    /// <summary>F8: make the video constant-rate (only a video that really is variable is touched). Default off.</summary>
    public bool ConstantRate { get; init; }
}
