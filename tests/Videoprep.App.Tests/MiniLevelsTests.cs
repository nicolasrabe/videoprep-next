using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Videoprep.App.Services;
using Videoprep.App.ViewModels;
using Videoprep.App.Views;
using Videoprep.Core.Models;
using Videoprep.Core.Storage;
using Videoprep.Core.Themes;

namespace Videoprep.App.Tests;

/// <summary>Monitors and windows that only write down what was asked of them.</summary>
internal sealed class FakeDesk : IScreenSource, IMiniSurfaces
{
    public IReadOnlyList<ScreenArea> All { get; set; } = [];
    public PixelBox FullBounds { get; set; }
    public bool FullMaximized { get; set; }
    public PixelBox? MiniBounds { get; private set; }
    public List<string> Log { get; } = [];
    public (MiniLevel Level, int X, int Y)? Mini { get; private set; }
    public PixelBox? Restored { get; private set; }
    public bool RestoredMaximized { get; private set; }

    /// <summary>The mini window as the system would size it: device independent size times the scale of the monitor it is on.</summary>
    public void ShowMini(MiniLevel level, int x, int y)
    {
        var (w, h) = MiniPlacer.SizeOf(level);
        var screen = MiniPlacement.ScreenOf(All, new PixelBox(x, y, 1, 1))!.Value;
        MiniBounds = new PixelBox(x, y, (int)Math.Round(w * screen.Scale), (int)Math.Round(h * screen.Scale));
        Mini = (level, x, y);
        Log.Add($"mini {level} at {x},{y}");
    }

    public void CloseMini() { MiniBounds = null; Mini = null; Log.Add("mini closed"); }
    public void HideFull() => Log.Add("full hidden");
    public void RestoreFull(PixelBox bounds, bool maximized) { Restored = bounds; RestoredMaximized = maximized; Log.Add("full restored"); }

    /// <summary>The person drags the mini window somewhere else.</summary>
    public void Drag(int x, int y)
    {
        if (Mini is not { } m) return;
        // Like the system: on another monitor the same window is as many pixels as that monitor's scale says.
        var (w, h) = MiniPlacer.SizeOf(m.Level);
        var screen = MiniPlacement.ScreenOf(All, new PixelBox(x, y, 1, 1))!.Value;
        MiniBounds = new PixelBox(x, y, (int)Math.Round(w * screen.Scale), (int)Math.Round(h * screen.Scale));
    }
}

/// <summary>RONDA 2 (spec 2.3): the three levels, where each window appears, and how the full window comes back. No window, no Avalonia.</summary>
public class MiniPlacerTests
{
    private static readonly ScreenArea Left = new(new(0, 0, 1920, 1080), new(0, 0, 1920, 1040), 1.0);
    private static readonly ScreenArea Right = new(new(1920, 0, 2560, 1440), new(1920, 0, 2560, 1400), 1.5);

    private static (MiniPlacer Placer, FakeDesk Desk) Make(PixelBox full, bool maximized = false, params ScreenArea[] screens)
    {
        var desk = new FakeDesk { All = screens.Length > 0 ? screens : [Left, Right], FullBounds = full, FullMaximized = maximized };
        return (new MiniPlacer(desk, desk), desk);
    }

    [Fact]
    public void Maxi_to_card_goes_to_the_top_right_of_the_full_windows_monitor_and_the_full_window_steps_aside()
    {
        var (placer, desk) = Make(new PixelBox(2100, 80, 1500, 1000));   // on the second monitor (150 %)
        placer.Show(MiniLevel.Mini);
        Assert.Equal((MiniLevel.Mini, 1920 + 2560 - 510, 0), desk.Mini);
        Assert.Equal(["mini Mini at 3970,0", "full hidden"], desk.Log);
        Assert.Equal(MiniLevel.Mini, placer.Level);
    }

