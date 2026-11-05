using Miniprep.App.Logic;
using Miniprep.App.Services;
using Miniprep.App.ViewModels;
using Videoprep.Core.Models;

namespace Miniprep.App.Tests;

/// <summary>The two tabs end to end without a window: a fake yt-dlp prints the video data, a fake downloader decides how downloads end.</summary>
[Collection("loc")]
public sealed class FlowTests : IDisposable
{
    private readonly MiniRig _rig = new();

    public FlowTests() => Loc.Instance.Language = "es";

    public void Dispose() => _rig.Dispose();

    private static async Task Typed(MiniMainViewModel vm, string link)
    {
        vm.Prep.Url = link;
        await vm.Prep.Settled();
        await Wait(() => !vm.Prep.IsLoading);
    }

    // ---------------- Prep ----------------

    [Fact]
    public async Task A_pasted_link_shows_the_preview_with_the_mockup_texts()
    {
        var vm = _rig.NewViewModel();
        await Typed(vm, "youtube.com/watch?v=Qw3r7yTz9Aa");
        Assert.True(vm.Prep.IsReady);
        Assert.False(vm.Prep.IsLoading);
        Assert.Equal("Entrevista completa", vm.Prep.Title);
        Assert.Equal("38:12", vm.Prep.Duration);
        Assert.Equal("Canal de ejemplo · 21 oct 2026 · 41 mil vistas", vm.Prep.ChannelLine);
        Assert.StartsWith("En este episodio", vm.Prep.Description);
        Assert.Equal("Listo", vm.Prep.StatusText);
        Assert.True(vm.Prep.CanAct);
    }

    [Fact]
    public async Task With_no_link_there_is_nothing_but_the_box()
    {
        var vm = _rig.NewViewModel();
        await Typed(vm, "");
        Assert.False(vm.Prep.IsReady);
        Assert.False(vm.Prep.IsLoading);
        Assert.False(vm.Prep.HasNotice);
        Assert.False(vm.Prep.HasError);
        Assert.Equal("", vm.Prep.StatusText);
        Assert.False(vm.Prep.CanAct);
        Assert.Equal(0, _rig.Runner.CallCount);
    }

    [Theory]
    [InlineData("https://www.youtube.com/playlist?list=PLabc123")]
    [InlineData("https://www.youtube.com/@canal")]
    public async Task A_list_or_a_channel_gets_the_notice_and_nothing_is_asked_to_the_site(string link)
    {
        var vm = _rig.NewViewModel();
        await Typed(vm, link);
        Assert.Equal(Loc.Instance["mp_list_notice"], vm.Prep.NoticeText);
        Assert.False(vm.Prep.CanAct);
        Assert.Equal(0, _rig.Runner.CallCount);
    }

    [Fact]
    public async Task Pegar_takes_the_first_link_from_the_clipboard()
    {
        _rig.Ui.Clipboard = "mirá esto https://youtu.be/Qw3r7yTz9Aa y esto otro";
        var vm = _rig.NewViewModel();
        await vm.Prep.PasteCommand.ExecuteAsync(null);
        await Wait(() => !vm.Prep.IsLoading);
        Assert.Contains("Qw3r7yTz9Aa", vm.Prep.Url);
        Assert.True(vm.Prep.IsReady);
    }

    [Fact]
    public async Task Anadir_adds_without_downloading_and_the_Cola_number_goes_up()
    {
        var vm = _rig.NewViewModel();
        await Typed(vm, "https://youtu.be/Qw3r7yTz9Aa");
        vm.Prep.AddCommand.Execute(null);
        Assert.Single(vm.Queue.Entries);
        Assert.Equal("Entrevista completa", vm.Queue.Entries[0].Item.Title);   // the data already known comes along
        Assert.Equal(1, vm.Badge);
        Assert.True(vm.ShowBadge);
        Assert.Equal("Añadido a la cola", vm.Toast);
        Assert.Empty(_rig.Downloader.Requests);
        vm.Prep.AddCommand.Execute(null);
        Assert.Equal(2, vm.Badge);
    }

