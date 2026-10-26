using Videoprep.Core.Engine;
using Videoprep.Core.Formats;
using Videoprep.Core.Media;
using Videoprep.Core.Models;

namespace Videoprep.Core.Tests;

public class FormatCatalogTests
{
    [Fact]
    public void Ten_formats_like_the_legacy_script()
    {
        Assert.Equal(["mp4", "movh", "movp", "webm", "mkv"], FormatCatalog.OfKind(MediaKind.Video).Select(f => f.Key));
        Assert.Equal(["m4a", "mp3", "wav", "flac", "opus"], FormatCatalog.OfKind(MediaKind.Audio).Select(f => f.Key));
        Assert.Equal(".mov", FormatCatalog.Get("movp").Extension);
        Assert.Equal("mp4", FormatCatalog.Get("nope").Key);
        Assert.Equal(new ExpectedCodecs("prores", "pcm_s24le"), FormatCatalog.Get("movp").Expected);
        Assert.Null(FormatCatalog.Get("mkv").Expected);
    }

    [Fact]
    public void H264_and_audio_cap_at_1080p_others_go_to_4k()
    {
        Assert.Equal(["1080p", "720p", "480p"], FormatCatalog.ResolutionChoices("mp4"));
        Assert.Equal(["1080p", "720p", "480p"], FormatCatalog.ResolutionChoices("mp3"));
        Assert.Equal(["4K", "1440p", "1080p", "720p"], FormatCatalog.ResolutionChoices("movp"));
        Assert.Equal("4K", FormatCatalog.ResolutionLabel(2160));
    }
}

public class JobPlannerTests
{
    private static QueueItem Item(int id, NamingMode naming, int clips = 0, string prefix = "Clip", string custom = "") => new()
    {
        Id = id, Url = "https://www.youtube.com/watch?v=jNQXAC9IVRw", Naming = naming, Prefix = prefix, Custom = custom,
        Clips = Enumerable.Range(0, clips).Select(i => new ClipRange($"00:00:0{i}:00", "")).ToList(),
    };

    [Fact]
    public void One_job_per_clip_only_the_first_downloads()
    {
        var jobs = JobPlanner.Build([Item(7, NamingMode.Original, clips: 3)], new Dictionary<string, int>());
        Assert.Equal(["7|0", "7|1", "7|2"], jobs.Select(j => j.Key));
        Assert.Equal([true, false, false], jobs.Select(j => j.Download));
        Assert.All(jobs, j => Assert.Null(j.Label));
        Assert.All(jobs, j => Assert.Equal(3, j.Count));
        Assert.True(jobs[0].IsRange);
        Assert.Equal(Site.Youtube, jobs[0].Site);
    }

    [Fact]
    public void Whole_video_is_one_job()
    {
        var j = Assert.Single(JobPlanner.Build([Item(1, NamingMode.Original)], new Dictionary<string, int>()));
        Assert.False(j.IsRange);
        Assert.True(j.Download);
    }

    [Fact]
    public void Series_numbers_continue_across_items_sharing_a_prefix()
    {
        var next = new Dictionary<string, int> { ["Clip"] = 4 };
        var jobs = JobPlanner.Build([Item(1, NamingMode.Series, clips: 2), Item(2, NamingMode.Series)], next);
        Assert.Equal(["Clip4", "Clip5", "Clip6"], jobs.Select(j => j.Label));
        Assert.Equal(7, next["Clip"]);
    }

    [Fact]
    public void Custom_names_get_a_number_only_with_several_clips()
    {
        var one = JobPlanner.Build([Item(1, NamingMode.Custom, custom: "Entrevista: final")], new Dictionary<string, int>());
        Assert.Equal("Entrevista final", one[0].Label);
        var two = JobPlanner.Build([Item(1, NamingMode.Custom, clips: 2, custom: "Toma")], new Dictionary<string, int>());
        Assert.Equal(["Toma - 1", "Toma - 2"], two.Select(j => j.Label));
    }