    [Fact]
    public void Card_bar_card_keeps_the_top_right_corner_and_the_full_window_comes_back_at_the_top_right_where_the_card_was()
    {
        var full = new PixelBox(150, 90, 1000, 700);
        var (placer, desk) = Make(full);
        placer.Show(MiniLevel.Mini);
        Assert.Equal((MiniLevel.Mini, 1920 - 340, 0), desk.Mini);

        placer.Show(MiniLevel.Micro);
        Assert.Equal((MiniLevel.Micro, 1920 - 380, 0), desk.Mini);   // same right edge (1920), same top
        placer.Show(MiniLevel.Mini);
        Assert.Equal((MiniLevel.Mini, 1920 - 340, 0), desk.Mini);

        placer.Close();
        Assert.Equal(new PixelBox(1920 - 1000, 0, 1000, 700), desk.Restored);   // same size, top right corner of the card (right edge 1920)
        Assert.False(desk.RestoredMaximized);
        Assert.Null(desk.Mini);
        Assert.Equal(MiniLevel.Maxi, placer.Level);
    }

    [Fact]
    public void Card_moved_to_the_other_monitor_then_back_to_maxi_opens_there_with_its_old_size_and_the_cards_corner()
    {
        var (placer, desk) = Make(new PixelBox(150, 90, 1000, 700));     // first monitor, 100 %
        placer.Show(MiniLevel.Mini);
        desk.Drag(2500, 300);                                            // second monitor, 150 %: the card is 510 px wide, right edge 3010
        placer.Close();
        // 1000x700 at 100 % = 1500x1050 at 150 %; the card's right edge (3010) would put the left edge at 1510, left of the monitor: it is pushed to the work area's left edge (1920)
        Assert.Equal(new PixelBox(1920, 300, 1500, 1050), desk.Restored);
    }

    [Fact]
    public void Back_to_maxi_never_leaves_the_work_area_and_is_never_bigger_than_it()
    {
        var (placer, desk) = Make(new PixelBox(150, 90, 1900, 1030));
        placer.Show(MiniLevel.Micro);
        desk.Drag(3500, 1390);                                           // bottom right of the 150 % monitor
        placer.Close();
        var r = desk.Restored!.Value;
        Assert.True(r.X >= 1920 && r.Right <= 1920 + 2560 && r.Y >= 0 && r.Bottom <= 1400, r.ToString());
    }

    [Fact]
    public void A_card_dragged_to_the_other_monitor_keeps_ITS_corner_when_it_becomes_a_bar()
    {
        var (placer, desk) = Make(new PixelBox(150, 90, 1000, 700));
        placer.Show(MiniLevel.Mini);
        desk.Drag(2500, 300);                                // now on the 150 % monitor: 510 px wide
        placer.Show(MiniLevel.Micro);
        Assert.Equal((MiniLevel.Micro, 2500 + 510 - 570, 300), desk.Mini);
    }

    [Fact]
    public void A_maximized_full_window_comes_back_maximized_and_the_mini_uses_its_monitor()
    {
        var (placer, desk) = Make(new PixelBox(1920, 0, 2560, 1400), maximized: true);
        placer.Show(MiniLevel.Micro);
        Assert.Equal((MiniLevel.Micro, 1920 + 2560 - 570, 0), desk.Mini);
        placer.Close();
        Assert.True(desk.RestoredMaximized);
        Assert.Equal(new PixelBox(1920, 0, 2560, 1400), desk.Restored);
    }

    [Fact]
    public void The_place_is_not_remembered_between_uses_each_time_it_follows_the_full_window()
    {
        var (placer, desk) = Make(new PixelBox(150, 90, 1000, 700));
        placer.Show(MiniLevel.Mini);
        desk.Drag(400, 400);
        placer.Close();
        desk.FullBounds = new PixelBox(2200, 100, 1400, 900);   // the full window moved to the other monitor meanwhile
        placer.Show(MiniLevel.Mini);
        Assert.Equal((MiniLevel.Mini, 3970, 0), desk.Mini);
    }

    [Fact]
    public void The_monitor_of_the_full_window_is_gone_and_a_taskbar_on_top_is_respected()
    {
        var onlyLeftWithTaskbarOnTop = new ScreenArea(new(0, 0, 1920, 1080), new(0, 48, 1920, 1032), 1.0);
        var (placer, desk) = Make(new PixelBox(4000, 100, 1000, 700), false, onlyLeftWithTaskbarOnTop);
        placer.Show(MiniLevel.Mini);
        Assert.Equal((MiniLevel.Mini, 1920 - 340, 48), desk.Mini);
    }