    [Fact]
    public async Task Descargar_asks_where_to_save_and_downloads_only_the_link_in_the_box()
    {
        var vm = _rig.NewViewModel();
        await Typed(vm, "https://youtu.be/Qw3r7yTz9Aa");
        vm.Queue.Add("https://youtu.be/zzzzzzzzzzz");          // something else waits in the queue: it must not be touched
        _rig.Ui.SavePath = Path.Combine(_rig.Dir, "salida", "Mi entrevista.mp3");
        await vm.Prep.DownloadCommand.ExecuteAsync(null);

        Assert.Equal("Entrevista completa", _rig.Ui.SaveSuggestions.Single());   // the title is the suggested name
        var req = Assert.Single(_rig.Downloader.Requests);
        Assert.Equal("mp3", req.Item.Format);
        Assert.Equal("Mi entrevista", req.Item.Custom);
        Assert.Equal(NamingMode.Custom, req.Item.Naming);
        Assert.Equal(Path.Combine(_rig.Dir, "salida"), req.Folder);
        Assert.Contains("Qw3r7yTz9Aa", req.Item.Url);
        Assert.False(req.BrowserCookies);
        Assert.Equal("Descarga lista", vm.Prep.StatusText);
        Assert.Equal(EntryStatus.Queued, vm.Queue.Entries[0].Status);
        Assert.Equal(Path.Combine(_rig.Dir, "salida"), vm.Settings.LastSaveFolder);
    }

    [Fact]
    public async Task Cancelling_Guardar_como_downloads_nothing()
    {
        var vm = _rig.NewViewModel();
        await Typed(vm, "https://youtu.be/Qw3r7yTz9Aa");
        _rig.Ui.SavePath = null;
        await vm.Prep.DownloadCommand.ExecuteAsync(null);
        Assert.Empty(_rig.Downloader.Requests);
        Assert.False(vm.Prep.IsDownloading);
    }

    [Fact]
    public async Task Detener_stops_the_download_and_leaves_the_link_ready()
    {
        _rig.Downloader.Behaviour = async (_, ct) =>
        {
            try { await Task.Delay(Timeout.Infinite, ct); } catch (OperationCanceledException) { }
            return new DownloadResult(false, true, "", "");
        };
        var vm = _rig.NewViewModel();
        await Typed(vm, "https://youtu.be/Qw3r7yTz9Aa");
        _rig.Ui.SavePath = Path.Combine(_rig.Dir, "a.mp4");
        var running = vm.Prep.DownloadCommand.ExecuteAsync(null);
        await Wait(() => vm.Prep.IsDownloading);
        Assert.Equal(37, vm.Prep.Progress);
        Assert.Contains("37%", vm.Prep.StatusText);
        Assert.Contains("2,1 MB/s", vm.Prep.StatusText);
        vm.Prep.StopCommand.Execute(null);
        await running;
        Assert.False(vm.Prep.IsDownloading);
        Assert.Equal("Descarga detenida", vm.Prep.StatusText);
        Assert.True(vm.Prep.CanAct);
    }

    [Fact]
    public async Task A_site_that_asks_for_login_offers_the_browser_cookies_and_only_uses_them_after_the_click()
    {
        var calls = 0;
        _rig.Downloader.Behaviour = (_, _) => Task.FromResult(++calls == 1
            ? new DownloadResult(false, false, "", "ERROR: [youtube] x: Sign in to confirm you're not a bot")
            : new DownloadResult(true, false, "x.mp4", ""));
        var vm = _rig.NewViewModel();
        await Typed(vm, "https://youtu.be/Qw3r7yTz9Aa");
        _rig.Ui.SavePath = Path.Combine(_rig.Dir, "a.mp4");
        await vm.Prep.DownloadCommand.ExecuteAsync(null);

        Assert.True(vm.Prep.HasError);
        Assert.Equal(Loc.Instance["mp_err_login"], vm.Prep.ErrorText);
        Assert.True(vm.Prep.CanRetryCookies);
        Assert.False(_rig.Downloader.Requests[0].BrowserCookies);

        await vm.Prep.RetryWithCookiesCommand.ExecuteAsync(null);
        Assert.True(_rig.Downloader.Requests[1].BrowserCookies);
        Assert.False(vm.Prep.HasError);
        Assert.Equal("Descarga lista", vm.Prep.StatusText);
    }

