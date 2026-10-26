using Videoprep.Core.Models;

namespace Videoprep.Core.Engine;

/// <summary>What the engine is doing with the current job (the UI shows "downloading" / "processing").</summary>
public enum EngineStep { Starting, Downloading, Processing, Verifying }

/// <summary>Everything the engine reports while it runs. The UI and the --demo command listen to these.</summary>
public abstract record EngineEvent;

/// <summary>
/// Job <paramref name="Number"/> of <paramref name="Total"/> (1-based) moved to a new step.
/// <paramref name="Plan"/> is set when a clip is being cut (frames and fps, for the demo and the UI).
/// </summary>
public sealed record StepEvent(Job Job, int Number, int Total, EngineStep Step, string Detail = "", Media.CutPlan? Plan = null) : EngineEvent;

public sealed record ProgressEvent(Job Job, int Number, int Total, ProgressLine Progress) : EngineEvent;

/// <summary>A row of the queue changed status; <paramref name="File"/> is the output file name when there is one.</summary>
public sealed record StatusEvent(Job Job, JobStatus Status, string File = "") : EngineEvent;

/// <summary>A clip was checked: frames and seconds expected vs. found (for the demo and the log).</summary>
public sealed record VerifiedEvent(Job Job, string File, long? Frames, double? Seconds, bool Ok, string Reason) : EngineEvent;

/// <summary>Final numbers of a batch (port of the counters of Complete-Batch).</summary>
public sealed record BatchResult(int Ok, int Warn, int Fail, IReadOnlyList<string> FailedItems, bool Cancelled,
                                 bool Blocked, IReadOnlyDictionary<string, JobStatus> Statuses,
                                 IReadOnlyDictionary<string, string> Files);

/// <summary>Fixed inputs of one batch.</summary>
public sealed record EngineContext
{
    public required ToolPaths Tools { get; init; }
    public required DownloadOptions Options { get; init; }
    public required string Folder { get; init; }
    /// <summary>Cookie sources to try in order (see <see cref="CookieStrategy.Chain"/>).</summary>
    public required IReadOnlyList<CookieSource> Cookies { get; init; }
    /// <summary>Global "already downloaded" archive (used when the option is on).</summary>
    public string? ArchiveFile { get; init; }
    /// <summary>Subscriptions pass their own archive and always use it.</summary>
    public bool ForceArchive { get; init; }
    /// <summary>
    /// Session cookie copy (AUDIT M5): when a browser is read, yt-dlp also writes its cookies here, and the next videos
    /// try this file first so the browser database is not read on every call. Deleted when videoprep closes.
    /// </summary>
    public string? SessionCookieFile { get; init; }
    /// <summary>Stop the whole batch at the first bot check / HTTP 429 (subscriptions). The UI keeps going.</summary>
    public bool StopWhenBlocked { get; init; }
}