    [Fact]
    public void Showing_maxi_closes_the_small_window_and_closing_twice_does_nothing()
    {
        var (placer, desk) = Make(new PixelBox(150, 90, 1000, 700));
        placer.Show(MiniLevel.Mini);
        placer.Show(MiniLevel.Maxi);
        Assert.Equal(MiniLevel.Maxi, placer.Level);
        var count = desk.Log.Count;
        placer.Close();
        Assert.Equal(count, desk.Log.Count);
    }
}

/// <summary>The view model side of the levels, and the Prep tab (fragments with names, paste, video / audio switch).</summary>
public sealed class MiniLevelsAndPrepTests : IDisposable
{
    private readonly TestRig _rig = new(tools: true);

    public MiniLevelsAndPrepTests() => Loc.Instance.Language = "es";

    public void Dispose() => _rig.Dispose();

    private const string Zoo = "https://www.youtube.com/watch?v=jNQXAC9IVRw";

    private MainViewModel Vm()
    {
        var vm = _rig.Create();
        vm.Folder = _rig.OutFolder;
        return vm;
    }

    [AvaloniaFact]
    public void Levels_go_maxi_card_bar_and_back_with_the_buttons_and_the_double_click_path()
    {
        var vm = Vm();
        Assert.Equal(MiniLevel.Maxi, vm.MiniLevel);
        vm.ToggleMiniCommand.Execute(null);                    // "Modo mini"
        Assert.Equal((MiniLevel.Mini, true), (vm.MiniLevel, vm.IsMiniCard));
        vm.DemoteMiniCommand.Execute(null);                    // the "▁" button
        Assert.Equal((MiniLevel.Micro, true), (vm.MiniLevel, vm.IsMicroBar));
        vm.DemoteMiniCommand.Execute(null);                    // nothing under the bar
        Assert.Equal(MiniLevel.Micro, vm.MiniLevel);
        vm.PromoteMiniCommand.Execute(null);                   // "⤢" or double click on the bar: back to the card
        Assert.Equal(MiniLevel.Mini, vm.MiniLevel);
        vm.PromoteMiniCommand.Execute(null);                   // "⤢" or double click on the card: the full window
        Assert.Equal((MiniLevel.Maxi, false), (vm.MiniLevel, vm.IsMiniActive));
        Assert.Equal([MiniLevel.Mini, MiniLevel.Micro, MiniLevel.Mini], _rig.Ui.MiniLevels);
        Assert.False(_rig.Ui.MiniShown);
        vm.PromoteMiniCommand.Execute(null);                   // nothing happens in the full window
        Assert.Equal(MiniLevel.Maxi, vm.MiniLevel);
    }

    [AvaloniaFact]
    public void The_bar_says_Copia_un_enlace_when_idle_and_the_card_has_a_fragment_box()
    {
        var vm = Vm();
        vm.ToggleMiniCommand.Execute(null);
        Assert.Equal("Copiá un enlace para descargar", vm.MiniText);

        Assert.False(vm.MiniHasFragment);
        vm.MiniHasFragment = true;                             // the "Fragmento" box opens start and end
        Assert.Single(vm.Clips);
        Assert.Same(vm.Clips[0], vm.MiniClip);
        vm.MiniClip!.Start = "00:00:05:00";
        vm.MiniHasFragment = false;
        Assert.Empty(vm.Clips);
        Assert.Null(vm.MiniClip);
    }

    [AvaloniaFact]
    public async Task Pegar_takes_links_from_the_clipboard_and_plain_text_goes_into_the_box()
    {
        var vm = Vm();
        _rig.Ui.UserCopies(Zoo);
        await vm.PasteLinkCommand.ExecuteAsync(null);
        Assert.True(vm.HasDraft);                              // a link: it is taken like a paste
        vm.LinkText = "";
        _rig.Ui.UserCopies("hola");
        await vm.PasteLinkCommand.ExecuteAsync(null);
        Assert.Equal("hola", vm.LinkText);
    }

