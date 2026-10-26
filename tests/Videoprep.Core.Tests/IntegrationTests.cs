using System.Globalization;
using Videoprep.Core.Engine;
using Videoprep.Core.Media;
using Videoprep.Core.Models;

namespace Videoprep.Core.Tests;

/// <summary>
/// Real ffmpeg/ffprobe. Found in VIDEOPREP_FFMPEG_DIR, in the repo's .tools folder, or in PATH; skipped when absent.
/// Test videos are generated, never downloaded. Each frame N is a flat gray of brightness 16 + (N mod 70) * 3, so
/// after a cut every frame tells which source frame it was: the tests check the count AND the first/last frame.
/// </summary>
internal static class Ffmpeg
{
    private static readonly Lazy<(string? Ffmpeg, string? Ffprobe)> Found = new(Locate);

    public static string? Exe => Found.Value.Ffmpeg;
    public static string? Probe => Found.Value.Ffprobe;

    public static void SkipIfMissing() =>
        Assert.SkipWhen(Exe is null || Probe is null, "ffmpeg/ffprobe not found (set VIDEOPREP_FFMPEG_DIR or put them in .tools/)");

    private static (string?, string?) Locate()
    {
        var name = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var probe = OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";
        var dirs = new List<string>();
        if (Environment.GetEnvironmentVariable("VIDEOPREP_FFMPEG_DIR") is { Length: > 0 } env) dirs.Add(env);
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent) dirs.Add(Path.Combine(d.FullName, ".tools"));
        dirs.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        foreach (var d in dirs)
        {
            var f = Path.Combine(d, name);
            var p = Path.Combine(d, probe);
            if (File.Exists(f) && File.Exists(p)) return (f, p);
        }
        return (null, null);
    }

    public static int Run(params string[] args)
    {
        var r = new ProcessRunner().RunAsync(new ProcessSpec(Exe!, args), _ => { }, CancellationToken.None).GetAwaiter().GetResult();
        return r.ExitCode;
    }

    public static int Luma(long frame) => 16 + (int)(frame % 70) * 3;

    /// <summary>Generated test video: flat frames numbered by brightness, keyframe every second, AAC tone.</summary>
    public static string MakeSource(string dir, string rate, double seconds, string name = "source.mp4")
    {
        var path = Path.Combine(dir, name);
        var d = seconds.ToString(CultureInfo.InvariantCulture);
        var code = Run("-hide_banner", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", $"color=c=gray:s=64x64:r={rate}:d={d},geq=lum='16+mod(N,70)*3':cb=128:cr=128",
            "-f", "lavfi", "-i", $"sine=frequency=440:sample_rate=48000:duration={d}",
            "-c:v", "libx264", "-crf", "8", "-pix_fmt", "yuv420p", "-g", "30", "-c:a", "aac", "-shortest", path);
        Assert.Equal(0, code);
        return path;
    }

    /// <summary>Every frame of a file, decoded to one gray byte (frame brightness).</summary>
    public static byte[] FrameLumas(string file, string tmpDir)
    {
        var raw = Path.Combine(tmpDir, Guid.NewGuid().ToString("N") + ".gray");
        Assert.Equal(0, Run("-hide_banner", "-loglevel", "error", "-y", "-i", file, "-map", "0:v:0",
                            // passthrough: one output frame per decoded frame, never duplicated to fill timestamp gaps
                            // (MKV/WebM store millisecond timestamps; the default mode added a frame with Ubuntu's ffmpeg).
                            "-vf", "scale=1:1:flags=area,format=gray", "-fps_mode", "passthrough", "-f", "rawvideo", raw));
        var bytes = File.ReadAllBytes(raw);
        File.Delete(raw);
        return bytes;
    }

    /// <summary>
    /// Video packets stored in the file, counted by ffprobe WITHOUT decoding (independent of the gray decode, and immune
    /// to audio decoder trouble: Ubuntu's ffprobe failed on Opus packets when asked to decode with -count_frames).
    /// For H.264, ProRes and VP9 in these files one packet is one frame.
    /// </summary>
    public static long CountFrames(string file)
    {
        var sb = new System.Text.StringBuilder();
        var r = new ProcessRunner().RunAsync(new ProcessSpec(Probe!, ["-v", "error", "-select_streams", "v:0", "-count_packets",
            "-show_entries", "stream=nb_read_packets", "-of", "csv=p=0", file]), l => sb.AppendLine(l), CancellationToken.None)
            .GetAwaiter().GetResult();
        Assert.Equal(0, r.ExitCode);
        // stderr is mixed in (Ubuntu's ffprobe warns about Opus packets here): keep the line that is just the number.
        var lines = sb.ToString().Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var number = lines.Select(l => l.TrimEnd(',')).FirstOrDefault(l => l.All(char.IsAsciiDigit) && l.Length > 0);
        Assert.True(number is not null, "ffprobe gave no frame count: " + sb);
        return long.Parse(number!, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Which source frame index (mod 70) a brightness belongs to. The gray conversion expands video range (16-235)
    /// to full range (0-255), so 16 + 3k comes back as 3k * 255 / 219.
    /// </summary>
    public static int FrameMod70(byte luma) => (int)Math.Round(luma * 219.0 / 255.0 / 3.0);

    public static MediaInfo ProbeFile(string file)
    {
        var sb = new System.Text.StringBuilder();
        var r = new ProcessRunner().RunAsync(new ProcessSpec(Probe!, FfmpegArgs.Probe(file)), l => sb.AppendLine(l), CancellationToken.None)
            .GetAwaiter().GetResult();
        Assert.Equal(0, r.ExitCode);
        return MediaInfo.ParseFfprobeJson(sb.ToString());
    }
}

[Trait("Category", "Integration")]
public sealed class FrameExactCutTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("videoprep-cut-").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private void AssertCut(string source, string start, string end, string format)
    {
        var info = Ffmpeg.ProbeFile(source);
        var timing = SourceTiming.FromVideo(info);
        Assert.True(timing.Ok, timing.Error);
        var plan = CutPlanner.Plan(start, end, timing);
        Assert.True(plan.Ok, plan.Error);

        var job = new Job { GroupId = 1, Index = 0, Count = 1, Url = "u", Site = Site.Youtube, Format = format, Cap = 1080, Start = start, End = end };
        var ext = Formats.FormatCatalog.Get(format).Extension;
        var output = Path.Combine(_dir, $"cut-{Guid.NewGuid():N}{ext}");
        var args = FfmpegArgs.Encode(job, plan, timing, 14, source, output);
        Assert.Equal(0, Ffmpeg.Run([.. args]));

        var lumas = Ffmpeg.FrameLumas(output, _dir);
        Assert.Equal(plan.Frames, Ffmpeg.CountFrames(output));
        Assert.Equal(plan.Frames, lumas.Length);
        Assert.Equal((int)(plan.StartFrame % 70), Ffmpeg.FrameMod70(lumas[0]));
        Assert.Equal((int)(plan.EndFrame % 70), Ffmpeg.FrameMod70(lumas[^1]));

        var outInfo = Ffmpeg.ProbeFile(output);
        var check = OutputVerifier.Check(outInfo, job.Kind, plan, info.FormatDuration, true);
        Assert.True(check.Ok, check.Reason);
    }

    [Theory]
    [InlineData("30000/1001", "00:00:01:00", "00:00:02:00")]   // 31 frames, start between keyframes
    [InlineData("30000/1001", "00:00:03:17", "00:00:05:02")]
    [InlineData("30000/1001", "", "00:00:00:09")]              // from the very start
    [InlineData("30000/1001", "-00:00:00:15", "")]             // the last 15 frames
    [InlineData("30000/1001", "523", "")]                      // digit entry: 00:00:05:23 to the end
    [InlineData("30000/1001", "00:00:07:28", "00:00:07:28")]   // a single frame (the last one: 8 s = frames 0-238)
    [InlineData("25", "00:00:02:13", "00:00:04:00")]
    [InlineData("60000/1001", "00:00:01:59", "00:00:03:00")]
    [InlineData("24000/1001", "00:00:00:01", "00:00:01:23")]
    public void Mp4_clips_are_frame_exact(string rate, string start, string end)
    {
        Ffmpeg.SkipIfMissing();
        var src = Ffmpeg.MakeSource(_dir, rate, 8);
        AssertCut(src, start, end, "mp4");
    }

    [Theory]
    [InlineData("movh")]
    [InlineData("movp")]
    [InlineData("webm")]
    [InlineData("mkv")]
    public void Other_video_formats_are_frame_exact(string format)
    {
        Ffmpeg.SkipIfMissing();
        var src = Ffmpeg.MakeSource(_dir, "30000/1001", 4);
        AssertCut(src, "00:00:01:10", "00:00:02:05", format);
    }

    [Fact]
    public void Drop_frame_timecode_after_the_first_minute()
    {
        Ffmpeg.SkipIfMissing();
        var src = Ffmpeg.MakeSource(_dir, "30000/1001", 62);
        // 00;01;00;02 is frame 1800 in 29.97 drop-frame.
        AssertCut(src, "00;01;00;02", "00;01;00;20", "mp4");
        var plan = CutPlanner.Plan("00;01;00;02", "", SourceTiming.FromVideo(Ffmpeg.ProbeFile(src)));
        Assert.Equal(1800, plan.StartFrame);
    }

    [Theory]
    [InlineData("m4a")]
    [InlineData("mp3")]
    [InlineData("wav")]
    [InlineData("flac")]
    [InlineData("opus")]
    public void Audio_clips_have_the_planned_length(string format)
    {
        Ffmpeg.SkipIfMissing();
        var src = Ffmpeg.MakeSource(_dir, "30000/1001", 6);
        var timing = SourceTiming.FromAudio(Ffmpeg.ProbeFile(src), 29.97);
        var plan = CutPlanner.Plan("00:00:01:00", "00:00:03:00", timing);
        var job = new Job { GroupId = 1, Index = 0, Count = 1, Url = "u", Site = Site.Youtube, Format = format, Cap = 1080, Start = "1", End = "2" };
        var output = Path.Combine(_dir, "a" + Formats.FormatCatalog.Get(format).Extension);
        Assert.Equal(0, Ffmpeg.Run([.. FfmpegArgs.Encode(job, plan, timing, 14, src, output)]));
        var check = OutputVerifier.Check(Ffmpeg.ProbeFile(output), job.Kind, plan, 6, true);
        Assert.True(check.Ok, check.Reason);
    }
}

