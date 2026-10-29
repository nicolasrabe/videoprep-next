using System.Text.Json;
using Videoprep.Core.Engine;
using Videoprep.Core.Formats;
using Videoprep.Core.Media;
using Videoprep.Core.Models;
using Videoprep.Core.Profiles;
using Videoprep.Core.Scraping;
using Videoprep.Core.Storage;

namespace Videoprep.Core.Tests;

/// <summary>F8 "Normalizar a velocidad constante" without ffmpeg: the detector, the rates, the arguments, the checks.</summary>
public sealed class ConstantRateTests
{
    // ---------------- frame times ----------------

    /// <summary>n frame times at a constant interval, rounded to a clock tick (1/1000 for MKV/WebM, 1/30000 for MP4).</summary>
    private static List<double> Constant(int n, double interval, double tick = 0) =>
        Enumerable.Range(0, n).Select(i => tick > 0 ? Math.Round(i * interval / tick) * tick : i * interval).ToList();

    /// <summary>The pattern of the real test clip: ~50 fps, ~30 fps then ~10 fps (frames dropped from a 60 fps video).</summary>
    private static List<double> Variable()
    {
        var t = new List<double>();
        for (var n = 0; n < 360; n++)
        {
            var time = n / 60.0;
            var keep = time < 2 ? n % 6 != 1 : time < 4 ? n % 2 == 1 : n % 6 == 0;
            if (keep) t.Add(time);
        }
        return t;
    }

    private static MediaInfo Info(double nominal, double average) => new(
        new VideoStreamInfo("h264", 64, 64, Rational.Parse($"{(int)Math.Round(nominal * 1000)}/1000"), Rational.Parse($"{(int)Math.Round(average * 1000)}/1000"), null, 6),
        new AudioStreamInfo("aac", 48000, 1, 6), 6);

    private static FrameRateAnalysis Analyze(IReadOnlyList<double> times, double nominal, double average) =>
        VfrDetector.Analyze(Info(nominal, average), [times]);

    [Theory]
    [InlineData(30.0, 0.0)]            // exact times
    [InlineData(30000.0 / 1001, 1.0 / 30000)]   // an MP4 clock
    [InlineData(30.0, 0.001)]          // MKV/WebM: times rounded to the millisecond (33, 33, 34 ms...)
    [InlineData(60.0, 0.001)]          // 16 and 17 ms
    [InlineData(23.976, 0.001)]
    [InlineData(25.0, 1.0 / 90000)]    // an MPEG-TS clock
    public void A_constant_rate_video_is_never_taken_for_variable(double fps, double tick)
    {
        var a = Analyze(Constant(900, 1 / fps, tick), fps, fps);
        Assert.False(a.IsVariable, a.Reason);
        Assert.Equal(0, a.Irregular);
    }

    [Fact]
    public void Dropped_frames_make_it_variable_and_the_target_is_the_nearest_standard_rate()
    {
        var times = Variable();
        var a = Analyze(times, 60, (times.Count - 1) / (times[^1] - times[0]));
        Assert.True(a.IsVariable, a.Reason);
        Assert.InRange(a.AverageFps, 30, 32);
        Assert.Equal(new Rational(30, 1), a.Target);
        Assert.True(a.Irregular >= 3);
        Assert.Contains("VARIABLE", a.LogText);
        Assert.Contains("30 fps", a.LogText);
    }

    [Fact]
    public void Only_a_look_at_the_start_of_a_long_video_is_not_enough_so_the_middle_counts_too()
    {
        // Regular at the start, irregular in the middle window.
        var info = Info(30, 28);
        var a = VfrDetector.Analyze(info, [Constant(600, 1 / 30.0), Variable()]);
        Assert.True(a.IsVariable);
    }

    [Fact]
    public void One_freeze_in_a_constant_video_is_not_enough_unless_the_two_ffprobe_rates_also_disagree()
    {
        var times = Constant(900, 1 / 30.0);
        times = times.Take(400).Concat(times.Skip(400).Select(t => t + 1.0)).ToList();   // a one second gap
        Assert.False(Analyze(times, 30, 29.9).IsVariable);                 // 1 irregular interval, rates agree: left alone
        Assert.True(Analyze(times, 30, 28.0).IsVariable);                  // rates disagree by 7 %: variable
    }