    [Fact]
    public void Series_prefixes_are_listed_once() =>
        Assert.Equal(["Clip", "B"], JobPlanner.SeriesPrefixes(
            [Item(1, NamingMode.Series), Item(2, NamingMode.Series), Item(3, NamingMode.Series, prefix: "B"), Item(4, NamingMode.Original)]));
}

public class YtDlpArgsTests
{
    private static readonly ToolPaths Tools = new(@"C:\t\yt-dlp.exe", @"C:\t\ffmpeg.exe", @"C:\t\ffprobe.exe", @"C:\t\deno.exe");

    private static Job J(string fmt, Site site = Site.Youtube, int cap = 1080, string? label = null) => new()
    {
        GroupId = 1, Index = 0, Count = 1, Url = "https://www.youtube.com/watch?v=jNQXAC9IVRw", Site = site,
        Format = fmt, Cap = cap, Label = label, Download = true,
    };

    private static readonly DownloadOptions Fallback = new();
    private static readonly DownloadOptions Strict = new() { ConvertFallback = false };

    [Fact]
    public void Format_selection_per_format()
    {
        const string s = YtDlpArgs.StrictH264;
        Assert.Equal(["-f", s + "/bv*+ba/b", "-S", "res:1080,fps", "--merge-output-format", "mp4/mkv"], YtDlpArgs.FormatArgs(J("mp4"), Fallback));
        Assert.Equal(["-f", s, "-S", "res:720,fps", "--merge-output-format", "mp4", "--remux-video", "mp4"], YtDlpArgs.FormatArgs(J("mp4", cap: 720), Strict));
        Assert.Equal(["-f", s, "-S", "res:1080,fps", "--merge-output-format", "mp4", "--remux-video", "mov"], YtDlpArgs.FormatArgs(J("movh"), Strict));
        Assert.Equal(["-f", "bv*+ba/b", "-S", "res:2160,fps", "--merge-output-format", "mkv"], YtDlpArgs.FormatArgs(J("movp", cap: 2160), Fallback));
        Assert.Equal(["-f", "bv*[ext=webm]+ba[ext=webm]/bv*+ba/b", "-S", "res:1080,fps", "--merge-output-format", "webm/mkv"], YtDlpArgs.FormatArgs(J("webm"), Fallback));
        Assert.Equal(["-f", "bv*+ba/b", "-S", "res:1080,fps", "--merge-output-format", "mkv"], YtDlpArgs.FormatArgs(J("mkv"), Fallback));
        Assert.Equal(["-f", "bv*+ba/b", "-S", "res:1080,fps", "--merge-output-format", "mp4/mkv"], YtDlpArgs.FormatArgs(J("mp4", Site.Other), Strict));
        Assert.Equal(["-f", "ba/b", "-x", "--audio-format", "mp3", "--audio-quality", "0"], YtDlpArgs.FormatArgs(J("mp3"), Fallback));
    }

    [Fact]
    public void Full_download_command_default_options()
    {
        var a = YtDlpArgs.Download(J("mp4"), Fallback, Tools, CookieSource.FromBrowser("firefox"), @"D:\Videos",
                                   hasClips: false, archiveFile: @"C:\a\downloaded.txt");
        Assert.Equal(
        [
            "--ignore-config", "--newline", "--progress", "--no-playlist", "--no-mtime", "--continue", "--no-overwrites",
            "--windows-filenames", "--retries", "10", "--fragment-retries", "10", "--sleep-requests", "1",
            "-f", YtDlpArgs.StrictH264 + "/bv*+ba/b", "-S", "res:1080,fps", "--merge-output-format", "mp4/mkv",
            "--no-write-subs", "--no-write-auto-subs", "--no-write-thumbnail",
            "-P", @"D:\Videos", "-o", "%(title,id).100s.%(ext)s",
            "--ffmpeg-location", @"C:\t", "--js-runtimes", @"deno:C:\t\deno.exe",
            "--progress-template", YtDlpArgs.ProgressTemplate, "--print", "after_move:filepath",
            "--cookies-from-browser", "firefox",
            "https://www.youtube.com/watch?v=jNQXAC9IVRw",
        ], a);
    }

