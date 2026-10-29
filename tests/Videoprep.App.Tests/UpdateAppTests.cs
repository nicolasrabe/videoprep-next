using Avalonia.Headless.XUnit;
using Videoprep.App.Services;
using Videoprep.App.ViewModels;
using Videoprep.Core.Themes;
using Videoprep.Core.Storage;
using Videoprep.Core.Tests;
using Videoprep.Core.Update;

namespace Videoprep.App.Tests;

/// <summary>F9 in the screens' logic: the check when it opens, the button in Ajustes, the banner and the click on "Actualizar".</summary>
public sealed class UpdateAppTests : IDisposable
{
    private TestRig? _rig;

    public UpdateAppTests() => Loc.Instance.Language = "es";

    public void Dispose() => _rig?.Dispose();

    private MainViewModel Open(bool publish = true, bool updates = true, string installed = "0.2.0", bool selfUpdate = true, Action<TestRig>? before = null)
    {
        _rig = new TestRig(updates: updates, installedVersion: installed, selfUpdate: selfUpdate);
        if (publish) CopyHub(UpdateKit.Publish().Hub);
        before?.Invoke(_rig);
        return _rig.Create();
    }

    private void CopyHub(FakeGitHub from)
    {
        foreach (var (k, v) in from.Routes) _rig!.Hub.Routes[k] = v;
    }

    private int ApiCalls => _rig!.Hub.Asked.Count(a => a.Contains("/releases?", StringComparison.Ordinal));

    [AvaloniaFact]
    public async Task On_opening_a_newer_version_shows_the_banner_and_the_check_is_remembered()
    {
        var vm = Open();
        Assert.False(vm.UpdateBannerVisible);
        await vm.OnShownAsync();
        Assert.True(vm.UpdateBannerVisible);
        Assert.Equal("Hay una versión nueva: 0.3.0", vm.UpdateBannerText);
        Assert.Equal(1, ApiCalls);
        Assert.NotEqual("", vm.Settings.UpdateLast);
        Assert.NotEqual("", SettingsStore.Load(_rig!.Paths.SettingsFile, out _).UpdateLast);

        await _rig.Create().OnShownAsync();                       // opened again within a day: no new look
        Assert.Equal(1, ApiCalls);
    }

    [AvaloniaFact]
    public async Task With_the_option_off_nothing_is_asked_when_opening()
    {
        var vm = Open(before: r => SettingsStore.Save(r.Paths.SettingsFile, new Settings { Lang = "es", UpdateCheck = false }));
        Assert.False(vm.UpdateCheckOn);
        await vm.OnShownAsync();
        Assert.Equal(0, ApiCalls);
        Assert.False(vm.UpdateBannerVisible);
        Assert.Empty(_rig!.Hub.Asked);
    }

    [AvaloniaFact]
    public async Task The_option_is_on_by_default_and_the_choice_is_saved()
    {
        var vm = Open();
        Assert.True(vm.UpdateCheckOn);
        vm.UpdateCheckOn = false;
        Assert.False(SettingsStore.Load(_rig!.Paths.SettingsFile, out _).UpdateCheck);
        await Task.CompletedTask;
    }

    [AvaloniaFact]
    public async Task The_button_checks_now_even_if_it_checked_today_and_says_when_there_is_nothing_new()
    {
        var vm = Open(installed: "0.3.0");
        await vm.CheckUpdatesNowCommand.ExecuteAsync(null);
        Assert.Equal("Ya tenés la última versión (0.3.0).", vm.UpdateStatus);
        Assert.False(vm.UpdateBannerVisible);
        await vm.CheckUpdatesNowCommand.ExecuteAsync(null);
        Assert.Equal(2, ApiCalls);
        Assert.Empty(_rig!.Ui.Messages);                        // quiet: no dialog for "nothing new"
    }

