using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Videoprep.App.Services;
using Videoprep.App.ViewModels;
using Videoprep.App.Views;
using Videoprep.Core.Engine;
using Videoprep.Core.Models;
using Videoprep.Core.Preview;
using Videoprep.Core.Scraping;
using Videoprep.Core.Storage;

namespace Videoprep.App.Tests;

/// <summary>RONDA 4: the shared components (foldable card, loading text).</summary>
public sealed class SharedComponentsTests
{
    [AvaloniaFact]
    public void A_foldable_card_folds_on_a_click_on_its_header_and_shows_its_summary_only_while_folded()
    {
        var body = new TextBlock { Text = "inside" };
        var card = new CollapsibleSection { Title = "Filters", Summary = "last 7 days", Content = body };
        var window = new Window { Content = card, Width = 400, Height = 200 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.True(card.IsExpanded);
        Assert.False(card.SummaryVisible);

        var header = card.GetVisualDescendants().OfType<Button>().First(b => b.Name == "PART_Header");
        header.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.False(card.IsExpanded);
        Assert.True(card.SummaryVisible);                                // the one-line summary appears when folded
        Assert.False(card.GetVisualDescendants().OfType<ContentPresenter>().First(p => ReferenceEquals(p.Content, body)).IsVisible);

        card.Summary = "";                                              // nothing to say: no empty line
        Assert.False(card.SummaryVisible);
        header.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(card.IsExpanded);
        window.Close();
    }

    [AvaloniaFact]
    public void A_card_that_grows_stretches_while_open_and_a_folded_one_does_not()
    {
        var card = new CollapsibleSection { Title = "Results", Grows = true, Content = new TextBlock { Text = "x" } };
        Assert.Equal(Avalonia.Layout.VerticalAlignment.Stretch, card.VerticalAlignment);
        card.IsExpanded = false;
        Assert.Equal(Avalonia.Layout.VerticalAlignment.Top, card.VerticalAlignment);
        Assert.Equal(Avalonia.Layout.VerticalAlignment.Top, new CollapsibleSection { Title = "Plain" }.VerticalAlignment);
    }

    [AvaloniaFact]
    public void Loading_text_says_what_is_loading_in_both_languages()
    {
        try
        {
            Loc.Instance.Language = "es";
            var text = new LoadingText { What = Loc.Instance["load_card"] };
            Assert.Equal("Cargando la ficha...", text.Sentence);
            Loc.Instance.Language = "en";
            text.What = Loc.Instance["load_card"];
            Assert.Equal("Loading the card...", text.Sentence);
        }
        finally { Loc.Instance.Language = "es"; }
    }
}

/// <summary>RONDA 4: the queue bar, reordering, thumbnails and the subscriptions menu, at the view model level.</summary>
public sealed class Round4ViewModelTests : IDisposable
{
    private readonly TestRig _rig = new(tools: true);
    private const string A = "https://www.youtube.com/watch?v=aaaaaaaaaaa";
    private const string B = "https://www.youtube.com/watch?v=bbbbbbbbbbb";
    private const string C = "https://www.youtube.com/watch?v=ccccccccccc";

    public Round4ViewModelTests() => Loc.Instance.Language = "es";

    public void Dispose() => _rig.Dispose();

    private MainViewModel QueueOf3(out List<int> ids)
    {
        var vm = _rig.Create();
        vm.SmartPaste($"{A}\n{B}\n{C}");
        ids = vm.Items.Select(i => i.Id).ToList();
        Assert.Equal(3, ids.Count);
        return vm;
    }

