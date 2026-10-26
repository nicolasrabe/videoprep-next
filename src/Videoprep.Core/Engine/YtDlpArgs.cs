using System.Globalization;
using Videoprep.Core.Formats;
using Videoprep.Core.Models;

namespace Videoprep.Core.Engine;

/// <summary>
/// yt-dlp argument lists (one string per argument, for ProcessStartInfo.ArgumentList; never a joined command line).
/// Port of Get-DownloadArgs, Start-Attempt and Start-DetailAttempt, with the 0.2 changes checked in docs/VERIFY.md.
/// </summary>
public static class YtDlpArgs
{
    /// <summary>H.264 video + AAC preferred; never format 18 (360p), VP9 or AV1. Same as $FmtSafe.</summary>
    public const string StrictH264 = "(bv*[vcodec^=avc1]+ba[acodec^=mp4a])/(bv*[vcodec^=avc1]+ba)/(b[vcodec^=avc1])";

    /// <summary>Machine-readable progress line, parsed by <see cref="ProgressLine"/>.</summary>
    public const string ProgressTemplate =
        "download:VP|%(progress._percent_str)s|%(progress._speed_str)s|%(progress._eta_str)s";

    /// <summary>Printed once per finished file with its final absolute path (AUDIT H2).</summary>
    public const string FinalPathPrint = "after_move:filepath";

    /// <summary>File name cap (AUDIT M9: 100 instead of 140, plus --windows-filenames).</summary>
    public const string TitleTemplate = "%(title,id).100s";

    /// <summary>Preview line; fields split by '|' with the title last (it may contain '|').</summary>
    public const string DetailTemplate =
        "DET|%(duration_string)s|%(height)s|%(fps)s|%(vcodec)s|%(acodec)s|%(upload_date)s|%(uploader)s|%(thumbnail)s|%(title)s";

    private static string Sort(int cap) => string.Create(CultureInfo.InvariantCulture, $"res:{cap},fps");

    /// <summary>Format selection for one job (port of Get-DownloadArgs).</summary>
    public static IReadOnlyList<string> FormatArgs(Job job, DownloadOptions opt)
    {
        if (job.Kind == MediaKind.Audio)
            return ["-f", "ba/b", "-x", "--audio-format", job.Format, "--audio-quality", "0"];

        var sort = Sort(job.Cap);
        switch (job.Format)
        {
            case "movp": return ["-f", "bv*+ba/b", "-S", sort, "--merge-output-format", "mkv"];
            case "webm": return ["-f", "bv*[ext=webm]+ba[ext=webm]/bv*+ba/b", "-S", sort, "--merge-output-format", "webm/mkv"];
            case "mkv": return ["-f", "bv*+ba/b", "-S", sort, "--merge-output-format", "mkv"];
        }
        // Other sites: best available, converted afterwards if needed.
        if (job.Site == Site.Other) return ["-f", "bv*+ba/b", "-S", sort, "--merge-output-format", "mp4/mkv"];

        // MP4 and MOV (H.264): strict H.264 + AAC, optional fallback to the best stream.
        if (opt.ConvertFallback) return ["-f", StrictH264 + "/bv*+ba/b", "-S", sort, "--merge-output-format", "mp4/mkv"];
        return job.Format == "movh"
            ? ["-f", StrictH264, "-S", sort, "--merge-output-format", "mp4", "--remux-video", "mov"]
            : ["-f", StrictH264, "-S", sort, "--merge-output-format", "mp4", "--remux-video", "mp4"];
    }

    /// <summary>
    /// Full download command for one attempt (port of Start-Attempt).
    /// <paramref name="archiveFile"/> is used only when <see cref="DownloadOptions.SkipDownloaded"/> is on;
    /// subscriptions pass their own archive with <paramref name="forceArchive"/>.
    /// </summary>
    /// <param name="dumpCookiesTo">AUDIT M5: when reading a browser, also write its cookies to this file
    /// (<c>--cookies FILE</c> both reads and saves the jar; docs/VERIFY.md).</param>
    public static IReadOnlyList<string> Download(Job job, DownloadOptions opt, ToolPaths tools, CookieSource cookies,
                                                 string folder, bool hasClips, string? archiveFile, bool forceArchive = false,
                                                 string? dumpCookiesTo = null)
    {
        var a = new List<string>
        {
            "--ignore-config", "--newline", "--progress", "--no-playlist", "--no-mtime",
            "--continue", "--no-overwrites", "--windows-filenames",
            "--retries", "10", "--fragment-retries", "10",
            "--sleep-requests", opt.SleepRequests.ToString(CultureInfo.InvariantCulture),
        };
        a.AddRange(FormatArgs(job, opt));

        if (opt.Subtitles)
        {
            var langs = opt.SubtitleLanguages.Trim();
            if (langs.Length == 0) langs = "en";
            a.AddRange(["--write-subs", "--write-auto-subs", "--sub-langs", langs, "--convert-subs", "srt"]);
        }
        else
        {
            a.AddRange(["--no-write-subs", "--no-write-auto-subs"]);
        }
        if (opt.Thumbnail) a.AddRange(["--write-thumbnail", "--convert-thumbnails", "jpg"]);
        else a.Add("--no-write-thumbnail");

        if (opt.LimitMBps > 0) a.AddRange(["--limit-rate", opt.LimitMBps.ToString(CultureInfo.InvariantCulture) + "M"]);
        if (archiveFile is not null && (opt.SkipDownloaded || forceArchive)) a.AddRange(["--download-archive", archiveFile]);

        var name = job.Label ?? TitleTemplate;
        if (hasClips) name += "__full";
        a.AddRange(["-P", folder, "-o", name + ".%(ext)s"]);

        AddTools(a, tools);
        a.AddRange(["--progress-template", ProgressTemplate, "--print", FinalPathPrint]);
        a.AddRange(cookies.Args());
        if (dumpCookiesTo is not null && cookies.Kind == CookieKind.Browser) a.AddRange(["--cookies", dumpCookiesTo]);
        a.Add(job.Url);
        return a;
    }

    /// <summary>
    /// Preview command (port of Start-DetailAttempt). AUDIT X2: audio formats ask for the best audio,
    /// not the H.264 video selector.
    /// </summary>
    public static IReadOnlyList<string> Preview(string url, bool otherSite, string formatKey, int cap, ToolPaths tools,
                                                CookieSource cookies)
    {
        var fmt = FormatCatalog.Get(formatKey);
        string selector;
        if (fmt.Kind == MediaKind.Audio) selector = "ba/b";
        else if (!otherSite && fmt.IsH264) selector = StrictH264;
        else selector = "bv*+ba/b";

        var a = new List<string>
        {
            "--ignore-config", "--no-playlist", "--no-warnings", "-f", selector, "-S", Sort(cap),
            "--print", DetailTemplate,
        };
        AddTools(a, tools, ffmpeg: false);
        a.AddRange(cookies.Args());
        a.Add(url);
        return a;
    }

    /// <summary>yt-dlp self-update (port of Start-UpdateRun).</summary>
    public static IReadOnlyList<string> Update() => ["-U"];

    private static void AddTools(List<string> a, ToolPaths tools, bool ffmpeg = true)
    {
        if (ffmpeg && tools.Ffmpeg is not null)
            a.AddRange(["--ffmpeg-location", Path.GetDirectoryName(tools.Ffmpeg)!]);
        // Must be an absolute path: a relative one is silently ignored (docs/VERIFY.md).
        if (tools.Deno is not null) a.AddRange(["--js-runtimes", "deno:" + tools.Deno]);
    }
}
