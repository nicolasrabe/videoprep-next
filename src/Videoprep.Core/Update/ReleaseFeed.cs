using System.Runtime.InteropServices;
using System.Text.Json;

namespace Videoprep.Core.Update;

/// <summary>Reads GitHub's list of releases and picks the newer one (if any) for this system.</summary>
public static class ReleaseFeed
{
    public const int MaxReleases = 30;

    /// <summary>"win-x64", "osx-arm64"... The names of the zips in a release (release.yml).</summary>
    public static string CurrentRid() =>
        OperatingSystem.IsWindows() ? "win-x64"
        : OperatingSystem.IsMacOS() ? (RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64")
        : "linux-x64";

    /// <param name="includePrereleases">Null = while the installed version is 0.x (everything shipped so far is a pre-release), yes.</param>
    public static ReleaseInfo? PickNewer(string json, SemVer current, string rid, bool? includePrereleases = null)
    {
        var withPre = includePrereleases ?? current.Major == 0;
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
        ReleaseInfo? best = null;
        foreach (var r in doc.RootElement.EnumerateArray().Take(MaxReleases))
        {
            if (r.ValueKind != JsonValueKind.Object || Bool(r, "draft")) continue;
            var tag = Str(r, "tag_name");
            if (!SemVer.TryParse(tag, out var version)) continue;
            var pre = Bool(r, "prerelease") || version.Pre.Length > 0;
            if (pre && !withPre) continue;
            if (!version.IsNewerThan(current) || best is not null && !version.IsNewerThan(best.Version)) continue;

            var names = new Dictionary<string, long>(StringComparer.Ordinal);
            if (r.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                foreach (var a in assets.EnumerateArray())
                    if (Str(a, "name") is { Length: > 0 } n) names[n] = a.TryGetProperty("size", out var sz) && sz.TryGetInt64(out var sv) ? sv : 0;

            var zipName = $"videoprep-{tag}-{rid}.zip";
            // The addresses are built from the tag, not copied from the list, so a list cannot point the download elsewhere.
            var zip = names.TryGetValue(zipName, out var zs) ? new ReleaseAsset(zipName, UpdateEndpoint.AssetUrl(tag!, zipName), zs) : null;
            var sums = names.TryGetValue(UpdateEndpoint.SumsFileName, out var ss)
                ? new ReleaseAsset(UpdateEndpoint.SumsFileName, UpdateEndpoint.AssetUrl(tag!, UpdateEndpoint.SumsFileName), ss) : null;
            var page = Uri.TryCreate(Str(r, "html_url"), UriKind.Absolute, out var p) && UpdateEndpoint.IsAllowed(p) ? p : UpdateEndpoint.ReleasesPage;
            var notes = Str(r, "body") ?? "";
            best = new ReleaseInfo(version, tag!, pre, page, notes.Length > 4000 ? notes[..4000] : notes, zip, sums);
        }
        return best;
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
