using Avalonia.Headless.XUnit;
using Videoprep.App.Services;
using Videoprep.App.ViewModels;
using Videoprep.Core.Models;
using Videoprep.Core.Storage;

namespace Videoprep.App.Tests;

/// <summary>The Download / Queue / Settings behavior of 0.1.1, driven like a user would (no window needed).</summary>
public sealed class MainViewModelTests : IDisposable
{
    private readonly TestRig _rig = new();

    public MainViewModelTests() => Loc.Instance.Language = "es";

    public void Dispose() => _rig.Dispose();

    private const string Zoo = "https://www.youtube.com/watch?v=jNQXAC9IVRw";

    [AvaloniaFact]
    public void Pasting_a_second_link_sends_the_first_to_the_queue()
    {
        var vm = _rig.Create();
        Assert.True(vm.SmartPaste("mirá esto: https://youtu.be/jNQXAC9IVRw"));
        Assert.True(vm.HasDraft);
        Assert.Empty(vm.Items);

        vm.IsAudio = true;
        vm.SelectedFormat = vm.FormatChoices.First(f => f.Key == "wav");
        Assert.True(vm.SmartPaste("https://vimeo.com/123"));

        var first = Assert.Single(vm.Items);
        Assert.Equal(Zoo, first.Url);
        Assert.Equal("wav", first.Format);
        Assert.True(vm.HasDraft);                         // the Vimeo link is now the draft
        Assert.False(vm.IsAudio);                         // fields reset for the new link
        Assert.Equal("", vm.LinkText);
    }

    [AvaloniaFact]
    public void Several_links_at_once_go_straight_to_the_queue()
    {
        var vm = _rig.Create();
        vm.SmartPaste(Zoo);
        vm.SmartPaste("https://vimeo.com/1\nhttps://vimeo.com/2\ntwitch.tv/videos/3");
        Assert.Equal(4, vm.Items.Count);                  // draft first, then the three
        Assert.False(vm.HasDraft);
        Assert.Equal(4, vm.QueueRows.Count);
        Assert.Equal(vm.L["prev_hint"], vm.PreviewTitle);
    }

    [AvaloniaFact]
    public void Text_without_links_is_pasted_as_text()
    {
        var vm = _rig.Create();
        Assert.False(vm.SmartPaste("hola qué tal"));
        Assert.False(vm.SmartPaste("mirá Hello_World"));   // AUDIT M10: an 11-letter word in a sentence is not an ID
        Assert.Empty(vm.Items);
    }

    [AvaloniaFact]
    public void Send_to_queue_keeps_clips_names_and_format()
    {
        var vm = _rig.Create();
        vm.SmartPaste(Zoo);
        vm.AddClipCommand.Execute(null);
        vm.AddClipCommand.Execute(null);
        vm.Clips[0].Start = "00:00:01:00";
        vm.Clips[0].End = "00:00:02:00";
        vm.Clips[1].Start = "-00:00:00:15";
        vm.NameSeries = true;
        vm.SeriesText = "Toma";
        vm.SelectedFormat = vm.FormatChoices.First(f => f.Key == "movp");
        vm.SelectedResolution = "4K";
        Assert.Equal("Ejemplo: Toma1.mov, Toma2.mov, Toma3.mov", vm.ExampleText);

        vm.SendToQueueCommand.Execute(null);
        var it = Assert.Single(vm.Items);
        Assert.Equal(("movp", 2160, NamingMode.Series, "Toma"), (it.Format, it.Cap, it.Naming, it.Prefix));
        Assert.Equal([new ClipRange("00:00:01:00", "00:00:02:00"), new ClipRange("-00:00:00:15", "")], it.Clips);
        Assert.Equal(2, vm.QueueRows.Count);
        Assert.Equal("00:00:01:00 -> 00:00:02:00", vm.QueueRows[0].Clip);
        Assert.Equal("-00:00:00:15 -> final", vm.QueueRows[1].Clip);
        Assert.Empty(vm.Clips);
    }

    [AvaloniaFact]
    public void Invalid_timecode_is_explained_and_nothing_is_sent()
    {
        var vm = _rig.Create();
        vm.SmartPaste(Zoo);
        vm.AddClipCommand.Execute(null);
        vm.Clips[0].Start = "1:2:3:4:5";
        Assert.False(vm.Clips[0].StartValid);
        vm.SendToQueueCommand.Execute(null);
        Assert.Empty(vm.Items);
        Assert.Contains("\"1:2:3:4:5\" no es un código de tiempo válido", Assert.Single(_rig.Ui.Messages));
    }

    [AvaloniaFact]
    public void Custom_name_must_be_typed()
    {
        var vm = _rig.Create();
        vm.SmartPaste(Zoo);
        vm.NameCustom = true;
        Assert.False(vm.NameOriginal);
        vm.SendToQueueCommand.Execute(null);
        Assert.Empty(vm.Items);
        Assert.Equal(vm.L["msg_custom"], Assert.Single(_rig.Ui.Messages));
        // A second link sends it anyway, silently falling back to the original title (legacy behavior).
        vm.SmartPaste("https://vimeo.com/9");
        Assert.Equal(NamingMode.Original, Assert.Single(vm.Items).Naming);
    }

