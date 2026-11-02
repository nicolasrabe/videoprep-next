using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Videoprep.App.Services;
using Videoprep.App.ViewModels;
using Videoprep.App.Views;
using Videoprep.Core.Engine;
using Videoprep.Core.Models;
using Videoprep.Core.Storage;

namespace Videoprep.App.Tests;

/// <summary>Fix round: thumbnail menu always on, row-only context menus, queue data completed in the background.</summary>
[Collection("fake yt-dlp environment")]
public sealed class QueueDataTests : IDisposable
{
    private readonly TestRig _rig = new(tools: true);
    private readonly string _calls;

    public QueueDataTests()
    {
        Loc.Instance.Language = "es";
        _calls = Path.Combine(_rig.Dir, "calls.txt");
        Environment.SetEnvironmentVariable("FAKE_YTDLP_CALLS", _calls);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("FAKE_YTDLP_CALLS", null);
        _rig.Dispose();
    }

    private static string Url(string id) => "https://fake.test/ok/" + id;

    private void SaveOldQueue(params string[] ids) =>
        QueueStore.Save(_rig.Paths.QueueFile, ids.Select((id, i) => new QueueItem { Id = i + 1, Url = Url(id) }));

    private static void SetStatus(MainViewModel vm, string key, JobStatus s) =>
        ((Dictionary<string, JobStatus>)typeof(MainViewModel).GetField("_rowStatus", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(vm)!)[key] = s;

    private List<string[]> Calls() => File.Exists(_calls) ? File.ReadAllLines(_calls).Select(l => l.Split('\u001f')).ToList() : [];

    [AvaloniaFact]
    public void The_thumbnail_menu_is_on_for_any_row_whatever_its_state()
    {
        SaveOldQueue("a", "b", "c", "d");
        var vm = _rig.Create();
        var rows = vm.QueueRows.ToList();
        SetStatus(vm, "1|0", JobStatus.OK);
        SetStatus(vm, "2|0", JobStatus.Downloading);
        SetStatus(vm, "3|0", JobStatus.ERROR);
        Assert.False(vm.CanThumbnail);                                   // nothing selected
        foreach (var r in rows)
        {
            vm.SetQueueSelection([r]);
            Assert.True(vm.CanThumbnail, r.Key);
        }
        vm.SetQueueSelection([rows[0], rows[1]]);
        Assert.True(vm.CanThumbnail);
        vm.SetQueueSelection([rows[0]]);
        Assert.False(vm.CanEditOne);                                     // editing is locked on a done row, the thumbnail is not
    }

    [AvaloniaFact]
    public async Task Saving_a_thumbnail_from_a_done_row_asks_for_the_picture_at_the_click()
    {
        SaveOldQueue("a");
        var vm = _rig.Create();
        SetStatus(vm, "1|0", JobStatus.OK);
        var jpg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3 };
        var thumbs = Path.Combine(_rig.Paths.CacheDir, "thumbs");
        Directory.CreateDirectory(thumbs);
        File.WriteAllBytes(Path.Combine(thumbs, Videoprep.Core.Preview.ThumbnailService.CacheName(Videoprep.Core.Text.UrlParser.LinkKey(Url("a"))) + ".img"), jpg);
        _rig.Ui.SavePath = Path.Combine(_rig.Dir, "t.jpg");
        vm.SetQueueSelection([vm.QueueRows[0]]);
        await vm.SaveQueueThumbnailCommand.ExecuteAsync(null);
        Assert.Equal(jpg, await File.ReadAllBytesAsync(_rig.Ui.SavePath));
    }

    [AvaloniaFact]
    public async Task Items_without_data_get_title_and_description_in_the_background_one_at_a_time_and_are_saved()
    {
        SaveOldQueue("a", "b", "c");
        var vm = _rig.Create();
        await vm.EnrichTask;
        Assert.Equal(["Fake video a", "Fake video b", "Fake video c"], vm.Items.Select(i => i.Title));
        Assert.All(vm.Items, i => Assert.Contains("película corta", i.Description));
        Assert.All(vm.QueueRows, r => Assert.False(r.DataLoading));
        Assert.Equal(3, Calls().Count);                                  // one request each, one after the other
        Assert.Equal(3, _rig.Pauses.Count(p => p == TimeSpan.FromSeconds(1.5)));   // a short pause between requests

        var saved = QueueStore.Load(_rig.Paths.QueueFile, out _);          // kept in the queue file...
        Assert.Equal(["Fake video a", "Fake video b", "Fake video c"], saved.Select(i => i.Title));
        var again = _rig.Create();                                         // ...so the next start asks for nothing
        await again.EnrichTask;
        Assert.Equal(3, Calls().Count);
        Assert.Equal("Fake video a", again.Items[0].Title);
    }

