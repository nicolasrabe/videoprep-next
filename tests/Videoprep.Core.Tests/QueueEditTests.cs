using Videoprep.Core.Engine;
using Videoprep.Core.Models;
using Videoprep.Core.Preview;

namespace Videoprep.Core.Tests;

/// <summary>Editing the queue (F2), subfolders per imported file (F1) and the row card data (F3).</summary>
public sealed class QueueEditTests
{
    private static QueueItem Item(int id, NamingMode naming = NamingMode.Original, string custom = "", string prefix = "Video", int clips = 0) => new()
    {
        Id = id, Url = "https://vimeo.com/" + id, Naming = naming, Custom = custom, Prefix = prefix,
        Clips = Enumerable.Range(0, clips).Select(i => new ClipRange($"{i}:00", $"{i}:10")).ToList(),
    };

    [Fact]
    public void Rename_sets_a_custom_name_and_an_empty_name_goes_back_to_the_title()
    {
        var renamed = QueueEdits.Rename(Item(1, NamingMode.Series, prefix: "Escena"), "  Entrevista  ");
        Assert.Equal((NamingMode.Custom, "Entrevista"), (renamed.Naming, renamed.Custom));
        Assert.Equal(NamingMode.Original, QueueEdits.Rename(renamed, " ").Naming);
    }

    [Fact]
    public void Several_clips_with_one_name_become_name_1_name_2()
    {
        var names = QueueEdits.OutputNames([QueueEdits.Rename(Item(1, clips: 2), "Nombre"), Item(2)], _ => null, new Dictionary<string, int>());
        Assert.Equal(["Nombre - 1", "Nombre - 2", null], names);
    }

    [Fact]
    public void Batch_change_touches_only_what_was_chosen()
    {
        var mp3 = Item(1) with { Format = "mp3", Cap = 720 };
        var video = Item(2) with { Format = "mp4", Cap = 1080 };
        var e = new BatchEdit { Format = null, Cap = 480 };
        Assert.Equal(720, QueueEdits.Apply(mp3, e, null).Cap);       // audio has no resolution
        Assert.Equal(480, QueueEdits.Apply(video, e, null).Cap);
        Assert.Equal("mp4", QueueEdits.Apply(video, e, null).Format);
        Assert.Equal("movp", QueueEdits.Apply(video, new BatchEdit { Format = "movp" }, null).Format);
        Assert.Equal(video, QueueEdits.Apply(video, new BatchEdit(), "Título"));
    }

    [Fact]
    public void Batch_names_series_add_text_and_replace()
    {
        var custom = Item(1, NamingMode.Custom, "Entrevista parte 1");
        var titled = Item(2);
        var series = Item(3, NamingMode.Series, prefix: "Toma");

        var s = QueueEdits.Apply(custom, new BatchEdit { Names = NameChange.Series, Prefix = "Escena:" }, null);
        Assert.Equal((NamingMode.Series, "Escena"), (s.Naming, s.Prefix));

        var add = new BatchEdit { Names = NameChange.AddText, Before = "F1 - ", After = " (bruto)" };
        Assert.Equal("F1 - Entrevista parte 1 (bruto)", QueueEdits.Apply(custom, add, null).Custom);
        Assert.Equal((NamingMode.Custom, "F1 - Me at the zoo (bruto)"), (QueueEdits.Apply(titled, add, "Me at the zoo").Naming, QueueEdits.Apply(titled, add, "Me at the zoo").Custom));
        Assert.Equal(titled, QueueEdits.Apply(titled, add, null));   // title not known yet: left as it is
        Assert.Equal((NamingMode.Series, "F1 - Toma (bruto)"), (QueueEdits.Apply(series, add, null).Naming, QueueEdits.Apply(series, add, null).Prefix));

        var rep = new BatchEdit { Names = NameChange.Replace, Find = "PARTE", ReplaceWith = "toma" };
        Assert.Equal("Entrevista toma 1", QueueEdits.Apply(custom, rep, null).Custom);
    }

    [Fact]
    public void Series_names_continue_numbering_per_folder()
    {
        var a = Item(1, NamingMode.Series, prefix: "Escena");
        var b = Item(2, NamingMode.Series, prefix: "Escena", clips: 2);
        var c = Item(3, NamingMode.Series, prefix: "Escena") with { SubFolder = "guion" };
        var next = new Dictionary<string, int> { ["Escena"] = 4 };
        Assert.Equal(["Escena4", "Escena5", "Escena6", "Escena1"], QueueEdits.OutputNames([a, b, c], _ => null, next));
        Assert.Equal("guion/Escena", JobPlanner.SeriesKey(c));
        Assert.Equal([("", "Escena"), ("guion", "Escena")], JobPlanner.SeriesTargets([a, b, c]));
        var jobs = JobPlanner.Build([a, c], new Dictionary<string, int> { ["Escena"] = 7 });
        Assert.Equal(["Escena7", "Escena1"], jobs.Select(j => j.Label));
        Assert.Equal("guion", jobs[1].SubFolder);
    }