    [AvaloniaFact]
    public void Name_example_numbers_only_with_several_clips_AUDIT_X5()
    {
        var vm = _rig.Create();
        Assert.Equal("Ejemplo: Título del video.mp4", vm.ExampleText);
        vm.AddClipCommand.Execute(null);
        vm.AddClipCommand.Execute(null);
        Assert.Equal("Ejemplo: Título del video - 1.mp4, Título del video - 2.mp4", vm.ExampleText);
    }

    [AvaloniaFact]
    public void Edit_brings_the_item_back_and_send_replaces_it_in_place()
    {
        var vm = _rig.Create();
        vm.SmartPaste("https://vimeo.com/1\nhttps://vimeo.com/2");
        var second = vm.Items[1];
        vm.SetQueueSelection([vm.QueueRows[1]]);
        vm.EditSelectedCommand.Execute(null);
        Assert.Equal(MainViewModel.TabDownload, vm.SelectedTab);
        Assert.True(vm.HasDraft);
        vm.IsAudio = true;
        vm.SendToQueueCommand.Execute(null);
        Assert.Equal(2, vm.Items.Count);
        Assert.Equal(second.Id, vm.Items[1].Id);
        Assert.Equal("m4a", vm.Items[1].Format);
    }

    [AvaloniaFact]
    public void Delete_key_removes_selected_items_and_keeps_a_sane_selection()
    {
        var vm = _rig.Create();
        vm.SmartPaste("https://vimeo.com/1\nhttps://vimeo.com/2\nhttps://vimeo.com/3");
        vm.SetQueueSelection([vm.QueueRows[1]]);
        Assert.Equal(1, vm.DeleteKey());                  // the row that took its place
        Assert.Equal(["https://vimeo.com/1", "https://vimeo.com/3"], vm.Items.Select(i => i.Url));
        vm.SetQueueSelection([vm.QueueRows[1]]);
        Assert.Equal(0, vm.DeleteKey());                  // last row removed: select the new last one
        vm.SetQueueSelection([vm.QueueRows[0]]);
        Assert.Equal(-1, vm.DeleteKey());                 // queue empty
        Assert.Empty(vm.Items);
    }

    [AvaloniaFact]
    public void Up_and_down_move_items()
    {
        var vm = _rig.Create();
        vm.SmartPaste("https://vimeo.com/1\nhttps://vimeo.com/2");
        var moved = 0;
        vm.QueueItemMoved += id => moved = id;
        vm.SetQueueSelection([vm.QueueRows[1]]);
        vm.MoveUpCommand.Execute(null);
        Assert.Equal("https://vimeo.com/2", vm.Items[0].Url);
        Assert.Equal(vm.Items[0].Id, moved);
    }

    [AvaloniaFact]
    public void The_queue_survives_closing_videoprep_AUDIT_H6()
    {
        var vm = _rig.Create();
        vm.SmartPaste("https://vimeo.com/1\nhttps://vimeo.com/2");
        var again = _rig.Create();
        Assert.Equal(["https://vimeo.com/1", "https://vimeo.com/2"], again.Items.Select(i => i.Url));
        again.SmartPaste("https://vimeo.com/3\nhttps://vimeo.com/4");
        Assert.Equal(4, again.Items.Select(i => i.Id).Distinct().Count());   // ids keep counting
    }

    [AvaloniaFact]
    public async Task Load_txt_adds_every_link()
    {
        var file = Path.Combine(_rig.Dir, "links.txt");
        File.WriteAllText(file, "jNQXAC9IVRw\r\nhttps://vimeo.com/5\r\nuna línea sin enlace\r\n");
        _rig.Ui.FileToPick = file;
        _rig.Ui.Review = _ => true;   // 0.3: a review window comes first; the user presses "Agregar"
        var vm = _rig.Create();
        await vm.LoadTxtCommand.ExecuteAsync(null);
        Assert.Equal([Zoo, "https://vimeo.com/5"], vm.Items.Select(i => i.Url));
    }

    [AvaloniaFact]
    public void Theme_and_language_are_applied_and_saved()
    {
        var vm = _rig.Create();
        Core.Themes.Palette? applied = null;
        vm.PaletteChanged += p => applied = p;
        vm.SelectedThemeIndex = 1;                        // dark
        Assert.True(applied!.IsDark);
        Assert.False(vm.IsCustomTheme);
        vm.SelectedThemeIndex = 5;
        Assert.True(vm.IsCustomTheme);

        Assert.Equal("Todo listo", vm.StatusText);
        vm.SelectedLanguageIndex = 1;
        Assert.Equal("Ready", vm.StatusText);
        Assert.Equal("Automatic: try every installed browser (Firefox first)", vm.CookieChoices[0]);

        var saved = SettingsStore.Load(_rig.Paths.SettingsFile, out _);
        Assert.Equal(("custom", "en"), (saved.Theme, saved.Lang));
    }