/// <summary>The whole engine with the fake yt-dlp "downloading" a generated video and real ffmpeg cutting it.</summary>
[Trait("Category", "Integration")]
[Collection("engine")]
public sealed class EngineWithFfmpegTests : IDisposable
{
    private readonly string _src;
    private readonly FakeYtDlpRig _rig;

    public EngineWithFfmpegTests()
    {
        var gen = Directory.CreateTempSubdirectory("videoprep-src-").FullName;
        _src = Ffmpeg.Exe is null ? "" : Ffmpeg.MakeSource(gen, "30000/1001", 6);
        _rig = new FakeYtDlpRig(_src.Length > 0 ? _src : null);
    }

    public void Dispose()
    {
        _rig.Dispose();
        if (_src.Length > 0) Directory.Delete(Path.GetDirectoryName(_src)!, true);
    }

    private EngineContext Ctx(DownloadOptions? opt = null) => _rig.Context(tools: new ToolPaths(FakeYtDlpRig.FakeExe, Ffmpeg.Exe, Ffmpeg.Probe, null))
        with { Options = opt ?? new DownloadOptions() };

    private static IReadOnlyList<Job> ThreeClips(string label = "") =>
        JobPlanner.Build([new QueueItem
        {
            Id = 1, Url = "https://fake.test/ok/v1", Format = "mp4",
            Naming = label.Length > 0 ? NamingMode.Series : NamingMode.Original, Prefix = label.Length > 0 ? label : "Video",
            Clips = [new("00:00:01:00", "00:00:01:29"), new("00:00:02:10", "00:00:03:00"), new("-00:00:00:15", "")],
        }], new Dictionary<string, int>());

