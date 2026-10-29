namespace Videoprep.Core.Import;

/// <summary>How a link was written in the file: typed as text, or hidden behind other text (a hyperlink).</summary>
public enum LinkHow { Text, Hyperlink }

/// <summary>
/// One link found in a file. <see cref="Context"/> is the hyperlink's visible text, or a piece of the sentence around
/// a typed link ("" when the link was alone on its line).
/// </summary>
public sealed record FoundLink(string Url, string Context, LinkHow How);

/// <summary>Why a file could not be read (the UI explains each one).</summary>
public enum ReadStatus { Ok, NotYet, UnknownType, TooBig, Damaged, CannotOpen }

public sealed record LinkFileResult(ReadStatus Status, IReadOnlyList<FoundLink> Links, string Detail = "")
{
    public static LinkFileResult Fail(ReadStatus status, string detail = "") => new(status, [], detail);
}

/// <summary>
/// Limits for files that come from anywhere (they are untrusted). The text limit applies to what is actually read:
/// the whole file for .txt/.md/.html/.csv, the uncompressed text parts for .docx (images inside are never opened).
/// </summary>
public sealed record ImportLimits
{
    public long MaxTextBytes { get; init; } = 20L * 1024 * 1024;
    public long MaxFileBytes { get; init; } = 200L * 1024 * 1024;
    public int MaxZipEntries { get; init; } = 5000;
    public int MaxLinks { get; init; } = 5000;

    public static ImportLimits Default { get; } = new();
}

/// <summary>Thrown when a file goes over a limit while it is being read (zip bomb, huge text).</summary>
public sealed class ImportLimitException(string message) : Exception(message);
