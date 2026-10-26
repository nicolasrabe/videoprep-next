using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Videoprep.App.Services;
using Videoprep.Core.Engine;
using Videoprep.Core.Formats;
using Videoprep.Core.Scraping;
using Videoprep.Core.Text;

namespace Videoprep.App.ViewModels;

/// <summary>One row of the Explorar table.</summary>
public sealed partial class ExploreRow : ObservableObject
{
    public required ListingEntry Entry { get; init; }
    [ObservableProperty] private bool _selected;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _expanded;

    public string Title => Entry.IsPlaylist ? "▸ " + Entry.Title : Entry.Title;
    public string DateText => Entry.Date is { } d ? (Entry.DateIsApprox ? "~" : "") + d.ToString("yyyy-MM-dd") : "";
    public string DurationText => Filters.FormatDuration(Entry.DurationSec);
    public string ViewsText => Filters.FormatViews(Entry.Views, Loc.Instance.Language);
    public bool IsPlaylist => Entry.IsPlaylist;
    public bool CanSelect => !Entry.IsPlaylist;
    public bool CanExpand => Entry.IsPlaylist && !Expanded;
}

/// <summary>
/// Explorar ("list first", SCRAPING_SPEC 1): list a channel or playlist, filter locally, pick, send to the queue.
/// The listing is kept for the session per link and tab set, so "Aplicar filtros" never asks the site again.
/// </summary>
public sealed partial class ExploreViewModel : ObservableObject
{
    private readonly MainViewModel _host;
    private readonly List<ListingEntry> _all = [];
    private string _listedKey = "";
    private CancellationTokenSource? _cts;