    [AvaloniaFact]
    public async Task Without_internet_the_automatic_check_is_silent_and_the_button_explains_without_a_dialog()
    {
        var vm = Open(publish: false);
        await vm.OnShownAsync();
        Assert.Empty(_rig!.Ui.Messages);
        Assert.Equal("", vm.Settings.UpdateLast);               // a failed check does not count: it tries again next time
        await vm.CheckUpdatesNowCommand.ExecuteAsync(null);
        Assert.StartsWith("No se pudo buscar actualizaciones", vm.UpdateStatus);
        Assert.Empty(_rig.Ui.Messages);
    }

    [AvaloniaFact]
    public async Task Release_notes_open_the_releases_page_and_Ahora_no_hides_the_banner()
    {
        var vm = Open();
        await vm.CheckUpdatesNowCommand.ExecuteAsync(null);
        vm.OpenReleaseNotesCommand.Execute(null);
        Assert.Equal(["https://github.com/nicolasrabe/videoprep-next/releases/tag/v0.3.0"], _rig!.Ui.OpenedUrls);
        vm.DismissUpdateCommand.Execute(null);
        Assert.False(vm.UpdateBannerVisible);
        await vm.CheckUpdatesNowCommand.ExecuteAsync(null);     // a new check brings it back
        Assert.True(vm.UpdateBannerVisible);
    }

    [AvaloniaFact]
    public async Task Actualizar_downloads_checks_hands_over_to_the_new_program_and_closes_this_one()
    {
        var vm = Open();
        await vm.CheckUpdatesNowCommand.ExecuteAsync(null);
        _rig!.Ui.ChoiceAnswers.Enqueue(true);
        await vm.InstallUpdateCommand.ExecuteAsync(null);

        Assert.Contains("0.3.0", _rig.Ui.Choices[0]);
        Assert.Contains("videoprep.exe.old", _rig.Ui.Choices[0]);              // the way back is explained before the click
        var started = Assert.Single(_rig.Started);
        Assert.Equal(1, _rig.Ui.AppClosed);
        Assert.True(File.Exists(started.Exe));
        Assert.Equal(_rig.Paths.UpdatesDir, Path.GetDirectoryName(Path.GetDirectoryName(started.Exe)));
        var args = started.Args;
        Assert.Equal("--apply-update", args[0]);
        Assert.True(ApplyRequest.TryParse(args, started.Exe, out var req));
        Assert.Equal((_rig.InstalledExe, "0.2.0", "0.3.0", Environment.ProcessId), (req.Target, req.FromVersion, req.ToVersion, req.WaitPid));
        Assert.Equal(Sha256Sums.HashFile(started.Exe), req.ExpectedSha256);
        Assert.Equal(UpdateKit.FakeExe("installed"), File.ReadAllBytes(_rig.InstalledExe));   // this program is not touched: the new one does the swap
    }

    [AvaloniaFact]
    public async Task Nothing_happens_without_the_click_on_the_question()
    {
        var vm = Open();
        await vm.CheckUpdatesNowCommand.ExecuteAsync(null);
        _rig!.Ui.ChoiceAnswers.Enqueue(false);                  // "Cancelar"
        await vm.InstallUpdateCommand.ExecuteAsync(null);
        Assert.Empty(_rig.Started);
        Assert.Equal(0, _rig.Ui.AppClosed);
        Assert.DoesNotContain(_rig.Hub.Asked, a => a.Contains("/download/", StringComparison.Ordinal));   // not even downloaded
    }

    [AvaloniaFact]
    public async Task While_something_downloads_the_update_does_not_act()
    {
        var vm = Open();
        await vm.CheckUpdatesNowCommand.ExecuteAsync(null);
        vm.IsRunning = true;
        await vm.InstallUpdateCommand.ExecuteAsync(null);
        Assert.Equal(["Hay una descarga en curso. Esperá a que termine para actualizar."], _rig!.Ui.Messages);
        Assert.Empty(_rig.Started);
        Assert.Empty(_rig.Ui.Choices);
        Assert.Equal(0, _rig.Ui.AppClosed);
    }

