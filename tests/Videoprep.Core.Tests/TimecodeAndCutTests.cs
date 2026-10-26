using Videoprep.Core.Media;
using Videoprep.Core.Text;

namespace Videoprep.Core.Tests;

public class TimecodeTests
{
    [Theory]
    [InlineData("1523", 0, 0, 15, 23)]          // tip text: 1523 -> 00:00:15:23
    [InlineData("100", 0, 0, 1, 0)]             // tip text: 100 -> 00:00:01:00
    [InlineData("00:01:23:15", 0, 1, 23, 15)]
    [InlineData("1:23", 0, 0, 1, 23)]           // shorter forms fill from the left
    [InlineData("2:03:04", 0, 2, 3, 4)]
    [InlineData("  01:00:00:00 ", 1, 0, 0, 0)]
    [InlineData("12345678", 12, 34, 56, 78)]    // frames are checked later, against the video's fps
    public void Parses_valid_forms(string text, int h, int m, int s, int f)
    {
        var tc = Timecode.Parse(text);
        Assert.True(tc.Valid);
        Assert.False(tc.Blank);
        Assert.Equal((h, m, s, f), (tc.H, tc.M, tc.S, tc.F));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Empty_is_blank(string? text)
    {
        var tc = Timecode.Parse(text);
        Assert.True(tc.Blank);
        Assert.False(tc.Valid);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("00:60:00:00")]   // minutes >= 60
    [InlineData("00:00:60:00")]   // seconds >= 60
    [InlineData("123456789")]     // more than 8 digits
    [InlineData("1:2:3:4:5")]
    [InlineData("1.5")]
    [InlineData("-")]
    public void Rejects_invalid(string text) => Assert.False(Timecode.Parse(text).Valid);

    [Fact]
    public void Minus_means_from_the_end()
    {
        var tc = Timecode.Parse("-00:00:00:15");
        Assert.True(tc.Valid);
        Assert.True(tc.Negative);
        Assert.Equal(15, tc.F);
    }

    [Fact]
    public void Semicolon_means_drop_frame()
    {
        var tc = Timecode.Parse("00;01;00;02");
        Assert.True(tc.Valid);
        Assert.True(tc.DropFrame);
    }

    [Theory]
    [InlineData("00:00:01:00", 30, false, 30)]
    [InlineData("00:01:00:00", 25, false, 1500)]
    [InlineData("00;01;00;02", 30, true, 1800)]     // SMPTE: 00:01:00;02 is frame 1800 at 29.97 DF
    [InlineData("00;10;00;00", 30, true, 17982)]    // 10 minutes DF
    [InlineData("01;00;00;00", 30, true, 107892)]   // 1 hour DF
    [InlineData("00;01;00;04", 60, true, 3600)]     // 59.94 DF drops 4 per minute
    [InlineData("00;01;00;02", 30, false, 1802)]    // ';' ignored when the source is not NTSC
    [InlineData("00;01;00;02", 25, true, 1502)]     // and when the base is not 30/60
    public void Frame_index(string text, int fpsBase, bool ntsc, long expected) =>
        Assert.Equal(expected, Timecode.Parse(text).ToFrameIndex(fpsBase, ntsc));
}

public class CutPlannerTests
{
    // 29.97 fps, 120 frames (4.004 s): the ntsc2997 fixture.
    private static readonly SourceTiming Ntsc = new(true, "", 30000, 1001, 30, true, 120, "h264", "aac", false);

    [Fact]
    public void Plain_range_includes_both_ends_with_quarter_frame_guard()
    {
        var p = CutPlanner.Plan("00:00:01:00", "00:00:02:00", Ntsc);
        Assert.True(p.Ok, p.Error);
        Assert.Equal(30, p.StartFrame);
        Assert.Equal(60, p.EndFrame);
        Assert.Equal(31, p.Frames);
        const double fd = 1001.0 / 30000;
        Assert.Equal((30 - 0.25) * fd, p.T0, 9);
        Assert.Equal((60 + 0.75) * fd - (30 - 0.25) * fd, p.Duration, 9);
        Assert.Equal(30000.0 / 1001, p.Fps, 9);
        Assert.Equal("", p.Note);
    }

    [Fact]
    public void Start_at_zero_has_no_guard()
    {
        var p = CutPlanner.Plan("", "00:00:00:09", Ntsc);
        Assert.True(p.Ok);
        Assert.Equal(0, p.T0);
        Assert.Equal(10, p.Frames);
    }

    [Fact]
    public void Blank_end_goes_to_the_last_frame()
    {
        var p = CutPlanner.Plan("00:00:03:00", "", Ntsc);
        Assert.Equal((90, 119), (p.StartFrame, p.EndFrame));
    }

    [Fact]
    public void Negative_start_takes_the_last_frames()
    {
        var p = CutPlanner.Plan("-00:00:00:15", "", Ntsc);
        Assert.True(p.Ok);
        Assert.Equal((105, 119, 15L), (p.StartFrame, p.EndFrame, p.Frames));
    }

    [Fact]
    public void End_past_the_video_is_clamped_with_a_note()
    {
        var p = CutPlanner.Plan("00:00:01:00", "00:00:10:00", Ntsc);
        Assert.True(p.Ok);
        Assert.Equal(119, p.EndFrame);
        Assert.Equal("End was past the last frame - used the last frame", p.Note);
    }

    [Theory]
    [InlineData("00:00:01:30", "", "frames must be 0-29 for this video (30 fps)")]
    [InlineData("xx", "", "invalid timecode")]
    [InlineData("", "-00:00:01:00", "End cannot start with a minus sign")]
    [InlineData("-00:00:05:00", "", "Start is before the beginning of the video")]
    [InlineData("00:00:04:00", "", "Start is after the end of the video (120 frames)")]
    [InlineData("00:00:02:00", "00:00:01:00", "End is before Start")]
    public void Errors_match_the_legacy_messages(string start, string end, string error)
    {
        var p = CutPlanner.Plan(start, end, Ntsc);
        Assert.False(p.Ok);
        Assert.Equal(error, p.Error);
    }
}