    [Fact]
    public void Full_download_command_all_options_and_clips()
    {
        var opt = new DownloadOptions
        {
            Subtitles = true, SubtitleLanguages = " ", Thumbnail = true, SkipDownloaded = true, LimitMBps = 5, SleepRequests = 3,
        };
        var a = YtDlpArgs.Download(J("mp4", label: "Clip4"), opt, Tools, CookieSource.FromFile(@"C:\c\cookies.txt"),
                                   @"D:\V", hasClips: true, archiveFile: @"C:\a\downloaded.txt").ToList();
        Assert.Equal("3", a[a.IndexOf("--sleep-requests") + 1]);
        Assert.Equal(["--write-subs", "--write-auto-subs", "--sub-langs", "en", "--convert-subs", "srt"], a.Skip(a.IndexOf("--write-subs")).Take(6));
        Assert.Contains("--write-thumbnail", a);
        Assert.Equal("5M", a[a.IndexOf("--limit-rate") + 1]);
        Assert.Equal(@"C:\a\downloaded.txt", a[a.IndexOf("--download-archive") + 1]);
        Assert.Equal("Clip4__full.%(ext)s", a[a.IndexOf("-o") + 1]);
        Assert.Equal(["--cookies", @"C:\c\cookies.txt"], a.Skip(a.IndexOf("--cookies")).Take(2));
    }

    [Fact]
    public void Archive_only_when_asked()
    {
        var a = YtDlpArgs.Download(J("mp4"), Fallback, Tools, CookieSource.NoCookies, "D", false, "arch.txt");
        Assert.DoesNotContain("--download-archive", a);
        Assert.DoesNotContain("--cookies", a);
        Assert.DoesNotContain("--cookies-from-browser", a);
        var b = YtDlpArgs.Download(J("mp4"), Fallback, Tools, CookieSource.NoCookies, "D", false, "sub.txt", forceArchive: true);
        Assert.Contains("--download-archive", b);
    }

    [Fact]
    public void Missing_tools_are_left_out()
    {
        var a = YtDlpArgs.Download(J("mp4"), Fallback, new ToolPaths("y", null, null, null), CookieSource.NoCookies, "D", false, null);
        Assert.DoesNotContain("--ffmpeg-location", a);
        Assert.DoesNotContain("--js-runtimes", a);
    }

    [Fact]
    public void Preview_selector_by_format_AUDIT_X2()
    {
        static string Sel(IReadOnlyList<string> a) => a[a.ToList().IndexOf("-f") + 1];
        var u = "https://www.youtube.com/watch?v=jNQXAC9IVRw";
        Assert.Equal(YtDlpArgs.StrictH264, Sel(YtDlpArgs.Preview(u, false, "mp4", 1080, Tools, CookieSource.NoCookies)));
        Assert.Equal("ba/b", Sel(YtDlpArgs.Preview(u, false, "mp3", 1080, Tools, CookieSource.NoCookies)));
        Assert.Equal("bv*+ba/b", Sel(YtDlpArgs.Preview(u, false, "webm", 2160, Tools, CookieSource.NoCookies)));
        Assert.Equal("bv*+ba/b", Sel(YtDlpArgs.Preview(u, true, "mp4", 1080, Tools, CookieSource.NoCookies)));
        var p = YtDlpArgs.Preview(u, false, "mp4", 720, Tools, CookieSource.NoCookies);
        Assert.Equal(["--ignore-config", "--no-playlist", "--no-warnings", "-f", YtDlpArgs.StrictH264, "-S", "res:720,fps",
                      "--print", YtDlpArgs.DetailTemplate, "--js-runtimes", @"deno:C:\t\deno.exe", u], p);
    }
}

public class FfmpegArgsTests
{
    private static readonly SourceTiming H264 = new(true, "", 30000, 1001, 30, true, 120, "h264", "aac", false);
    private static readonly SourceTiming Vp9 = H264 with { VideoCodec = "vp9", AudioCodec = "opus" };

