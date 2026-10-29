using Videoprep.Core.Engine;
using Videoprep.Core.Media;
using Videoprep.Core.Models;

namespace Videoprep.Core.Tests;

/// <summary>
/// CI diagnostic for the WebM cut (Opus audio, "Error parsing Opus packet header" on Ubuntu). It makes the SAME cut that
/// <see cref="FrameExactCutTests"/> makes for WebM, once with every ffmpeg named in VIDEOPREP_DIAG_FFMPEGS
/// ("label=folder;label=folder"), into VIDEOPREP_DIAG_DIR. Then tests/ci/webm-diagnostic.sh decodes each file with each
/// ffmpeg and prints the versions, the messages and the audio stream data. It does nothing (and costs nothing) when those
/// variables are not set, so a local run is unchanged. It checks only that every ffmpeg can make the cut.
/// </summary>
[Trait("Category", "Integration")]
public sealed class WebmCutDiagnostic
{
    [Fact]
    public async Task Makes_the_webm_cut_with_each_ffmpeg_for_the_ci_diagnostic()
    {
        var outDir = Environment.GetEnvironmentVariable("VIDEOPREP_DIAG_DIR");
        var list = Environment.GetEnvironmentVariable("VIDEOPREP_DIAG_FFMPEGS");
        if (string.IsNullOrEmpty(outDir) || string.IsNullOrEmpty(list)) return;   // only the CI diagnostic step sets these
        Ffmpeg.SkipIfMissing();
        Directory.CreateDirectory(outDir);

        // The same input and the same cut as Other_video_formats_are_frame_exact("webm").
        var source = Ffmpeg.MakeSource(outDir, "30000/1001", 4, "source.mp4");
        var timing = SourceTiming.FromVideo(Ffmpeg.ProbeFile(source));
        Assert.True(timing.Ok, timing.Error);
        var plan = CutPlanner.Plan("00:00:01:10", "00:00:02:05", timing);
        Assert.True(plan.Ok, plan.Error);
        var job = new Job { GroupId = 1, Index = 0, Count = 1, Url = "u", Site = Site.Youtube, Format = "webm", Cap = 1080, Start = "00:00:01:10", End = "00:00:02:05" };

        foreach (var item in list.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = item.IndexOf('=', StringComparison.Ordinal);
            var label = item[..eq];
            var exe = Path.Combine(item[(eq + 1)..], "ffmpeg");
            var output = Path.Combine(outDir, $"cut-{label}.webm");
            var args = FfmpegArgs.Encode(job, plan, timing, 14, source, output);
            File.WriteAllText(Path.Combine(outDir, $"cut-{label}.args.txt"), string.Join(' ', args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)));
            var lines = new List<string>();
            var r = await new ProcessRunner().RunAsync(new ProcessSpec(exe, args), l => { lock (lines) lines.Add(l); }, TestContext.Current.CancellationToken);
            File.WriteAllLines(Path.Combine(outDir, $"cut-{label}.make.log.txt"), lines);
            Assert.True(r.ExitCode == 0, $"{label}: ffmpeg could not make the cut: {string.Join(" | ", lines)}");
        }
    }
}
