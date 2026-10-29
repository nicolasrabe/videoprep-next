namespace Videoprep.Core.Update;

/// <summary>Looks for a newer release and, when asked, downloads it, checks its SHA-256 and unpacks it. Never installs anything.</summary>
public sealed class UpdateChecker(UpdateClient client)
{
    /// <summary>At most once a day (a failed check does not count, the next start tries again).</summary>
    public static bool IsDue(string lastCheck, DateTimeOffset now) =>
        !DateTimeOffset.TryParse(lastCheck, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var last)
        || last > now || (now - last).TotalHours >= 24;

    public async Task<ReleaseInfo?> CheckAsync(SemVer current, string rid, CancellationToken ct)
    {
        var json = await client.GetStringAsync(UpdateEndpoint.ReleasesApi, UpdateClient.MaxListBytes, ct).ConfigureAwait(false);
        try { return ReleaseFeed.PickNewer(json, current, rid); }
        catch (System.Text.Json.JsonException ex) { throw new UpdateException(UpdateFailure.Network, "the list of releases is not valid JSON: " + ex.Message); }
    }
}

public sealed class UpdateStager(UpdateClient client, string updatesDir, Func<string, string?>? versionOf = null)
{
    public const string ExeName = "videoprep.exe";

    /// <summary>The folder of one version inside the updates folder (only ever deleted from here).</summary>
    public string FolderFor(SemVer version) => Path.Combine(updatesDir, version.ToString());

    public async Task<StagedUpdate> PrepareAsync(ReleaseInfo release, IProgress<double>? progress, CancellationToken ct)
    {
        if (release.Zip is null) throw new UpdateException(UpdateFailure.NoInstaller, $"release {release.Tag} has no zip for this system");
        if (release.Sums is null) throw new UpdateException(UpdateFailure.NoChecksum, $"release {release.Tag} has no {UpdateEndpoint.SumsFileName}");

        var sumsText = await client.GetStringAsync(release.Sums.Url, UpdateClient.MaxSumsBytes, ct).ConfigureAwait(false);
        if (!Sha256Sums.Parse(sumsText).TryGetValue(release.Zip.Name, out var expected))
            throw new UpdateException(UpdateFailure.NoChecksum, $"{UpdateEndpoint.SumsFileName} does not list {release.Zip.Name}");

        var dir = FolderFor(release.Version);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        var zipPath = Path.Combine(dir, release.Zip.Name);
        try
        {
            await using (var file = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                await client.DownloadAsync(release.Zip.Url, file, UpdateClient.MaxZipBytes, progress, ct).ConfigureAwait(false);

            var actual = Sha256Sums.HashFile(zipPath);
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
                throw new UpdateException(UpdateFailure.ChecksumMismatch, $"{release.Zip.Name}: SHA-256 is {actual}, {UpdateEndpoint.SumsFileName} says {expected}");

            var exe = Path.Combine(dir, ExeName);
            SafeZip.ExtractOnly(zipPath, ExeName, exe, UpdateClient.MaxZipBytes);
            ExeInspector.Check(exe, release.Version, versionOf);
            File.Delete(zipPath);
            return new StagedUpdate(release.Version, exe, Sha256Sums.HashFile(exe));
        }
        catch
        {
            try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    /// <summary>Removes what earlier updates left (everything but the folder of <paramref name="keep"/>, if any).</summary>
    public void CleanOld(SemVer? keep = null)
    {
        if (!Directory.Exists(updatesDir)) return;
        foreach (var d in Directory.GetDirectories(updatesDir))
        {
            if (keep is { } k && string.Equals(Path.GetFileName(d), k.ToString(), StringComparison.Ordinal)) continue;
            try { Directory.Delete(d, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