    [AvaloniaFact]
    public void The_video_audio_switch_and_the_single_name_box()
    {
        var vm = Vm();
        Assert.True(vm.IsVideo);
        vm.SetKindCommand.Execute("audio");
        Assert.True(vm.IsAudio);
        vm.SetKindCommand.Execute("video");
        Assert.True(vm.IsVideo);

        Assert.Equal(0, vm.NameModeIndex);
        vm.NameModeIndex = 2;
        Assert.True(vm.NameSeries);
        Assert.False(vm.NameOriginal);
        vm.NameModeIndex = 1;
        Assert.True(vm.NameCustom);
        Assert.Equal(1, vm.NameModeIndex);
        vm.NameModeIndex = 0;
        Assert.True(vm.NameOriginal);
    }

    [AvaloniaFact]
    public void Fragments_switch_the_name_section_off_and_removing_them_all_switches_it_on()
    {
        var vm = Vm();
        vm.NameModeIndex = 1;
        vm.CustomText = "Mi nombre";
        Assert.True(vm.CustomFieldEnabled);
        vm.AddClipCommand.Execute(null);
        Assert.False(vm.NamesEnabled);
        Assert.False(vm.CustomFieldEnabled);
        Assert.True(vm.HasClips);
        Assert.Equal("Con fragmentos, cada uno lleva su propio nombre", vm.ExampleText);
        vm.Clips.Clear();
        Assert.True(vm.NamesEnabled);
        Assert.True(vm.CustomFieldEnabled);
        Assert.Equal("Mi nombre", vm.CustomText);              // what was typed is still there
    }

    [AvaloniaFact]
    public void Sending_with_fragments_keeps_each_fragment_name_and_the_word_for_the_others()
    {
        var vm = Vm();
        vm.SmartPaste(Zoo);
        vm.AddClipCommand.Execute(null);
        vm.AddClipCommand.Execute(null);
        vm.Clips[0].Start = "00:00:01:00";
        vm.Clips[0].End = "00:00:02:00";
        vm.Clips[0].Name = "  Entrada ";
        vm.Clips[1].Start = "00:00:03:00";                     // no name
        vm.SendToQueueCommand.Execute(null);

        var item = Assert.Single(vm.Items);
        Assert.Equal(NamingMode.Original, item.Naming);
        Assert.Equal("parte", item.PartWord);
        Assert.Equal([new ClipRange("00:00:01:00", "00:00:02:00", "Entrada"), new ClipRange("00:00:03:00", "")], item.Clips);
        Assert.Equal(["Entrada", "Me at the zoo - parte 2"], vm.QueueRows.Select(r => r.PlannedName));   // "Título original - parte N"

        // Editing it brings the names back to the boxes.
        vm.EditSelectedCommand.Execute(null);
        vm.QueueRows[0].IsExpanded = false;
    }

    [AvaloniaFact]
    public void In_English_the_word_is_part()
    {
        try
        {
            var vm = Vm();
            vm.SelectedLanguageIndex = 1;   // English, like the Settings tab does
            vm.SmartPaste(Zoo);
            vm.AddClipCommand.Execute(null);
            vm.Clips[0].Start = "00:00:01:00";
            vm.SendToQueueCommand.Execute(null);
            Assert.Equal("part", Assert.Single(vm.Items).PartWord);
            Assert.Equal("With fragments, each one gets its own name", Loc.Instance["names_frag_note"]);
        }
        finally
        {
            Loc.Instance.Language = "es";
        }
    }
}

/// <summary>The new screens in the real controls: tabs with icons, the compact Prep tab, the card and the bar.</summary>
public sealed class PrepAndMiniScreensTests : IDisposable
{
    private readonly TestRig _rig = new(tools: true);

    public PrepAndMiniScreensTests() => Loc.Instance.Language = "es";

    public void Dispose() => _rig.Dispose();

