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
using Videoprep.Core;
using Videoprep.Core.Storage;
using Videoprep.Core.Themes;

namespace Videoprep.App.Tests;

/// <summary>
/// The real window, rendered without a screen. Screenshots of every tab in every theme and both languages are saved to
/// bin/.../screenshots/ (also uploaded by the CI) so a person can look at them.
/// </summary>
public sealed partial class MainWindowTests : IDisposable
{
    private readonly TestRig _rig = new();

    public void Dispose() => _rig.Dispose();

    [GeneratedRegex("^[a-z]{1,8}_[a-zA-Z0-9_]+$")]
    private static partial Regex LooksLikeKey();

    private (MainWindow Window, MainViewModel Vm) Open(string lang, string theme, string custom = "#d9e1ea")
    {
        Loc.Instance.Language = lang;
        SettingsStore.Save(_rig.Paths.SettingsFile, new Settings { Lang = lang });   // the app starts in this language
        var window = new MainWindow { Width = 1000, Height = 740 };
        var vm = new MainViewModel(_rig.Services, window, a => a());
        vm.Settings.CustomColor = custom;
        vm.Settings.Theme = theme;
        ThemeService.Apply(Application.Current!, Palette.For(theme, custom));
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    private static string Save(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name + ".png");
        frame!.Save(path, PngBitmapEncoderOptions.Default);
        return path;
    }

    [AvaloniaFact]
    public void Window_is_resizable_and_titled()
    {
        var (w, _) = Open("es", "light");
        Assert.Equal("videoprep", w.Title);
        Assert.True(w.CanResize);
        Assert.Equal((900, 640), (w.MinWidth, w.MinHeight));
        Assert.Equal(AppInfo.Name, w.Title);
    }

    [AvaloniaTheory]
    [InlineData("es")]
    [InlineData("en")]
    public void Every_tab_in_both_languages_without_untranslated_keys(string lang)
    {
        var (w, vm) = Open(lang, "light");
        vm.SmartPaste("https://vimeo.com/1\nhttps://www.youtube.com/watch?v=jNQXAC9IVRw");
        vm.AddClipCommand.Execute(null);
        string[] names = ["1-descargar", "2-cola", "3-scraping", "4-ajustes", "5-registro"];
        for (var tab = 0; tab < 5; tab++)
        {
            vm.SelectedTab = tab;
            Save(w, $"{lang}-{names[tab]}");
            var texts = w.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible)
                .Select(t => t.Text ?? "").Where(t => t.Length > 0).ToList();
            Assert.NotEmpty(texts);
            var raw = texts.Where(t => LooksLikeKey().IsMatch(t)).ToList();
            Assert.True(raw.Count == 0, $"untranslated keys on tab {tab}: {string.Join(", ", raw)}");
            Assert.DoesNotContain(texts, t => t.Contains("{0}"));
        }
        vm.Scraping.ShowSubscriptionsView();
        vm.SelectedTab = MainViewModel.TabScraping;
        Save(w, $"{lang}-3b-scraping-suscripciones");
    }

    [AvaloniaTheory]
    [InlineData("light")]
    [InlineData("dark")]
    [InlineData("beige")]
    [InlineData("gray")]
    [InlineData("slate")]
    [InlineData("custom")]
    public void Every_theme_paints_the_window(string theme)
    {
        var (w, vm) = Open("es", theme, "#3a2f4a");
        vm.SmartPaste("https://vimeo.com/1");
        vm.AddClipCommand.Execute(null);
        vm.AddClipCommand.Execute(null);
        vm.Clips[1].Start = "mal";
        Save(w, $"tema-{theme}-descargar");
        vm.SelectedTab = MainViewModel.TabQueue;
        Save(w, $"tema-{theme}-cola");
        vm.SelectedTab = MainViewModel.TabScraping;
        Save(w, $"tema-{theme}-scraping");

        var expected = Avalonia.Media.Color.Parse(Palette.For(theme, "#3a2f4a").Bg);
        Assert.Equal(expected, ((Avalonia.Media.ISolidColorBrush)Application.Current!.Resources["Bg"]!).Color);
    }

    [AvaloniaFact]
    public void Switching_language_refreshes_every_text()
    {
        var (w, vm) = Open("es", "light");
        vm.SelectedLanguageIndex = 0;
        vm.SmartPaste("https://vimeo.com/1\nhttps://vimeo.com/2");
        vm.SelectedTab = MainViewModel.TabQueue;
        Dispatcher.UIThread.RunJobs();
        var tabs = w.GetVisualDescendants().OfType<TabItem>().ToList();
        Assert.Equal("Descargar", tabs[0].Header);
        Assert.Equal("Video completo", vm.QueueRows[0].Clip);

        vm.SelectedLanguageIndex = 1;   // English, from the Settings tab
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Download", tabs[0].Header);
        Assert.Equal("Whole video", vm.QueueRows[0].Clip);
        Assert.Equal("Queued", vm.QueueRows[0].Status);
        var shown = w.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Whole video", shown);
        Assert.DoesNotContain("Video completo", shown);
    }

    [AvaloniaFact]
    public void Every_table_lets_the_user_widen_its_columns()
    {
        var (w, vm) = Open("es", "light");
        var grids = new List<DataGrid>();
        foreach (var tab in new[] { MainViewModel.TabQueue, MainViewModel.TabScraping })
        {
            vm.SelectedTab = tab;
            Dispatcher.UIThread.RunJobs();
            grids.AddRange(w.GetVisualDescendants().OfType<DataGrid>());
        }
        vm.Scraping.ShowSubscriptionsView();
        Dispatcher.UIThread.RunJobs();
        grids.AddRange(w.GetVisualDescendants().OfType<DataGrid>());
        Assert.True(grids.Distinct().Count() >= 3);
        Assert.All(grids, g => Assert.True(g.CanUserResizeColumns));
    }

    [AvaloniaFact]
    public void Narrow_window_still_fits_at_the_minimum_size()
    {
        var (w, vm) = Open("es", "light");
        w.Width = 900;
        w.Height = 640;
        vm.SmartPaste("https://vimeo.com/1");
        Save(w, "minimo-900x640-descargar");
        vm.SelectedTab = MainViewModel.TabScraping;
        Save(w, "minimo-900x640-scraping");
    }
}
