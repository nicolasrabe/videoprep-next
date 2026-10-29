using Avalonia.Headless.XUnit;
using Videoprep.App.Services;
using Videoprep.App.ViewModels;
using Videoprep.Core.Import;
using Videoprep.Core.Models;
using Videoprep.Core.Preview;
using Videoprep.Core.Tests;

namespace Videoprep.App.Tests;

/// <summary>0.3 round 1 in the view model: load a file (F1), edit the queue (F2), expandable rows (F3).</summary>
public sealed class QueueFeatureTests : IDisposable
{
    private readonly TestRig _rig = new(tools: true);

    public QueueFeatureTests() => Loc.Instance.Language = "es";

    public void Dispose() => _rig.Dispose();

    private const string Bunny = "https://www.youtube.com/watch?v=aqz-KE-bpKQ";
    private const string Zoo = "https://www.youtube.com/watch?v=jNQXAC9IVRw";

    private string Script()
    {
        var d = new DocxBuilder();
        d.Para("Escena 1: ", "https://youtu.be/aqz-KE-", "bpKQ", " el arranque");
        d.Body($"<w:p>{DocxBuilder.Run("Final: ")}{d.Hyperlink("https://youtu.be/jNQXAC9IVRw?si=x", "mirar este clip")}</w:p>");
        d.Para("Otra vez: https://www.youtube.com/watch?v=jNQXAC9IVRw&t=90");
        d.Para("Canal: https://www.youtube.com/@BlenderStudio");
        d.Body($"<w:p>{d.Hyperlink("https://miempresa.com/informe.pdf", "informe del cliente")}</w:p>");
        d.Para("Ya está en la cola: https://vimeo.com/9");
        return d.Save(_rig.Dir, "guion-video-23.docx");
    }

    private int OEmbedCalls() { lock (_rig.Web.Requests) return _rig.Web.Requests.Count(r => r.Contains("oembed", StringComparison.Ordinal)); }

    [AvaloniaFact]
    public async Task Loading_a_docx_shows_the_review_and_adds_only_what_was_checked()
    {
        var vm = _rig.Create();
        vm.SmartPaste("https://vimeo.com/9");
        vm.SendDraft(interactive: false);
        var callsBefore = OEmbedCalls();

        ImportReviewViewModel? seen = null;
        _rig.Ui.Review = r =>
        {
            seen = r;
            r.Rows[0].Name = "F1";
            r.UseSubfolder = true;
            return true;
        };
        await vm.ImportLinksFromFileAsync(Script());

        Assert.NotNull(seen);
        Assert.Equal("Enlaces encontrados en \"guion-video-23.docx\"", seen!.Title);
        Assert.Equal(
            [CandidateState.New, CandidateState.New, CandidateState.Repeated, CandidateState.Channel, CandidateState.NotVideo, CandidateState.InQueue],
            seen.Rows.Select(r => r.Candidate.State));
        Assert.Equal(["Texto", "Hipervínculo", "Texto", "Texto", "Hipervínculo", "Texto"], seen.Rows.Select(r => r.How));
        Assert.Equal("mirar este clip", seen.Rows[1].Context);
        Assert.Equal("Escena 1: … el arranque", seen.Rows[0].Context);
        Assert.Equal("Agregar 2 a la cola", seen.AddText);
        Assert.StartsWith("El de la pestaña Descargar (MP4", seen.SelectedFormat.Label);
        Assert.Contains("No es un video", seen.Rows[4].State);
        Assert.NotEmpty(seen.Rows[3].Explanation);

        Assert.Equal(["https://vimeo.com/9", Bunny, Zoo], vm.Items.Select(i => i.Url));
        Assert.Equal((NamingMode.Custom, "F1", "guion-video-23"), (vm.Items[1].Naming, vm.Items[1].Custom, vm.Items[1].SubFolder));
        Assert.Equal(NamingMode.Original, vm.Items[2].Naming);
        Assert.Equal("F1", vm.QueueRows[1].Name);

        // Titles in the background: only the unnamed YouTube link, with oEmbed, one at a time.
        await vm.TitleLookup;
        Assert.Equal(callsBefore + 1, OEmbedCalls());
        Assert.Equal("Me at the zoo", vm.QueueRows[2].Name);
        Assert.Equal(vm.L["st_ready"], vm.StatusText);
        Assert.DoesNotContain(_rig.Web.Requests, r => r.Contains("ytimg", StringComparison.Ordinal));   // no pictures
    }

    [AvaloniaFact]
    public async Task Cancel_in_the_review_adds_nothing_and_asks_nothing_from_the_network()
    {
        var vm = _rig.Create();
        await vm.ImportLinksFromFileAsync(Script());
        Assert.Empty(vm.Items);
        Assert.Empty(_rig.Web.Requests);
    }

    [AvaloniaFact]
    public async Task Title_lookup_pauses_between_requests_and_stops_when_the_queue_is_emptied()
    {
        var file = Path.Combine(_rig.Dir, "muchos.txt");
        File.WriteAllLines(file, Enumerable.Range(0, 120).Select(i => $"https://youtu.be/{i:D11}"));
        _rig.Ui.Review = _ => true;
        var vm = _rig.Create();
        await vm.ImportLinksFromFileAsync(file);
        await vm.TitleLookup;
        Assert.Equal(120, vm.Items.Count);
        Assert.Equal(MainViewModel.MaxTitleLookups, OEmbedCalls());          // at most 100 per file
        lock (_rig.Pauses) Assert.Equal(MainViewModel.MaxTitleLookups - 1, _rig.Pauses.Count(p => p == MainViewModel.TitlePause));

        vm.ClearQueueCommand.Execute(null);
        Assert.Empty(vm.Items);
        vm.UndoCommand.Execute(null);
        Assert.Equal(120, vm.Items.Count);                                    // emptying can be undone too
    }

    [AvaloniaFact]
    public void Opening_the_queue_again_asks_nothing_from_the_network()
    {
        var vm = _rig.Create();
        vm.SmartPaste("https://vimeo.com/1\nhttps://vimeo.com/2");
        _rig.Web.Requests.Clear();
        var again = _rig.Create();
        Assert.Equal(2, again.QueueRows.Count);
        Assert.Empty(_rig.Web.Requests);
    }

    [AvaloniaTheory]
    [InlineData("guion.doc", "Por ahora no se leen estos archivos (.doc)")]
    [InlineData("guion.pdf", "Por ahora no se leen estos archivos (.pdf)")]
    [InlineData("foto.png", "videoprep lee enlaces de archivos")]
    [InlineData("vacio.txt", "No se encontraron enlaces")]
    public async Task Files_that_cannot_be_used_explain_why(string name, string message)
    {
        var path = Path.Combine(_rig.Dir, name);
        File.WriteAllText(path, "sin enlaces");
        var vm = _rig.Create();
        await vm.ImportLinksFromFileAsync(path);
        Assert.StartsWith(message, Assert.Single(_rig.Ui.Messages));
        Assert.Null(_rig.Ui.LastReview);
    }

    [AvaloniaFact]
    public async Task Dropping_a_file_on_the_queue_loads_it()
    {
        var path = Path.Combine(_rig.Dir, "notas.md");
        File.WriteAllText(path, "- [toma buena](https://vimeo.com/76979871)\n");
        _rig.Ui.Review = _ => true;
        var vm = _rig.Create();
        await vm.DropOnQueueAsync(null, [path]);
        Assert.Equal("https://vimeo.com/76979871", Assert.Single(vm.Items).Url);
        Assert.Equal("toma buena", _rig.Ui.LastReview!.Rows[0].Context);
    }

    [AvaloniaFact]
    public void Rename_in_the_row_and_undo()
    {
        var vm = _rig.Create();
        vm.SmartPaste("https://vimeo.com/1\nhttps://vimeo.com/2");
        var row = vm.QueueRows[0];
        Assert.True(vm.CanRename(row));
        Assert.Equal("", vm.RenameStartText(row));
        vm.Rename(row, "Entrevista");
        Assert.Equal(("Entrevista", NamingMode.Custom), (vm.QueueRows[0].Name, vm.Items[0].Naming));
        Assert.Equal("Entrevista", vm.RenameStartText(vm.QueueRows[0]));
        Assert.True(vm.CanUndo);
        vm.UndoCommand.Execute(null);
        Assert.Equal(NamingMode.Original, vm.Items[0].Naming);
        Assert.Equal("https://vimeo.com/1", vm.QueueRows[0].Name);
        Assert.Equal("Se deshizo el último cambio de la cola.", vm.StatusText);
    }

    [AvaloniaFact]
    public void Several_clips_with_one_name_are_numbered()
    {
        var vm = _rig.Create();
        vm.SmartPaste("https://vimeo.com/1");
        vm.AddClipCommand.Execute(null);
        vm.AddClipCommand.Execute(null);
        vm.Clips[0].Start = "0:00:01:00";
        vm.Clips[1].Start = "0:00:05:00";
        vm.SendDraft(interactive: true);
        vm.Rename(vm.QueueRows[0], "Nombre");
        Assert.Equal(["Nombre - 1", "Nombre - 2"], vm.QueueRows.Select(r => r.Name));
    }

    [AvaloniaFact]
    public async Task Batch_change_with_preview_then_undo()
    {
        var vm = _rig.Create();
        vm.Folder = _rig.OutFolder;   // series numbers look at the destination folder
        vm.SmartPaste("https://vimeo.com/1\nhttps://vimeo.com/2\nhttps://vimeo.com/3");
        vm.Rename(vm.QueueRows[0], "Entrevista - parte 1");
        vm.SetQueueSelection(vm.QueueRows.Take(2));
        _rig.Ui.Batch = b =>
        {
            Assert.Equal("Cambiar 2 elementos de la cola", b.Title);
            Assert.Equal("(sin cambios)", b.SelectedFormat.Label);
            b.SelectedNames = b.NameModes.First(m => m.Value == nameof(NameChange.Series));
            b.Prefix = "Escena";
            Assert.Equal(["Entrevista - parte 1  →  Escena1.mp4", "https://vimeo.com/2  →  Escena2.mp4"], b.Preview);
            b.SelectedFormat = b.Formats.First(f => f.Value == "movp");
            Assert.EndsWith("Escena1.mov", b.Preview[0]);
            return true;
        };
        await vm.BatchEditCommand.ExecuteAsync(null);
        Assert.Equal(["Escena1", "Escena2", "https://vimeo.com/3"], vm.QueueRows.Select(r => r.Name));
        Assert.Equal(["movp", "movp", "mp4"], vm.Items.Select(i => i.Format));
        vm.UndoCommand.Execute(null);
        Assert.Equal(["mp4", "mp4", "mp4"], vm.Items.Select(i => i.Format));
    }

    [AvaloniaFact]
    public void Duplicate_inserts_a_copy_below()
    {
        var vm = _rig.Create();
        vm.SmartPaste("https://vimeo.com/1\nhttps://vimeo.com/2");
        vm.SetQueueSelection([vm.QueueRows[0]]);
        vm.DuplicateCommand.Execute(null);
        Assert.Equal(["https://vimeo.com/1", "https://vimeo.com/1", "https://vimeo.com/2"], vm.Items.Select(i => i.Url));
        Assert.Equal(3, vm.Items.Select(i => i.Id).Distinct().Count());
    }

    [AvaloniaFact]
    public async Task Finished_rows_are_locked_kept_and_can_be_shown_or_downloaded_again()
    {
        var vm = _rig.Create();
        vm.Folder = _rig.OutFolder;
        vm.SmartPaste("https://fake.test/ok/a1\nhttps://fake.test/ok/a2");
        await vm.StartBatchAsync();
        var done = vm.QueueRows[0];
        Assert.True(done.IsLocked);
        Assert.False(vm.CanRename(done));
        Assert.Contains("Volver a descargar", _rig.Ui.Messages[^1]);

        // Mostrar en carpeta: the exact file.
        vm.SetQueueSelection([done]);
        Assert.True(vm.CanShowInFolder);
        Assert.False(vm.CanEditOne);
        await vm.ShowInFolderCommand.ExecuteAsync(null);
        Assert.Equal(Path.Combine(_rig.OutFolder, "Fake video a1.mp4"), Assert.Single(_rig.Ui.ShownFiles));

        // Download again with everything finished: nothing runs, the user is told how to repeat.
        await vm.StartBatchAsync();
        Assert.Equal(vm.L["msg_all_done"], _rig.Ui.Messages[^1]);

        // Volver a descargar: back in the queue, unlocked; the next Download runs only that one (and never overwrites).
        vm.SetQueueSelection([vm.QueueRows[0]]);
        Assert.True(vm.CanRequeue);
        vm.RequeueCommand.Execute(null);
        Assert.Equal("En cola", vm.QueueRows[0].Status);
        Assert.False(vm.QueueRows[0].IsLocked);
        await vm.StartBatchAsync();
        Assert.Equal("Fake video a1.mp4", vm.QueueRows[0].File);   // the fake reuses an existing file, like yt-dlp
        Assert.Equal("Listo 🔒", vm.QueueRows[1].Status);
    }

    [AvaloniaFact]
    public async Task Imported_subfolder_is_where_the_files_go()
    {
        var file = Path.Combine(_rig.Dir, "Guion: final.txt".Replace(":", ""));
        File.WriteAllText(file, "https://fake.test/ok/s1\n");
        _rig.Ui.Review = r =>
        {
            Assert.False(r.Rows[0].Checked);   // an unknown site starts unchecked; the user ticks it
            r.AllCommand.Execute(null);
            r.UseSubfolder = true;
            return true;
        };
        var vm = _rig.Create();
        vm.Folder = _rig.OutFolder;
        await vm.ImportLinksFromFileAsync(file);
        Assert.Equal("Guion final", vm.Items[0].SubFolder);
        Assert.Contains("Guion final", vm.QueueRows[0].Format);
        await vm.StartBatchAsync();
        Assert.True(File.Exists(Path.Combine(_rig.OutFolder, "Guion final", "Fake video s1.mp4")));
    }

    // ---------------- expandable rows (F3) ----------------

    private static async Task Until(Func<bool> done)
    {
        for (var i = 0; i < 200 && !done(); i++) await Task.Delay(50);
        Assert.True(done(), "timed out");
    }

    [AvaloniaFact]
    public async Task Selecting_one_row_opens_its_card_and_several_rows_open_none()
    {
        var vm = _rig.Create();
        vm.SmartPaste("https://fake.test/ok/c1\nhttps://fake.test/ok/c2");
        vm.SetQueueSelection([vm.QueueRows[0]]);
        Assert.True(vm.QueueRows[0].IsExpanded);
        var card = vm.QueueRows[0].Card!;
        await Until(() => card.IsReady);
        Assert.Equal("Fake video c1", card.Title);
        Assert.Contains("Fake channel", card.Meta);
        Assert.Contains("vistas", card.Meta);
        Assert.Equal("38:12", card.Duration);
        Assert.Contains("Licencia Creative Commons.", card.Description);
        Assert.Equal("Se descargará: MP4 - H.264 + AAC · 1080p  ·  Fake video c1.mp4", card.Will);
        Assert.Equal("Fake video c1", vm.QueueRows[0].Name);   // other sites: the title once the row was opened

        vm.SetQueueSelection([vm.QueueRows[1]]);
        Assert.False(vm.QueueRows[0].IsExpanded);
        Assert.True(vm.QueueRows[1].IsExpanded);
        vm.ToggleRow(vm.QueueRows[1]);                          // a click on the open row closes it
        Assert.False(vm.QueueRows[1].IsExpanded);
        vm.SetQueueSelection(vm.QueueRows);
        Assert.DoesNotContain(vm.QueueRows, r => r.IsExpanded);

        card.OpenLinkCommand.Execute(null);
        card.CopyLinkCommand.Execute(null);
        Assert.Equal(["https://fake.test/ok/c1"], _rig.Ui.OpenedUrls);
        Assert.Equal("https://fake.test/ok/c1", _rig.Ui.Clipboard);
    }

    [AvaloniaFact]
    public async Task Moving_quickly_through_rows_never_runs_two_requests_at_once()
    {
        Environment.SetEnvironmentVariable("FAKE_YTDLP_INFO_DELAY_MS", "400");
        try
        {
            var vm = _rig.Create();
            vm.SmartPaste(string.Join('\n', Enumerable.Range(1, 6).Select(i => $"https://fake.test/ok/m{i}")));
            foreach (var row in vm.QueueRows.ToList()) vm.SetQueueSelection([row]);   // arrow key down, six times
            await Until(() => vm.QueueRows[^1].Card!.IsReady);
            Assert.Equal(1, _rig.Services.Info.MostAtOnce);
            Assert.True(_rig.Services.Info.RunsStarted <= 2, $"{_rig.Services.Info.RunsStarted} runs");
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKE_YTDLP_INFO_DELAY_MS", null);
        }
    }

    [AvaloniaFact]
    public async Task A_card_that_cannot_be_read_says_why_and_can_be_retried()
    {
        var vm = _rig.Create();
        vm.SmartPaste("https://fake.test/error/e1");
        vm.SendDraft(interactive: false);
        vm.SetQueueSelection([vm.QueueRows[0]]);
        var card = vm.QueueRows[0].Card!;
        await Until(() => card.IsFailed);
        Assert.Contains("HTTP Error 404", card.FailText);
        Assert.Contains(_rig.Log.Recent(), l => l.StartsWith("Card: yt-dlp failed", StringComparison.Ordinal));
        card.RetryCommand.Execute(null);
        Assert.True(card.IsLoading);
        await Until(() => card.IsFailed);
    }

    // ---------------- fragment warning (F2) ----------------

    [AvaloniaFact]
    public void Clip_beyond_the_known_duration_turns_red_and_says_how_long_the_video_is()
    {
        var clip = new ClipRowViewModel(_ => { }) { Start = "00:10:00:00", End = "00:40:00:00" };
        Assert.False(clip.HasWarning);                       // duration unknown: no warning
        clip.SetDuration(2292, "38:12", 30);
        Assert.True(clip.EndBad);
        Assert.False(clip.StartBad);
        Assert.Equal("El video dura 38:12", clip.Warning);
        clip.End = "00:38:00:00";
        Assert.False(clip.HasWarning);
    }
}