    [AvaloniaFact]
    public async Task A_download_that_does_not_match_its_sha256_is_not_installed_and_the_page_is_offered()
    {
        var vm = Open(publish: false, before: r =>
        {
            var (hub, _, _) = UpdateKit.Publish(sumsOverride: new string('0', 64) + "  videoprep-v0.3.0-win-x64.zip\n");
            foreach (var (k, v) in hub.Routes) r.Hub.Routes[k] = v;
        });
        await vm.CheckUpdatesNowCommand.ExecuteAsync(null);
        _rig!.Ui.ChoiceAnswers.Enqueue(true);                   // confirm the update
        _rig.Ui.ChoiceAnswers.Enqueue(true);                    // and then "Abrir la página"
        await vm.InstallUpdateCommand.ExecuteAsync(null);
        Assert.Empty(_rig.Started);
        Assert.Equal(0, _rig.Ui.AppClosed);
        Assert.Contains("no coincide con su comprobación (SHA-256)", _rig.Ui.Choices[1]);
        Assert.Equal(["https://github.com/nicolasrabe/videoprep-next/releases/tag/v0.3.0"], _rig.Ui.OpenedUrls);
        Assert.True(vm.IsIdle);                                 // the buttons come back
        Assert.False(Directory.Exists(Path.Combine(_rig.Paths.UpdatesDir, "0.3.0")));
    }

