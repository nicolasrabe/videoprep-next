using System.Text.RegularExpressions;
using Videoprep.Core.Storage;

namespace Videoprep.Core.Engine;

/// <summary>A browser yt-dlp can read cookies from, and where its profile lives on Windows ($BrowserDefs).</summary>
public sealed record BrowserDef(string Key, string Name, string WindowsProfile);

public static partial class CookieStrategy
{
    public static IReadOnlyList<BrowserDef> Browsers { get; } =
    [
        new("firefox", "Firefox", @"%APPDATA%\Mozilla\Firefox\Profiles"),
        new("chrome", "Chrome", @"%LOCALAPPDATA%\Google\Chrome\User Data"),
        new("edge", "Edge", @"%LOCALAPPDATA%\Microsoft\Edge\User Data"),
        new("brave", "Brave", @"%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data"),
        new("opera", "Opera", @"%APPDATA%\Opera Software\Opera Stable"),
        new("vivaldi", "Vivaldi", @"%LOCALAPPDATA%\Vivaldi\User Data"),
        new("chromium", "Chromium", @"%LOCALAPPDATA%\Chromium\User Data"),
        new("whale", "Whale", @"%LOCALAPPDATA%\Naver\Naver Whale\User Data"),
    ];

    /// <summary>
    /// Output that means "this cookie source did not work, try the next one" (legacy $RetryRegex).
    /// Includes YouTube's bot check.
    /// </summary>
    [GeneratedRegex("Could not copy .*cookie|Failed to decrypt|could not find .*cookie|cookie database|DPAPI|Sign in to confirm|not a bot",
                    RegexOptions.IgnoreCase)]
    public static partial Regex RetryOutput();

    /// <summary>YouTube's bot check / rate limit: in subscriptions this stops the whole run (SCRAPING_SPEC 4).</summary>
    [GeneratedRegex(@"Sign in to confirm|not a bot|HTTP Error 429|Too Many Requests", RegexOptions.IgnoreCase)]
    public static partial Regex BlockedOutput();

    /// <summary>Browsers whose profile folder exists (Windows). <paramref name="exists"/> is injectable for tests.</summary>
    public static IReadOnlyList<string> InstalledBrowsers(Func<string, bool>? exists = null)
    {
        exists ??= Directory.Exists;
        if (!OperatingSystem.IsWindows() && exists == Directory.Exists) return [];   // macOS paths: after 0.2.0
        return Browsers.Where(b => exists(Environment.ExpandEnvironmentVariables(b.WindowsProfile))).Select(b => b.Key).ToList();
    }

    /// <summary>
    /// The order of cookie sources to try for one video (port of Get-BrowserList).
    /// "auto" = every installed browser, then no cookies; the source that worked last time goes first (AUDIT X3).
    /// </summary>
    public static IReadOnlyList<CookieSource> Chain(Settings settings, IReadOnlyList<string> installedBrowsers,
                                                    Func<string, bool>? fileExists = null)
    {
        fileExists ??= File.Exists;
        switch (settings.CookieSource)
        {
            case "none":
                return [CookieSource.NoCookies];
            case "file":
                return settings.CookieFile.Length > 0 && fileExists(settings.CookieFile)
                    ? [CookieSource.FromFile(settings.CookieFile)]
                    : [CookieSource.NoCookies];
            case "auto":
                var list = installedBrowsers.Select(CookieSource.FromBrowser).Append(CookieSource.NoCookies).ToList();
                return PreferFirst(list, settings.PreferredBrowser);
            default:
                return [CookieSource.FromBrowser(settings.CookieSource)];
        }
    }

    /// <summary>Moves the browser that worked to the front (port of the "remember the source" step).</summary>
    public static IReadOnlyList<CookieSource> PreferFirst(IReadOnlyList<CookieSource> chain, string? browserKey)
    {
        if (browserKey is null) return chain;
        var hit = chain.FirstOrDefault(c => c.Kind == CookieKind.Browser && c.Value == browserKey);
        return hit is null ? chain : [hit, .. chain.Where(c => c != hit)];
    }

    /// <summary>Same idea for a source object (used by the engine after a retry succeeded).</summary>
    public static IReadOnlyList<CookieSource> MoveToFront(IReadOnlyList<CookieSource> chain, CookieSource worked) =>
        [worked, .. chain.Where(c => c != worked)];
}
