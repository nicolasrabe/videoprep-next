using System.Globalization;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Videoprep.App.Services;
using Videoprep.App.ViewModels;
using Videoprep.Core.Import;
using Videoprep.Core.Models;
using Videoprep.Core.Profiles;
using Videoprep.Core.Scraping;
using Videoprep.Core.Storage;

namespace Videoprep.App.Tests;

/// <summary>Round 2 of 0.3.0 in the view model: mini window (F4), clipboard (F5), profiles (F6), list of sources (F7).</summary>
public sealed class ExtrasFeatureTests : IDisposable
{
    private readonly TestRig _rig = new(tools: true);

    public ExtrasFeatureTests() => Loc.Instance.Language = "es";

    public void Dispose() => _rig.Dispose();

    private const string Bunny = "https://www.youtube.com/watch?v=aqz-KE-bpKQ";
    private const string Zoo = "https://www.youtube.com/watch?v=jNQXAC9IVRw";

    private MainViewModel Vm()
    {
        var vm = _rig.Create();
        vm.Folder = _rig.OutFolder;
        return vm;
    }

    // ======================= F4: mini =======================

    [AvaloniaFact]
    public async Task Mini_shows_idle_then_the_result_and_never_a_link()
    {
        var vm = Vm();
        var seen = new List<(string Text, string Counter, MiniKind Kind)>();
        vm.PropertyChanged += (_, e) =>
        {
            if (vm.IsMiniActive && e.PropertyName is nameof(MainViewModel.MiniText) or nameof(MainViewModel.MiniCounter)) seen.Add((vm.MiniText, vm.MiniCounter, vm.MiniKind));
        };
        vm.SmartPaste("https://fake.test/ok/a1\nhttps://fake.test/ok/a2");
        vm.ToggleMiniCommand.Execute(null);
        Assert.True(vm.IsMiniActive);
        Assert.True(_rig.Ui.MiniShown);
        Assert.Equal(("Todo listo", MiniKind.Idle), (vm.MiniText, vm.MiniKind));

        await vm.StartBatchAsync();
        Assert.Equal("Todo listo · 2 de 2 terminados", vm.MiniText);
        Assert.Equal(MiniKind.Idle, vm.MiniKind);
        Assert.Contains(seen, s => s.Kind == MiniKind.Downloading && s.Counter == "1/2");
        Assert.Contains(seen, s => s.Kind == MiniKind.Downloading && s.Counter == "2/2");
        // Never a link, in any state it went through.
        Assert.DoesNotContain(seen, s => s.Text.Contains("http", StringComparison.OrdinalIgnoreCase) || s.Text.Contains("fake.test", StringComparison.Ordinal));
        Assert.All(seen.Where(s => s.Kind == MiniKind.Downloading), s => Assert.False(string.IsNullOrWhiteSpace(s.Text)));
    }

