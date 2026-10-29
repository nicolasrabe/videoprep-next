namespace Videoprep.Core.Update;

/// <summary>
/// The one place that says where updates come from. Only HTTPS, only GitHub's own hosts, no user name or port in the address;
/// every address the program is about to visit (and every redirect) goes through <see cref="IsAllowed"/>.
/// </summary>
public static class UpdateEndpoint
{
    /// <summary>The repository the releases are read from. Changing it is the only edit needed to point updates somewhere else.</summary>
    public const string Repository = "nicolasrabe/videoprep-next";

    public const string SumsFileName = "SHA256SUMS.txt";

    private static readonly string[] ExactHosts = ["github.com", "api.github.com"];

    public static Uri ReleasesApi => Base is { } b ? new Uri(b, "releases") : new Uri($"https://api.github.com/repos/{Repository}/releases?per_page=30");

    /// <summary>The page people can open to read the notes or download by hand.</summary>
    public static Uri ReleasesPage => Base ?? new Uri($"https://github.com/{Repository}/releases");

    /// <summary>The address of a file of a release. Built here from the tag, never taken from the downloaded list.</summary>
    public static Uri AssetUrl(string tag, string fileName) =>
        Base is { } b
            ? new Uri(b, $"download/{Uri.EscapeDataString(tag)}/{Uri.EscapeDataString(fileName)}")
            : new Uri($"https://github.com/{Repository}/releases/download/{Uri.EscapeDataString(tag)}/{Uri.EscapeDataString(fileName)}");

    public static bool IsAllowed(Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri) return false;
        if (DebugAllowed(uri)) return true;
        if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length > 0) return false;
        var host = uri.IdnHost.ToLowerInvariant();
        return ExactHosts.Contains(host) || host.EndsWith(".githubusercontent.com", StringComparison.Ordinal) && host.Length > ".githubusercontent.com".Length;
    }

#if DEBUG
    // Development builds only: a local test server instead of GitHub. It is compiled out of Release builds (a test in CI checks that
    // this text is not in them) and even here it accepts nothing but plain http on 127.0.0.1.
    private static Uri? Base
    {
        get
        {
            var text = Environment.GetEnvironmentVariable("VIDEOPREP_UPDATE_URL");
            if (string.IsNullOrWhiteSpace(text) || !Uri.TryCreate(text.EndsWith('/') ? text : text + "/", UriKind.Absolute, out var u)) return null;
            return u.Scheme == Uri.UriSchemeHttp && u.Host == "127.0.0.1" ? u : null;
        }
    }

    private static bool DebugAllowed(Uri uri) => Base is { } b && uri.Scheme == Uri.UriSchemeHttp && uri.Host == "127.0.0.1" && uri.Port == b.Port && uri.UserInfo.Length == 0;
#else
    private static Uri? Base => null;

    private static bool DebugAllowed(Uri uri) => false;
#endif
}