    [Fact]
    public async Task Downloads_once_cuts_three_exact_clips_then_deletes_the_full_file()
    {
        Ffmpeg.SkipIfMissing();
        var r = await _rig.Engine.RunAsync(ThreeClips(), Ctx(), TestContext.Current.CancellationToken);

        Assert.Equal((3, 0, 0), (r.Ok, r.Warn, r.Fail));
        Assert.Single(_rig.Calls());                                     // downloaded once
        string[] expected = ["Fake video v1 - 1.mp4", "Fake video v1 - 2.mp4", "Fake video v1 - 3.mp4"];
        Assert.Equal(expected, r.Files.OrderBy(kv => kv.Key).Select(kv => kv.Value));
        long[] frames = [30, 21, 15];
        for (var i = 0; i < 3; i++)
            Assert.Equal(frames[i], Ffmpeg.FrameLumas(Path.Combine(_rig.Folder, expected[i]), _rig.Dir).Length);
        Assert.False(File.Exists(Path.Combine(_rig.Folder, "Fake video v1__full.mp4")));   // deleted only at the end
        Assert.Empty(Directory.GetFiles(_rig.Folder, "*.__tmp*"));
        Assert.All(_rig.Events.OfType<VerifiedEvent>(), e => Assert.True(e.Ok, e.Reason));
        Assert.Equal(3, _rig.Events.OfType<VerifiedEvent>().Count());
    }