    [Fact]
    public void Too_few_frames_cannot_tell_and_the_video_is_left_alone()
    {
        var a = Analyze([0, 0.1, 0.5, 0.6, 2.0], 30, 5);
        Assert.False(a.IsVariable);
        Assert.Contains("only 5 frames", a.Reason);
    }

    [Fact]
    public void Frame_times_out_of_order_because_of_B_frames_are_sorted_first()
    {
        var t = Constant(300, 1 / 30.0);
        var shuffled = t.Select((x, i) => (x, k: i / 3 * 3 + (2 - i % 3))).OrderBy(p => p.k).Select(p => p.x).ToList();
        Assert.False(Analyze(shuffled, 30, 30).IsVariable);
    }

    // ---------------- rates ----------------

    [Theory]
    [InlineData(31.19, 30, 1)]
    [InlineData(29.9, 30000, 1001)]
    [InlineData(27.0, 25, 1)]
    [InlineData(59.5, 60000, 1001)]
    [InlineData(54.0, 50, 1)]
    [InlineData(12.0, 24000, 1001)]    // very slow: the lowest standard rate
    [InlineData(23.5, 24000, 1001)]
    [InlineData(100.0, 60, 1)]         // faster than 60: 60
    public void The_average_snaps_to_a_standard_rate(double average, int num, int den) =>
        Assert.Equal(new Rational(num, den), FrameRates.Snap(average));

    [Fact]
    public void Rates_are_written_for_people_and_for_ffmpeg()
    {
        Assert.Equal(("29.97", "30000/1001"), (FrameRates.Label(new(30000, 1001)), FrameRates.ForFfmpeg(new(30000, 1001))));
        Assert.Equal(("30", "30"), (FrameRates.Label(new(30, 1)), FrameRates.ForFfmpeg(new(30, 1))));
        Assert.Equal("23.976", FrameRates.Label(new(24000, 1001)));
    }

    [Fact]
    public void Timecodes_of_a_variable_video_count_on_the_new_frame_grid()
    {
        var t = SourceTiming.ForConstantRate(Info(60, 31.2), new Rational(30, 1), 5.766667);
        Assert.Equal((true, 30, 1, 30, false, 173L), (t.Ok, t.Num, t.Den, t.FpsBase, t.Ntsc, t.TotalFrames));
        var n = SourceTiming.ForConstantRate(Info(60, 29.9), new Rational(30000, 1001), 10);
        Assert.Equal((30, true, 300L), (n.FpsBase, n.Ntsc, n.TotalFrames));
        var plan = CutPlanner.Plan("00:00:01:00", "00:00:01:29", t);
        Assert.True(plan.Ok);
        Assert.Equal(30, plan.Frames);                                 // one second = 30 frames of the new grid
    }

    // ---------------- ffmpeg arguments ----------------

    private static Job J(string format, bool range = false) => new()
    {
        GroupId = 1, Index = 0, Count = 1, Url = "u", Site = Site.Youtube, Format = format, Cap = 1080, ConstantRate = true,
        Start = range ? "00:00:01:00" : "", End = range ? "00:00:01:29" : "",
    };

    private static CfrPlan Cfr(int num = 30, int den = 1) => new(new Rational(num, den), 5.766667, Analyze(Variable(), 60, 31.2));

    private static string Joined(IReadOnlyList<string> a) => string.Join(' ', a);

    [Theory]
    [InlineData("mp4", "libx264")]
    [InlineData("movh", "libx264")]
    [InlineData("mkv", "libx264")]
    [InlineData("movp", "prores_ks")]
    [InlineData("webm", "libvpx-vp9")]
    public void A_whole_video_is_made_constant_in_one_pass_with_fps_mode_and_never_longer_than_the_original(string format, string codec)
    {
        var timing = SourceTiming.ForConstantRate(Info(60, 31.2), new Rational(30, 1), 5.766667);
        var a = FfmpegArgs.Encode(J(format), null, timing, 14, "in.mp4", "out.tmp", Cfr());
        var line = Joined(a);
        Assert.Contains("-fps_mode cfr -r 30", line);
        Assert.DoesNotContain("-vsync", line);
        Assert.DoesNotContain("-c:v copy", line);                        // a constant rate forces a re-encode of the video
        Assert.Contains("-c:v " + codec, line);
        Assert.Contains("-t 5.766667", line);                            // without this limit the test clip got 5 frames too many
        Assert.DoesNotContain("-frames:v", line);
    }

