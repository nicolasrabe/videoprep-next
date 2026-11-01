using System.Text.Json;
using Videoprep.Core.Models;

namespace Videoprep.Core.Tests;

/// <summary>RONDA 2: where the mini and micro windows go (no window involved) and the names of fragments.</summary>
public class MiniPlacementTests
{
    // Two monitors side by side: a 1920x1080 at 100 % with the taskbar at the bottom, and a 2560x1440 at 150 % to its right.
    private static readonly ScreenArea Left = new(new(0, 0, 1920, 1080), new(0, 0, 1920, 1040), 1.0);
    private static readonly ScreenArea Right = new(new(1920, 0, 2560, 1440), new(1920, 0, 2560, 1400), 1.5);
    private static readonly ScreenArea[] Both = [Left, Right];

    [Fact]
    public void The_card_goes_to_the_top_right_of_the_monitor_of_the_full_window()
    {
        var onLeft = MiniPlacement.TopRightOfWindowScreen(Both, new(200, 100, 1000, 700), 340, 296)!.Value;
        Assert.Equal((1920 - 340, 0), (onLeft.X, onLeft.Y));

        var onRight = MiniPlacement.TopRightOfWindowScreen(Both, new(2200, 100, 1500, 1050), 340, 296)!.Value;
        Assert.Equal(Right, onRight.Screen);
        Assert.Equal((1920 + 2560 - (int)(340 * 1.5), 0), (onRight.X, onRight.Y));   // this monitor's own scale: 510 px wide
    }

    [Fact]
    public void A_window_between_two_monitors_counts_for_the_one_where_most_of_it_is()
    {
        // 1400 px on the left monitor, 400 px on the right one.
        var pick = MiniPlacement.TopRightOfWindowScreen(Both, new(520, 100, 1800, 800), 340, 296)!.Value;
        Assert.Equal(Left, pick.Screen);
        var other = MiniPlacement.TopRightOfWindowScreen(Both, new(1500, 100, 1800, 800), 340, 296)!.Value;
        Assert.Equal(Right, other.Screen);
    }

    [Fact]
    public void A_maximized_window_counts_for_its_monitor()
    {
        var max = MiniPlacement.TopRightOfWindowScreen(Both, new(1920, 0, 2560, 1400), 380, 40)!.Value;
        Assert.Equal((1920 + 2560 - (int)(380 * 1.5), 0), (max.X, max.Y));
        Assert.Equal(60, MiniPlacement.PixelSize(Right, 380, 40).Height);
    }

    [Fact]
    public void It_stays_inside_the_work_area_and_never_over_the_taskbar()
    {
        var top = new ScreenArea(new(0, 0, 1920, 1080), new(0, 48, 1920, 1032), 1.0);   // taskbar on top
        var p = MiniPlacement.TopRightOfWindowScreen([top], new(100, 100, 800, 600), 340, 296)!.Value;
        Assert.Equal((1920 - 340, 48), (p.X, p.Y));
        var left = new ScreenArea(new(0, 0, 1920, 1080), new(60, 0, 1860, 1080), 1.0);   // taskbar on the left
        var q = MiniPlacement.TopRightOfWindowScreen([left], new(100, 100, 800, 600), 340, 296)!.Value;
        Assert.Equal((1920 - 340, 0), (q.X, q.Y));
        // Taller than the work area: pushed up, never below it.
        var small = new ScreenArea(new(0, 0, 800, 300), new(0, 0, 800, 260), 1.0);
        var r = MiniPlacement.TopRightOfWindowScreen([small], new(0, 0, 800, 300), 340, 296)!.Value;
        Assert.Equal(0, r.Y);
    }

    [Fact]
    public void Between_card_and_bar_the_top_right_corner_does_not_move()
    {
        // The card sits at the right monitor's corner (510 x 444 px); the bar is 570 x 60 px there: same right edge, same top.
        var card = new PixelBox(3970, 0, 510, 444);
        Assert.Equal((3910, 0), MiniPlacement.KeepTopRight(Both, card, 380, 40)!.Value);
        // ... and back to the card from the bar.
        var bar = new PixelBox(3910, 0, 570, 60);
        Assert.Equal((3970, 0), MiniPlacement.KeepTopRight(Both, bar, 340, 296)!.Value);
        // A card the person dragged somewhere else keeps ITS corner.
        var dragged = new PixelBox(500, 300, 340, 296);
        Assert.Equal((460, 300), MiniPlacement.KeepTopRight(Both, dragged, 380, 40)!.Value);
    }

