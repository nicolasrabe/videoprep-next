using System.Text.Json.Serialization;

namespace Videoprep.Core.Storage;

/// <summary>
/// Everything saved in settings.json (AUDIT M8). Property names are the legacy JSON keys where they existed,
/// so 0.1.1 and 0.2 can read each other's file. Out-of-range values are fixed by <see cref="Normalize"/>.
/// </summary>
public sealed class Settings
{
    public const int CurrentVersion = 2;

    /// <summary>Browsers in the legacy order ($BrowserDefs); the old "cookie" index points into this list.</summary>
    public static readonly IReadOnlyList<string> BrowserKeys =
        ["firefox", "chrome", "edge", "brave", "opera", "vivaldi", "chromium", "whale"];

    public static readonly IReadOnlyList<string> ThemeNames = ["light", "dark", "beige", "gray", "slate", "custom"];

    [JsonPropertyName("version")] public int Version { get; set; } = CurrentVersion;
    [JsonPropertyName("folder")] public string Folder { get; set; } = "";
    /// <summary>"orig" or "series" (custom is per link, never the default).</summary>
    [JsonPropertyName("naming")] public string Naming { get; set; } = "orig";
    [JsonPropertyName("prefix")] public string Prefix { get; set; } = "Video";

    /// <summary>"auto", a browser key, "file" or "none".</summary>
    [JsonPropertyName("cookieSource")] public string CookieSource { get; set; } = "auto";
    /// <summary>Legacy index (0 = auto, 1..8 = browsers, 9 = file, 10 = none); written too so 0.1.1 keeps working.</summary>
    [JsonPropertyName("cookie")] public int? LegacyCookieIndex { get; set; }
    [JsonPropertyName("cookieFile")] public string CookieFile { get; set; } = "";
    /// <summary>Cookie source that worked last time, tried first (AUDIT X3).</summary>
    [JsonPropertyName("preferredBrowser")] public string? PreferredBrowser { get; set; }

    [JsonPropertyName("subs")] public bool Subs { get; set; }
    [JsonPropertyName("subLangs")] public string SubLangs { get; set; } = "en,es";
    [JsonPropertyName("thumbs")] public bool Thumbs { get; set; }
    [JsonPropertyName("archive")] public bool Archive { get; set; }
    [JsonPropertyName("fallback")] public bool Fallback { get; set; } = true;
    [JsonPropertyName("keepFull")] public bool KeepFull { get; set; }
    [JsonPropertyName("crf")] public int Crf { get; set; } = 14;
    [JsonPropertyName("limit")] public int Limit { get; set; }
    [JsonPropertyName("sleep")] public int Sleep { get; set; } = 1;

    [JsonPropertyName("theme")] public string Theme { get; set; } = "light";
    [JsonPropertyName("customColor")] public string CustomColor { get; set; } = "#d9e1ea";
    /// <summary>"es" or "en"; empty = follow the system language.</summary>
    [JsonPropertyName("lang")] public string Lang { get; set; } = "";
    [JsonPropertyName("autoUpdate")] public bool AutoUpdate { get; set; }
    [JsonPropertyName("lastUpdate")] public string LastUpdate { get; set; } = "";
    [JsonPropertyName("sound")] public bool Sound { get; set; } = true;
    [JsonPropertyName("openDone")] public bool OpenDone { get; set; }

    /// <summary>Fix bad or old values (legacy ranges: crf 10-28, limit 0-500, sleep 0-30) and migrate the cookie index.</summary>
    public void Normalize(bool fromLegacyFile)
    {
        if (fromLegacyFile && LegacyCookieIndex is { } idx) CookieSource = CookieSourceFromIndex(idx);
        if (!IsValidCookieSource(CookieSource)) CookieSource = "auto";
        LegacyCookieIndex = CookieIndex(CookieSource);

        if (Crf is < 10 or > 28) Crf = 14;
        if (Limit is < 0 or > 500) Limit = 0;
        if (Sleep is < 0 or > 30) Sleep = 1;
        if (!ThemeNames.Contains(Theme)) Theme = "light";
        if (Lang is not ("es" or "en")) Lang = "";
        if (Naming is not ("orig" or "series")) Naming = "orig";
        if (string.IsNullOrWhiteSpace(Prefix)) Prefix = "Video";
        if (PreferredBrowser is not null && !BrowserKeys.Contains(PreferredBrowser)) PreferredBrowser = null;
        Version = CurrentVersion;
    }

    public static bool IsValidCookieSource(string s) => s is "auto" or "file" or "none" || BrowserKeys.Contains(s);

    public static string CookieSourceFromIndex(int i) =>
        i <= 0 ? "auto"
        : i <= BrowserKeys.Count ? BrowserKeys[i - 1]
        : i == BrowserKeys.Count + 1 ? "file"
        : i == BrowserKeys.Count + 2 ? "none"
        : "auto";

    public static int CookieIndex(string source) => source switch
    {
        "auto" => 0,
        "file" => BrowserKeys.Count + 1,
        "none" => BrowserKeys.Count + 2,
        _ => BrowserKeys.ToList().IndexOf(source) + 1,
    };
}