    [Fact]
    public async Task A_plain_failure_offers_Reintentar_but_not_the_cookies()
    {
        _rig.Downloader.Behaviour = (_, _) => Task.FromResult(new DownloadResult(false, false, "", "ERROR: network is unreachable"));
        var vm = _rig.NewViewModel();
        await Typed(vm, "https://youtu.be/Qw3r7yTz9Aa");
        _rig.Ui.SavePath = Path.Combine(_rig.Dir, "a.mp4");
        await vm.Prep.DownloadCommand.ExecuteAsync(null);
        Assert.True(vm.Prep.HasError);
        Assert.False(vm.Prep.CanRetryCookies);
        Assert.Contains("network is unreachable", vm.Prep.ErrorText);
    }

    [Fact]
    public async Task When_reading_the_link_needs_a_login_the_error_offers_the_cookies()
    {
        using var rig = new MiniRig(_ => ["ERROR: [youtube] x: Sign in to confirm you're not a bot"], exitCode: 1, browsers: ["firefox"]);
        var vm = rig.NewViewModel();
        vm.Prep.Url = "https://youtu.be/Qw3r7yTz9Aa";
        await vm.Prep.Settled();
        await Wait(() => !vm.Prep.IsLoading);
        Assert.True(vm.Prep.HasError);
        Assert.True(vm.Prep.CanRetryCookies);
        Assert.False(vm.Prep.CanAct);
        Assert.Equal(1, rig.Runner.CallCount);   // no cookies were tried by itself
        Assert.DoesNotContain(rig.Runner.Calls[0], a => a.StartsWith("--cookies"));

        rig.Script = _ => ["INFO|" + MiniRig.InfoJson];
        await vm.Prep.RetryWithCookiesCommand.ExecuteAsync(null);
        await Wait(() => rig.Runner.CallCount >= 2);
        Assert.Contains(rig.Runner.Calls[^1], a => a == "--cookies-from-browser");
    }

    // ---------------- Cola ----------------

    private static async Task Wait(Func<bool> condition)
    {
        for (var i = 0; i < 400 && !condition(); i++) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }

    [Fact]
    public async Task Iniciar_cola_downloads_in_order_into_the_one_folder_and_marks_the_results()
    {
        var vm = _rig.NewViewModel();
        var a = vm.Queue.Add("https://youtu.be/aaaaaaaaaaa").Id;
        var b = vm.Queue.Add("https://youtu.be/bbbbbbbbbbb").Id;
        var c = vm.Queue.Add("https://youtu.be/ccccccccccc").Id;
        _rig.Downloader.Behaviour = (r, _) => Task.FromResult(r.Item.Id == b
            ? new DownloadResult(false, false, "", "ERROR: boom")
            : new DownloadResult(true, false, Path.Combine(r.Folder, "v" + r.Item.Id + ".mp4"), ""));
        vm.Settings.Folder = Path.Combine(_rig.Dir, "cola");
        await vm.Cola.RunAsync();

        Assert.Equal([a, b, c], _rig.Downloader.Requests.Select(r => r.Item.Id));
        Assert.All(_rig.Downloader.Requests, r => Assert.Equal(Path.Combine(_rig.Dir, "cola"), r.Folder));
        Assert.All(_rig.Downloader.Requests, r => Assert.False(r.BrowserCookies));
        Assert.Equal(EntryStatus.Done, vm.Queue.Get(a)!.Status);
        Assert.Equal(EntryStatus.Error, vm.Queue.Get(b)!.Status);
        Assert.Contains("boom", vm.Queue.Get(b)!.Error);
        Assert.Equal(EntryStatus.Done, vm.Queue.Get(c)!.Status);
        Assert.False(vm.Cola.IsRunning);
        Assert.Equal(0, vm.Badge - vm.Queue.PendingCount);
    }

    [Fact]
    public async Task Detener_in_the_queue_puts_the_current_row_back_and_leaves_the_rest_waiting()
    {
        var vm = _rig.NewViewModel();
        var a = vm.Queue.Add("https://youtu.be/aaaaaaaaaaa").Id;
        var b = vm.Queue.Add("https://youtu.be/bbbbbbbbbbb").Id;
        _rig.Downloader.Behaviour = async (_, ct) =>
        {
            try { await Task.Delay(Timeout.Infinite, ct); } catch (OperationCanceledException) { }
            return new DownloadResult(false, true, "", "");
        };
        var run = vm.Cola.ToggleRunCommand.ExecuteAsync(null);
        await Wait(() => vm.Queue.Get(a)!.Status == EntryStatus.Downloading);
        Assert.True(vm.Cola.IsRunning);
        Assert.Equal("■ Detener", vm.Cola.StartText);
        await vm.Cola.ToggleRunCommand.ExecuteAsync(null);   // the same button now stops
        await run;
        Assert.Equal(EntryStatus.Queued, vm.Queue.Get(a)!.Status);
        Assert.Equal(EntryStatus.Queued, vm.Queue.Get(b)!.Status);
        Assert.Single(_rig.Downloader.Requests);
        Assert.Equal("▶ Iniciar cola", vm.Cola.StartText);
    }

