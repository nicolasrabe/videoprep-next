using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Videoprep.App.Services;
using Videoprep.App.ViewModels;
using Videoprep.App.Views;
using Videoprep.Core.Models;
using Videoprep.Core.Profiles;
using Videoprep.Core.Scraping;
using Videoprep.Core.Storage;
using Videoprep.Core.Themes;

namespace Videoprep.App.Tests;

/// <summary>F8 "Velocidad constante" in the screens: the checkbox, Opciones, profiles, "Cambiar N elementos", Scraping.</summary>
public sealed class ConstantRateAppTests : IDisposable
{
    private readonly TestRig _rig = new(tools: true);

    public ConstantRateAppTests() => Loc.Instance.Language = "es";

    public void Dispose() => _rig.Dispose();

    private MainViewModel Vm()
    {
        var vm = _rig.Create();
        vm.Folder = _rig.OutFolder;
        return vm;
    }

    [AvaloniaFact]
    public void It_is_off_by_default_and_a_video_sent_to_the_queue_carries_it_only_when_it_is_a_video_format()
    {
        var vm = Vm();
        Assert.False(vm.ConstantRate);
        Assert.False(vm.Settings.ConstantRate);

        vm.SmartPaste("https://vimeo.com/1");
        vm.ConstantRate = true;
        vm.SendDraft(interactive: false);
        Assert.True(vm.Items[0].ConstantRate);
        Assert.Contains("MP4 - H.264 + AAC · 1080p · velocidad constante", vm.QueueRows[0].Format);

        vm.SmartPaste("https://vimeo.com/2");
        vm.IsAudio = true;                                  // audio formats have no frame rate: the option never applies
        vm.SendDraft(interactive: false);
        Assert.False(vm.Items[1].ConstantRate);
        Assert.DoesNotContain("velocidad constante", vm.QueueRows[1].Format);
        Assert.False(vm.ConstantRate);                       // a new draft starts from the setting again
    }

    [AvaloniaFact]
    public void Opciones_sets_the_default_for_new_items_and_is_saved()
    {
        var vm = Vm();
        _rig.Ui.Options = o => { o.ConstantRate = true; return true; };
        vm.ShowOptionsCommand.Execute(null);
        Assert.True(vm.Settings.ConstantRate);
        Assert.True(vm.ConstantRate);                        // the Download tab shows it at once
        Assert.True(SettingsStore.Load(_rig.Paths.SettingsFile, out _).ConstantRate);
        Assert.True(_rig.Create().ConstantRate);             // and the next session starts with it
    }

    [AvaloniaFact]
    public void Editing_a_queue_item_brings_its_choice_back_to_the_Download_tab()
    {
        var vm = Vm();
        vm.SmartPaste("https://vimeo.com/1");
        vm.ConstantRate = true;
        vm.SendDraft(interactive: false);
        vm.ConstantRate = false;
        vm.SetQueueSelection([vm.QueueRows[0]]);
        vm.EditSelectedCommand.Execute(null);
        Assert.True(vm.ConstantRate);
        vm.ConstantRate = false;
        vm.SendDraft(interactive: false);                    // the edited item goes back with the new choice
        Assert.False(vm.Items[0].ConstantRate);
    }

    [AvaloniaFact]
    public async Task A_profile_saves_and_applies_it()
    {
        var vm = Vm();
        vm.ConstantRate = true;
        _rig.Ui.Texts.Enqueue("Clips de Twitch");
        await vm.SaveProfileCommand.ExecuteAsync(null);
        var p = Assert.Single(vm.Profiles.User);
        Assert.True(p.ConstantRate);
        Assert.Contains("velocidad constante", vm.ManagedProfiles.First(r => r.Profile.Id == p.Id).Detail);

        vm.ConstantRate = false;
        vm.SelectedProfileChoice = vm.ProfileChoices.First(c => c.Label == "Clips de Twitch");
        Assert.True(vm.ConstantRate);
        vm.SelectedProfileChoice = vm.ProfileChoices.First(c => c.Label == "Premiere 1080p");   // a profile that does not ask for it: off
        Assert.False(vm.ConstantRate);
    }

