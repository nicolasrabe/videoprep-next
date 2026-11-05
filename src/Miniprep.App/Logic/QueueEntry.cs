using System.Text.Json.Serialization;
using Videoprep.Core.Formats;
using Videoprep.Core.Models;

namespace Miniprep.App.Logic;

public enum EntryStatus { Queued, Downloading, Done, Error }

/// <summary>The quality choices of the row menu. "Best" is the best the format offers: H.264 for Premiere stops at 1080p.</summary>
public enum Quality { Best, P1080, P720, P480 }

public static class Qualities
{
    public static int Cap(Quality q) => q switch { Quality.P720 => 720, Quality.P480 => 480, _ => FormatCatalog.DefaultCap };

    public static string Command(Quality q) => q switch { Quality.Best => "best", Quality.P1080 => "1080", Quality.P720 => "720", _ => "480" };

    public static Quality? FromCommand(string c) => c switch { "best" => Quality.Best, "1080" => Quality.P1080, "720" => Quality.P720, "480" => Quality.P480, _ => null };

    public static string Label(Quality q) => q switch { Quality.P1080 => "1080p", Quality.P720 => "720p", Quality.P480 => "480p", _ => "" };
}

/// <summary>One row of the queue: the shared <see cref="QueueItem"/> plus what miniprep adds (state, quality, error, file).</summary>
public sealed record QueueEntry
{
    public required QueueItem Item { get; init; }
    public EntryStatus Status { get; init; } = EntryStatus.Queued;
    public Quality Quality { get; init; } = Quality.P1080;
    /// <summary>Why the last attempt failed (English, from the engine). Empty when it did not.</summary>
    public string Error { get; init; } = "";
    /// <summary>The finished file (full path), when there is one.</summary>
    public string File { get; init; } = "";

    [JsonIgnore] public int Id => Item.Id;
    [JsonIgnore] public bool IsAudio => Item.Kind == MediaKind.Audio;
    /// <summary>The name the file will have: the chosen one, else the video's title (empty = not known yet).</summary>
    [JsonIgnore] public string FileName => Item.Naming == NamingMode.Custom && Item.Custom.Trim().Length > 0 ? Item.Custom : Item.Title;
}