    [AvaloniaFact]
    public async Task Custom_color_is_checked()
    {
        var vm = _rig.Create();
        vm.SelectedThemeIndex = 5;                        // the button only shows with the custom theme
        _rig.Ui.ColorToPick = "verde";
        await vm.PickColorCommand.ExecuteAsync(null);
        Assert.Equal(vm.L["msg_bad_color"], Assert.Single(_rig.Ui.Messages));
        _rig.Ui.ColorToPick = "#204060";
        Core.Themes.Palette? applied = null;
        vm.PaletteChanged += p => applied = p;
        await vm.PickColorCommand.ExecuteAsync(null);
        Assert.Equal("#204060", applied!.Bg);
    }

    [AvaloniaFact]
    public void Cookie_choice_maps_to_settings()
    {
        var vm = _rig.Create();
        Assert.Equal(11, vm.CookieChoices.Count);         // automatic + 8 browsers + file + none
        vm.SelectedCookieIndex = 10;
        Assert.Equal("none", vm.Settings.CookieSource);
        Assert.Equal("none", SettingsStore.Load(_rig.Paths.SettingsFile, out _).CookieSource);
    }

    [AvaloniaFact]
    public async Task Download_checks_folder_queue_and_tools_first()
    {
        var vm = _rig.Create();
        vm.Folder = "";
        await vm.StartBatchAsync();
        Assert.Equal(vm.L["msg_nofolder_sel"], _rig.Ui.Messages[^1]);

        vm.Folder = _rig.OutFolder;
        await vm.StartBatchAsync();
        Assert.Equal(vm.L["msg_empty"], _rig.Ui.Messages[^1]);

        vm.SmartPaste("https://vimeo.com/1");
        _rig.Ui.Answers.Enqueue(false);                   // "download the tools now?" -> no
        await vm.StartBatchAsync();
        Assert.Contains("yt-dlp, ffmpeg, deno", Assert.Single(_rig.Ui.Questions));
        Assert.False(vm.IsRunning);
        Assert.Single(vm.Items);                           // the draft went to the queue, nothing ran
    }

    [AvaloniaFact]
    public void Weekly_update_rule()
    {
        var now = DateTimeOffset.Parse("2026-10-26T12:00:00-03:00");
        Assert.True(MainViewModel.UpdateIsDue("", now));
        Assert.True(MainViewModel.UpdateIsDue("2026-10-19T11:00:00-03:00", now));
        Assert.False(MainViewModel.UpdateIsDue("2026-10-20T12:00:00-03:00", now));
    }
}

/// <summary>Download with the fake yt-dlp: the whole path from the button to the queue statuses.</summary>
public sealed class DownloadRunTests : IDisposable
{
    private readonly TestRig _rig = new(tools: true);

    public DownloadRunTests() => Loc.Instance.Language = "es";

    public void Dispose() => _rig.Dispose();

    [AvaloniaFact]
    public async Task Download_runs_the_queue_and_shows_the_results()
    {
        var vm = _rig.Create();
        vm.Folder = _rig.OutFolder;
        vm.SmartPaste("https://fake.test/ok/a1\nhttps://fake.test/error/a2");
        await vm.StartBatchAsync();

        Assert.False(vm.IsRunning);
        Assert.Equal("Terminado: 1 bien, 0 para revisar, 1 con error", vm.StatusText);
        Assert.Equal(["Listo 🔒", "Error"], vm.QueueRows.Select(r => r.Status));   // 0.3: finished rows are locked
        Assert.Equal("Fake video a1.mp4", vm.QueueRows[0].File);
        Assert.True(File.Exists(Path.Combine(_rig.OutFolder, "Fake video a1.mp4")));
        Assert.True(vm.CanRetry);
        Assert.Equal(1, _rig.Ui.Sounds);
        Assert.Contains(_rig.Log.Recent(), l => l.StartsWith("=== Done: 1 OK", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task Series_numbering_asks_before_continuing_after_existing_files()
    {
        Directory.CreateDirectory(_rig.OutFolder);
        File.WriteAllText(Path.Combine(_rig.OutFolder, "Toma3.mp4"), "");
        var vm = _rig.Create();
        vm.Folder = _rig.OutFolder;
        vm.NameSeries = true;
        vm.SeriesText = "Toma";
        vm.SmartPaste("https://fake.test/ok/s1");
        vm.SendToQueueCommand.Execute(null);

        _rig.Ui.Answers.Enqueue(false);
        await vm.StartBatchAsync();
        Assert.Contains("Toma1, Toma2... (hasta Toma3)", Assert.Single(_rig.Ui.Questions));
        Assert.Equal(vm.L["rs_Queued"], vm.QueueRows[0].Status);   // nothing ran

        _rig.Ui.Answers.Enqueue(true);
        await vm.StartBatchAsync();
        Assert.Equal("Toma4.mp4", vm.QueueRows[0].File);
    }
}