    private (MainWindow Window, MainViewModel Vm) Open(string lang = "es", string theme = "dark")
    {
        Loc.Instance.Language = lang;
        SettingsStore.Save(_rig.Paths.SettingsFile, new Settings { Lang = lang });
        var window = new MainWindow { Width = 1000, Height = 700 };
        var vm = new MainViewModel(_rig.Services, window, a => Dispatcher.UIThread.Post(a));
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

    private static T? Find<T>(Visual root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == name);

    private static IEnumerable<string> VisibleTexts(Visual root) =>
        root.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "").Where(t => t.Length > 0);

    [AvaloniaTheory]
    [InlineData("es", "Prep", "Cola", "Scraping", "Ajustes", "Registro", "Modo mini", "Preparar y descargar un video")]
    [InlineData("en", "Prep", "Queue", "Scraping", "Settings", "Log", "Mini mode", "Prepare and download a video")]
    public void Tabs_have_an_icon_before_the_name_the_Prep_tooltip_and_the_mini_button_at_the_right(string lang, params string[] expected)
    {
        var (w, _) = Open(lang);
        var tabs = w.GetVisualDescendants().OfType<TabItem>().ToList();
        Assert.Equal(5, tabs.Count);
        for (var i = 0; i < 5; i++)
        {
            var header = Assert.IsAssignableFrom<Panel>(tabs[i].Header);
            Assert.IsType<Avalonia.Controls.Shapes.Path>(header.Children[0]);          // the icon comes first
            Assert.Equal(expected[i], Assert.IsType<TextBlock>(header.Children[1]).Text);
        }
        Assert.Equal(expected[6], ToolTip.GetTip(tabs[0]));
        var mini = Find<Button>(w, "MiniButton")!;
        Assert.True(mini.IsEffectivelyVisible);
        Assert.Equal(HorizontalAlignment.Right, mini.HorizontalAlignment);
        Assert.Contains(expected[5], VisibleTexts(mini));
        Assert.DoesNotContain(w.GetVisualDescendants().OfType<DownloadView>().SelectMany(d => d.GetVisualDescendants().OfType<Button>()), b => b.Name == "MiniButton");   // no longer inside Prep
        w.Close();
    }

    [AvaloniaFact]
    public void Prep_is_compact_with_a_big_preview_and_names_that_switch_off_with_fragments()
    {
        var (w, vm) = Open();
        Assert.True(Find<Button>(w, "PasteButton")!.IsEffectivelyVisible);
        Assert.True(Find<Border>(w, "InfoIcon")!.IsEffectivelyVisible);
        Assert.StartsWith("Compatible con Premiere", (string)ToolTip.GetTip(Find<Border>(w, "InfoIcon")!)!, StringComparison.Ordinal);   // what the format does, in the "i"
        Save(w, "v031r2-prep-sin-fragmentos");

        vm.AddClipCommand.Execute(null);
        vm.AddClipCommand.Execute(null);
        vm.Clips[0].Start = "00:00:10:00";
        vm.Clips[0].End = "00:00:20:00";
        vm.Clips[0].Name = "Entrada";
        Dispatcher.UIThread.RunJobs();
        var names = Find<Border>(w, "NamesCard")!;
        Assert.False(names.IsEnabled);
        Assert.Contains("Con fragmentos, cada uno lleva su propio nombre", VisibleTexts(w));
        Assert.Equal(2, w.GetVisualDescendants().OfType<TextBox>().Count(t => t.PlaceholderText == Loc.Instance["ph_frag_name"]));
        Save(w, "v031r2-prep-con-fragmentos");

        vm.Clips.Clear();
        Dispatcher.UIThread.RunJobs();
        Assert.True(Find<Border>(w, "NamesCard")!.IsEnabled);
        Assert.DoesNotContain(VisibleTexts(w), t => t.Contains("{0}") || System.Text.RegularExpressions.Regex.IsMatch(t, "^[a-z]{1,8}_[a-zA-Z0-9_]+$"));
        w.Close();
    }