    [AvaloniaFact]
    public async Task Mini_turns_red_when_something_failed()
    {
        var vm = Vm();
        vm.SmartPaste("https://fake.test/ok/a1\nhttps://fake.test/error/a2");
        vm.ToggleMiniCommand.Execute(null);
        await vm.StartBatchAsync();
        Assert.Equal("Terminado: 1 bien, 1 con error", vm.MiniText);
        Assert.True(vm.MiniIsProblem);
        Assert.DoesNotContain("fake.test", vm.MiniText, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void Mini_closes_from_its_button_and_asks_the_big_window_to_quit_from_the_menu()
    {
        var vm = Vm();
        vm.ToggleMiniCommand.Execute(null);
        vm.SetMiniPosition(120, 340);
        vm.CloseMiniCommand.Execute(null);
        Assert.False(vm.IsMiniActive);
        Assert.False(_rig.Ui.MiniShown);
        // The place is kept for the next time (also in the settings file).
        Assert.Equal((120, 340), (SettingsStore.Load(_rig.Paths.SettingsFile, out _).MiniX, SettingsStore.Load(_rig.Paths.SettingsFile, out _).MiniY));

        vm.ToggleMiniCommand.Execute(null);
        vm.QuitFromMiniCommand.Execute(null);
        Assert.Equal(1, _rig.Ui.AppClosed);                  // the big window closes (it asks first when something runs)
        vm.ToggleMiniCommand.Execute(null);                  // the same button also toggles
        Assert.False(vm.IsMiniActive);
    }

    [AvaloniaFact]
    public void Always_on_top_is_a_saved_setting_on_by_default_that_can_change_while_the_mini_is_open()
    {
        var vm = Vm();
        Assert.True(vm.MiniOnTop);
        vm.ToggleMiniCommand.Execute(null);
        vm.MiniOnTop = false;
        Assert.False(SettingsStore.Load(_rig.Paths.SettingsFile, out _).MiniOnTop);
        Assert.False(_rig.Create().MiniOnTop);               // remembered by the next session
    }

    [AvaloniaFact]
    public void Setup_and_updates_show_their_status_in_the_mini()
    {
        var vm = Vm();
        vm.ToggleMiniCommand.Execute(null);
        vm.IsRunning = true;                                 // what first-time setup does
        vm.StatusText = "Descargando yt-dlp 42 %";
        Assert.Equal((MiniKind.Busy, "Descargando yt-dlp 42 %"), (vm.MiniKind, vm.MiniText));
        vm.IsRunning = false;
        Assert.Equal(MiniKind.Idle, vm.MiniKind);
    }

    // ======================= F5: clipboard =======================

    private async Task Copy(MainViewModel vm, string text)
    {
        _rig.Ui.UserCopies(text);
        await vm.PollClipboardAsync();
    }

    [AvaloniaFact]
    public async Task The_clipboard_is_never_read_unless_the_option_is_on()
    {
        var vm = Vm();
        Assert.False(vm.ClipboardWatch);
        Assert.False(vm.Settings.ClipboardWatch);
        await Copy(vm, Bunny);
        Assert.Empty(vm.Items);
        Assert.Equal(0, _rig.Ui.ClipboardReads);
    }

    [AvaloniaFact]
    public async Task With_the_option_on_a_copied_video_link_goes_to_the_queue_and_the_content_is_never_logged()
    {
        var vm = Vm();
        _rig.Ui.UserCopies(Zoo);                             // already copied when the option is turned on: not taken
        vm.ClipboardWatch = true;
        Assert.True(SettingsStore.Load(_rig.Paths.SettingsFile, out _).ClipboardWatch);
        await vm.PollClipboardAsync();
        Assert.Empty(vm.Items);

        vm.ToggleMiniCommand.Execute(null);
        vm.IsAudio = true;                                   // what the Download tab has chosen now is what the copied links get
        vm.SelectedFormat = vm.FormatChoices.First(f => f.Key == "mp3");
        await Copy(vm, "mirá esto " + Bunny + "&t=30 está buenísimo");
        var item = Assert.Single(vm.Items);
        Assert.Equal((Bunny, NamingMode.Original, 0, "mp3"), (item.Url, item.Naming, item.Clips.Count, item.Format));   // whole video
        Assert.Equal("Se agregó 1 enlace a la cola", vm.StatusText);
        Assert.Equal(("Se agregó 1 enlace a la cola", MiniKind.Notice), (vm.MiniText, vm.MiniKind));
        Assert.Contains(_rig.Log.Recent(), l => l == "Clipboard: 1 link(s) added to the queue.");
        Assert.DoesNotContain(_rig.Log.Recent(), l => l.Contains("aqz-KE-bpKQ", StringComparison.Ordinal) || l.Contains("buenísimo", StringComparison.Ordinal));

        await Copy(vm, Bunny);                               // the same link again: no duplicate
        await Copy(vm, "https://www.lanacion.com.ar/politica/nota-nid123/");   // a web page is not a video
        await Copy(vm, "hola");
        Assert.Single(vm.Items);

        vm.ClipboardWatch = false;                           // off again: nothing more is read
        var reads = _rig.Ui.ClipboardReads;
        await Copy(vm, Zoo);
        Assert.Equal(reads, _rig.Ui.ClipboardReads);
        Assert.Single(vm.Items);
    }

    [AvaloniaFact]
    public async Task What_videoprep_copies_itself_is_ignored()
    {
        var vm = Vm();
        vm.ClipboardWatch = true;
        vm.LogLine("Pegá " + Zoo + " en Descargar");         // the log contains a video link
        vm.CopyLogCommand.Execute(null);                     // "Copiar registro" puts it on the clipboard
        await vm.PollClipboardAsync();
        Assert.Empty(vm.Items);
        await Copy(vm, Zoo);                                 // the user copies the same link by hand: that one counts
        Assert.Single(vm.Items);
    }

    [AvaloniaFact]
    public async Task More_than_20_links_wait_for_an_answer_and_the_mini_says_so()
    {
        var vm = Vm();
        vm.ClipboardWatch = true;
        var many = string.Join('\n', Enumerable.Range(0, 25).Select(i => $"https://youtu.be/{i:D11}"));

        // The mini is open: nothing is asked (no window may pop up over what the user is doing).
        vm.ToggleMiniCommand.Execute(null);
        await Copy(vm, many);
        Assert.Empty(vm.Items);
        Assert.Empty(_rig.Ui.Questions);
        Assert.Equal("25 enlaces copiados: abrí la ventana grande para decidir", vm.MiniText);

        // Back to the big window: now it asks, and "No" adds nothing.
        _rig.Ui.Answers.Enqueue(false);
        vm.CloseMiniCommand.Execute(null);
        await vm.ResolvePendingClipboardAsync();
        Assert.Equal("Copiaste 25 enlaces de video. ¿Agregarlos todos a la cola?", Assert.Single(_rig.Ui.Questions));
        Assert.Empty(vm.Items);

        // With the big window in front it asks at once; "Sí" adds them all.
        _rig.Ui.Answers.Enqueue(true);
        await Copy(vm, many + "\nhttps://youtu.be/AAAAAAAAAAA");
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal(26, vm.Items.Count);
        Assert.Equal("Se agregaron 26 enlaces a la cola", vm.StatusText);
    }

    [AvaloniaFact]
    public async Task A_big_copy_waits_while_the_big_window_is_behind_and_is_asked_when_it_comes_forward()
    {
        var vm = Vm();
        vm.ClipboardWatch = true;
        _rig.Ui.MainActive = false;
        await Copy(vm, string.Join('\n', Enumerable.Range(0, 21).Select(i => $"https://youtu.be/{i:D11}")));
        Assert.Empty(_rig.Ui.Questions);
        _rig.Ui.Answers.Enqueue(true);
        _rig.Ui.MainActive = true;
        vm.OnMainActivated();
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal(21, vm.Items.Count);
    }

    [AvaloniaFact]
    public async Task The_watcher_waits_for_the_copy_to_settle_and_skips_a_copy_replaced_meanwhile()
    {
        var found = new List<IReadOnlyList<string>>();
        var source = new SequencedSource();
        var delays = 0;
        var watcher = new ClipboardWatcher(source, (_, _) => { delays++; source.OnDelay?.Invoke(); return Task.CompletedTask; }, found.Add, useTimer: false);
        watcher.Start();

        source.Set(Bunny);
        source.OnDelay = () => source.Set(Zoo);                // copied again while waiting: this round is skipped
        await watcher.PollOnceAsync(TestContext.Current.CancellationToken);
        Assert.Empty(found);
        Assert.Equal(0, source.Reads);                         // nothing read yet

        source.OnDelay = null;
        await watcher.PollOnceAsync(TestContext.Current.CancellationToken);
        Assert.Equal([[Zoo]], found.Select(f => f.ToList()));
        Assert.Equal(1, source.Reads);

        await watcher.PollOnceAsync(TestContext.Current.CancellationToken);   // unchanged: no read at all
        Assert.Equal(1, source.Reads);
        Assert.Equal(2, delays);
    }

    [AvaloniaFact]
    public async Task Without_a_change_number_the_text_is_compared_instead()
    {
        var found = new List<IReadOnlyList<string>>();
        var source = new SequencedSource { Sequence = 0 };
        var watcher = new ClipboardWatcher(source, (_, _) => Task.CompletedTask, found.Add, useTimer: false);
        watcher.Start();
        source.Text = Bunny;
        await watcher.PollOnceAsync(TestContext.Current.CancellationToken);
        await watcher.PollOnceAsync(TestContext.Current.CancellationToken);
        Assert.Single(found);
        source.Text = Zoo;
        await watcher.PollOnceAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, found.Count);
    }

    private sealed class SequencedSource : IClipboardSource
    {
        public long Sequence { get; set; } = 1;
        public string? Text { get; set; }
        public int Reads { get; private set; }
        public Action? OnDelay { get; set; }
        public long ClipboardSequence => Sequence;
        public void Set(string text) { Text = text; Sequence++; }
        public Task<string?> ReadClipboardTextAsync() { Reads++; return Task.FromResult(Text); }
    }

    // ======================= F6: profiles =======================

    [AvaloniaFact]
    public void Examples_are_in_the_picker_and_picking_one_fills_format_resolution_and_names()
    {
        var vm = Vm();
        Assert.Equal(["Premiere 1080p", "ProRes 4K", "Solo audio (M4A)"], vm.ProfileChoices.Select(c => c.Label));

        vm.SelectedProfileChoice = vm.ProfileChoices[1];
        Assert.Equal(("movp", "4K", false), (vm.SelectedFormat!.Key, vm.SelectedResolution, vm.IsAudio));
        Assert.Null(vm.SelectedProfileChoice);               // the box goes back to "Elegir un perfil..."

        vm.SelectedProfileChoice = vm.ProfileChoices[2];
        Assert.True(vm.IsAudio);
        Assert.Equal("m4a", vm.SelectedFormat!.Key);

        vm.SelectedProfileChoice = vm.ProfileChoices[0];
        Assert.Equal((false, "mp4", "1080p", true), (vm.IsAudio, vm.SelectedFormat!.Key, vm.SelectedResolution, vm.NameOriginal));
    }

    [AvaloniaFact]
    public async Task Save_the_current_choices_as_a_profile_and_use_it_again()
    {
        var vm = Vm();
        vm.SelectedFormat = vm.FormatChoices.First(f => f.Key == "movp");
        vm.SelectedResolution = "1440p";
        vm.NameSeries = true;
        vm.SeriesText = "Toma";
        _rig.Ui.Texts.Enqueue("  Mis   tomas ");
        await vm.SaveProfileCommand.ExecuteAsync(null);

        var p = Assert.Single(vm.Profiles.User);
        Assert.Equal(("Mis tomas", "movp", 1440, "series", "Toma"), (p.Name, p.Format, p.Cap, p.Naming, p.Prefix));
        Assert.Equal("Perfil guardado: Mis tomas", vm.StatusText);
        Assert.Contains("Mis tomas", vm.ProfileChoices.Select(c => c.Label));
        Assert.Single(ProfileStore.Load(_rig.Paths.ProfilesFile, out _).User);                        // saved to profiles.json

        vm.NameOriginal = true;
        vm.SelectedFormat = vm.FormatChoices.First(f => f.Key == "mp4");
        vm.SelectedProfileChoice = vm.ProfileChoices.First(c => c.Label == "Mis tomas");
        Assert.Equal(("movp", "1440p", true, "Toma"), (vm.SelectedFormat!.Key, vm.SelectedResolution, vm.NameSeries, vm.SeriesText));
    }

    [AvaloniaTheory]
    [InlineData("", "Escribí un nombre para el perfil.")]
    [InlineData("premiere 1080P", "Ya hay un perfil con ese nombre.")]
    [InlineData("Solo audio (M4A)", "Ya hay un perfil con ese nombre.")]
    [InlineData("Audio only (M4A)", "Ya hay un perfil con ese nombre.")]
    public async Task A_profile_cannot_have_an_empty_name_or_an_example_name(string name, string message)
    {
        var vm = Vm();
        _rig.Ui.Texts.Enqueue(name);
        await vm.SaveProfileCommand.ExecuteAsync(null);
        Assert.Equal(message, Assert.Single(_rig.Ui.Messages));
        Assert.Empty(vm.Profiles.User);
    }

    /// <summary>A subscription that follows a profile (written to subscriptions.json before the window opens).</summary>
    private MainViewModel VmWithFollower(out FormatProfile profile)
    {
        var store = ProfileStore.Load(_rig.Paths.ProfilesFile, out _);
        profile = store.Add(new FormatProfile { Name = "Voz", Format = "m4a", Cap = 720 });
        var p = profile;
        SubscriptionStore.Save(_rig.Paths.SubscriptionsFile, new SubscriptionFile
        {
            Subscriptions =
            [
                new Subscription { Id = "sub_1", Name = "@canal-uno", Url = "https://vimeo.com/uno", Folder = "D", Profile = new SubscriptionProfile { Format = "m4a", Cap = 720, ProfileId = p.Id } },
                new Subscription { Id = "sub_2", Name = "@canal-dos", Url = "https://vimeo.com/dos", Folder = "D", Profile = new SubscriptionProfile { Format = "mp4", Cap = 1080, ProfileId = p.Id } },
                new Subscription { Id = "sub_3", Name = "@propios", Url = "https://vimeo.com/tres", Folder = "D" },
            ],
        });
        var vm = Vm();
        Assert.Equal(["Voz", "Voz", "(valores propios)"], vm.Scraping.Subscriptions.Rows.Select(r => r.ProfileText));   // the table says which profile
        return vm;
    }

    private Subscription StoredSub(string id) => SubscriptionStore.Load(_rig.Paths.SubscriptionsFile, out _).Subscriptions.Single(s => s.Id == id);

    [AvaloniaFact]
    public async Task Editing_a_profile_that_subscriptions_follow_warns_with_their_names_and_Cancel_changes_nothing()
    {
        var vm = VmWithFollower(out var p);
        vm.SelectedFormat = vm.FormatChoices.First(f => f.Key == "movp");
        vm.SelectedResolution = "720p";
        _rig.Ui.Texts.Enqueue("voz");                        // the same name, other case: it replaces the values
        _rig.Ui.Answers.Enqueue(true);                       // "replace its values?"
        _rig.Ui.ChoiceAnswers.Enqueue(false);                // Cancelar
        await vm.SaveProfileCommand.ExecuteAsync(null);

        var warning = Assert.Single(_rig.Ui.Choices);
        Assert.Contains("«Voz»", warning);
        Assert.Contains("• @canal-uno", warning);
        Assert.Contains("• @canal-dos", warning);
        Assert.DoesNotContain("@propios", warning);
        Assert.Equal("m4a", vm.Profiles.Find(p.Id)!.Format);                                           // unchanged
        Assert.Equal("mp4", StoredSub("sub_2").Profile.Format);

        // Continuar: the profile changes and the subscriptions keep the new values as their own.
        vm.SelectedFormat = vm.FormatChoices.First(f => f.Key == "movp");
        vm.SelectedResolution = "720p";
        _rig.Ui.Texts.Enqueue("Voz");
        _rig.Ui.Answers.Enqueue(true);
        _rig.Ui.ChoiceAnswers.Enqueue(true);
        await vm.SaveProfileCommand.ExecuteAsync(null);
        Assert.Equal(("movp", 720), (vm.Profiles.Find(p.Id)!.Format, vm.Profiles.Find(p.Id)!.Cap));
        Assert.Equal(("movp", 720, p.Id), (StoredSub("sub_2").Profile.Format, StoredSub("sub_2").Profile.Cap, StoredSub("sub_2").Profile.ProfileId));
        Assert.Null(StoredSub("sub_3").Profile.ProfileId);
        Assert.Single(vm.Profiles.User);                                                                // replaced, not duplicated
    }

    [AvaloniaFact]
    public async Task Renaming_warns_too_and_the_table_shows_the_new_name()
    {
        var vm = VmWithFollower(out var p);
        vm.SelectedManagedProfile = vm.ManagedProfiles.First(r => r.Profile.Id == p.Id);

        _rig.Ui.Texts.Enqueue("Voz en off");
        _rig.Ui.ChoiceAnswers.Enqueue(false);
        await vm.RenameProfileCommand.ExecuteAsync(null);
        Assert.Contains("Si le cambiás el nombre", Assert.Single(_rig.Ui.Choices));
        Assert.Equal("Voz", vm.Profiles.Find(p.Id)!.Name);

        _rig.Ui.Texts.Enqueue("Voz en off");
        _rig.Ui.ChoiceAnswers.Enqueue(true);
        await vm.RenameProfileCommand.ExecuteAsync(null);
        Assert.Equal("Voz en off", vm.Profiles.Find(p.Id)!.Name);
        Assert.Equal(["Voz en off", "Voz en off", "(valores propios)"], vm.Scraping.Subscriptions.Rows.Select(r => r.ProfileText));
    }

    [AvaloniaFact]
    public async Task Deleting_warns_and_the_subscriptions_keep_their_values_without_a_profile()
    {
        var vm = VmWithFollower(out var p);
        vm.SelectedManagedProfile = vm.ManagedProfiles.First(r => r.Profile.Id == p.Id);
        _rig.Ui.ChoiceAnswers.Enqueue(false);
        await vm.DeleteProfileCommand.ExecuteAsync(null);
        Assert.Contains("sin perfil", Assert.Single(_rig.Ui.Choices));
        Assert.NotNull(vm.Profiles.Find(p.Id));                                                         // Cancelar: still there

        _rig.Ui.ChoiceAnswers.Enqueue(true);
        await vm.DeleteProfileCommand.ExecuteAsync(null);
        Assert.Null(vm.Profiles.Find(p.Id));
        Assert.DoesNotContain(vm.ProfileChoices, c => c.Label == "Voz");
        var s = StoredSub("sub_1");
        Assert.Equal((null, "m4a", 720), (s.Profile.ProfileId, s.Profile.Format, s.Profile.Cap));
        Assert.Equal(["(valores propios)", "(valores propios)", "(valores propios)"], vm.Scraping.Subscriptions.Rows.Select(r => r.ProfileText));
    }

    [AvaloniaFact]
    public async Task Deleting_a_profile_nobody_follows_just_asks_once_and_the_examples_cannot_be_changed()
    {
        var vm = Vm();
        _rig.Ui.Texts.Enqueue("Sin uso");
        await vm.SaveProfileCommand.ExecuteAsync(null);
        vm.SelectedManagedProfile = vm.ManagedProfiles.First(r => r.Label == "Sin uso");
        _rig.Ui.Answers.Enqueue(true);
        await vm.DeleteProfileCommand.ExecuteAsync(null);
        Assert.Equal("¿Borrar el perfil «Sin uso»?", Assert.Single(_rig.Ui.Questions));
        Assert.Empty(_rig.Ui.Choices);
        Assert.Empty(vm.Profiles.User);

        vm.SelectedManagedProfile = vm.ManagedProfiles[0];
        Assert.EndsWith("(ejemplo)", vm.ManagedProfiles[0].Label);
        await vm.DeleteProfileCommand.ExecuteAsync(null);
        await vm.RenameProfileCommand.ExecuteAsync(null);
        Assert.All(_rig.Ui.Messages, m => Assert.Equal("Los perfiles de ejemplo no se pueden cambiar.", m));
        Assert.Equal(2, _rig.Ui.Messages.Count);
    }

    [AvaloniaFact]
    public async Task Profiles_export_and_import_as_json_without_replacing_anything()
    {
        var vm = Vm();
        _rig.Ui.Texts.Enqueue("Mi perfil");
        await vm.SaveProfileCommand.ExecuteAsync(null);
        var file = Path.Combine(_rig.Dir, "perfiles.json");
        _rig.Ui.SavePath = file;
        await vm.ExportProfilesCommand.ExecuteAsync(null);
        Assert.Contains("\"name\": \"Mi perfil\"", File.ReadAllText(file));

        _rig.Ui.OpenPath = file;
        await vm.ImportProfilesCommand.ExecuteAsync(null);
        Assert.Equal(["Mi perfil", "Mi perfil (2)"], vm.Profiles.User.Select(p => p.Name));
        Assert.Contains("Perfiles importados: 1.", _rig.Ui.Messages[^1]);

        File.WriteAllText(file, "esto no es json");
        await vm.ImportProfilesCommand.ExecuteAsync(null);
        Assert.StartsWith("No se pudo importar", _rig.Ui.Messages[^1]);
        Assert.Equal(2, vm.Profiles.User.Count);
    }

    [AvaloniaFact]
    public async Task The_queue_dialog_can_apply_a_profile()
    {
        var vm = Vm();
        vm.SmartPaste("https://vimeo.com/1\nhttps://vimeo.com/2");
        vm.Rename(vm.QueueRows[0], "Entrevista");
        vm.SetQueueSelection(vm.QueueRows);
        _rig.Ui.Batch = b =>
        {
            Assert.Equal("(sin cambios)", b.SelectedProfile.Label);
            b.SelectedProfile = b.Profiles.First(c => c.Label == "ProRes 4K");
            // A profile that says "title of the video" leaves the typed names alone.
            Assert.Equal(("MOV - ProRes 422 HQ", "4K", "(sin cambios)"), (b.SelectedFormat.Label, b.SelectedResolution.Label, b.SelectedNames.Label));
            Assert.Equal("Entrevista  →  Entrevista.mov", b.Preview[0]);
            return true;
        };
        await vm.BatchEditCommand.ExecuteAsync(null);
        Assert.Equal(["movp", "movp"], vm.Items.Select(i => i.Format));
        Assert.Equal([2160, 2160], vm.Items.Select(i => i.Cap));
        Assert.Equal([NamingMode.Custom, NamingMode.Original], vm.Items.Select(i => i.Naming));    // names untouched
    }

    [AvaloniaFact]
    public async Task The_import_window_offers_the_profiles_and_a_series_profile_names_the_items()
    {
        var store = ProfileStore.Load(_rig.Paths.ProfilesFile, out _);
        store.Add(new FormatProfile { Name = "Escenas", Format = "mp4", Cap = 720, Naming = "series", Prefix = "Escena" });
        var vm = Vm();
        var file = Path.Combine(_rig.Dir, "enlaces.txt");
        File.WriteAllText(file, "https://vimeo.com/76979871\nhttps://vimeo.com/76979872\n");
        _rig.Ui.Review = r =>
        {
            Assert.Equal(["El de la pestaña Descargar (MP4 - H.264 + AAC · 1080p)", "Perfil · Premiere 1080p", "Perfil · ProRes 4K", "Perfil · Solo audio (M4A)", "Perfil · Escenas"],
                         r.Formats.Take(5).Select(f => f.Label));
            r.AllCommand.Execute(null);
            r.SelectedFormat = r.Formats.First(f => f.Label == "Perfil · Escenas");
            return true;
        };
        await vm.ImportLinksFromFileAsync(file);
        Assert.Equal([(720, NamingMode.Series, "Escena"), (720, NamingMode.Series, "Escena")], vm.Items.Select(i => (i.Cap, i.Naming, i.Prefix)));
    }

    [AvaloniaFact]
    public void The_subscription_dialog_follows_a_profile_until_something_is_changed_by_hand()
    {
        var store = ProfileStore.InMemory();
        var p = store.Add(new FormatProfile { Name = "Voz", Format = "m4a", Cap = 720, Naming = "series", Prefix = "Voz" });
        var choices = new[] { new ProfileChoice(p, "Voz") };
        var dlg = new SubscriptionEditViewModel(null, _ => Task.FromResult<string?>(null), choices) { Url = "https://vimeo.com/uno", Folder = "D:\\x" };
        Assert.Equal("(valores propios)", dlg.SelectedProfile!.Label);

        dlg.SelectedProfile = dlg.Profiles[1];
        Assert.Equal(("m4a", true, "Voz"), (dlg.Format.FormatKey, dlg.NameSeries, dlg.Prefix));
        var sub = dlg.Build(out _)!;
        Assert.Equal((p.Id, "m4a", "series"), (sub.Profile.ProfileId, sub.Profile.Format, sub.Profile.Naming));   // follows it and keeps the values

        dlg.Format.SelectedFormat = dlg.Format.FormatChoices.First(f => f.Key == "mp3");                          // by hand: own values
        Assert.Equal("(valores propios)", dlg.SelectedProfile.Label);
        Assert.Null(dlg.Build(out _)!.Profile.ProfileId);

        // Opening it again shows the profile it follows.
        var again = new SubscriptionEditViewModel(sub, _ => Task.FromResult<string?>(null), choices);
        Assert.Equal("Voz", again.SelectedProfile!.Label);
    }

    [AvaloniaFact]
    public void The_scraping_format_box_takes_a_profile()
    {
        var vm = Vm();
        var format = vm.Scraping.Explore.Format;
        Assert.Equal(3, format.ProfileChoices.Count);
        format.PickedProfile = format.ProfileChoices[1];
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(("movp", "4K"), (format.FormatKey, format.SelectedResolution));
        Assert.Null(format.PickedProfile);
    }

    // ======================= F7: list of sources =======================

    private string[] SourceFiles(string folder) =>
        Directory.Exists(folder) ? Directory.GetFiles(folder, "fuentes-*.*").OrderBy(f => f).ToArray() : [];

    [AvaloniaFact]
    public async Task No_list_is_written_unless_the_option_is_on()
    {
        var vm = Vm();
        vm.SmartPaste("https://fake.test/ok/a1");
        await vm.StartBatchAsync();
        Assert.False(vm.Settings.SourceList);
        Assert.Empty(SourceFiles(_rig.OutFolder));
    }

    [AvaloniaFact]
    public async Task A_finished_batch_writes_the_list_with_the_exact_file_names_and_without_session_data()
    {
        var vm = Vm();
        vm.ListCulture = new CultureInfo("es-AR");
        vm.SourceListOn = true;
        vm.SmartPaste("https://fake.test/ok/a1?token=SECRET123&x=1\nhttps://fake.test/error/a2\nhttps://fake.test/ok/a3");
        await vm.StartBatchAsync();

        var file = Assert.Single(SourceFiles(_rig.OutFolder));
        Assert.Matches(@"fuentes-\d{8}-\d{4}\.csv$", file);
        var bytes = File.ReadAllBytes(file);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);                                                   // BOM: Excel reads the accents
        var text = File.ReadAllText(file);
        Assert.DoesNotContain("SECRET123", text);
        var rows = Core.Import.MarkupLinks.CsvRows(text).ToList();
        Assert.Equal(["Título", "Canal", "Enlace", "Nombre del archivo guardado", "Ruta", "Formato", "Fecha de descarga", "Estado"], rows[0]);
        Assert.Equal(4, rows.Count);                                                                    // header + one line per output (the failed one too)
        Assert.Equal(["Fake video a1", "Fake channel", "https://fake.test/ok/a1?x=1", "Fake video a1.mp4", Path.Combine(_rig.OutFolder, "Fake video a1.mp4"), "MP4 - H.264 + AAC · 1080p"], rows[1].Take(6));
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$", rows[1][6]);
        Assert.Equal("Listo", rows[1][7]);
        Assert.Equal(("Error", ""), (rows[2][7], rows[2][3]));                                         // no file for a failed video
        Assert.Contains("Lista de fuentes guardada: fuentes-", vm.StatusText);
        Assert.True(File.Exists(Path.Combine(_rig.OutFolder, rows[3][3])));
    }