    private static void SetStatus(MainViewModel vm, string key, JobStatus status) =>
        ((Dictionary<string, JobStatus>)typeof(MainViewModel).GetField("_rowStatus", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(vm)!)[key] = status;

    [AvaloniaFact]
    public void The_counter_counts_downloading_waiting_and_done_with_the_right_plural()
    {
        var vm = QueueOf3(out var ids);
        Assert.Equal("0 descargando · 3 en cola · 0 listas", vm.QueueCounter);
        SetStatus(vm, $"{ids[0]}|0", JobStatus.OK);
        SetStatus(vm, $"{ids[1]}|0", JobStatus.Downloading);
        Assert.Equal("1 descargando · 1 en cola · 1 lista", vm.QueueCounter);
        Loc.Instance.Language = "en";
        try { Assert.Equal("1 downloading · 1 queued · 1 done", vm.QueueCounter); }
        finally { Loc.Instance.Language = "es"; }
    }

    [AvaloniaFact]
    public void Pause_is_only_offered_while_something_runs()
    {
        var vm = QueueOf3(out _);
        Assert.False(vm.CanPause);
        vm.PauseQueueCommand.Execute(null);
        Assert.False(vm.PauseRequested);                       // nothing runs: nothing to pause
    }

    [AvaloniaFact]
    public void Dragging_rows_changes_the_order_and_the_order_is_saved_in_the_queue_file()
    {
        var vm = QueueOf3(out var ids);
        Assert.True(vm.MoveItemsBefore([ids[2]], 0));          // the last row dropped on the first
        Assert.Equal([ids[2], ids[0], ids[1]], vm.Items.Select(i => i.Id));
        Assert.Equal([ids[2], ids[0], ids[1]], vm.QueueRows.Select(r => r.ItemId));
        var saved = QueueStore.Load(_rig.Paths.QueueFile, out var problem);
        Assert.Null(problem);
        Assert.Equal([ids[2], ids[0], ids[1]], saved.Select(i => i.Id));
        Assert.True(vm.CanUndo);                               // one Ctrl+Z puts it back
        vm.Undo();
        Assert.Equal(ids, vm.Items.Select(i => i.Id));
    }

    [AvaloniaFact]
    public void Rows_that_are_downloading_or_done_cannot_be_picked_up_or_covered()
    {
        var vm = QueueOf3(out var ids);
        SetStatus(vm, $"{ids[0]}|0", JobStatus.OK);
        vm.SetQueueSelection([]);                              // the table is rebuilt from the status
        typeof(MainViewModel).GetMethod("RebuildQueueRows", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(vm, null);
        Assert.False(vm.CanDrag(vm.QueueRows[0]));             // done: no grip
        Assert.True(vm.CanDrag(vm.QueueRows[1]));
        Assert.False(vm.MoveItemsBefore([ids[0]], 2));         // cannot be moved
        Assert.True(vm.MoveItemsBefore([ids[2]], 0));          // dropped above the done one: lands right below it
        Assert.Equal([ids[0], ids[2], ids[1]], vm.Items.Select(i => i.Id));
    }

    [AvaloniaFact]
    public void The_queue_bar_uses_the_same_commands_as_Prep()
    {
        var vm = _rig.Create();
        var window = new MainWindow { Width = 1100, Height = 780, DataContext = vm };
        window.Show();
        vm.SelectedTab = MainViewModel.TabQueue;
        Dispatcher.UIThread.RunJobs();
        var all = window.GetVisualDescendants().OfType<Button>().ToList();
        Assert.Same(vm.DownloadCommand, all.First(b => b.Name == "QueueStartButton").Command);
        Assert.Same(vm.StopCommand, all.First(b => b.Name == "QueueStopButton").Command);
        Assert.Same(vm.PauseQueueCommand, all.First(b => b.Name == "QueuePauseButton").Command);
        Assert.Equal("Iniciar cola", all.First(b => b.Name == "QueueStartButton").Content);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Download_thumbnail_opens_the_dialog_at_once_with_the_clean_title_and_saves_the_same_bytes()
    {
        var vm = _rig.Create();
        var jpg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 5, 6, 7 };
        var thumbs = Path.Combine(_rig.Paths.CacheDir, "thumbs");
        Directory.CreateDirectory(thumbs);
        File.WriteAllBytes(Path.Combine(thumbs, "aaaaaaaaaaa.img"), jpg);          // already seen: no network is needed
        _rig.Ui.SavePath = Path.Combine(_rig.Dir, "miniatura.jpg");

        await vm.SaveThumbnailAsync(A, "Mi video: ¿qué tal?");
        Assert.Equal(["Mi video qué tal"], _rig.Ui.ThumbnailNames.Select(n => n.Replace("¿", "").Replace("?", "").Replace(":", "")).ToList());
        Assert.DoesNotContain(':', _rig.Ui.ThumbnailNames[0]);
        Assert.DoesNotContain('?', _rig.Ui.ThumbnailNames[0]);
        Assert.Equal(jpg, await File.ReadAllBytesAsync(_rig.Ui.SavePath));            // same format: the bytes as they are
        Assert.Contains("miniatura.jpg", vm.StatusText);
        Assert.DoesNotContain(_rig.Web.Requests, r => r.Contains("ytimg"));
    }

    [AvaloniaFact]
    public async Task Cancelling_the_save_dialog_saves_nothing_and_a_missing_picture_says_so()
    {
        var vm = _rig.Create();
        _rig.Ui.SavePath = null;
        await vm.SaveThumbnailAsync(A, "t");
        Assert.Empty(_rig.Ui.Messages);

        _rig.Ui.SavePath = Path.Combine(_rig.Dir, "x.jpg");
        await vm.SaveThumbnailAsync(B, "t");                   // no picture anywhere (the fake web answers 404)
        Assert.False(File.Exists(_rig.Ui.SavePath));
        Assert.Contains(_rig.Ui.Messages, m => m.Contains("miniatura"));
    }

    private SubscriptionsViewModel Subs(MainViewModel vm) => vm.Scraping.Subscriptions;

    private async Task<SubscriptionsViewModel> TwoSubs(MainViewModel vm)
    {
        _rig.Ui.FillSubscription = d => { d.Url = "https://www.youtube.com/@uno"; d.Folder = _rig.OutFolder; };
        await Subs(vm).AddCommand.ExecuteAsync(null);
        _rig.Ui.FillSubscription = d => { d.Url = "https://www.youtube.com/@dos"; d.Folder = Path.Combine(_rig.Dir, "no-existe"); };
        await Subs(vm).AddCommand.ExecuteAsync(null);
        return Subs(vm);
    }

    [AvaloniaFact]
    public async Task Subscription_menu_pauses_resumes_and_pauses_all_and_the_choice_is_saved()
    {
        var vm = _rig.Create();
        var subs = await TwoSubs(vm);
        subs.Selected = subs.Rows[0];
        Assert.True(subs.SelectedActive);
        subs.TogglePauseCommand.Execute(null);
        Assert.True(subs.SelectedPaused);
        Assert.False(subs.Rows[0].Sub.Enabled);
        subs.TogglePauseCommand.Execute(null);
        Assert.True(subs.SelectedActive);

        subs.PauseAllCommand.Execute(null);
        Assert.All(subs.Rows, r => Assert.False(r.Enabled));
        var saved = SubscriptionStore.Load(_rig.Paths.SubscriptionsFile, out _);
        Assert.All(saved.Subscriptions, s => Assert.False(s.Enabled));
    }

    [AvaloniaFact]
    public async Task Subscription_menu_opens_the_channel_copies_the_link_and_opens_the_folder()
    {
        Directory.CreateDirectory(_rig.OutFolder);
        var vm = _rig.Create();
        var subs = await TwoSubs(vm);
        subs.Selected = subs.Rows[0];
        await subs.OpenChannelCommand.ExecuteAsync(null);
        Assert.Equal(["https://www.youtube.com/@uno"], _rig.Ui.OpenedUrls);
        await subs.CopyChannelLinkCommand.ExecuteAsync(null);
        Assert.Equal("https://www.youtube.com/@uno", _rig.Ui.Clipboard);
        await subs.OpenDownloadsFolderCommand.ExecuteAsync(null);
        Assert.Equal([_rig.OutFolder], _rig.Ui.OpenedFolders);

        subs.Selected = subs.Rows[1];                          // its folder does not exist yet: a message, not a crash
        await subs.OpenDownloadsFolderCommand.ExecuteAsync(null);
        Assert.Single(_rig.Ui.OpenedFolders);
        Assert.Contains(_rig.Ui.Messages, m => m.Contains("carpeta"));
    }

    [AvaloniaFact]
    public async Task The_subscription_menu_shows_different_items_on_a_subscription_and_on_empty_space()
    {
        var vm = _rig.Create();
        await TwoSubs(vm);
        vm.SelectedTab = MainViewModel.TabScraping;
        vm.Scraping.ShowSubscriptionsView();
        var window = new MainWindow { Width = 1100, Height = 780, DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var grid = window.GetVisualDescendants().OfType<DataGrid>().First(g => g.Name == "SubsGrid");
        var headers = grid.ContextMenu!.Items.OfType<MenuItem>().Select(m => m.Header as string).ToList();
        Assert.Equal(["Revisar ahora", "Pausar", "Reanudar", "Editar...", "Abrir canal en el navegador", "Copiar enlace del canal",
                      "Abrir carpeta de descargas", "Quitar suscripción...", "Agregar suscripción...", "Revisar todas ahora", "Pausar todas"], headers);
        grid.ContextMenu.DataContext = vm.Scraping;                         // a menu takes its data context when it opens: do the same
        var items = grid.ContextMenu.Items.OfType<MenuItem>().ToDictionary(m => (string)m.Header!);

        Subs(vm).MenuOnRow = true;
        Subs(vm).Selected = Subs(vm).Rows[0];
        Dispatcher.UIThread.RunJobs();
        Assert.True(items["Revisar ahora"].IsVisible && items["Quitar suscripción..."].IsVisible);
        Assert.True(items["Pausar"].IsVisible && !items["Reanudar"].IsVisible);
        Assert.False(items["Agregar suscripción..."].IsVisible);

        Subs(vm).MenuOnRow = false;
        Dispatcher.UIThread.RunJobs();
        Assert.False(items["Revisar ahora"].IsVisible);
        Assert.True(items["Agregar suscripción..."].IsVisible && items["Revisar todas ahora"].IsVisible && items["Pausar todas"].IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void The_thumbnail_item_is_in_the_queue_scraping_and_prep_menus()
    {
        var vm = _rig.Create();
        var window = new MainWindow { Width = 1100, Height = 780, DataContext = vm };
        window.Show();
        List<Avalonia.Visual> Tab(int tab)
        {
            vm.SelectedTab = tab;
            Dispatcher.UIThread.RunJobs();
            return window.GetVisualDescendants().ToList();
        }
        var prep = Tab(MainViewModel.TabDownload);
        var holder = prep.OfType<Border>().First(b => b.ContextMenu?.Items.OfType<MenuItem>().Any(m => m.Name == "PreviewThumbMenu") == true);
        Assert.Equal("Descargar miniatura...", holder.ContextMenu!.Items.OfType<MenuItem>().First(m => m.Name == "PreviewThumbMenu").Header);

        var queue = Tab(MainViewModel.TabQueue);
        Assert.Contains(queue.OfType<DataGrid>().First(g => g.ItemsSource == vm.QueueRows).ContextMenu!.Items.OfType<MenuItem>(), m => (m.Header as string) == "Descargar miniatura...");

        vm.Scraping.ShowExploreView();
        var scraping = Tab(MainViewModel.TabScraping);
        Assert.Equal("Descargar miniatura...", scraping.OfType<DataGrid>().First(g => g.Name == "ResultsGrid").ContextMenu!.Items.OfType<MenuItem>().First().Header);
        window.Close();
    }
}

/// <summary>RONDA 4 (spec B): Scraping steps that fold, and a table that takes the free height.</summary>
[Collection("fake yt-dlp environment")]
public sealed class ScrapingFoldTests : IDisposable
{
    private readonly TestRig _rig = new(tools: true);
    private readonly string _listing;

    public ScrapingFoldTests()
    {
        Loc.Instance.Language = "es";
        _listing = Path.Combine(_rig.Dir, "listing");
        Directory.CreateDirectory(_listing);
        foreach (var tab in new[] { "videos", "shorts", "streams", "playlists" })
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "listing", $"youtube-{tab}.jsonl"), Path.Combine(_listing, tab + ".jsonl"));
        File.WriteAllText(Path.Combine(_listing, "exact.txt"), "Q1fMMYyt0I4|20261012|855|not_live|public\n");
        Environment.SetEnvironmentVariable("FAKE_YTDLP_LISTING", _listing);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("FAKE_YTDLP_LISTING", null);
        _rig.Dispose();
    }

    private static async Task Until(Func<bool> done)
    {
        for (var i = 0; i < 300 && !done(); i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(30);
        }
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task The_link_box_has_the_new_placeholder_and_the_old_hint_is_gone()
    {
        var vm = _rig.Create();
        var window = new MainWindow { Width = 1100, Height = 780, DataContext = vm };
        vm.SelectedTab = MainViewModel.TabScraping;
        vm.Scraping.ShowExploreView();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var box = window.GetVisualDescendants().OfType<TextBox>().First(t => t.Name == "SourceBox");
        Assert.Equal("Pegá acá enlaces de canales, listas de reproducción, etc.", box.PlaceholderText);
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), t => (t.Text ?? "").StartsWith("Pegá el enlace de un canal"));
        Loc.Instance.Language = "en";
        try { Assert.Equal("Paste channel links, playlists, etc. here", box.PlaceholderText); }
        finally { Loc.Instance.Language = "es"; }
        await Task.CompletedTask;
        window.Close();
    }

    [AvaloniaFact]
    public async Task Search_folds_the_filters_with_a_one_line_summary_and_Reiniciar_opens_everything_again()
    {
        var vm = _rig.Create();
        vm.Settings.ScrapingLegalShown = true;
        var window = new MainWindow { Width = 1100, Height = 780, DataContext = vm };
        vm.SelectedTab = MainViewModel.TabScraping;
        vm.Scraping.ShowExploreView();
        window.Show();
        var ex = vm.Scraping.Explore;
        ex.Today = () => new DateOnly(2026, 10, 26);
        ex.LoadDelay = TimeSpan.Zero;
        ex.SourceText = "@fake";
        await ex.LoadTask;
        await Until(() => ex.HasSource);
        Assert.True(ex.FiltersExpanded && ex.LinkExpanded);

        var filters = window.GetVisualDescendants().OfType<CollapsibleSection>().First(c => c.Name == "FiltersCard");
        var results = window.GetVisualDescendants().OfType<CollapsibleSection>().First(c => c.Name == "ResultsCard");
        Assert.False(results.IsVisible);                                   // nothing searched yet
        ex.Filter.FromText = "2026-10-01";
        ex.Filter.ToText = "2026-10-31";
        await ex.SearchCommand.ExecuteAsync(null);
        await Until(() => !ex.IsSearching);
        Assert.False(ex.FiltersExpanded);                                  // "Buscar videos" folded the filters
        Assert.False(filters.IsExpanded);
        Assert.True(filters.SummaryVisible);
        Assert.Equal(ex.SummaryText, filters.Summary);
        Assert.True(results.IsVisible && results.Grows);
        Assert.Equal(Avalonia.Layout.VerticalAlignment.Stretch, results.VerticalAlignment);

        // Changing a filter does not unfold anything nor ask again by itself.
        var asked = _rig.Web.Requests.Count;
        ex.Filter.MaxCountText = "2";
        Assert.False(ex.FiltersExpanded);
        Assert.Equal(asked, _rig.Web.Requests.Count);

        _rig.Ui.ChoiceAnswers.Enqueue(true);                               // the results are marked: Reiniciar asks first
        await ex.ResetCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(ex.FiltersExpanded && ex.LinkExpanded);
        Assert.False(ex.HasSource);
        window.Close();
    }

    [AvaloniaFact]
    public async Task A_folded_link_keeps_the_channel_name_in_its_header_and_the_results_text_shows_while_the_first_ones_load()
    {
        var vm = _rig.Create();
        vm.Settings.ScrapingLegalShown = true;
        var window = new MainWindow { Width = 1100, Height = 780, DataContext = vm };
        vm.SelectedTab = MainViewModel.TabScraping;
        vm.Scraping.ShowExploreView();
        window.Show();
        var ex = vm.Scraping.Explore;
        ex.LoadDelay = TimeSpan.Zero;
        ex.SourceText = "@fake";
        await ex.LoadTask;
        await Until(() => ex.HasSource);
        ex.LinkExpanded = false;
        Dispatcher.UIThread.RunJobs();
        var name = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "SourceNameText");
        Assert.True(name.IsEffectivelyVisible);                            // still there, the box and the tabs are not
        Assert.False(window.GetVisualDescendants().OfType<TextBox>().First(t => t.Name == "SourceBox").IsEffectivelyVisible);
        Assert.False(ex.ResultsLoading);                                   // nothing searching, nothing to wait for
        window.Close();
    }
}