    [AvaloniaFact]
    public async Task While_waiting_a_row_shows_the_link_and_the_loading_text()
    {
        var vm = _rig.Create();
        await vm.EnrichTask;
        vm.SmartPaste(Url("zzz") + Environment.NewLine + Url("yyy"));      // two new items, never asked for
        var rows = vm.QueueRows.ToList();
        Assert.Equal(Url("zzz"), rows[0].Name);                            // the link while there is no title
        vm.StartEnrich();
        Assert.True(rows[0].DataLoading);                                  // "Cargando los datos..." until the answer comes
        await vm.EnrichTask;
        Assert.False(vm.QueueRows[0].DataLoading);
        Assert.Equal("Fake video zzz", vm.QueueRows[0].Name);
        Assert.Equal("Fake video yyy", vm.Items[1].Title);
    }

    [AvaloniaFact]
    public async Task A_failed_read_shows_the_reason_in_the_row_and_in_the_log_and_is_not_asked_again()
    {
        SaveOldQueue("a");
        var vm = _rig.Create();
        await vm.EnrichTask;
        vm.Items[0] = vm.Items[0] with { Title = "" };
        typeof(MainViewModel).GetMethod("RebuildQueueRows", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(vm, null);
        typeof(MainViewModel).GetMethod("NoteDataFailure", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(vm, [Videoprep.Core.Text.UrlParser.LinkKey(Url("a")), Url("a"), "ERROR: HTTP Error 429: Too Many Requests"]);
        Assert.Contains("429", vm.QueueRows[0].DataError);
        Assert.Contains(_rig.Log.Recent(), l => l.Contains("Queue data") && l.Contains("429"));
        var before = Calls().Count;
        vm.StartEnrich();                                                  // gave up on it: no new request
        Assert.Equal(before, Calls().Count);
    }

    [AvaloniaFact]
    public async Task Refresh_data_asks_again_for_every_selected_row_and_keeps_the_new_data()
    {
        SaveOldQueue("a", "b");
        var vm = _rig.Create();
        await vm.EnrichTask;
        var asked = Calls().Count;
        vm.Items[0] = vm.Items[0] with { Title = "viejo" };
        vm.SetQueueSelection(vm.QueueRows.ToList());
        await vm.RefreshQueueDataCommand.ExecuteAsync(null);
        Assert.Equal(asked + 2, Calls().Count);
        Assert.Equal(["Fake video a", "Fake video b"], vm.Items.Select(i => i.Title));
        Assert.Equal("Fake video a", QueueStore.Load(_rig.Paths.QueueFile, out _)[0].Title);
    }

    [AvaloniaFact]
    public async Task Clicking_a_row_opens_its_card_and_never_starts_a_download()
    {
        SaveOldQueue("a", "b");
        var vm = _rig.Create();
        await vm.EnrichTask;
        File.Delete(_calls);
        vm.SetQueueSelection([vm.QueueRows[0]]);                          // what a click does: select, open
        vm.ToggleRow(vm.QueueRows[0]);
        vm.ToggleRow(vm.QueueRows[0]);
        await Task.Delay(200);
        Assert.False(vm.IsRunning);
        Assert.All(vm.QueueRows, r => Assert.Equal(Videoprep.App.Services.Loc.Instance["rs_Queued"], r.Status));
        Assert.DoesNotContain(Calls(), c => !c.Contains("--skip-download"));   // only data requests, nothing was downloaded
        Assert.Empty(Directory.Exists(_rig.OutFolder) ? Directory.GetFiles(_rig.OutFolder) : []);
    }

    [AvaloniaFact]
    public async Task The_row_menus_open_only_on_a_row_never_on_empty_space_or_headers()
    {
        SaveOldQueue("a", "b");
        var vm = _rig.Create();
        await vm.EnrichTask;
        var window = new MainWindow { Width = 1100, Height = 780, DataContext = vm };
        window.Show();
        vm.SelectedTab = MainViewModel.TabQueue;
        Dispatcher.UIThread.RunJobs();
        var grid = window.GetVisualDescendants().OfType<DataGrid>().First(g => g.ItemsSource == vm.QueueRows);
        var menu = grid.ContextMenu!;
        var row = grid.GetVisualDescendants().OfType<DataGridRow>().First();
        var onRow = row.TranslatePoint(new Avalonia.Point(200, 10), window)!.Value;
        var empty = grid.TranslatePoint(new Avalonia.Point(200, grid.Bounds.Height - 40), window)!.Value;
        var header = grid.TranslatePoint(new Avalonia.Point(200, 12), window)!.Value;

        foreach (var (point, expected) in new[] { (empty, false), (header, false), (onRow, true) })
        {
            window.MouseDown(point, MouseButton.Right);
            window.MouseUp(point, MouseButton.Right);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(expected, menu.IsOpen);
            menu.Close();
            Dispatcher.UIThread.RunJobs();
        }
        window.Close();
    }
}