    [AvaloniaFact]
    public async Task The_queue_dialog_sets_it_for_the_selected_videos_and_a_profile_can_turn_it_on()
    {
        var vm = Vm();
        vm.SmartPaste("https://vimeo.com/1\nhttps://vimeo.com/2\nhttps://vimeo.com/3");
        vm.Items[2] = vm.Items[2] with { Format = "mp3" };
        vm.SetQueueSelection(vm.QueueRows);
        var store = vm.Profiles;
        store.Add(new FormatProfile { Name = "Twitch", Format = "mp4", Cap = 1080, ConstantRate = true });
        _rig.Ui.Batch = b =>
        {
            Assert.Equal("(sin cambios)", b.SelectedConstant.Label);
            Assert.Equal(["(sin cambios)", "Sí", "No"], b.ConstantChoices.Select(c => c.Label));
            b.SelectedConstant = b.ConstantChoices[1];
            return true;
        };
        await vm.BatchEditCommand.ExecuteAsync(null);
        Assert.Equal([true, true, false], vm.Items.Select(i => i.ConstantRate));   // the audio item cannot have it

        _rig.Ui.Batch = b =>
        {
            b.SelectedConstant = b.ConstantChoices[2];                              // "No"
            return true;
        };
        await vm.BatchEditCommand.ExecuteAsync(null);
        Assert.Equal([false, false, false], vm.Items.Select(i => i.ConstantRate));
        vm.UndoCommand.Execute(null);
        Assert.Equal([true, true, false], vm.Items.Select(i => i.ConstantRate));   // and it can be undone

        var dlg = new BatchEditViewModel(vm.Items.ToList(), 0, _ => null, _ => 1, vm.ProfileList());
        dlg.SelectedProfile = dlg.Profiles.First(c => c.Label == "Twitch");
        Assert.Equal("Sí", dlg.SelectedConstant.Label);
        dlg.SelectedProfile = dlg.Profiles.First(c => c.Label == "Premiere 1080p");
        Assert.Equal("Sí", dlg.SelectedConstant.Label);                             // a profile that does not ask for it changes nothing
    }

    [AvaloniaFact]
    public async Task Links_from_a_file_and_from_the_clipboard_start_with_what_the_tab_has()
    {
        var vm = Vm();
        vm.ConstantRate = true;
        vm.ClipboardWatch = true;
        _rig.Ui.UserCopies("https://youtu.be/aqz-KE-bpKQ");
        await vm.PollClipboardAsync();
        Assert.True(Assert.Single(vm.Items).ConstantRate);

        var file = Path.Combine(_rig.Dir, "enlaces.txt");
        File.WriteAllText(file, "https://vimeo.com/76979871\n");
        _rig.Ui.Review = r => { r.AllCommand.Execute(null); return true; };
        await vm.ImportLinksFromFileAsync(file);
        Assert.True(vm.Items[1].ConstantRate);

        // A profile chosen in the import window brings its own choice.
        vm.Profiles.Add(new FormatProfile { Name = "Sin cte", Format = "mp4", Cap = 720 });
        File.WriteAllText(file, "https://vimeo.com/76979872\n");
        _rig.Ui.Review = r =>
        {
            r.AllCommand.Execute(null);
            r.SelectedFormat = r.Formats.First(f => f.Label == "Perfil · Sin cte");
            return true;
        };
        await vm.ImportLinksFromFileAsync(file);
        Assert.False(vm.Items[2].ConstantRate);
    }

    [AvaloniaFact]
    public void Scraping_subscription_dialog_and_Explorar_carry_it()
    {
        var store = ProfileStore.InMemory();
        var p = store.Add(new FormatProfile { Name = "Twitch", Format = "mp4", Cap = 1080, ConstantRate = true });
        var choices = new[] { new ProfileChoice(p, "Twitch") };
        var dlg = new SubscriptionEditViewModel(null, _ => Task.FromResult<string?>(null), choices) { Url = "https://vimeo.com/uno", Folder = "D:\\x" };

        dlg.SelectedProfile = dlg.Profiles[1];
        Assert.True(dlg.Format.ConstantRate);
        var sub = dlg.Build(out _)!;
        Assert.Equal((true, p.Id), (sub.Profile.ConstantRate, sub.Profile.ProfileId));

        dlg.Format.ConstantRate = false;                     // by hand: own values
        Assert.Equal("(valores propios)", dlg.SelectedProfile!.Label);
        var own = dlg.Build(out _)!;
        Assert.False(own.Profile.ConstantRate);
        Assert.Null(own.Profile.ProfileId);

        var vm = Vm();
        vm.Profiles.Add(new FormatProfile { Name = "Twitch", Format = "mp4", Cap = 1080, ConstantRate = true });
        var picker = vm.Scraping.Explore.Format;
        picker.ConstantRate = true;
        vm.AddToQueue([("https://vimeo.com/5", "T")], picker.FormatKey, picker.Cap, picker.ConstantRate);
        Assert.True(Assert.Single(vm.Items).ConstantRate);
    }
}

