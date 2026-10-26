namespace Videoprep.Core.Engine;

/// <summary>Options window values that change the commands (legacy $Opt). Ranges match the legacy dialog.</summary>
public sealed record DownloadOptions
{
    public bool Subtitles { get; init; }
    public string SubtitleLanguages { get; init; } = "en,es";
    public bool Thumbnail { get; init; }
    /// <summary>Skip videos already downloaded (yt-dlp archive file).</summary>
    public bool SkipDownloaded { get; init; }
    /// <summary>If there is no H.264, take the best stream and convert to H.264 + AAC (default on).</summary>
    public bool ConvertFallback { get; init; } = true;
    public bool KeepFull { get; init; }
    /// <summary>Clip quality, 10-28 (lower = better).</summary>
    public int Crf { get; init; } = 14;
    /// <summary>Speed limit in MB/s, 0 = none (0-500).</summary>
    public int LimitMBps { get; init; }
    /// <summary>Seconds between requests (0-30).</summary>
    public int SleepRequests { get; init; } = 1;
}

/// <summary>Absolute paths of the external tools (null = missing).</summary>
public sealed record ToolPaths(string? YtDlp, string? Ffmpeg, string? Ffprobe, string? Deno)
{
    public IReadOnlyList<string> Missing()
    {
        var m = new List<string>();
        if (YtDlp is null) m.Add("yt-dlp");
        if (Ffmpeg is null || Ffprobe is null) m.Add("ffmpeg");
        if (Deno is null) m.Add("deno");
        return m;
    }
}

public enum CookieKind { None, File, Browser }

/// <summary>Where yt-dlp reads the login cookies from (port of Get-CookieArgs / Get-CookieLabel).</summary>
public sealed record CookieSource(CookieKind Kind, string Value = "")
{
    public static readonly CookieSource NoCookies = new(CookieKind.None);
    public static CookieSource FromBrowser(string browserKey) => new(CookieKind.Browser, browserKey);
    public static CookieSource FromFile(string path) => new(CookieKind.File, path);

    public IReadOnlyList<string> Args() => Kind switch
    {
        CookieKind.File => ["--cookies", Value],
        CookieKind.Browser => ["--cookies-from-browser", Value],
        _ => [],
    };

    /// <summary>English text for the log.</summary>
    public string Label => Kind switch
    {
        CookieKind.File => "cookies.txt file",
        CookieKind.Browser => Value + " cookies",
        _ => "no login cookies",
    };
}