    [Fact]
    public async Task Only_one_download_runs_at_a_time()
    {
        var vm = _rig.NewViewModel();
        vm.Queue.Add("https://youtu.be/aaaaaaaaaaa");
        _rig.Downloader.Behaviour = async (_, ct) =>
        {
            try { await Task.Delay(Timeout.Infinite, ct); } catch (OperationCanceledException) { }
            return new DownloadResult(false, true, "", "");
        };
        var run = vm.Cola.ToggleRunCommand.ExecuteAsync(null);
        await Wait(() => vm.Cola.IsRunning);
        await Typed(vm, "https://youtu.be/Qw3r7yTz9Aa");
        _rig.Ui.SavePath = Path.Combine(_rig.Dir, "a.mp4");
        await vm.Prep.DownloadCommand.ExecuteAsync(null);
        Assert.Equal([Loc.Instance["mp_busy"]], _rig.Ui.Messages);
        Assert.Single(_rig.Downloader.Requests);
        await vm.Cola.ToggleRunCommand.ExecuteAsync(null);
        await run;
    }

    [Fact]
    public async Task Only_waiting_rows_reorder_by_dragging_and_the_order_survives_a_restart()
    {
        var vm = _rig.NewViewModel();
        var a = vm.Queue.Add("https://youtu.be/aaaaaaaaaaa").Id;
        var b = vm.Queue.Add("https://youtu.be/bbbbbbbbbbb").Id;
        var c = vm.Queue.Add("https://youtu.be/ccccccccccc").Id;
        vm.Queue.SetStatus(a, EntryStatus.Done);
        Assert.False(vm.Cola.Rows.First(r => r.Id == a).CanDrag);
        Assert.True(vm.Cola.MoveRow(vm.Cola.Rows.First(r => r.Id == c), 1));
        Assert.Equal([a, c, b], vm.Cola.Rows.Select(r => r.Id));
        Assert.False(vm.Cola.MoveRow(vm.Cola.Rows.First(r => r.Id == a), 2));

        await Task.Yield();
        var again = _rig.NewViewModel();     // the queue file is read again
        Assert.Equal([a, c, b], again.Cola.Rows.Select(r => r.Id));
        Assert.Equal(EntryStatus.Done, again.Queue.Get(a)!.Status);
    }