    [AvaloniaFact]
    public void The_card_and_the_bar_have_their_sizes_their_contents_and_the_red_pin()
    {
        var (w, vm) = Open();
        var mini = new MiniWindow { DataContext = vm };
        mini.ApplyLevel(MiniLevel.Mini);
        vm.ToggleMiniCommand.Execute(null);                    // the fake window of the test rig: the real one is made here
        mini.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal((340d, 296d), (mini.Width, mini.Height));
        Assert.True(Find<Grid>(mini, "Card")!.IsEffectivelyVisible);
        Assert.False(Find<Panel>(mini, "Bar")!.IsEffectivelyVisible);
        Assert.True(Find<TextBox>(mini, "CardLink")!.IsEffectivelyVisible);
        Assert.True(Find<Button>(mini, "CardPaste")!.IsEffectivelyVisible);
        Assert.True(Find<ComboBox>(mini, "CardProfile")!.IsEffectivelyVisible);
        Assert.True(Find<CheckBox>(mini, "CardFragment")!.IsEffectivelyVisible);
        Assert.False(Find<Grid>(mini, "CardFragmentRow")!.IsEffectivelyVisible);
        Assert.Equal("A la cola", Find<Button>(mini, "CardQueue")!.Content);
        Assert.Equal("Descargar", Find<Button>(mini, "CardDownload")!.Content);
        Assert.True(Find<ToggleButton>(mini, "PinButton")!.IsChecked);   // "siempre encima" is on by default

        vm.MiniHasFragment = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(Find<Grid>(mini, "CardFragmentRow")!.IsEffectivelyVisible);
        Save(mini, "v031r2-mini-ficha");

        vm.DemoteMiniCommand.Execute(null);
        mini.ApplyLevel(MiniLevel.Micro);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal((380d, 40d), (mini.Width, mini.Height));
        Assert.True(Find<Panel>(mini, "Bar")!.IsEffectivelyVisible);
        Assert.False(Find<Grid>(mini, "Card")!.IsEffectivelyVisible);
        Assert.Equal("Copiá un enlace para descargar", Find<TextBlock>(mini, "BarText")!.Text);
        Assert.False(Find<Button>(mini, "BarStop")!.IsEffectivelyVisible);   // "Detener" only while it downloads
        Save(mini, "v031r2-mini-micro");
        mini.CloseForReal();
        w.Close();
    }

    [AvaloniaFact]
    public void A_double_click_goes_one_level_up_only_on_empty_places_never_on_a_box()
    {
        var (w, vm) = Open();
        vm.ToggleMiniCommand.Execute(null);                    // card
        vm.DemoteMiniCommand.Execute(null);                    // bar
        var mini = new MiniWindow { DataContext = vm };
        mini.ApplyLevel(MiniLevel.Micro);
        mini.Show();
        Dispatcher.UIThread.RunJobs();

        // On the text of the bar (not a control): up to the card.
        RaiseDoubleTap(mini, Find<TextBlock>(mini, "BarText")!);
        Assert.Equal(MiniLevel.Mini, vm.MiniLevel);

        // On the text box of the card: nothing.
        mini.ApplyLevel(MiniLevel.Mini);
        Dispatcher.UIThread.RunJobs();
        RaiseDoubleTap(mini, Find<TextBox>(mini, "CardLink")!);
        Assert.Equal(MiniLevel.Mini, vm.MiniLevel);
        // On the profile list and the fragment box: nothing.
        RaiseDoubleTap(mini, Find<ComboBox>(mini, "CardProfile")!);
        RaiseDoubleTap(mini, Find<CheckBox>(mini, "CardFragment")!);
        Assert.Equal(MiniLevel.Mini, vm.MiniLevel);
        // On the title of the card (empty place): up to the full window.
        RaiseDoubleTap(mini, Find<TextBlock>(mini, "CardTitle")!);
        Assert.Equal(MiniLevel.Maxi, vm.MiniLevel);
        mini.CloseForReal();
        w.Close();
    }

    /// <summary>A real double click (two quick presses) on the middle of a control, through the headless input.</summary>
    private static void RaiseDoubleTap(Window window, Control target)
    {
        var at = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window) ?? new Point(5, 5);
        window.MouseMove(at);
        for (var i = 0; i < 2; i++)
        {
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
        }
        Dispatcher.UIThread.RunJobs();
    }
}