    [Fact]
    public void A_constant_rate_video_still_copies_its_streams_when_the_option_finds_nothing_to_do()
    {
        var timing = new SourceTiming(true, "", 30, 1, 30, false, 180, "h264", "aac", false);
        var line = Joined(FfmpegArgs.Encode(J("mp4"), null, timing, 14, "in.mp4", "out.tmp"));    // no CfrPlan = untouched
        Assert.Contains("-c:v copy", line);
        Assert.DoesNotContain("-fps_mode", line);
    }

    [Fact]
    public void A_clip_is_cut_and_made_constant_in_the_same_pass_on_the_new_frame_grid()
    {
        var timing = SourceTiming.ForConstantRate(Info(60, 31.2), new Rational(30, 1), 5.766667);
        var plan = CutPlanner.Plan("00:00:01:00", "00:00:01:29", timing);
        var line = Joined(FfmpegArgs.Encode(J("mp4", range: true), plan, timing, 14, "in.mp4", "out.tmp", Cfr()));
        Assert.Contains("-ss 0.991667", line);                          // (30 - 0.25) / 30
        Assert.Contains("-t 1 ", line + " ");
        Assert.Contains("-frames:v 30", line);
        Assert.Contains("-fps_mode cfr -r 30", line);
        Assert.True(line.IndexOf("-ss", StringComparison.Ordinal) < line.IndexOf("-i in.mp4", StringComparison.Ordinal));   // fast seek on the input
    }

    [Theory]
    [InlineData("mp4", true)]
    [InlineData("movh", true)]
    [InlineData("movp", true)]
    [InlineData("webm", false)]
    [InlineData("mkv", false)]
    public void NTSC_rates_are_exact_in_mp4_and_mov_because_the_track_clock_is_30000(string format, bool timescale)
    {
        var timing = SourceTiming.ForConstantRate(Info(60, 29.9), new Rational(30000, 1001), 5.766667);
        var line = Joined(FfmpegArgs.Encode(J(format), null, timing, 14, "in.mp4", "out.tmp", Cfr(30000, 1001)));
        Assert.Contains("-fps_mode cfr -r 30000/1001", line);
        Assert.Equal(timescale, line.Contains("-video_track_timescale 30000", StringComparison.Ordinal));
    }

    [Fact]
    public void Audio_formats_ignore_the_option()
    {
        var timing = new SourceTiming(true, "", 30, 1, 30, false, 180, "", "aac", false);
        var line = Joined(FfmpegArgs.Encode(J("mp3"), null, timing, 14, "in.mp4", "out.mp3", Cfr()));
        Assert.DoesNotContain("-fps_mode", line);
        Assert.DoesNotContain("-t 5", line);
    }

    // ---------------- the checks before anything is replaced ----------------

    /// <summary>What an ideal made-constant file looks like: n frames at the rate, audio from -0.021 s to the end.</summary>
    private static StreamPackets Good(int frames, double rate = 30, double audioStart = -0.021, double audioEnd = -1)
    {
        var times = Enumerable.Range(0, frames).Select(i => i / rate).ToList();
        return new StreamPackets(times, audioStart, audioEnd < 0 ? frames / rate : audioEnd);
    }

    private static CfrPlan PlanWithSource(double seconds = 5.766667, double sourceAudioStart = -0.021, double sourceAudioEnd = 5.766667) =>
        new(new Rational(30, 1), seconds, Analyze(Variable(), 60, 31.2),
            new StreamPackets(Variable(), sourceAudioStart, sourceAudioEnd));