    [Fact]
    public async Task One_row_is_open_at_a_time_and_the_x_removes_it()
    {
        var vm = _rig.NewViewModel();
        vm.Queue.Add("https://youtu.be/aaaaaaaaaaa");
        vm.Queue.Add("https://youtu.be/bbbbbbbbbbb");
        var (r1, r2) = (vm.Cola.Rows[0], vm.Cola.Rows[1]);
        vm.Cola.ToggleOpen(r1);
        Assert.True(r1.IsOpen);
        vm.Cola.ToggleOpen(r2);
        Assert.False(r1.IsOpen);
        Assert.True(r2.IsOpen);
        vm.Cola.ToggleOpen(r2);
        Assert.False(r2.IsOpen);
        vm.Cola.Remove(r1);
        Assert.Single(vm.Cola.Rows);
        Assert.Equal(1, vm.Badge);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task The_row_menu_commands_do_what_they_say()
    {
        var vm = _rig.NewViewModel();
        var id = vm.Queue.Add("https://youtu.be/aaaaaaaaaaa").Id;
        var row = vm.Cola.Rows[0];

        _rig.Ui.Texts.Enqueue("Mi nombre");
        await vm.Cola.RunMenuCommand(row, "rename");
        Assert.Equal("Mi nombre", vm.Queue.Get(id)!.FileName);
        Assert.Equal("Mi nombre", row.Title);

        await vm.Cola.RunMenuCommand(row, "fmt:mp3");
        Assert.Equal("mp3", vm.Queue.Get(id)!.Item.Format);
        Assert.Contains("MP3 audio", row.Meta);

        await vm.Cola.RunMenuCommand(row, "fmt:mp4");
        await vm.Cola.RunMenuCommand(row, "q:720");
        Assert.Contains("MP4 720p", row.Meta);

        await vm.Cola.RunMenuCommand(row, "copy");
        Assert.Equal("https://youtu.be/aaaaaaaaaaa", _rig.Ui.Clipboard);
        Assert.Equal("Enlace copiado", vm.Toast);

        await vm.Cola.RunMenuCommand(row, "open");
        Assert.Equal(["https://youtu.be/aaaaaaaaaaa"], _rig.Ui.Opened);

        await vm.Cola.RunMenuCommand(row, "thumb");        // no picture yet: a clear message, no crash
        Assert.Equal([Loc.Instance["mp_thumb_none"]], _rig.Ui.Messages);

        await vm.Cola.RunMenuCommand(row, "remove");
        Assert.Empty(vm.Queue.Entries);
    }

    [Fact]
    public async Task Rows_without_data_are_completed_in_the_background_one_at_a_time()
    {
        var vm = _rig.NewViewModel();
        var a = vm.Queue.Add("https://www.youtube.com/watch?v=aaaaaaaaaaa").Id;
        var b = vm.Queue.Add("https://www.youtube.com/watch?v=bbbbbbbbbbb").Id;
        await vm.Cola.FillTask;
        await Wait(() => vm.Queue.Get(a)!.Item.Title.Length > 0 && vm.Queue.Get(b)!.Item.Title.Length > 0);
        Assert.Equal("Entrevista completa", vm.Queue.Get(a)!.Item.Title);
        Assert.Equal("Canal de ejemplo", vm.Queue.Get(b)!.Item.Channel);
        Assert.StartsWith("En este episodio", vm.Queue.Get(a)!.Item.Description);
        Assert.Equal(1, _rig.Services.Info.MostAtOnce);        // never two yt-dlp at once
        Assert.Equal(2, _rig.Runner.CallCount);                // one per row, no repeats
        Assert.Equal("Canal de ejemplo · 38:12 · MP4 1080p", vm.Cola.Rows[0].Meta);
    }

    [Fact]
    public async Task A_row_that_fails_to_complete_is_not_asked_again_in_the_session()
    {
        using var rig = new MiniRig(_ => ["ERROR: [youtube] x: Video unavailable"], exitCode: 1);
        var vm = rig.NewViewModel();
        vm.Queue.Add("https://www.youtube.com/watch?v=aaaaaaaaaaa");
        await vm.Cola.FillTask;
        var calls = rig.Runner.CallCount;
        vm.Queue.Add("https://www.youtube.com/watch?v=bbbbbbbbbbb");
        await vm.Cola.FillTask;
        Assert.Equal(calls + 1, rig.Runner.CallCount);
    }

    [Fact]
    public async Task Cambiar_changes_the_one_folder_and_remembers_it()
    {
        var vm = _rig.NewViewModel();
        _rig.Ui.FolderToPick = Path.Combine(_rig.Dir, "destino");
        await vm.Cola.ChangeFolderCommand.ExecuteAsync(null);
        Assert.Equal(_rig.Ui.FolderToPick, vm.Cola.Folder);
        Assert.Equal(_rig.Ui.FolderToPick, MiniSettings.Load(_rig.Paths.SettingsFile).Folder);
    }

    [Fact]
    public async Task Quitar_listos_and_the_counter()
    {
        var vm = _rig.NewViewModel();
        var a = vm.Queue.Add("https://youtu.be/aaaaaaaaaaa").Id;
        vm.Queue.Add("https://youtu.be/bbbbbbbbbbb");
        vm.Queue.SetStatus(a, EntryStatus.Done);
        Assert.Equal("1 en cola · 1 listo", vm.Cola.Counter);
        vm.Cola.ClearDoneCommand.Execute(null);
        Assert.Single(vm.Cola.Rows);
        await Task.CompletedTask;
    }
}

[CollectionDefinition("loc", DisableParallelization = true)]
public sealed class LocCollection;
