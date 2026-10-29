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
using Videoprep.Core.Profiles;
using Videoprep.Core.Scraping;
using Videoprep.Core.Storage;
using Videoprep.Core.Themes;

namespace Videoprep.App.Tests;

/// <summary>
/// Round 2 screens in the real controls, rendered without a screen, in every theme and both languages (PNG files in
/// bin/.../screenshots/, names start with "v03r2-"): the mini window in its three states, the new Settings cards, the
/// profile boxes in Download, "Cambiar N elementos" and the subscription dialog, and the profile column of the table.
/// </summary>
public sealed partial class ExtrasScreensTests : IDisposable
{
    private readonly TestRig _rig = new(tools: true);

    public void Dispose() => _rig.Dispose();

    [GeneratedRegex("^[a-z]{1,8}_[a-zA-Z0-9_]+$")]
    private static partial Regex LooksLikeKey();

    public static TheoryData<string, string> Combos() => QueueScreensTests.Combos();

    private (MainWindow Window, MainViewModel Vm) Open(string lang, string theme, int height = 740)
    {
        Loc.Instance.Language = lang;
        SettingsStore.Save(_rig.Paths.SettingsFile, new Settings { Lang = lang });
        var store = ProfileStore.Load(_rig.Paths.ProfilesFile, out _);
        store.Add(new FormatProfile { Name = lang == "es" ? "Entrevistas HD" : "HD interviews", Format = "mp4", Cap = 720, Naming = "series", Prefix = "Entrevista" });
        SubscriptionStore.Save(_rig.Paths.SubscriptionsFile, new SubscriptionFile
        {
            Subscriptions =
            [
                new Subscription { Id = "sub_1", Name = "@BlenderStudio", Url = "https://www.youtube.com/@BlenderStudio", Folder = "D:\\Videos", LastCheck = "2026-10-26T09:14:00-03:00", LastNew = 2,
                                   Profile = new SubscriptionProfile { Format = "movp", Cap = 2160, ProfileId = "builtin:prores-4k" } },
                new Subscription { Id = "sub_2", Name = "twitch.tv/nasa", Url = "https://www.twitch.tv/nasa/videos", Folder = "D:\\Videos", LastNew = 0,
                                   Profile = new SubscriptionProfile { Format = "mp4", Cap = 720 } },
            ],
        });
        var window = new MainWindow { Width = 1000, Height = height };
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
    public void Mini_window_in_its_states(string lang, string theme)
    {
        var (_, vm) = Open(lang, theme);
        var states = new (string Name, Action Set)[]
        {
            ("descargando", () => (vm.MiniKind, vm.MiniText, vm.MiniPercent, vm.MiniPercentText, vm.MiniCounter) =
                (MiniKind.Downloading, "Big Buck Bunny 60fps 4K - Official Blender Foundation Short Film", 62, "62 %", "2/5")),
            ("listo", () => (vm.MiniKind, vm.MiniText, vm.MiniPercent, vm.MiniPercentText, vm.MiniCounter) =
                (MiniKind.Idle, vm.L.Format("mini_done", 5, 5), 0, "", "")),
            ("error", () => (vm.MiniKind, vm.MiniText, vm.MiniPercent, vm.MiniPercentText, vm.MiniCounter) =
                (MiniKind.Problems, vm.L.Format("mini_problems", 4, 1), 0, "", "")),
            ("aviso", () => (vm.MiniKind, vm.MiniText, vm.MiniPercent, vm.MiniPercentText, vm.MiniCounter) =
                (MiniKind.Notice, vm.L["clip_added_one"], 0, "", "")),
        };
        foreach (var (name, set) in states)
        {
            set();
            var mini = new MiniWindow { DataContext = vm };
            mini.Show();
            Assert.Equal((300, 28), (mini.Width, mini.Height));
            Assert.False(mini.ShowInTaskbar);
            Assert.Equal(vm.MiniOnTop, mini.Topmost);
            Save(mini, $"v03r2-{lang}-{theme}-mini-{name}");
            NoRawKeys(mini);
            mini.Close();
        }
    }

    [AvaloniaFact]
    public void Mini_has_its_menu_with_close_mode_always_on_top_and_quit_and_the_always_on_top_changes_live()
    {
        var (_, vm) = Open("es", "light");
        var mini = new MiniWindow { DataContext = vm };
        mini.Show();
        var menu = mini.GetVisualDescendants().OfType<Border>().Select(b => b.ContextMenu).First(m => m is not null)!;
        Assert.Equal(["Cerrar modo mini", "Siempre encima", "Cerrar videoprep"], menu.Items.OfType<MenuItem>().Select(i => i.Header as string));
        Assert.True(mini.Topmost);
        vm.MiniOnTop = false;                                   // the pin button or the menu: it changes while the window is open
        Assert.False(mini.Topmost);
        vm.MiniOnTop = true;
        Assert.True(mini.Topmost);
        mini.Close();
    }

    [AvaloniaTheory]
    [MemberData(nameof(Combos))]
    public void Settings_and_download_with_the_new_options(string lang, string theme)
    {
        var (w, vm) = Open(lang, theme, height: 1250);
        vm.SmartPaste("https://fake.test/ok/p1");
        vm.SelectedTab = MainViewModel.TabDownload;
        Save(w, $"v03r2-{lang}-{theme}-descargar-perfil");
        NoRawKeys(w);
        vm.SelectedTab = MainViewModel.TabSettings;
        vm.ClipboardWatch = true;
        vm.SourceListOn = true;
        vm.SelectedManagedProfile = vm.ManagedProfiles[^1];
        Dispatcher.UIThread.RunJobs();
        Save(w, $"v03r2-{lang}-{theme}-ajustes");
        NoRawKeys(w);
    }

    [AvaloniaTheory]
    [MemberData(nameof(Combos))]
    public void Queue_dialog_and_subscriptions_with_profiles(string lang, string theme)
    {
        var (w, vm) = Open(lang, theme);
        vm.SmartPaste("https://fake.test/ok/a1\nhttps://fake.test/ok/a2");
        var batch = new BatchEditViewModel(vm.Items.ToList(), 0, _ => null, _ => 1, vm.ProfileList());
        batch.SelectedProfile = batch.Profiles.First(p => p.Profile?.Id == "builtin:prores-4k");
        var batchWindow = new Window { Width = 640, Height = 500, Content = new Border { Padding = new Thickness(18), Child = new BatchEditView { DataContext = batch } } };
        batchWindow.Show();
        Save(batchWindow, $"v03r2-{lang}-{theme}-cambiar-varios-perfil");
        NoRawKeys(batchWindow);
        batchWindow.Close();

        vm.Scraping.ShowSubscriptionsView();
        vm.SelectedTab = MainViewModel.TabScraping;
        Dispatcher.UIThread.RunJobs();
        Save(w, $"v03r2-{lang}-{theme}-suscripciones-perfil");
        NoRawKeys(w);
        Assert.Equal(["ProRes 4K", lang == "es" ? "(valores propios)" : "(own values)"], vm.Scraping.Subscriptions.Rows.Select(r => r.ProfileText));

        var edit = new SubscriptionEditViewModel(vm.Scraping.Subscriptions.Rows[0].Sub, _ => Task.FromResult<string?>(null), vm.ProfileList());
        var dialog = new Window { Width = 780, Height = 760, Content = new ScrollViewer { Content = new SubscriptionEditView { DataContext = edit } } };
        dialog.Show();
        Save(dialog, $"v03r2-{lang}-{theme}-dialogo-suscripcion-perfil");
        NoRawKeys(dialog);
        dialog.Close();
    }
}