    [Fact]
    public async Task A_full_file_that_was_already_there_is_never_deleted_AUDIT_X6()
    {
        Ffmpeg.SkipIfMissing();
        var existing = Path.Combine(_rig.Folder, "Fake video v1__full.mp4");
        File.Copy(_src, existing);
        var r = await _rig.Engine.RunAsync(ThreeClips(), Ctx(), TestContext.Current.CancellationToken);
        Assert.Equal(3, r.Ok);
        Assert.True(File.Exists(existing));
        Assert.Contains("already in the folder, it is reused (and never deleted)", _rig.LogText);
    }

    [Fact]
    public async Task Keep_full_option_and_series_names()
    {
        Ffmpeg.SkipIfMissing();
        var r = await _rig.Engine.RunAsync(ThreeClips("Toma"), Ctx(new DownloadOptions { KeepFull = true }), TestContext.Current.CancellationToken);
        Assert.Equal(["Toma1.mp4", "Toma2.mp4", "Toma3.mp4"], r.Files.OrderBy(kv => kv.Key).Select(kv => kv.Value));
        Assert.True(File.Exists(Path.Combine(_rig.Folder, "Toma1__full.mp4")));
        Assert.Contains("the full video was kept", _rig.LogText);
    }

    [Fact]
    public async Task Existing_output_names_are_never_overwritten()
    {
        Ffmpeg.SkipIfMissing();
        File.WriteAllText(Path.Combine(_rig.Folder, "Toma1.mp4"), "mine");
        var jobs = ThreeClips("Toma").Take(1).Select(j => j with { Count = 1 }).ToList();
        var r = await _rig.Engine.RunAsync(jobs, Ctx(), TestContext.Current.CancellationToken);
        Assert.Equal("Toma1_2.mp4", r.Files["1|0"]);
        Assert.Equal("mine", File.ReadAllText(Path.Combine(_rig.Folder, "Toma1.mp4")));
    }

    [Fact]
    public async Task Bad_timecode_keeps_the_download_and_asks_for_review()
    {
        Ffmpeg.SkipIfMissing();
        var jobs = JobPlanner.Build([new QueueItem { Id = 1, Url = "https://fake.test/ok/v2", Clips = [new("00:00:30:00", "")] }],
                                    new Dictionary<string, int>());
        var r = await _rig.Engine.RunAsync(jobs, Ctx(), TestContext.Current.CancellationToken);
        Assert.Equal(JobStatus.REVIEW, r.Statuses["1|0"]);
        Assert.True(File.Exists(Path.Combine(_rig.Folder, "Fake video v2__full.mp4")));
        Assert.Contains("Start is after the end of the video", _rig.LogText);
        Assert.StartsWith("Start is after the end of the video (", r.Reasons["1|0"]);
    }

    [Fact]
    public async Task Frames_beyond_the_fps_explain_why_like_the_real_480p_case()
    {
        // Nico's first real run: 480p H.264 from YouTube is 30 fps, so "00:00:12:59" is not a valid frame.
        Ffmpeg.SkipIfMissing();
        var jobs = JobPlanner.Build([new QueueItem { Id = 1, Url = "https://fake.test/ok/v4", Clips = [new("00:00:01:00", "00:00:02:59")] }],
                                    new Dictionary<string, int>());
        var r = await _rig.Engine.RunAsync(jobs, Ctx(), TestContext.Current.CancellationToken);
        Assert.Equal(JobStatus.REVIEW, r.Statuses["1|0"]);
        Assert.Equal("frames must be 0-29 for this video (30 fps)", r.Reasons["1|0"]);
        Assert.True(File.Exists(Path.Combine(_rig.Folder, "Fake video v4__full.mp4")));
    }

    [Fact]
    public async Task ProRes_whole_video_conversion_is_verified_and_the_mkv_removed()
    {
        Ffmpeg.SkipIfMissing();
        var jobs = JobPlanner.Build([new QueueItem { Id = 1, Url = "https://fake.test/ok/v3", Format = "movp" }], new Dictionary<string, int>());
        var r = await _rig.Engine.RunAsync(jobs, Ctx(), TestContext.Current.CancellationToken);
        Assert.Equal(JobStatus.OK, r.Statuses["1|0"]);
        Assert.Equal("Fake video v3.mov", r.Files["1|0"]);
        var info = Ffmpeg.ProbeFile(Path.Combine(_rig.Folder, "Fake video v3.mov"));
        Assert.Equal(("prores", "pcm_s24le"), (info.Video!.Codec, info.Audio!.Codec));
        Assert.False(File.Exists(Path.Combine(_rig.Folder, "Fake video v3.mp4")));
    }
}