    [Fact]
    public void A_good_result_passes_a_whole_video_may_differ_by_one_frame_in_count_and_length()
    {
        var plan = PlanWithSource();
        Assert.True(CfrVerifier.Check(Good(173), plan, null).Ok);
        // The audio is cut at the same length limit as the video, so it does not grow with an extra frame.
        Assert.True(CfrVerifier.Check(Good(174, audioEnd: 5.766667), plan, null).Ok);       // one frame more
        Assert.True(CfrVerifier.Check(Good(172, audioEnd: 5.766667), plan, null).Ok);       // one frame less
    }

    [Theory]
    [InlineData(178, "the result has 178 frames, expected 173")]      // what ffmpeg did without the length limit: 5 frames more
    [InlineData(160, "the result has 160 frames, expected 173")]
    [InlineData(0, "the result has no video frames")]
    public void A_wrong_number_of_frames_is_refused(int frames, string reason)
    {
        var result = CfrVerifier.Check(Good(frames), PlanWithSource(), null);
        Assert.False(result.Ok);
        Assert.Equal(reason, result.Reason);
    }

    [Fact]
    public void A_clip_needs_exactly_the_planned_frames()
    {
        var timing = SourceTiming.ForConstantRate(Info(60, 31.2), new Rational(30, 1), 5.766667);
        var plan = CutPlanner.Plan("00:00:01:00", "00:00:01:29", timing);
        var cfr = PlanWithSource();
        Assert.True(CfrVerifier.Check(Good(30, audioStart: 0, audioEnd: 1.0), cfr, plan).Ok);
        Assert.Equal("the result has 31 frames, expected 30", CfrVerifier.Check(Good(31, audioStart: 0, audioEnd: 1.0), cfr, plan).Reason);
    }

    [Fact]
    public void A_length_that_changed_by_more_than_one_frame_is_refused()
    {
        // 173 frames but spread over 6.2 s: right count, wrong length (frames at 28 per second).
        var slow = new StreamPackets(Enumerable.Range(0, 173).Select(i => i * 6.2 / 173).ToList(), -0.021, 5.766667);
        var r = CfrVerifier.Check(slow, PlanWithSource(), null);
        Assert.False(r.Ok);
        Assert.Contains("more than one frame of difference", r.Reason);
    }

    [Fact]
    public void Audio_that_drifted_from_the_video_is_refused()
    {
        var plan = PlanWithSource();
        // The audio starts half a second later than it did in the original.
        var late = CfrVerifier.Check(Good(173, audioStart: 0.479, audioEnd: 5.766667), plan, null);
        Assert.False(late.Ok);
        Assert.Contains("out of step", late.Reason);
        // The audio is 0.4 s shorter than the original's.
        var shortAudio = CfrVerifier.Check(Good(173, audioEnd: 5.366), plan, null);
        Assert.False(shortAudio.Ok);
        Assert.Contains("the audio lasts", shortAudio.Reason);
        // The audio vanished.
        Assert.Equal("the result has no audio, the original had", CfrVerifier.Check(new StreamPackets(Good(173).VideoTimes, null, null), plan, null).Reason);
    }

    [Fact]
    public void A_source_that_was_already_out_of_step_is_not_blamed_on_the_conversion()
    {
        // The original's audio started 80 ms after its video and ended 90 ms early; the result keeps exactly that.
        var plan = PlanWithSource(sourceAudioStart: 0.08, sourceAudioEnd: 5.676667);
        Assert.True(CfrVerifier.Check(Good(173, audioStart: 0.08, audioEnd: 5.676667), plan, null).Ok);
    }

    [Fact]
    public void A_result_that_is_still_irregular_is_refused_but_milliseconds_of_rounding_are_not()
    {
        var plan = PlanWithSource();
        var rounded = new StreamPackets(Enumerable.Range(0, 173).Select(i => Math.Round(i / 30.0, 3)).ToList(), -0.021, 5.766667);
        Assert.True(CfrVerifier.Check(rounded, plan, null).Ok);                  // WebM-style 33/34 ms
        var times = Good(173).VideoTimes.ToList();
        for (var i = 40; i < 173; i += 2) times[i] += 0.012;                       // every second frame 12 ms late
        var bad = CfrVerifier.Check(new StreamPackets(times, -0.021, 5.766667), plan, null);
        Assert.False(bad.Ok);
        Assert.Contains("irregular frame rate", bad.Reason);
    }

