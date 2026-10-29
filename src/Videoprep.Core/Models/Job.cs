using Videoprep.Core.Formats;

namespace Videoprep.Core.Models;

/// <summary>Row status codes; the UI translates them with the "rs_" + code i18n keys.</summary>
public enum JobStatus { Queued, Downloading, Cutting, OK, WARN, REVIEW, ERROR, Skipped }

/// <summary>
/// One output file: the whole video, or one clip of it (port of the jobs built in Start-Batch).
/// Only the first job of a group (<see cref="Download"/> = true) downloads; the rest reuse the downloaded file.
/// </summary>
public sealed record Job
{
    public required int GroupId { get; init; }
    public required int Index { get; init; }
    public required int Count { get; init; }
    public string Key => $"{GroupId}|{Index}";
    public required string Url { get; init; }
    public required Site Site { get; init; }
    public required string Format { get; init; }
    public MediaKind Kind => FormatCatalog.Get(Format).Kind;
    public required int Cap { get; init; }
    /// <summary>Series or custom name; null = use the video title.</summary>
    public string? Label { get; init; }
    public string Start { get; init; } = "";
    public string End { get; init; } = "";
    public bool IsRange => Start.Length > 0 || End.Length > 0;
    public bool Download { get; init; }
    public double? Fps { get; init; }
    /// <summary>Folder inside the batch folder ("" = the batch folder itself).</summary>
    public string SubFolder { get; init; } = "";
}
