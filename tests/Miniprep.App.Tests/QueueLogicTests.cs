using Miniprep.App.Logic;
using Videoprep.Core.Models;
using Videoprep.Core.Preview;

namespace Miniprep.App.Tests;

/// <summary>The queue of miniprep without any window: edits, reordering, saving and loading, names and the row menu.</summary>
public class QueueLogicTests
{
    private static MiniQueue Three(out int a, out int b, out int c)
    {
        var q = new MiniQueue();
        a = q.Add("https://www.youtube.com/watch?v=aaaaaaaaaaa").Id;
        b = q.Add("https://www.youtube.com/watch?v=bbbbbbbbbbb").Id;
        c = q.Add("https://www.youtube.com/watch?v=ccccccccccc").Id;
        return q;
    }

    private static int[] Order(MiniQueue q) => q.Entries.Select(e => e.Id).ToArray();

    [Fact]
    public void A_new_row_is_mp4_1080p_and_waiting()
    {
        var e = new MiniQueue().Add("https://www.youtube.com/watch?v=aaaaaaaaaaa");
        Assert.Equal("mp4", e.Item.Format);
        Assert.Equal(1080, e.Item.Cap);
        Assert.Equal(EntryStatus.Queued, e.Status);
    }

    [Fact]
    public void Only_waiting_rows_move_and_never_above_a_row_that_is_not_waiting()
    {
        var q = Three(out var a, out var b, out var c);
        q.SetStatus(a, EntryStatus.Done);
        Assert.True(q.Move(c, 1));                          // c goes before b
        Assert.Equal([a, c, b], Order(q));
        Assert.False(q.Move(a, 2));                         // a finished row stays
        q.Move(b, 0);                                       // dropped above the finished row: it lands below it
        Assert.Equal([a, b, c], Order(q));
        q.SetStatus(c, EntryStatus.Downloading);
        Assert.False(q.Move(c, 0));                         // a row that downloads stays
    }

    [Fact]
    public void Moving_to_the_end_works()
    {
        var q = Three(out var a, out var b, out var c);
        Assert.True(q.Move(a, 3));
        Assert.Equal([b, c, a], Order(q));
    }

    [Fact]
    public void Remove_takes_out_a_row_but_not_one_that_downloads()
    {
        var q = Three(out var a, out var b, out _);
        q.SetStatus(b, EntryStatus.Downloading);
        Assert.False(q.Remove(b));
        Assert.True(q.Remove(a));
        Assert.Equal(2, q.Entries.Count);
    }

    [Fact]
    public void RemoveDone_only_takes_the_finished_ones()
    {
        var q = Three(out var a, out var b, out _);
        q.SetStatus(a, EntryStatus.Done);
        q.SetStatus(b, EntryStatus.Error, "boom");
        Assert.Equal(1, q.RemoveDone());
        Assert.Equal(2, q.Entries.Count);
    }

    [Fact]
    public void Counters_follow_the_states()
    {
        var q = Three(out var a, out var b, out _);
        q.SetStatus(a, EntryStatus.Downloading);
        q.SetStatus(b, EntryStatus.Done);
        Assert.Equal(1, q.QueuedCount);
        Assert.Equal(1, q.DoneCount);
        Assert.Equal(2, q.PendingCount);   // the number on the Cola tab: waiting + downloading
    }

    [Fact]
    public void Rename_sets_a_clean_name_and_empty_goes_back_to_the_title()
    {
        var q = Three(out var a, out _, out _);
        Assert.True(q.Rename(a, "Mi: video / 1?"));
        var e = q.Get(a)!;
        Assert.Equal(NamingMode.Custom, e.Item.Naming);
        Assert.DoesNotContain(':', e.FileName);
        Assert.DoesNotContain('/', e.FileName);
        Assert.DoesNotContain('?', e.FileName);
        q.Rename(a, "   ");
        Assert.Equal(NamingMode.Original, q.Get(a)!.Item.Naming);
    }

    [Fact]
    public void Format_and_quality_change_the_download_choices()
    {
        var q = Three(out var a, out _, out _);
        Assert.True(q.SetFormat(a, "mp3"));
        Assert.False(q.SetFormat(a, "mkv"));                 // only MP4 and MP3 exist here
        Assert.Equal("mp3", q.Get(a)!.Item.Format);
        q.SetQuality(a, Quality.P720);
        Assert.Equal(720, q.Get(a)!.Item.Cap);
        q.SetQuality(a, Quality.Best);
        Assert.Equal(1080, q.Get(a)!.Item.Cap);              // best = what H.264 for Premiere allows
    }

    [Fact]
    public void Missing_data_is_filled_but_what_is_known_is_never_overwritten()
    {
        var q = new MiniQueue();
        var id = q.Add("https://www.youtube.com/watch?v=aaaaaaaaaaa").Id;
        q.Rename(id, "Mi nombre");
        var info = new VideoInfo { Key = "aaaaaaaaaaa", Url = "u", State = InfoState.Ready, Title = "Titulo A", Channel = "Canal", Duration = "1:00", Description = "desc" };
        Assert.True(q.FillData(id, info));
        var e = q.Get(id)!;
        Assert.Equal("Titulo A", e.Item.Title);
        Assert.Equal("Mi nombre", e.FileName);                // the chosen name wins over the title
        Assert.False(q.FillData(id, info with { Title = "Otro", Channel = "Otro canal" }));
        Assert.Equal("Titulo A", q.Get(id)!.Item.Title);
        Assert.False(q.FillData(id, info with { State = InfoState.Failed }));
    }