    [AvaloniaFact]
    public async Task The_list_can_be_a_txt_and_goes_into_the_subfolder_of_each_item()
    {
        var vm = Vm();
        vm.SourceListOn = true;
        vm.SourceListFormatIndex = 1;
        vm.SmartPaste("https://fake.test/ok/b1\nhttps://fake.test/ok/b2");
        vm.SendDraft(interactive: false);
        vm.Items[1] = vm.Items[1] with { SubFolder = "guion" };
        await vm.StartBatchAsync();

        var main = Assert.Single(SourceFiles(_rig.OutFolder));
        var sub = Assert.Single(SourceFiles(Path.Combine(_rig.OutFolder, "guion")));
        Assert.EndsWith(".txt", main);
        Assert.Equal(2, File.ReadAllLines(main).Length);                                               // header + b1
        Assert.Contains("Fake video b2.mp4", File.ReadAllText(sub));
        Assert.DoesNotContain("Fake video b2.mp4", File.ReadAllText(main));
    }

    [AvaloniaFact]
    public async Task Export_list_of_sources_from_the_queue_menu()
    {
        var vm = Vm();
        vm.SmartPaste("https://fake.test/ok/c1\nhttps://fake.test/ok/c2");
        await vm.ExportSourcesCommand.ExecuteAsync(null);
        Assert.Contains("No hay filas \"Listo\"", _rig.Ui.Messages[^1]);                              // nothing finished yet

        await vm.StartBatchAsync();
        var path = Path.Combine(_rig.Dir, "mis-fuentes.csv");
        _rig.Ui.SavePath = path;
        vm.ListCulture = CultureInfo.InvariantCulture;
        vm.SetQueueSelection([vm.QueueRows[1]]);                                                       // only the selected finished row
        await vm.ExportSourcesCommand.ExecuteAsync(null);
        var rows = Core.Import.MarkupLinks.CsvRows(File.ReadAllText(path)).ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal("Fake video c2.mp4", rows[1][3]);

        vm.SetQueueSelection([]);                                                                      // nothing selected: every finished row
        _rig.Ui.SavePath = Path.Combine(_rig.Dir, "todo.txt");
        await vm.ExportSourcesCommand.ExecuteAsync(null);
        Assert.Equal(3, File.ReadAllLines(_rig.Ui.SavePath).Length);
    }

    [AvaloniaFact]
    public void New_settings_reload_in_the_next_session()
    {
        var vm = Vm();
        vm.SourceListOn = true;
        vm.SourceListFormatIndex = 1;
        vm.ClipboardWatch = true;
        var again = _rig.Create();
        Assert.True(again.SourceListOn);
        Assert.Equal(1, again.SourceListFormatIndex);
        Assert.True(again.ClipboardWatch);
        Assert.Equal(["CSV (se abre en Excel)", "TXT"], again.SourceListFormats);
    }
}