    [AvaloniaFact]
    public async Task A_release_without_the_checksum_file_is_never_installed_by_itself()
    {
        var vm = Open(publish: false, before: r =>
        {
            r.Hub.Text(FakeGitHub.Api, UpdateKit.ReleaseJson("v0.3.0", prerelease: true, withSums: false));
        });
        await vm.CheckUpdatesNowCommand.ExecuteAsync(null);
        _rig!.Ui.ChoiceAnswers.Enqueue(false);
        await vm.InstallUpdateCommand.ExecuteAsync(null);
        Assert.Empty(_rig.Started);
        Assert.Single(_rig.Ui.Choices);                         // straight to "open the page", no download question
        Assert.DoesNotContain(_rig.Hub.Asked, a => a.Contains("/download/", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task Where_videoprep_cannot_replace_itself_it_says_so_and_offers_the_page()
    {
        var vm = Open(selfUpdate: false);                       // macOS / Linux
        await vm.CheckUpdatesNowCommand.ExecuteAsync(null);
        _rig!.Ui.ChoiceAnswers.Enqueue(true);
        await vm.InstallUpdateCommand.ExecuteAsync(null);
        Assert.Contains("no puede instalar la actualización solo", _rig.Ui.Choices[0]);
        Assert.Single(_rig.Ui.OpenedUrls);
        Assert.Empty(_rig.Started);
    }

    [AvaloniaFact]
    public async Task A_folder_without_write_permission_is_found_out_before_downloading()
    {
        var vm = Open();
        await vm.CheckUpdatesNowCommand.ExecuteAsync(null);
        Directory.Delete(Path.GetDirectoryName(_rig!.InstalledExe)!, true);   // the install folder is gone: the same "cannot write" path as a denied one
        _rig.Ui.ChoiceAnswers.Enqueue(false);
        await vm.InstallUpdateCommand.ExecuteAsync(null);
        Assert.Contains("no tiene permiso para reemplazarse", _rig.Ui.Choices[0]);
        Assert.Contains("administrador", _rig.Ui.Choices[0]);
        Assert.DoesNotContain(_rig.Hub.Asked, a => a.Contains("/download/", StringComparison.Ordinal));
        Assert.Empty(_rig.Started);
    }

    [AvaloniaFact]
    public async Task If_the_installer_cannot_start_nothing_is_closed()
    {
        var vm = Open();
        await vm.CheckUpdatesNowCommand.ExecuteAsync(null);
        _rig!.StartWorks = false;
        _rig.Ui.ChoiceAnswers.Enqueue(true);
        _rig.Ui.ChoiceAnswers.Enqueue(false);
        await vm.InstallUpdateCommand.ExecuteAsync(null);
        Assert.Equal(0, _rig.Ui.AppClosed);
        Assert.Contains("No se pudo iniciar el instalador", _rig.Ui.Choices[1]);
        Assert.True(vm.IsIdle);
    }

    [AvaloniaFact]
    public async Task The_next_start_tells_what_the_last_update_did_once()
    {
        var vm = Open(updates: true, publish: false, before: r =>
            UpdateResult.Write(r.Paths.UpdatesDir, new UpdateResult(true, "0.2.9", "0.3.0", null, "")));
        await vm.OnShownAsync();
        var done = Assert.Single(_rig!.Ui.Messages);
        Assert.Contains("se actualizó a la versión 0.3.0", done);
        Assert.Contains("videoprep.exe.old", done);
        await _rig.Create().OnShownAsync();
        Assert.Single(_rig.Ui.Messages);                        // told once

        UpdateResult.Write(_rig.Paths.UpdatesDir, new UpdateResult(false, "0.2.9", "0.3.0", UpdateFailure.FilesLocked, "busy"));
        await _rig.Create().OnShownAsync();
        Assert.Contains("ocupado", _rig.Ui.Messages[1]);
        Assert.Contains("sigue funcionando", _rig.Ui.Messages[1]);
    }

    [AvaloniaFact]
    public async Task The_updates_folder_is_tidied_when_opening()
    {
        var vm = Open(publish: false, before: r => Directory.CreateDirectory(Path.Combine(r.Paths.UpdatesDir, "0.2.5")));
        await vm.OnShownAsync();
        Assert.False(Directory.Exists(Path.Combine(_rig!.Paths.UpdatesDir, "0.2.5")));
    }
}

/// <summary>The banner and the Ajustes card on screen, in every theme and both languages (PNG files starting with "v03r3-").</summary>
public sealed partial class UpdateScreensTests : IDisposable
{
    private readonly TestRig _rig = new(updates: true);

    public void Dispose() => _rig.Dispose();

    [System.Text.RegularExpressions.GeneratedRegex("^[a-z]{1,8}_[a-zA-Z0-9_]+$")]
    private static partial System.Text.RegularExpressions.Regex LooksLikeKey();

    public static TheoryData<string, string> Combos() => QueueScreensTests.Combos();

    [AvaloniaTheory]
    [MemberData(nameof(Combos))]
    public async Task The_banner_and_the_settings_card_in_every_theme_and_language(string lang, string theme)
    {
        Loc.Instance.Language = lang;
        SettingsStore.Save(_rig.Paths.SettingsFile, new Settings { Lang = lang });
        foreach (var (k, v) in UpdateKit.Publish().Hub.Routes) _rig.Hub.Routes[k] = v;
        var window = new Views.MainWindow { Width = 1000, Height = 740 };
        var vm = new MainViewModel(_rig.Services, window, a => Avalonia.Threading.Dispatcher.UIThread.Post(a));
        vm.Settings.CustomColor = "#3a2f4a";
        vm.Settings.Theme = theme;
        vm.Folder = _rig.OutFolder;
        ThemeService.Apply(Avalonia.Application.Current!, Palette.For(theme, "#3a2f4a"));
        window.DataContext = vm;
        window.Show();
        await vm.CheckUpdatesNowCommand.ExecuteAsync(null);
        Assert.True(vm.UpdateBannerVisible);

        vm.SelectedTab = MainViewModel.TabDownload;
        Save(window, $"v03r3-{lang}-{theme}-actualizacion-aviso");
        NoRawKeys(window);
        vm.SelectedTab = MainViewModel.TabSettings;
        Save(window, $"v03r3-{lang}-{theme}-actualizacion-ajustes");
        NoRawKeys(window);
        window.Close();
    }

    private static void Save(Avalonia.Controls.TopLevel window, string name)
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var frame = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window);
        Assert.NotNull(frame);
        var dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(dir);
        frame!.Save(Path.Combine(dir, name + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    private static void NoRawKeys(Avalonia.Visual root)
    {
        var texts = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root).OfType<Avalonia.Controls.TextBlock>()
            .Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "").ToList();
        Assert.DoesNotContain(texts, t => LooksLikeKey().IsMatch(t));
        Assert.DoesNotContain(texts, t => t.Contains("{0}", StringComparison.Ordinal));
    }
}