/// <summary>The new F8 controls on screen, in every theme and both languages (PNG files starting with "v03r3-").</summary>
public sealed partial class ConstantRateScreensTests : IDisposable
{
    private readonly TestRig _rig = new(tools: true);

    public void Dispose() => _rig.Dispose();

    [GeneratedRegex("^[a-z]{1,8}_[a-zA-Z0-9_]+$")]
    private static partial Regex LooksLikeKey();

    public static TheoryData<string, string> Combos() => QueueScreensTests.Combos();

    private (MainWindow Window, MainViewModel Vm) Open(string lang, string theme)
    {
        Loc.Instance.Language = lang;
        SettingsStore.Save(_rig.Paths.SettingsFile, new Settings { Lang = lang });
        var window = new MainWindow { Width = 1000, Height = 740 };
        var vm = new MainViewModel(_rig.Services, window, a => Dispatcher.UIThread.Post(a));
        vm.Settings.CustomColor = "#3a2f4a";
        vm.Settings.Theme = theme;
        vm.Folder = _rig.OutFolder;
        ThemeService.Apply(Application.Current!, Palette.For(theme, "#3a2f4a"));
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    private static void Save(TopLevel window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(dir);
        frame!.Save(Path.Combine(dir, name + ".png"), PngBitmapEncoderOptions.Default);
    }

    private static void NoRawKeys(Visual root)
    {
        var texts = root.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "").ToList();
        Assert.DoesNotContain(texts, t => LooksLikeKey().IsMatch(t));
        Assert.DoesNotContain(texts, t => t.Contains("{0}", StringComparison.Ordinal));
    }

    [AvaloniaTheory]
    [MemberData(nameof(Combos))]
    public void The_new_controls_in_every_theme_and_language(string lang, string theme)
    {
        var (w, vm) = Open(lang, theme);
        vm.ConstantRate = true;
        vm.SmartPaste("https://fake.test/ok/c1\nhttps://fake.test/ok/c2");
        vm.SendDraft(interactive: false);
        vm.SmartPaste("https://fake.test/ok/c3");
        vm.SelectedTab = MainViewModel.TabDownload;
        Save(w, $"v03r3-{lang}-{theme}-descargar-cte");
        NoRawKeys(w);

        vm.SelectedTab = MainViewModel.TabQueue;
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(vm.QueueRows, r => r.Format.Contains(Loc.Instance["fmt_cfr"], StringComparison.Ordinal));
        Save(w, $"v03r3-{lang}-{theme}-cola-cte");

        var settings = new Settings { ConstantRate = true };
        var options = new Window { Width = 620, Height = 560, Content = new Border { Padding = new Thickness(18), Child = new OptionsView { DataContext = OptionsViewModel.From(settings) } } };
        options.Show();
        Save(options, $"v03r3-{lang}-{theme}-opciones");
        NoRawKeys(options);
        options.Close();

        var batch = new BatchEditViewModel(vm.Items.ToList(), 0, _ => null, _ => 1, vm.ProfileList());
        batch.SelectedConstant = batch.ConstantChoices[1];
        var batchWindow = new Window { Width = 640, Height = 520, Content = new Border { Padding = new Thickness(18), Child = new BatchEditView { DataContext = batch } } };
        batchWindow.Show();
        Save(batchWindow, $"v03r3-{lang}-{theme}-cambiar-varios-cte");
        NoRawKeys(batchWindow);
        batchWindow.Close();

        var edit = new SubscriptionEditViewModel(null, _ => Task.FromResult<string?>(null), vm.ProfileList()) { Url = "https://www.twitch.tv/nasa/videos", Folder = "D:\\Videos" };
        edit.Format.ConstantRate = true;
        var dialog = new Window { Width = 780, Height = 760, Content = new ScrollViewer { Content = new SubscriptionEditView { DataContext = edit } } };
        dialog.Show();
        Save(dialog, $"v03r3-{lang}-{theme}-suscripcion-cte");
        NoRawKeys(dialog);
        dialog.Close();
    }
}