    [Fact]
    public void Finished_rows_are_not_downloaded_again()
    {
        var jobs = JobPlanner.Build([Item(1, clips: 2), Item(2)], new Dictionary<string, int>());
        var left = JobPlanner.WithoutDone(jobs, key => key is "1|0");
        Assert.Equal(["1|1", "2|0"], left.Select(j => j.Key));
        Assert.All(left, j => Assert.True(j.Download));   // the remaining clip downloads its video again
    }

    [Theory]
    [InlineData("guion", "guion")]
    [InlineData("..", "")]
    [InlineData("..\\..\\Windows", "....Windows")]
    [InlineData("C:\\Temp", "CTemp")]
    [InlineData("\\\\server\\share", "servershare")]
    public void A_subfolder_never_points_outside_the_destination(string sub, string expected)
    {
        var job = JobPlanner.Build([Item(1) with { SubFolder = sub }], new Dictionary<string, int>())[0];
        var dest = Path.Combine(Path.GetTempPath(), "dest");
        var folder = JobEngine.FolderFor(dest, job);
        Assert.Equal(expected.Length == 0 ? dest : Path.Combine(dest, expected), folder);
        Assert.StartsWith(dest, Path.GetFullPath(folder));
    }

    [Theory]
    [InlineData("38:12", 2292.0)]
    [InlineData("19", 19.0)]
    [InlineData("1:02:03", 3723.0)]
    [InlineData("NA", null)]
    [InlineData("", null)]
    public void Durations_from_yt_dlp(string text, double? seconds) => Assert.Equal(seconds, QueueEdits.ParseDuration(text));

    [Theory]
    [InlineData("00:38:12:00", false, false)]   // the very end is fine for an end
    [InlineData("00:38:12:00", true, true)]     // ...but a start there leaves nothing
    [InlineData("00:38:13:00", false, true)]
    [InlineData("00:40:00:00", false, true)]
    [InlineData("00:40:00", false, false)]      // short forms fill from the right: 40 seconds
    [InlineData("-00:38:00:00", true, false)]   // from the end, inside the video
    [InlineData("-00:39:00:00", true, true)]
    [InlineData("mal", false, false)]           // invalid text has its own red mark
    [InlineData("", false, false)]
    public void Clip_beyond_the_video_duration_is_flagged(string tc, bool isStart, bool beyond) =>
        Assert.Equal(beyond, QueueEdits.BeyondDuration(tc, 2292, 30, isEnd: !isStart));

    [Fact]
    public void Card_data_is_parsed_and_the_description_capped()
    {
        var json = """{"title": "Big Buck Bunny 60fps 4K", "channel": null, "uploader": "Blender", "upload_date": "20141110", "view_count": 23481387, "duration": 635, "duration_string": "10:35", "description": "DESC", "thumbnail": "https://i.ytimg.com/x.webp"}"""
            .Replace("DESC", new string('a', 5000));
        var info = VideoInfoService.Parse("aqz-KE-bpKQ", "https://www.youtube.com/watch?v=aqz-KE-bpKQ", json);
        Assert.Equal(("Big Buck Bunny 60fps 4K", "Blender", "20141110", 23481387L, "10:35", 635.0),
                     (info.Title, info.Channel, info.UploadDate, info.Views!.Value, info.Duration, info.DurationSeconds!.Value));
        Assert.Equal(VideoInfoService.MaxDescription + 1, info.Description.Length);
        Assert.EndsWith("…", info.Description);
    }

    [Fact]
    public void Card_command_asks_for_one_json_line_and_downloads_nothing()
    {
        var args = YtDlpArgs.Info("https://vimeo.com/1", new ToolPaths(null, null, null, null), CookieSource.NoCookies);
        Assert.Contains("--skip-download", args);
        Assert.Contains("--no-playlist", args);
        Assert.Equal(YtDlpArgs.InfoTemplate, args[args.ToList().IndexOf("--print") + 1]);
        Assert.Equal("https://vimeo.com/1", args[^1]);
    }
}
