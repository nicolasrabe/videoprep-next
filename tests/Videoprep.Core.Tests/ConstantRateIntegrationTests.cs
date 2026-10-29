using Videoprep.Core.Engine;
using Videoprep.Core.Media;
using Videoprep.Core.Models;

namespace Videoprep.Core.Tests;

/// <summary>
/// F8 with the real ffmpeg: a video whose frame times are truly irregular (not just two different frame rates in the header),
/// with audio of the same length, made constant by the engine in every format, with exact clips, left alone when it is already
/// constant, and kept untouched when a check fails. The ffmpeg is the one the app downloads (.tools\ here, the same build in the CI).
/// </summary>
[Trait("Category", "Integration")]
[Collection("engine")]
public sealed class ConstantRateIntegrationTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("videoprep-vfr-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); }
        catch (IOException) { }
    }

    private static ToolPaths Tools => new(FakeYtDlpRig.FakeExe, Ffmpeg.Exe, Ffmpeg.Probe, null);

    private static EngineContext Ctx(FakeYtDlpRig rig) => rig.Context(tools: Tools);

    private static IReadOnlyList<Job> Jobs(string format, bool constant = true, IReadOnlyList<ClipRange>? clips = null) =>
        JobPlanner.Build([new QueueItem { Id = 1, Url = "https://fake.test/ok/v1", Format = format, ConstantRate = constant, Clips = clips ?? [] }],
                         new Dictionary<string, int>());

    private static IReadOnlyList<ClipRange> ThreeClips =>
        [new("00:00:01:00", "00:00:01:29"), new("00:00:02:10", "00:00:03:00"), new("-00:00:00:15", "")];

    private static async Task<IReadOnlyList<IReadOnlyList<double>>> Sample(string file)
    {
        var info = Ffmpeg.ProbeFile(file);
        return await PacketProbe.SampleVideoAsync(new ProcessRunner(), Ffmpeg.Probe!, file, info.FormatDuration, TestContext.Current.CancellationToken);
    }

    // ---------------- the test clip is what it claims to be ----------------

    [Fact]
    public async Task The_test_clip_is_really_variable_and_a_constant_one_is_not()
    {
        Ffmpeg.SkipIfMissing();
        var vfr = Ffmpeg.MakeVfrSource(_dir);
        var info = Ffmpeg.ProbeFile(vfr);
        Assert.True(info.LooksVariableFrameRate, info.Summary);                       // ffprobe's own two rates disagree
        Assert.Equal(60.0, info.Video!.RFrameRate.Value);
        Assert.InRange(info.Video.AvgFrameRate.Value, 30, 32);

        var analysis = VfrDetector.Analyze(info, await Sample(vfr));
        Assert.True(analysis.IsVariable, analysis.Reason);
        Assert.Equal(new Rational(30, 1), analysis.Target);
        // The audio is as long as the video (the premise of the checks).
        Assert.True(Math.Abs((info.Video.Duration ?? 0) - (info.Audio!.Duration ?? 0)) < 0.02, info.Summary);

        var constant = Ffmpeg.MakeSource(_dir, "30000/1001", 6, "constant.mp4");
        var control = VfrDetector.Analyze(Ffmpeg.ProbeFile(constant), await Sample(constant));
        Assert.False(control.IsVariable, control.Reason);
    }

    [Theory]
    [InlineData("mp4")]
    [InlineData("webm")]
    public async Task Audio_that_outlasts_the_video_is_kept_whole(string format)
    {
        // Real recordings often have audio half a second longer than the video: a time limit at the video's end would cut it.
        Ffmpeg.SkipIfMissing();
        var vfr = Ffmpeg.MakeVfrSource(_dir, 6, "long-audio.mp4", audioSeconds: 6.5);
        var original = Ffmpeg.ProbeFile(vfr);
        Assert.True(original.Audio!.Duration > original.Video!.Duration + 0.3, original.Summary);
        using var rig = new FakeYtDlpRig(vfr);

        var r = await new JobEngine(new ProcessRunner(), rig.Log).RunAsync(Jobs(format), Ctx(rig), TestContext.Current.CancellationToken);

        Assert.True((r.Ok, r.Warn, r.Fail) == (1, 0, 0), rig.LogText);
        var stats = await PacketProbe.StatsAsync(new ProcessRunner(), Ffmpeg.Probe!, Path.Combine(rig.Folder, r.Files["1|0"]), TestContext.Current.CancellationToken);
        Assert.InRange(stats.AudioEnd!.Value - stats.AudioFirst!.Value, original.Audio.Duration!.Value - 0.06, original.Audio.Duration.Value + 0.06);
    }

    // ---------------- a real clip, only when VIDEOPREP_REAL_CLIP points at one (never in the CI) ----------------

    /// <summary>
    /// Runs a clip from outside the repository through the real engine (MP4 and ProRes MOV, whole) and writes what the engine
    /// answered to <c>VIDEOPREP_REAL_CLIP_OUT</c> (a folder) so it can be opened in an editor. Without the variable it passes at once.
    /// </summary>
    [Fact]
    public async Task A_real_clip_from_the_user_goes_through_the_engine()
    {
        var clip = Environment.GetEnvironmentVariable("VIDEOPREP_REAL_CLIP");
        if (string.IsNullOrEmpty(clip) || !File.Exists(clip)) return;
        Ffmpeg.SkipIfMissing();
        var report = new List<string>();
        foreach (var (format, ext) in new[] { ("mp4", ".mp4"), ("movp", ".mov") })
        {
            var work = Directory.CreateTempSubdirectory("videoprep-real-").FullName;
            try
            {
                var src = Path.Combine(work, "src.mp4");
                File.Copy(clip, src);
                using var rig = new FakeYtDlpRig(src);
                var before = Ffmpeg.Sha256(src);
                var r = await new JobEngine(new ProcessRunner(), rig.Log).RunAsync(Jobs(format), Ctx(rig), TestContext.Current.CancellationToken);
                report.Add($"== {format}: ok={r.Ok} warn={r.Warn} fail={r.Fail}");
                report.Add(rig.LogText);
                Assert.True((r.Ok, r.Warn, r.Fail) == (1, 0, 0), rig.LogText);
                var outFile = Path.Combine(rig.Folder, r.Files["1|0"]);
                var info = Ffmpeg.ProbeFile(outFile);
                var analysis = VfrDetector.Analyze(info, await Sample(outFile));
                report.Add($"result: {info.Summary}; variable now: {analysis.IsVariable}");
                Assert.False(analysis.IsVariable, analysis.Reason);
                var dest = Environment.GetEnvironmentVariable("VIDEOPREP_REAL_CLIP_OUT");
                if (!string.IsNullOrEmpty(dest))
                {
                    Directory.CreateDirectory(dest);
                    File.Copy(outFile, Path.Combine(dest, "resultado-" + format + ext), true);
                }
                Assert.Equal(before, Ffmpeg.Sha256(src));
            }
            finally { try { Directory.Delete(work, true); } catch (IOException) { } }
        }
        var outDir = Environment.GetEnvironmentVariable("VIDEOPREP_REAL_CLIP_OUT");
        if (!string.IsNullOrEmpty(outDir)) File.WriteAllLines(Path.Combine(outDir, "informe.txt"), report);
    }

    // ---------------- a whole video, every format ----------------

    [Theory]
    [InlineData("mp4", ".mp4")]
    [InlineData("movh", ".mov")]
    [InlineData("movp", ".mov")]
    [InlineData("webm", ".webm")]
    [InlineData("mkv", ".mkv")]
    public async Task A_whole_variable_video_becomes_constant_with_audio_in_step(string format, string ext)
    {
        Ffmpeg.SkipIfMissing();
        var vfr = Ffmpeg.MakeVfrSource(_dir);
        var sourceSeconds = Ffmpeg.ProbeFile(vfr).Video!.Duration!.Value;
        using var rig = new FakeYtDlpRig(vfr);

        var r = await rig.Engine.RunAsync(Jobs(format), Ctx(rig), TestContext.Current.CancellationToken);

        Assert.True((r.Ok, r.Warn, r.Fail) == (1, 0, 0), rig.LogText);
        Assert.Equal("Fake video v1" + ext, r.Files["1|0"]);                          // the clean name, never "Fake video v1_2"
        var output = Path.Combine(rig.Folder, r.Files["1|0"]);
        Assert.Equal([r.Files["1|0"]], Directory.GetFiles(rig.Folder).Select(Path.GetFileName));   // the original is gone, no temp file is left
        Assert.Contains("frame rate: VARIABLE", rig.LogText);
        Assert.Contains("will be made constant at 30 fps", rig.LogText);

        // Checked on its own, not through the app's verifier: the frames, the rate, the length, the audio.
        var frames = Ffmpeg.CountFrames(output);
        Assert.InRange(frames, Math.Round(sourceSeconds * 30) - 1, Math.Round(sourceSeconds * 30) + 1);
        var info = Ffmpeg.ProbeFile(output);
        Assert.InRange(info.Video!.AvgFrameRate.Value, 29.97, 30.03);
        if (format is "mp4" or "movh" or "movp") Assert.Equal(new Rational(30, 1), info.Video.RFrameRate);
        Assert.InRange(info.FormatDuration!.Value, sourceSeconds - 1.0 / 30 - 0.04, sourceSeconds + 1.0 / 30 + 0.04);
        Ffmpeg.AssertAudioDecodes(output);
        var after = await Sample(output);
        Assert.False(VfrDetector.Analyze(info, after).IsVariable);
        if (format == "mp4") Assert.Equal("aac", info.Audio!.Codec);                  // the audio was copied, not re-encoded
    }

    [Fact]
    public async Task An_NTSC_target_is_exact_in_an_mp4()
    {
        Ffmpeg.SkipIfMissing();
        // 59.94 fps frames, every second one kept and every 14th of those dropped: about 27.8 fps on average -> 29.97.
        var vfr = Ffmpeg.MakeVfrSource(_dir, baseRate: "60000/1001", select: "not(mod(n,2))*mod(n,28)");
        Assert.True(Ffmpeg.ProbeFile(vfr).LooksVariableFrameRate);
        using var rig = new FakeYtDlpRig(vfr);

        var r = await rig.Engine.RunAsync(Jobs("mp4"), Ctx(rig), TestContext.Current.CancellationToken);

        Assert.Equal(JobStatus.OK, r.Statuses["1|0"]);
        Assert.Contains("29.97 fps", rig.LogText);
        var output = Path.Combine(rig.Folder, r.Files["1|0"]);
        var info = Ffmpeg.ProbeFile(output);
        Assert.Equal((new Rational(30000, 1001), new Rational(30000, 1001)), (info.Video!.RFrameRate, info.Video.AvgFrameRate));
        Assert.Equal("1/30000", Ffmpeg.StreamField(output, "v:0", "time_base"));       // the track clock is the numerator: exact
        await Task.CompletedTask;
    }

    // ---------------- cut and constant in the same pass ----------------

    [Theory]
    [InlineData("mp4", ".mp4")]
    [InlineData("movp", ".mov")]
    [InlineData("webm", ".webm")]
    public async Task Exact_clips_of_a_variable_video_on_the_new_frame_grid(string format, string ext)
    {
        Ffmpeg.SkipIfMissing();
        var vfr = Ffmpeg.MakeVfrSource(_dir);
        using var rig = new FakeYtDlpRig(vfr);

        var r = await rig.Engine.RunAsync(Jobs(format, clips: ThreeClips), Ctx(rig), TestContext.Current.CancellationToken);

        Assert.Equal((3, 0, 0), (r.Ok, r.Warn, r.Fail));
        string[] names = [$"Fake video v1 - 1{ext}", $"Fake video v1 - 2{ext}", $"Fake video v1 - 3{ext}"];
        Assert.Equal(names, r.Files.OrderBy(kv => kv.Key).Select(kv => kv.Value));
        long[] expected = [30, 21, 15];                                                // one second, 21 frames, the last 15
        for (var i = 0; i < 3; i++)
        {
            var output = Path.Combine(rig.Folder, names[i]);
            Assert.Equal(expected[i], Ffmpeg.CountFrames(output));                    // exact, counted by decoding the video only
            Ffmpeg.AssertAudioDecodes(output);
            var info = Ffmpeg.ProbeFile(output);
            Assert.InRange(info.Video!.AvgFrameRate.Value, 29.97, 30.03);
            Assert.False(VfrDetector.Analyze(info, await Sample(output)).IsVariable);
        }
        Assert.DoesNotContain(Directory.GetFiles(rig.Folder), f => f.Contains("__full", StringComparison.Ordinal));   // the full video went after the clips
        Assert.Contains("variable -> 30 fps constant", rig.LogText);
    }

    // ---------------- nothing to do, nothing done ----------------

    [Fact]
    public async Task A_constant_rate_video_is_never_re_encoded()
    {
        Ffmpeg.SkipIfMissing();
        var constant = Ffmpeg.MakeSource(_dir, "30000/1001", 6, "constant.mp4");
        using var rig = new FakeYtDlpRig(constant);

        var r = await rig.Engine.RunAsync(Jobs("mp4"), Ctx(rig), TestContext.Current.CancellationToken);

        Assert.Equal(JobStatus.OK, r.Statuses["1|0"]);
        Assert.Equal(Ffmpeg.Sha256(constant), Ffmpeg.Sha256(Path.Combine(rig.Folder, "Fake video v1.mp4")));   // the same bytes
        Assert.Contains("frame rate: constant", rig.LogText);
        Assert.DoesNotContain(".__tmp", rig.LogText);
        Assert.DoesNotContain("converting", rig.LogText);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task With_the_option_off_a_variable_video_is_left_as_it_came()
    {
        Ffmpeg.SkipIfMissing();
        var vfr = Ffmpeg.MakeVfrSource(_dir);
        using var rig = new FakeYtDlpRig(vfr);

        var r = await rig.Engine.RunAsync(Jobs("mp4", constant: false), Ctx(rig), TestContext.Current.CancellationToken);

        Assert.Equal(JobStatus.OK, r.Statuses["1|0"]);
        Assert.Equal(Ffmpeg.Sha256(vfr), Ffmpeg.Sha256(Path.Combine(rig.Folder, "Fake video v1.mp4")));
        Assert.DoesNotContain("frame rate:", rig.LogText);
        await Task.CompletedTask;
    }

    // ---------------- a failed check keeps the original ----------------

    /// <summary>Runs the real ffmpeg but with one extra option before the output file, to make the result wrong.</summary>
    private sealed class SpoilingRunner(params string[] extra) : IProcessRunner
    {
        private readonly ProcessRunner _real = new();

        public Task<ProcessResult> RunAsync(ProcessSpec spec, Action<string> onLine, CancellationToken ct)
        {
            if (!spec.Args.Contains("-fps_mode")) return _real.RunAsync(spec, onLine, ct);
            var args = spec.Args.Take(spec.Args.Count - 1).Concat(extra).Append(spec.Args[^1]).ToList();
            return _real.RunAsync(spec with { Args = args }, onLine, ct);
        }
    }

    [Theory]
    [InlineData("mp4", "-t|4", "the result has 120 frames")]                        // far too short: the usual check of the engine
    [InlineData("mp4", "-an", "the result has no audio stream")]               // the audio vanished
    [InlineData("webm", "-af|asetpts=PTS+0.5/TB", "audio and video are out of step")]  // the audio drifted half a second: the F8 check
    public async Task When_a_check_fails_the_original_stays_intact_and_the_row_goes_to_review(string format, string spoil, string reason)
    {
        Ffmpeg.SkipIfMissing();
        var vfr = Ffmpeg.MakeVfrSource(_dir);
        using var rig = new FakeYtDlpRig(vfr);
        var engine = new JobEngine(new SpoilingRunner(spoil.Split('|')), rig.Log);

        var r = await engine.RunAsync(Jobs(format), Ctx(rig), TestContext.Current.CancellationToken);

        Assert.True(r.Statuses["1|0"] == JobStatus.REVIEW, rig.LogText);
        Assert.StartsWith(reason, r.Reasons["1|0"].Replace("the result did not pass the check: ", ""));
        Assert.Equal((0, 1, 0), (r.Ok, r.Warn, r.Fail));
        // Nothing was deleted or replaced: the original is there, byte for byte, and no temporary file is left.
        Assert.Equal(["Fake video v1.mp4"], Directory.GetFiles(rig.Folder).Select(Path.GetFileName));
        Assert.Equal(Ffmpeg.Sha256(vfr), Ffmpeg.Sha256(Path.Combine(rig.Folder, "Fake video v1.mp4")));
        Assert.Contains("REVIEW", rig.LogText);
    }

    [Fact]
    public async Task A_clip_whose_check_fails_keeps_the_full_video_too()
    {
        Ffmpeg.SkipIfMissing();
        var vfr = Ffmpeg.MakeVfrSource(_dir);
        using var rig = new FakeYtDlpRig(vfr);
        var engine = new JobEngine(new SpoilingRunner("-an"), rig.Log);

        var r = await engine.RunAsync(Jobs("mp4", clips: [new("00:00:01:00", "00:00:01:29")]), Ctx(rig), TestContext.Current.CancellationToken);

        Assert.Equal(JobStatus.REVIEW, r.Statuses["1|0"]);
        Assert.Equal(["Fake video v1__full.mp4"], Directory.GetFiles(rig.Folder).Select(Path.GetFileName));
        await Task.CompletedTask;
    }
}