    [Fact]
    public void Without_a_measurable_original_audio_and_video_must_at_least_start_and_end_together()
    {
        var plan = new CfrPlan(new Rational(30, 1), 5.766667, Analyze(Variable(), 60, 31.2), null);
        Assert.True(CfrVerifier.Check(Good(173, audioStart: 0, audioEnd: 5.766667), plan, null).Ok);
        Assert.False(CfrVerifier.Check(Good(173, audioStart: 0, audioEnd: 5.0), plan, null).Ok);
    }

    // ---------------- reading ffprobe's lines ----------------

    [Theory]
    [InlineData("0.033000,0.033000", 0.033, 0.033)]
    [InlineData("0.854000,0.020000,953,", 0.854, 0.02)]
    [InlineData("1.5,", 1.5, null)]
    [InlineData("N/A,N/A", null, null)]
    [InlineData("[opus @ 0x58] Error parsing Opus packet header.", null, null)]
    public void Packet_lines_give_the_time_and_the_length_when_they_are_numbers(string line, double? time, double? duration) =>
        Assert.Equal((time, duration), PacketProbe.ParseLine(line));

    [Fact]
    public void A_window_of_packets_is_asked_from_a_time_and_a_count()
    {
        var a = PacketProbe.WindowArgs("f.mp4", 12.5, 300);
        Assert.Equal("12.5%+#300", a[a.ToList().IndexOf("-read_intervals") + 1]);
        Assert.Contains("v:0", a);
    }

    // ---------------- files from before F8 ----------------

    [Fact]
    public void A_queue_saved_before_F8_loads_with_the_option_off_and_the_option_is_saved_when_on()
    {
        var dir = Directory.CreateTempSubdirectory("videoprep-cfr-").FullName;
        var path = Path.Combine(dir, "queue.json");
        File.WriteAllText(path, """{"version":1,"items":[{"id":1,"url":"https://vimeo.com/1","format":"mp4","cap":1080,"naming":"original"}]}""");
        var old = Assert.Single(QueueStore.Load(path, out _));
        Assert.False(old.ConstantRate);
        QueueStore.Save(path, [old with { ConstantRate = true }]);
        Assert.True(Assert.Single(QueueStore.Load(path, out _)).ConstantRate);
        Assert.True(JobPlanner.Build([old with { ConstantRate = true }], new Dictionary<string, int>())[0].ConstantRate);
        Directory.Delete(dir, true);
    }

    [Fact]
    public void A_subscription_follows_the_constant_rate_of_its_profile_and_a_file_without_it_still_loads()
    {
        var store = ProfileStore.InMemory();
        var p = store.Add(new FormatProfile { Name = "Twitch", Format = "mp4", Cap = 1080, ConstantRate = true });
        var own = new SubscriptionProfile { Format = "mp4", ProfileId = p.Id };
        Assert.True(ProfileLinks.Effective(own, store).ConstantRate);
        Assert.False(ProfileLinks.Effective(own, ProfileStore.InMemory()).ConstantRate);           // profile gone: the subscription's own value
        var sub = new Subscription { Id = "s", Name = "s", Url = "https://vimeo.com/x", Folder = "D", Profile = own };
        Assert.True(ProfileLinks.Sync(sub, store.Find(p.Id)!).Profile.ConstantRate);

        var json = JsonSerializer.Serialize(new SubscriptionProfile { Format = "mp4" });
        Assert.DoesNotContain("constantRate", json);                                                // not written while off: old files stay as they were
        Assert.Contains("\"constantRate\":true", JsonSerializer.Serialize(new SubscriptionProfile { ConstantRate = true }));
        Assert.False(JsonSerializer.Deserialize<SubscriptionProfile>("""{"fmt":"mp4","cap":1080}""")!.ConstantRate);
    }

    [Fact]
    public void The_setting_is_off_by_default_and_a_profile_remembers_it()
    {
        Assert.False(new Settings().ConstantRate);
        var p = new FormatProfile { Name = "x", ConstantRate = true };
        Assert.False(p.SameChoices(p with { ConstantRate = false }));
        Assert.True(FormatCatalog.IsKnown("mp4"));
    }
}
