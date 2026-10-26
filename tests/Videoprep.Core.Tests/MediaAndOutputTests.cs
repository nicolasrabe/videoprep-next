using System.Text.Json;
using Videoprep.Core.Engine;
using Videoprep.Core.Media;

namespace Videoprep.Core.Tests;

internal static class Fixture
{
    public static string Text(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}

/// <summary>ffprobe JSON captured from real files (docs/VERIFY.md lists the ffmpeg version).</summary>
public class MediaInfoTests
{
    [Fact]
    public void Ntsc_test_clip()
    {
        var info = MediaInfo.ParseFfprobeJson(Fixture.Text("ffprobe-ntsc2997.json"));
        Assert.Equal("h264", info.Video!.Codec);
        Assert.Equal((320, 180), (info.Video.Width, info.Video.Height));
        Assert.Equal(new Rational(30000, 1001), info.Video.RFrameRate);
        Assert.Equal(120, info.Video.FrameCount);
        Assert.Equal("aac", info.Audio!.Codec);
        Assert.Equal(48000, info.Audio.SampleRate);
        Assert.False(info.LooksVariableFrameRate);
        Assert.Equal("video h264 320x180 29.97 fps | audio aac", info.Summary);

        var t = SourceTiming.FromVideo(info);
        Assert.True(t.Ok);
        Assert.Equal((30000, 1001, 30, true, 120L), (t.Num, t.Den, t.FpsBase, t.Ntsc, t.TotalFrames));
    }

    [Fact]
    public void Youtube_download()
    {
        var info = MediaInfo.ParseFfprobeJson(Fixture.Text("ffprobe-youtube-zoo.json"));
        var t = SourceTiming.FromVideo(info);
        Assert.Equal((15, 1, 15, false, 284L), (t.Num, t.Den, t.FpsBase, t.Ntsc, t.TotalFrames));
        Assert.Equal(("h264", "aac"), (t.VideoCodec, t.AudioCodec));
    }

    [Fact]
    public void Variable_frame_rate_is_flagged_AUDIT_M4()
    {
        var info = MediaInfo.ParseFfprobeJson(Fixture.Text("ffprobe-vfr.json"));
        Assert.True(info.LooksVariableFrameRate);
        Assert.Null(info.Audio);
        Assert.True(SourceTiming.FromVideo(info).VariableFrameRate);
    }

    [Fact]
    public void Audio_only_uses_the_video_fps_for_timecodes()
    {
        var info = MediaInfo.ParseFfprobeJson(Fixture.Text("ffprobe-audio.json"));
        Assert.Null(info.Video);
        Assert.Equal("pcm_s16le", info.Audio!.Codec);
        Assert.False(SourceTiming.FromVideo(info).Ok);

        var t = SourceTiming.FromAudio(info, 29.97);
        Assert.Equal((30000, 1001, 30, true), (t.Num, t.Den, t.FpsBase, t.Ntsc));
        Assert.Equal(149, t.TotalFrames);   // floor(5 s * 29.97)
        Assert.Equal(300, SourceTiming.FromAudio(info, 60).TotalFrames);
        Assert.Equal(150, SourceTiming.FromAudio(info, null).TotalFrames);   // unknown fps: 30 is assumed...
        Assert.True(SourceTiming.FpsWasGuessed(null));                       // ...and the caller is told (AUDIT X4)
        Assert.False(SourceTiming.FpsWasGuessed(25));
    }

    [Fact]
    public void Video_without_frame_count_uses_the_duration()
    {
        const string json = """{"streams":[{"codec_type":"video","codec_name":"vp9","r_frame_rate":"25/1","avg_frame_rate":"25/1","duration":"2.00"}],"format":{"duration":"2.1"}}""";
        var t = SourceTiming.FromVideo(MediaInfo.ParseFfprobeJson(json));
        Assert.Equal(50, t.TotalFrames);
    }

    [Fact]
    public void Cover_art_is_not_taken_for_video()
    {
        const string json = """{"streams":[{"codec_type":"audio","codec_name":"aac"},{"codec_type":"video","codec_name":"mjpeg","disposition":{"attached_pic":1}}],"format":{}}""";
        Assert.Null(MediaInfo.ParseFfprobeJson(json).Video);
    }