    public ExploreViewModel(MainViewModel host)
    {
        _host = host;
        Loc.Instance.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != "Item[]") return;
            RefreshStatuses();
            OnPropertyChanged(nameof(SelectedText));
        };
    }

    private Loc L => Loc.Instance;

    // ---- inputs
    [ObservableProperty] private string _sourceText = "";
    [ObservableProperty] private bool _tabVideos = true;
    [ObservableProperty] private bool _tabShorts;
    [ObservableProperty] private bool _tabStreams;
    [ObservableProperty] private bool _tabPlaylists;

    public FilterInputs Filter { get; } = new();

    // ---- table
    public ObservableCollection<ExploreRow> Rows { get; } = [];
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsIdle))] private bool _isSearching;
    public bool IsIdle => !IsSearching;
    [ObservableProperty] private string _statusLine = "";
    [ObservableProperty] private bool _hasApproxDates;
    public string SelectedText => L.Format("sc_selected", Rows.Count(r => r.Selected), Rows.Count(r => r.CanSelect));

    /// <summary>Format for "Enviar a la cola".</summary>
    public FormatPicker Format { get; } = new();

    public FilterSet? ReadFilters(out string? problem) => Filter.Read(out problem);

    /// <summary>The checked tabs, for "Suscribirme a esto..." too.</summary>
    public IReadOnlyList<SourceTab> CheckedTabs()

    {
        var tabs = new List<SourceTab>();
        if (TabVideos) tabs.Add(SourceTab.Videos);
        if (TabShorts) tabs.Add(SourceTab.Shorts);
        if (TabStreams) tabs.Add(SourceTab.Streams);
        if (TabPlaylists) tabs.Add(SourceTab.Playlists);
        return tabs;
    }

    [RelayCommand]
    private async Task Search()
    {
        if (IsSearching) return;
        var filters = ReadFilters(out var problem);
        if (filters is null)
        {
            await _host.Ui.ShowMessage(problem!, warning: true);
            return;
        }
        if (SourceUrl.UnsupportedProfile(SourceText) is { } site)
        {
            await _host.Ui.ShowMessage(L.Format("sc_profile_unsupported", site));
            return;
        }
        var targets = SourceUrl.Targets(SourceText, CheckedTabs());
        if (targets.Count == 0)
        {
            await _host.Ui.ShowMessage(L["sc_bad_url"], warning: true);
            return;
        }
        var tools = _host.CurrentTools();
        if (tools.YtDlp is null)
        {
            await _host.Ui.ShowMessage(L["msg_tools_missing"], warning: true);
            return;
        }
        if (!_host.Settings.ScrapingLegalShown)
        {
            // SCRAPING_SPEC: the legal notice is shown once, on first use of the tab.
            await _host.Ui.ShowMessage(L["sc_legal"]);
            _host.Settings.ScrapingLegalShown = true;
            _host.SaveSettings();
        }

        IsSearching = true;
        _all.Clear();
        Rows.Clear();
        _listedKey = "";
        OnPropertyChanged(nameof(SelectedText));
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var lister = _host.NewLister();
        var cookies = _host.ListingCookies();
        var notes = new List<string>();
        try
        {
            StatusLine = L.Format("sc_searching", 0);
            var results = await Task.Run(() => lister.ListAsync(targets, tools, cookies, e => _host.Post(() =>
            {
                _all.Add(e);
                StatusLine = L.Format("sc_searching", _all.Count);
            }), token));
            await _host.FlushPosts();

            foreach (var r in results)
            {
                var tabName = TabName(r.Target.Tab);
                switch (r.Outcome)
                {
                    case ListOutcome.NoSuchTab: notes.Add(L.Format("sc_no_tab", tabName)); break;
                    case ListOutcome.Failed: notes.Add(L.Format("sc_tab_failed", tabName.Length > 0 ? tabName : r.Target.Url)); break;
                    case ListOutcome.Blocked: notes.Add(L["sc_blocked"]); break;
                    case ListOutcome.Cancelled: notes.Add(L["sc_cancelled"]); break;
                }
            }
            var blocked = results.Any(r => r.Outcome == ListOutcome.Blocked);
            if (!token.IsCancellationRequested && !blocked)
            {
                // Exact data only for videos that passed everything else and miss a date/length a filter needs.
                var need = Filters.NeedsExact(Filters.Apply(_all, filters, IsDownloadedFn()), filters);
                if (need.Count > 0)
                {
                    var (infos, exactBlocked) = await Task.Run(() => lister.ExactAsync(need, tools, cookies,
                        (done, total) => _host.Post(() => StatusLine = L.Format("sc_confirming", done, total)), token));
                    await _host.FlushPosts();
                    var byId = infos.ToDictionary(i => i.Id);
                    for (var i = 0; i < _all.Count; i++)
                        if (byId.TryGetValue(_all[i].Id, out var x)) _all[i] = x.ApplyTo(_all[i]);
                    if (exactBlocked) notes.Add(L["sc_blocked"]);
                }
            }
            _listedKey = SourceText.Trim() + "|" + string.Join(",", CheckedTabs());
            Refilter(filters);
            StatusLine = string.Join("  ", new[] { L.Format("sc_found", _all.Count(e => !e.IsPlaylist), Rows.Count(r => r.CanSelect)) }.Concat(notes));
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            IsSearching = false;
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    /// <summary>"Aplicar filtros": re-filters the listing already in memory, no new requests.</summary>
    [RelayCommand]
    private async Task ApplyFilters()
    {
        var filters = ReadFilters(out var problem);
        if (filters is null)
        {
            await _host.Ui.ShowMessage(problem!, warning: true);
            return;
        }
        Refilter(filters);
        StatusLine = L.Format("sc_found", _all.Count(e => !e.IsPlaylist), Rows.Count(r => r.CanSelect));
    }

    private Func<ListingEntry, bool> IsDownloadedFn()
    {
        var archive = ArchiveStore.Load(_host.ArchivePath);
        return archive.Contains;
    }

    private void Refilter(FilterSet filters)
    {
        var archive = ArchiveStore.Load(_host.ArchivePath);
        var expanded = Rows.Where(r => r.Expanded).Select(r => r.Entry.Id).ToHashSet();
        Rows.Clear();
        foreach (var e in Filters.Apply(_all, filters, archive.Contains))
        {
            var row = new ExploreRow { Entry = e, Expanded = expanded.Contains(e.Id) };
            row.Status = StatusOf(e, archive);
            // New videos come checked, like the mockup; downloaded/queued/live/members ones do not.
            row.Selected = row.CanSelect && row.Status == L["sc_status_new"];
            row.PropertyChanged += (_, a) => { if (a.PropertyName == nameof(ExploreRow.Selected)) OnPropertyChanged(nameof(SelectedText)); };
            Rows.Add(row);
        }
        HasApproxDates = Rows.Any(r => r.Entry.DateIsApprox);
        OnPropertyChanged(nameof(SelectedText));
    }

    private string StatusOf(ListingEntry e, ArchiveStore archive)
    {
        if (e.IsPlaylist) return L["sc_status_playlist"];
        if (archive.Contains(e)) return L["sc_status_done"];
        if (_host.IsInQueue(e.Url)) return L["sc_status_queued"];
        if (e.IsLiveNow) return L["sc_status_live"];
        if (e.IsMembersOnly) return L["sc_status_members"];
        return L["sc_status_new"];
    }

    private void RefreshStatuses()
    {
        var archive = ArchiveStore.Load(_host.ArchivePath);
        foreach (var r in Rows) r.Status = StatusOf(r.Entry, archive);
    }

    private string TabName(SourceTab tab) => tab switch
    {
        SourceTab.Videos => L["sc_tabs_videos"],
        SourceTab.Shorts => L["sc_tabs_shorts"],
        SourceTab.Streams => L["sc_tabs_streams"],
        SourceTab.Playlists => L["sc_tabs_playlists"],
        _ => "",
    };

    [RelayCommand]
    private void SelectAll() { foreach (var r in Rows.Where(r => r.CanSelect)) r.Selected = true; }

    [RelayCommand]
    private void SelectNone() { foreach (var r in Rows) r.Selected = false; }

    [RelayCommand]
    private void Invert() { foreach (var r in Rows.Where(r => r.CanSelect)) r.Selected = !r.Selected; }

    /// <summary>Lists the videos of a playlist row and adds them to the table (SCRAPING_SPEC: "expands into its videos").</summary>
    [RelayCommand]
    private async Task Expand(ExploreRow? row)
    {
        if (row is null || !row.CanExpand || IsSearching) return;
        var filters = ReadFilters(out var problem);
        if (filters is null) { await _host.Ui.ShowMessage(problem!, warning: true); return; }
        var tools = _host.CurrentTools();
        if (tools.YtDlp is null) { await _host.Ui.ShowMessage(L["msg_tools_missing"], warning: true); return; }
        IsSearching = true;
        _cts = new CancellationTokenSource();
        try
        {
            var before = _all.Count;
            var lister = _host.NewLister();
            var target = new SourceUrl.Target(SourceTab.Single, row.Entry.Url);
            var results = await Task.Run(() => lister.ListAsync([target], tools, _host.ListingCookies(),
                e => _host.Post(() => { if (_all.All(x => x.Id != e.Id)) _all.Add(e with { Order = 100_000 + e.Order }); }), _cts.Token));
            await _host.FlushPosts();
            row.Expanded = true;
            Refilter(filters);
            var r = results[0];
            StatusLine = r.Outcome == ListOutcome.Ok
                ? L.Format("sc_found", _all.Count - before, Rows.Count(x => x.CanSelect))
                : r.Outcome == ListOutcome.Blocked ? L["sc_blocked"] : L.Format("sc_tab_failed", row.Entry.Title);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            IsSearching = false;
        }
    }

    [RelayCommand]
    private async Task SendToQueue()
    {
        var picked = Rows.Where(r => r.Selected && r.CanSelect).ToList();
        if (picked.Count == 0)
        {
            await _host.Ui.ShowMessage(L["sc_nothing_selected"]);
            return;
        }
        _host.AddToQueue(picked.Select(r => (r.Entry.Url, r.Entry.Title)), Format.FormatKey, Format.Cap);
        foreach (var r in picked)
        {
            r.Selected = false;
            r.Status = L["sc_status_queued"];
        }
        StatusLine = L.Format("sc_sent", picked.Count);
    }
}

/// <summary>The Scraping tab: Explorar and Suscripciones.</summary>
public sealed partial class ScrapingViewModel : ObservableObject
{
    public ScrapingViewModel(MainViewModel host)
    {
        Explore = new ExploreViewModel(host);
        Subscriptions = new SubscriptionsViewModel(host);
    }

    public ExploreViewModel Explore { get; }

    public SubscriptionsViewModel Subscriptions { get; }

    /// <summary>"Suscribirme a esto..." in Explorar.</summary>
    [RelayCommand]
    private async Task SubscribeFromExplore()
    {
        var before = Subscriptions.Rows.Count;
        await Subscriptions.AddFromExplore(Explore);
        if (Subscriptions.Rows.Count > before) ShowSubscriptionsView();
    }

    /// <summary>0 = Explorar, 1 = Suscripciones.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowExplore), nameof(ShowSubscriptions))]
    private int _subView;

    public bool ShowExplore => SubView == 0;
    public bool ShowSubscriptions => SubView == 1;

    public void ShowExploreView() => SubView = 0;
    public void ShowSubscriptionsView() => SubView = 1;
}
