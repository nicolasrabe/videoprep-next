namespace Videoprep.Core.Update;

/// <summary>Why an update did not happen. The app turns each into a sentence in the user's language.</summary>
public enum UpdateFailure
{
    Network,
    UnsafeAddress,
    TooBig,
    NoInstaller,
    NoChecksum,
    ChecksumMismatch,
    BadArchive,
    BadExecutable,
    WrongVersion,
    NoWritePermission,
    StillRunning,
    FilesLocked,
    NewVersionDidNotStart,
}

public sealed class UpdateException(UpdateFailure failure, string detail) : Exception(detail)
{
    public UpdateFailure Failure { get; } = failure;
}

public sealed record ReleaseAsset(string Name, Uri Url, long Size);

/// <param name="Zip">The zip for this system; null when the release has none (the user is sent to the page).</param>
/// <param name="Sums">SHA256SUMS.txt of the release; null when missing (nothing is installed without it).</param>
public sealed record ReleaseInfo(SemVer Version, string Tag, bool Prerelease, Uri PageUrl, string Notes, ReleaseAsset? Zip, ReleaseAsset? Sums);

/// <summary>A downloaded, checked and unpacked new version, ready to replace the one that runs.</summary>
public sealed record StagedUpdate(SemVer Version, string ExePath, string Sha256);