    [Theory]
    [InlineData("""{"streams":[{"codec_type":"video","codec_name":"h264","r_frame_rate":"0/0"}],"format":{}}""", "could not read the frame rate")]
    [InlineData("""{"streams":[{"codec_type":"video","codec_name":"h264","r_frame_rate":"30/1"}],"format":{}}""", "could not read the video length")]
    [InlineData("""{"streams":[{"codec_type":"video","codec_name":"h264","r_frame_rate":"30/1","nb_frames":"0"}],"format":{}}""", "the video has no frames")]
    public void Timing_errors_match_the_legacy_messages(string json, string error)
    {
        var t = SourceTiming.FromVideo(MediaInfo.ParseFfprobeJson(json));
        Assert.False(t.Ok);
        Assert.Equal(error, t.Error);
    }
}

public class OutputLineTests
{
    [Theory]
    [InlineData("VP| 41.5%|   7.16MiB/s|00:00", 41.5, "7.16MiB/s", "00:00")]
    [InlineData("VP|100.0%|Unknown B/s|NA", 100, "", "")]          // seen in the real run (docs/VERIFY.md)
    [InlineData("VP|  3.0%|  1.20MiB/s|01:02:03", 3, "1.20MiB/s", "01:02:03")]
    public void Progress(string line, double pct, string speed, string eta)
    {
        var p = ProgressLine.TryParse(line);
        Assert.NotNull(p);
        Assert.Equal((pct, speed, eta), (p!.Percent, p.Speed, p.Eta));
    }

    [Theory]
    [InlineData("[download]  45.2% of 10MiB")]
    [InlineData("VP|NA|x|y")]
    [InlineData(@"D:\out\VP|file.mp4")]
    public void Not_progress(string line) => Assert.Null(ProgressLine.TryParse(line));

    [Fact]
    public void Preview_line_from_the_real_run()
    {
        var d = PreviewDetails.TryParse(
            "DET|19|240|15|avc1.4D400C|mp4a.40.2|20050424|jawed|https://i.ytimg.com/vi/jNQXAC9IVRw/hqdefault.jpg?sqp=x|Me at the zoo");
        Assert.NotNull(d);
        Assert.Equal("19", d!.Duration);
        Assert.Equal("240p15", d.Resolution);
        Assert.Equal(15, d.Fps);
        Assert.Equal(("H.264", "AAC"), (d.VideoLabel, d.AudioLabel));
        Assert.Equal("2005-04-24", d.Date);
        Assert.Equal("jawed", d.Uploader);
        Assert.StartsWith("https://i.ytimg.com/", d.Thumbnail);
        Assert.Equal("Me at the zoo", d.Title);
    }

    [Fact]
    public void Preview_line_with_missing_fields_and_pipes_in_title()
    {
        var d = PreviewDetails.TryParse("DET|NA|NA|NA|vp09.00.40.08|opus|NA|NA|NA|A | B | C")!;
        Assert.Equal(("", "", (double?)null), (d.Duration, d.Resolution, d.Fps));
        Assert.Equal(("VP9", "Opus"), (d.VideoLabel, d.AudioLabel));
        Assert.Equal(("", "", ""), (d.Date, d.Uploader, d.Thumbnail));
        Assert.Equal("A | B | C", d.Title);
        Assert.Equal("", PreviewDetails.TryParse("DET|1:00|1080|30|none|none|x|y|z|NA")!.Title);
        Assert.Null(PreviewDetails.TryParse("[youtube] jNQXAC9IVRw: Downloading webpage"));
    }
}

/// <summary>The flat channel listing fixture used by Scraping in phase 5; checked here so its shape is pinned.</summary>
public class FlatListingFixtureTests
{
    [Fact]
    public void Has_ids_titles_durations_and_approximate_timestamps()
    {
        using var doc = JsonDocument.Parse(Fixture.Text("youtube-flat-channel-approx.json"));
        var entries = doc.RootElement.GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal(4, entries.Count);
        Assert.All(entries, e =>
        {
            Assert.Equal(11, e.GetProperty("id").GetString()!.Length);
            Assert.Equal(JsonValueKind.Number, e.GetProperty("duration").ValueKind);
            Assert.Equal(JsonValueKind.Number, e.GetProperty("timestamp").ValueKind);
            Assert.False(e.TryGetProperty("upload_date", out _));   // not sent at all in flat mode
        });
    }
}
