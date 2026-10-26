namespace Videoprep.Core.Engine;

/// <summary>
/// One downloaded video and the jobs (outputs) that come from it (AUDIT M12: the legacy kept these fields in a
/// hashtable updated from five functions). The full file is deleted after the last output only when nothing failed,
/// the user did not ask to keep it, and it did not exist before this run (AUDIT X6).
/// </summary>
public sealed class VideoGroup(int total, bool hasClips)
{
    public int Total { get; } = total;
    public bool HasClips { get; } = hasClips;

    /// <summary>Path of the downloaded file (the "__full" file when there are clips).</summary>
    public string? FullPath { get; private set; }

    /// <summary>Base for output names: the downloaded file name without extension and without "__full".</summary>
    public string Stem { get; private set; } = "";

    /// <summary>The downloaded file was already in the folder before this run (yt-dlp reused it).</summary>
    public bool PreExisting { get; private set; }

    public int Done { get; private set; }
    public bool Failed { get; private set; }
    /// <summary>Something went wrong with an output: keep the full file so the user can redo it.</summary>
    public bool Keep { get; private set; }

    public void SetDownloaded(string path, bool preExisting)
    {
        FullPath = path;
        PreExisting = preExisting;
        var stem = Path.GetFileNameWithoutExtension(path);
        if (stem.EndsWith("__full", StringComparison.Ordinal)) stem = stem[..^"__full".Length];
        Stem = stem;
    }

    public void MarkFailed() => Failed = true;
    public void MarkKeep() => Keep = true;
    public void OutputFinished() => Done++;

    public bool IsLast => Done >= Total;

    /// <summary>True when the full file may be deleted now (call after <see cref="OutputFinished"/>).</summary>
    public bool ShouldDeleteFull(bool keepFullOption) =>
        IsLast && FullPath is not null && !PreExisting && !Keep && !Failed && !(HasClips && keepFullOption);
}