    [Fact]
    public void The_queue_is_saved_and_comes_back_with_its_order_names_and_choices()
    {
        var file = Path.Combine(Directory.CreateTempSubdirectory("miniprep-q-").FullName, "queue.json");
        var q = MiniQueue.Load(file, out var problem);
        Assert.Null(problem);
        var a = q.Add("https://www.youtube.com/watch?v=aaaaaaaaaaa").Id;
        var b = q.Add("https://www.youtube.com/watch?v=bbbbbbbbbbb").Id;
        q.Rename(b, "Segundo");
        q.SetFormat(b, "mp3");
        q.SetQuality(a, Quality.P480);
        q.Move(b, 0);
        q.SetStatus(a, EntryStatus.Done, file: @"C:\Videos\a.mp4");

        var again = MiniQueue.Load(file, out problem);
        Assert.Null(problem);
        Assert.Equal([b, a], Order(again));
        Assert.Equal("Segundo", again.Get(b)!.FileName);
        Assert.Equal("mp3", again.Get(b)!.Item.Format);
        Assert.Equal(Quality.P480, again.Get(a)!.Quality);
        Assert.Equal(EntryStatus.Done, again.Get(a)!.Status);
        Assert.Equal(@"C:\Videos\a.mp4", again.Get(a)!.File);
        Assert.NotEqual(a, again.Add("https://www.youtube.com/watch?v=ddddddddddd").Id);   // ids keep counting
    }

    [Fact]
    public void A_row_left_downloading_by_a_crash_comes_back_waiting()
    {
        var file = Path.Combine(Directory.CreateTempSubdirectory("miniprep-q-").FullName, "queue.json");
        var q = MiniQueue.Load(file, out _);
        var a = q.Add("https://www.youtube.com/watch?v=aaaaaaaaaaa").Id;
        q.SetStatus(a, EntryStatus.Downloading);
        Assert.Equal(EntryStatus.Queued, MiniQueue.Load(file, out _).Get(a)!.Status);
    }

    [Fact]
    public void A_damaged_file_gives_an_empty_queue_and_says_why()
    {
        var file = Path.Combine(Directory.CreateTempSubdirectory("miniprep-q-").FullName, "queue.json");
        File.WriteAllText(file, "{ not json");
        var q = MiniQueue.Load(file, out var problem);
        Assert.Empty(q.Entries);
        Assert.NotNull(problem);
    }

    // ---------------- row menu ----------------

    [Fact]
    public void The_row_menu_has_every_entry_of_the_mockup_in_order()
    {
        var e = new MiniQueue().Add("https://youtu.be/aaaaaaaaaaa");
        var items = RowMenu.For(new QueueEntry { Item = e.Item }).Where(l => l.Kind == MenuKind.Item).Select(l => l.Command).ToArray();
        Assert.Equal(["rename", "fmt:mp4", "fmt:mp3", "q:best", "q:1080", "q:720", "q:480", "thumb", "open", "copy", "remove"], items);
    }

    [Fact]
    public void The_row_menu_marks_the_current_choices()
    {
        var entry = new QueueEntry { Item = new QueueItem { Id = 1, Url = "https://youtu.be/aaaaaaaaaaa", Format = "mp4", Cap = 720 }, Quality = Quality.P720 };
        var menu = RowMenu.For(entry);
        Assert.Equal(["fmt:mp4", "q:720"], menu.Where(l => l.Checked).Select(l => l.Command));

        var audio = entry with { Item = entry.Item with { Format = "mp3" } };
        var m2 = RowMenu.For(audio);
        Assert.Equal(["fmt:mp3"], m2.Where(l => l.Checked).Select(l => l.Command));
        Assert.All(m2.Where(l => l.Command.StartsWith("q:")), l => Assert.False(l.Enabled));   // quality means nothing for audio
    }

    [Fact]
    public void A_row_that_downloads_cannot_be_renamed_changed_or_removed_from_the_menu()
    {
        var e = new QueueEntry { Item = new QueueItem { Id = 1, Url = "https://youtu.be/aaaaaaaaaaa" }, Status = EntryStatus.Downloading };
        var menu = RowMenu.For(e).Where(l => l.Kind == MenuKind.Item).ToDictionary(l => l.Command);
        Assert.False(menu["rename"].Enabled);
        Assert.False(menu["fmt:mp3"].Enabled);
        Assert.False(menu["remove"].Enabled);
        Assert.True(menu["copy"].Enabled);
        Assert.True(menu["open"].Enabled);
        Assert.True(RowMenu.For(e).Single(l => l.Command == "remove").Danger);
    }

    [Fact]
    public void Every_menu_text_exists_in_both_languages()
    {
        var e = new QueueEntry { Item = new QueueItem { Id = 1, Url = "https://youtu.be/aaaaaaaaaaa" } };
        foreach (var line in RowMenu.For(e).Where(l => l.Label.Length > 0))
            foreach (var lang in new[] { "es", "en" })
                Assert.NotEqual(line.Label, Videoprep.Core.I18n.Localizer.Get(lang, line.Label));
    }
}