    private static Job J(string fmt, string start = "", string end = "") => new()
    {
        GroupId = 1, Index = 0, Count = 1, Url = "u", Site = Site.Youtube, Format = fmt, Cap = 1080, Start = start, End = end,
    };

    [Fact]
    public void Clip_mp4_uses_input_seek_duration_and_frame_guard()
    {
        var plan = CutPlanner.Plan("00:00:01:00", "00:00:02:00", H264);
        var a = FfmpegArgs.Encode(J("mp4", "00:00:01:00", "00:00:02:00"), plan, H264, 14, "src.mp4", "out.__tmp.mp4");
        Assert.Equal(
        [
            "-hide_banner", "-loglevel", "error", "-nostats", "-y", "-ss", "0.992658", "-i", "src.mp4", "-t", "1.034367",
            "-map", "0:v:0", "-map", "0:a:0?", "-frames:v", "31",
            "-c:v", "libx264", "-preset", "medium", "-crf", "14", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "256k",
            "-movflags", "+faststart", "out.__tmp.mp4",
        ], a);
    }

    [Fact]
    public void Whole_video_conversion_copies_what_already_matches()
    {
        Assert.Equal(["-c:v", "copy", "-c:a", "copy"],
            FfmpegArgs.Encode(J("mp4"), null, H264, 14, "s", "t").Where(x => x is "-c:v" or "-c:a" or "copy"));
        var vp9 = FfmpegArgs.Encode(J("movh"), null, Vp9, 18, "s", "t").ToList();
        Assert.Equal("libx264", vp9[vp9.IndexOf("-c:v") + 1]);
        Assert.Equal("18", vp9[vp9.IndexOf("-crf") + 1]);
        Assert.Equal("aac", vp9[vp9.IndexOf("-c:a") + 1]);
        Assert.DoesNotContain("-ss", vp9);
        Assert.DoesNotContain("-frames:v", vp9);
    }

    [Fact]
    public void Prores_webm_and_mkv()
    {
        var p = FfmpegArgs.Encode(J("movp"), null, H264, 14, "s", "t");
        Assert.Equal(["-c:v", "prores_ks", "-profile:v", "3", "-vendor", "apl0", "-pix_fmt", "yuv422p10le", "-c:a", "pcm_s24le", "-ar", "48000", "-movflags", "+faststart", "t"],
                     p.Skip(p.ToList().IndexOf("-c:v")));
        var w = FfmpegArgs.Encode(J("webm"), null, H264, 14, "s", "t").ToList();
        Assert.Equal("libvpx-vp9", w[w.IndexOf("-c:v") + 1]);
        Assert.DoesNotContain("-movflags", w);
        var m = FfmpegArgs.Encode(J("mkv"), null, Vp9, 14, "s", "t").ToList();
        Assert.Equal("libx264", m[m.IndexOf("-c:v") + 1]);
        Assert.DoesNotContain("-movflags", m);
    }

    [Theory]
    [InlineData("m4a", new[] { "-c:a", "aac", "-b:a", "256k" })]
    [InlineData("mp3", new[] { "-c:a", "libmp3lame", "-b:a", "320k" })]
    [InlineData("wav", new[] { "-c:a", "pcm_s16le" })]
    [InlineData("flac", new[] { "-c:a", "flac" })]
    [InlineData("opus", new[] { "-c:a", "libopus", "-b:a", "160k" })]
    public void Audio_clips(string fmt, string[] codec)
    {
        var audio = new SourceTiming(true, "", 30, 1, 30, false, 150, "", "opus", false);
        var plan = CutPlanner.Plan("00:00:01:00", "", audio);
        var a = FfmpegArgs.Encode(J(fmt, "00:00:01:00"), plan, audio, 14, "s.opus", "t");
        Assert.Equal(["-vn", "-map", "0:a:0", .. codec, "t"], a.Skip(a.ToList().IndexOf("-vn")));
        Assert.DoesNotContain("-frames:v", a);
    }

    [Fact]
    public void Probe_is_one_json_call() =>
        Assert.Equal(["-v", "error", "-show_streams", "-show_format", "-of", "json", "f.mp4"], FfmpegArgs.Probe("f.mp4"));
}