    [Fact]
    public void A_corner_that_would_leave_the_screen_is_pulled_back_in()
    {
        // A card low on the left monitor (its bottom is below the work area): the bar keeps the top right corner but stays above the taskbar.
        var low = new PixelBox(1500, 1020, 340, 296);
        Assert.Equal((1460, 1000), MiniPlacement.KeepTopRight(Both, low, 380, 40)!.Value);
        // Its corner past the left edge of the work area: pulled to the edge.
        var farLeft = new PixelBox(-100, 200, 340, 296);   // right edge at 240: a 380 wide bar would start at -140
        Assert.Equal((0, 200), MiniPlacement.KeepTopRight(Both, farLeft, 380, 40)!.Value);
    }

    [Fact]
    public void A_monitor_that_is_gone_falls_back_to_the_main_one_and_no_monitors_gives_nothing()
    {
        var gone = new PixelBox(5000, 100, 1000, 700);   // the second monitor was unplugged: on none
        var p = MiniPlacement.TopRightOfWindowScreen([Left], gone, 340, 296)!.Value;
        Assert.Equal((1920 - 340, 0), (p.X, p.Y));
        Assert.Null(MiniPlacement.TopRightOfWindowScreen([], gone, 340, 296));
        Assert.Null(MiniPlacement.KeepTopRight([], gone, 340, 296));
    }
}

public class FragmentNameTests
{
    private static QueueItem Item(params ClipRange[] clips) => new()
    {
        Id = 1, Url = "https://fake.test/ok/v1", Clips = clips, PartWord = "parte",
    };

    [Fact]
    public void A_fragment_name_wins_and_the_others_are_Titulo_original_parte_N()
    {
        var item = Item(new ClipRange("1", "2", "Entrada"), new ClipRange("3", "4"), new ClipRange("5", "6", "  "), new ClipRange("7", "8", "Final: el cierre"));
        var jobs = JobPlanner.Build([item], new Dictionary<string, int>());
        Assert.Equal(["Entrada", null, null, "Final el cierre"], jobs.Select(j => j.Label));
        Assert.All(jobs, j => Assert.Equal("parte", j.PartWord));

        var names = QueueEdits.OutputNames([item], _ => "Mi video", new Dictionary<string, int>());
        Assert.Equal(["Entrada", "Mi video - parte 2", "Mi video - parte 3", "Final el cierre"], names);
    }

    [Fact]
    public void One_unnamed_fragment_is_still_parte_1_and_an_old_item_keeps_the_old_name()
    {
        Assert.Equal(["Mi video - parte 1"], QueueEdits.OutputNames([Item(new ClipRange("1", "2"))], _ => "Mi video", new Dictionary<string, int>()));
        var old = new QueueItem { Id = 1, Url = "https://fake.test/ok/v1", Clips = [new ClipRange("1", "2"), new ClipRange("3", "4")] };   // no PartWord: queued by an older version
        Assert.Equal(["Mi video - 1", "Mi video - 2"], QueueEdits.OutputNames([old], _ => "Mi video", new Dictionary<string, int>()));
    }

    [Fact]
    public void A_whole_video_is_named_as_before()
    {
        var whole = new QueueItem { Id = 1, Url = "https://fake.test/ok/v1", PartWord = "parte" };
        Assert.Equal(["Mi video"], QueueEdits.OutputNames([whole], _ => "Mi video", new Dictionary<string, int>()));
    }

    [Fact]
    public void Queues_saved_before_the_names_existed_still_load()
    {
        const string old = "{\"Id\":3,\"Url\":\"https://fake.test/ok/v1\",\"Clips\":[{\"Start\":\"00:00:01:00\",\"End\":\"00:00:02:00\"}]}";
        var item = JsonSerializer.Deserialize<QueueItem>(old)!;
        Assert.Equal(new ClipRange("00:00:01:00", "00:00:02:00", ""), Assert.Single(item.Clips));
        Assert.Equal("", item.PartWord);
        var round = JsonSerializer.Deserialize<QueueItem>(JsonSerializer.Serialize(Item(new ClipRange("1", "2", "Entrada"))))!;
        Assert.Equal("Entrada", round.Clips[0].Name);
        Assert.Equal("parte", round.PartWord);
    }
}
