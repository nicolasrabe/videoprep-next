using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Videoprep.App.Services;
using Videoprep.Core.Engine;
using Videoprep.Core.Scraping;
using Videoprep.Core.Text;

namespace Videoprep.App.ViewModels;

/// <summary>One row of the results table.</summary>
public sealed partial class ExploreRow : ObservableObject
{
    public required ListingEntry Entry { get; init; }
    /// <summary>A video whose date could not be confirmed: listed apart, never mixed with the results.</summary>
    public bool IsUnconfirmed { get; init; }
    [ObservableProperty] private bool _selected;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _expanded;
    [ObservableProperty] private QueueRowCard? _card;

    public string Title => Entry.IsPlaylist ? "▸ " + Entry.Title : Entry.Title;
    public string DateText => Entry.Date is { } d ? (Entry.DateIsApprox ? "~" : "") + d.ToString("yyyy-MM-dd") : "";
    public string DurationText => Filters.FormatDuration(Entry.DurationSec);
    public string ViewsText => Filters.FormatViews(Entry.Views, Loc.Instance.Language);
    public bool IsPlaylist => Entry.IsPlaylist;
    public bool CanSelect => !Entry.IsPlaylist;
    public bool CanExpand => Entry.IsPlaylist && !Expanded;
}

/// <summary>
/// Scraping > Explorar in three steps (RONDA 1 spec 1.2): ① paste a link (one light request, no video is fetched), ② choose the
/// filters, ③ press "Buscar videos" and only then the table is filled, with what passes the filters. What was read is kept for the
/// session, so changing a filter never repeats a request (<see cref="ScrapeSearch"/>).
/// </summary>
public sealed partial class ExploreViewModel : ObservableObject
{
    private readonly MainViewModel _host;
    private readonly ScrapeSearch _search;
    /// <summary>Videos of playlists the person opened ("Mostrar videos"), added to what the search read.</summary>
    private readonly List<ListingEntry> _extra = [];
    private CancellationTokenSource? _cts;
    private bool _settingPreset;
    private int _loadTicket;
    private string _loadedSource = "";
    private SourceProbe? _probe;
    private IReadOnlyList<SourceUrl.Target> _lastTargets = [];

    public ExploreViewModel(MainViewModel host)
    {
        _host = host;
        _search = host.NewSearch();
        Filter.PropertyChanged += (_, e) =>
        {
            if (_settingPreset) return;
            if (e.PropertyName is nameof(FilterInputs.FromText) or nameof(FilterInputs.ToText))
                Preset = Filter.FromText.Trim().Length == 0 && Filter.ToText.Trim().Length == 0 ? 0 : 4;
            UpdateSummary();
        };
        Loc.Instance.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != "Item[]") return;
            RefreshStatuses();
            OnPropertyChanged(nameof(SelectedText));
            UpdateSummary();
            UpdateResultsText();
        };
        host.VideoInfoChanged += key =>
        {
            foreach (var r in Rows.Concat(UnconfirmedRows).Where(r => r.Expanded && UrlParser.LinkKey(r.Entry.Url) == key)) RefreshCard(r);
        };
    }

    private Loc L => Loc.Instance;

    /// <summary>Today, injectable for tests (the quick date buttons and the month).</summary>
    public Func<DateOnly> Today { get; set; } = () => DateOnly.FromDateTime(DateTime.Today);

    /// <summary>How long to wait after the text changes before loading it by itself (0 in tests).</summary>
    public TimeSpan LoadDelay { get; set; } = TimeSpan.FromMilliseconds(700);

    /// <summary>The automatic load that follows the last change of the link (tests wait for it).</summary>
    public Task LoadTask { get; private set; } = Task.CompletedTask;

    // ---- ① link
    [ObservableProperty] private string _sourceText = "";
    [ObservableProperty] private bool _tabVideos = true;
    [ObservableProperty] private bool _tabShorts;
    [ObservableProperty] private bool _tabStreams;
    [ObservableProperty] private bool _tabPlaylists;
    [ObservableProperty] private bool _isLoadingSource;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(NoSource))] private bool _hasSource;
    [ObservableProperty] private string _sourceName = "";
    [ObservableProperty] private string _sourceHandle = "";
    [ObservableProperty] private string _sourceLine = "";
    [ObservableProperty] private Bitmap? _avatar;
    [ObservableProperty] private string _loadNote = "";

    public bool NoSource => !HasSource;

    partial void OnSourceTextChanged(string value)
    {
        var trimmed = value.Trim();
        if (trimmed == _loadedSource) return;
        // A different link: what was identified no longer applies. The tables stay until the next search.
        HasSource = false;
        _probe = null;
        _loadedSource = "";
        LoadNote = "";
        UpdateSummary();
        if (SourceUrl.Targets(trimmed, CheckedTabs()).Count == 0 || SourceUrl.UnsupportedProfile(trimmed) is not null) return;
        var ticket = ++_loadTicket;
        LoadTask = LoadLaterAsync(ticket);
    }

    private async Task LoadLaterAsync(int ticket)
    {
        if (LoadDelay > TimeSpan.Zero) await Task.Delay(LoadDelay).ConfigureAwait(true);
        if (ticket != _loadTicket || IsSearching) return;
        await LoadCore().ConfigureAwait(true);
    }

    partial void OnTabVideosChanged(bool value) => UpdateSummary();
    partial void OnTabShortsChanged(bool value) => UpdateSummary();
    partial void OnTabStreamsChanged(bool value) => UpdateSummary();
    partial void OnTabPlaylistsChanged(bool value) => UpdateSummary();

    /// <summary>"Cargar": identifies the link with one light request (name, user, avatar). No video is listed.</summary>
    [RelayCommand]
    private async Task Load()
    {
        _loadTicket++;
        await LoadCore(force: true).ConfigureAwait(true);
    }

    private Task? _loadInFlight;

    /// <summary>One load at a time: asking again while it runs waits for it; nothing to do when this link is already identified.</summary>
    private async Task LoadCore(bool force = false)
    {
        if (_loadInFlight is { IsCompleted: false } running) { await running.ConfigureAwait(true); return; }
        if (IsSearching || !force && HasSource && _loadedSource == SourceText.Trim()) return;
        _loadInFlight = LoadInner();
        await _loadInFlight.ConfigureAwait(true);
    }

    private async Task LoadInner()
    {
        var text = SourceText.Trim();
        if (SourceUrl.UnsupportedProfile(text) is { } site)
        {
            await _host.Ui.ShowMessage(L.Format("sc_profile_unsupported", site));
            return;
        }
        var targets = SourceUrl.Targets(text, CheckedTabs());
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
        await ShowLegalOnce();
        IsLoadingSource = true;
        LoadNote = L["sc_loading"];
        try
        {
            var probe = await Task.Run(() => _host.NewLister().ProbeAsync(targets[0], tools, _host.ListingCookies(), CancellationToken.None));
            if (text != SourceText.Trim()) return;   // the link changed while loading
            if (probe.Outcome != ListOutcome.Ok)
            {
                HasSource = false;
                LoadNote = probe.Outcome == ListOutcome.Blocked ? L["sc_blocked"]
                         : probe.Outcome == ListOutcome.NoSuchTab ? L.Format("sc_no_tab", TabName(targets[0].Tab))
                         : L["sc_load_failed"];
                return;
            }
            _probe = probe;
            _loadedSource = text;
            SourceName = probe.Name;
            SourceHandle = probe.Handle;
            SourceLine = probe.Handle.Length > 0 ? probe.Handle : text;
            HasSource = true;
            LoadNote = L["sc_ready"];
            Avatar = null;
            if (probe.AvatarUrl is { } url && await _host.FetchBytes(url) is { } bytes)
            {
                try { Avatar = new Bitmap(new MemoryStream(bytes)); }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or NotSupportedException) { Avatar = null; }
            }
            UpdateSummary();
        }
        finally
        {
            IsLoadingSource = false;
        }
    }

    private async Task ShowLegalOnce()
    {
        if (_host.Settings.ScrapingLegalShown) return;
        // SCRAPING_SPEC: the legal notice is shown once, on first use of the tab.
        await _host.Ui.ShowMessage(L["sc_legal"]);
        _host.Settings.ScrapingLegalShown = true;
        _host.SaveSettings();
    }

    // ---- ② filters
    public FilterInputs Filter { get; } = new();

    /// <summary>0 = none, 1 = last 24 h, 2 = last 7 days, 3 = this month, 4 = between dates.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preset24h), nameof(Preset7d), nameof(PresetMonth), nameof(PresetBetween))]
    private int _preset;

    public bool Preset24h => Preset == 1;
    public bool Preset7d => Preset == 2;
    public bool PresetMonth => Preset == 3;
    public bool PresetBetween => Preset == 4;

    /// <summary>The quick date buttons: they only fill the two date boxes (same rule as typing the dates).</summary>
    [RelayCommand]
    private void QuickDates(string? which)
    {
        var today = Today();
        _settingPreset = true;
        try
        {
            switch (which)
            {
                case "24h": Filter.FromText = Fmt(today.AddDays(-1)); Filter.ToText = Fmt(today); Preset = 1; break;
                case "7d": Filter.FromText = Fmt(today.AddDays(-7)); Filter.ToText = Fmt(today); Preset = 2; break;
                case "month": Filter.FromText = Fmt(new DateOnly(today.Year, today.Month, 1)); Filter.ToText = Fmt(today); Preset = 3; break;
                default: Preset = 4; break;
            }
        }
        finally
        {
            _settingPreset = false;
        }
        UpdateSummary();
    }

    private static string Fmt(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [ObservableProperty] private string _summaryText = "";
    [ObservableProperty] private string _estimateText = "";

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

    private SearchRequest? BuildRequest(FilterSet filters)
    {
        var tools = _host.CurrentTools();
        if (tools.YtDlp is null) return null;
        var archive = ArchiveStore.Load(_host.ArchivePath);
        return new SearchRequest(SourceText.Trim(), CheckedTabs(), filters, archive.Contains, Today(), tools, _host.ListingCookies(), _probe?.TotalCount);
    }

    /// <summary>"Se va a buscar: ..." and "Pedidos estimados: X a Y", worked out from the boxes and from what is already read.</summary>
    private void UpdateSummary()
    {
        if (!HasSource) { SummaryText = ""; EstimateText = ""; return; }
        var f = Filter.Read(out _);
        if (f is null) { SummaryText = ""; EstimateText = ""; return; }
        var who = SourceHandle.Length > 0 ? SourceHandle : SourceName;
        var text = L.Format("sc_sum_videos", who);
        if (f.From is { } from && f.To is { } to) text += L.Format("sc_sum_between", DateWords(from), DateWords(to));
        else if (f.From is { } only) text += L.Format("sc_sum_since", DateWords(only));
        else if (f.To is { } until) text += L.Format("sc_sum_until", DateWords(until));
        text += f.MaxCount is { } n ? L.Format("sc_sum_limit", n) : L["sc_sum_nolimit"];
        if (f.UsesDuration || f.Include.Count > 0 || f.Exclude.Count > 0) text += L["sc_sum_extra"];
        SummaryText = text + ".";

        var req = BuildRequest(f);
        if (req is null) { EstimateText = ""; return; }
        var est = _search.Estimate(req);
        var line = est.Max == 0 ? L["sc_est_none"]
                 : est.Min == est.Max ? L.Format("sc_est_one", est.Min)
                 : L.Format("sc_est", est.Min, est.Max);
        if (f.UsesDates) line += L.Format("sc_est_dates", _search.ExactRequestCap);
        line += _host.ListingCookies().Kind == CookieKind.None ? L["sc_est_nocookies"] : L["sc_est_cookies"];
        EstimateText = line;
    }

    private string DateWords(DateOnly d) => d.ToDateTime(TimeOnly.MinValue).ToString("d 'de' MMMM 'de' yyyy", L.Culture);

    // ---- ③ results
    public ObservableCollection<ExploreRow> Rows { get; } = [];
    public ObservableCollection<ExploreRow> UnconfirmedRows { get; } = [];
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsIdle))] private bool _isSearching;
    public bool IsIdle => !IsSearching;
    [ObservableProperty] private bool _hasSearched;
    [ObservableProperty] private string _statusLine = "";
    [ObservableProperty] private bool _hasApproxDates;
    [ObservableProperty] private string _resultsHeader = "";
    [ObservableProperty] private string _datesText = "";
    [ObservableProperty] private string _unconfirmedText = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(UnconfirmedToggleText))] private bool _showUnconfirmed;
    public bool HasUnconfirmed => UnconfirmedRows.Count > 0;
    public string UnconfirmedToggleText => ShowUnconfirmed ? L["sc_unconf_hide"] : L["sc_unconf_see"];
    public string SelectedText => L.Format("sc_selected", Rows.Count(r => r.Selected), Rows.Count(r => r.CanSelect));

    /// <summary>Format for "Enviar a la cola".</summary>
    public FormatPicker Format { get; } = new();

    private int _reviewed;
    private int _passing;
    private int _datesA;
    private int _datesB;

    private void UpdateResultsText()
    {
        ResultsHeader = !HasSearched ? "" : _passing == 1 ? L.Format("sc_res_header_one", _reviewed) : L.Format("sc_res_header", _passing, _reviewed);
        DatesText = _datesB > 0 ? L.Format("sc_dates_confirmed", _datesA, _datesB) : "";
        UnconfirmedText = UnconfirmedRows.Count == 1 ? L["sc_unconf_one"] : L.Format("sc_unconf", UnconfirmedRows.Count);
        OnPropertyChanged(nameof(UnconfirmedToggleText));
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
        if (SourceUrl.Targets(SourceText, CheckedTabs()).Count == 0)
        {
            await _host.Ui.ShowMessage(L["sc_bad_url"], warning: true);
            return;
        }
        if (_host.CurrentTools().YtDlp is null)
        {
            await _host.Ui.ShowMessage(L["msg_tools_missing"], warning: true);
            return;
        }
        if (!HasSource || _loadedSource != SourceText.Trim())
        {
            await LoadCore();
            if (!HasSource) return;
        }
        var req = BuildRequest(filters)!;
        _lastTargets = SourceUrl.Targets(req.Source, req.Tabs);
        _extra.Clear();

        IsSearching = true;
        HasSearched = true;
        _reviewed = _passing = _datesA = _datesB = 0;
        Rows.Clear();
        UnconfirmedRows.Clear();
        UpdateResultsText();
        OnPropertyChanged(nameof(SelectedText));
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        try
        {
            StatusLine = L.Format("sc_searching", 0);
            var result = await Task.Run(() => _search.RunAsync(req, new Prompts(this), p => _host.Post(() => OnProgress(p)), token));
            await _host.FlushPosts();

            var notes = new List<string>();
            foreach (var r in result.Tabs)
            {
                var tabName = TabName(r.Target.Tab);
                switch (r.Outcome)
                {
                    case ListOutcome.NoSuchTab: notes.Add(L.Format("sc_no_tab", tabName)); break;
                    case ListOutcome.Failed: notes.Add(L.Format("sc_tab_failed", tabName.Length > 0 ? tabName : r.Target.Url)); break;
                }
            }
            if (result.Status == SearchStatus.Blocked) notes.Add(L["sc_blocked"]);
            if (result.Status == SearchStatus.Cancelled) notes.Add(L["sc_cancelled"]);
            if (result.Truncated) notes.Add(L["sc_truncated"]);
            if (result.Filters.MaxCount != filters.MaxCount) Filter.MaxCountText = result.Filters.MaxCount?.ToString(CultureInfo.InvariantCulture) ?? "";
            Refilter(result.Filters);
            if (result.Status == SearchStatus.Done && Rows.Count == 0 && UnconfirmedRows.Count == 0) notes.Add(L["sc_none_pass"]);
            StatusLine = string.Join("  ", notes);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            IsSearching = false;
            UpdateSummary();
        }
    }

    private void OnProgress(SearchProgress p)
    {
        _reviewed = p.Reviewed;
        _passing = p.Passing;
        if (p.DatesTotal > 0) { _datesA = p.DatesConfirmed; _datesB = p.DatesTotal; }
        StatusLine = p.Phase == "confirming" ? L.Format("sc_confirming", p.DatesConfirmed, p.DatesTotal) : L.Format("sc_searching", p.Reviewed);
        UpdateResultsText();
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    /// <summary>The filters applied again to what is already read, no request (also used when only a box changed).</summary>
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
        StatusLine = "";
    }

    private Func<ListingEntry, bool> IsDownloadedFn() => ArchiveStore.Load(_host.ArchivePath).Contains;

    private void Refilter(FilterSet filters)
    {
        var archive = ArchiveStore.Load(_host.ArchivePath);
        var open = Rows.Concat(UnconfirmedRows).Where(r => r.Expanded).Select(r => r.Entry.Id).ToHashSet();
        var all = _search.AllEntries(_lastTargets).Concat(_extra).ToList();
        var eval = Filters.Evaluate(all, filters, archive.Contains, Today());
        Rows.Clear();
        UnconfirmedRows.Clear();
        foreach (var e in eval.Shown)
        {
            var row = new ExploreRow { Entry = e, Expanded = open.Contains(e.Id) };
            row.Status = StatusOf(e, archive);
            // New videos come checked, like the mockup; downloaded/queued/live/members ones do not.
            row.Selected = row.CanSelect && row.Status == L["sc_status_new"];
            row.PropertyChanged += (_, a) => { if (a.PropertyName == nameof(ExploreRow.Selected)) OnPropertyChanged(nameof(SelectedText)); };
            if (row.Expanded) { row.Card = NewCard(row); RefreshCard(row); }
            Rows.Add(row);
        }
        // Without a confirmed date: apart and unchecked ("Ver cuáles").
        foreach (var e in eval.Unconfirmed)
        {
            var row = new ExploreRow { Entry = e, IsUnconfirmed = true, Status = StatusOf(e, archive) };
            row.PropertyChanged += (_, a) => { if (a.PropertyName == nameof(ExploreRow.Selected)) OnPropertyChanged(nameof(SelectedText)); };
            UnconfirmedRows.Add(row);
        }
        _reviewed = all.Count;
        _passing = eval.Shown.Count(e => !e.IsPlaylist);
        (_datesA, _datesB) = filters.UsesDates ? (_passing, _passing + UnconfirmedRows.Count) : (0, 0);
        if (UnconfirmedRows.Count == 0) ShowUnconfirmed = false;
        HasApproxDates = Rows.Any(r => r.Entry.DateIsApprox);
        OnPropertyChanged(nameof(HasUnconfirmed));
        OnPropertyChanged(nameof(SelectedText));
        UpdateResultsText();
    }

    [RelayCommand]
    private void ToggleUnconfirmed() => ShowUnconfirmed = !ShowUnconfirmed;

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
        foreach (var r in Rows.Concat(UnconfirmedRows)) r.Status = StatusOf(r.Entry, archive);
    }

    private string TabName(SourceTab tab) => tab switch
    {
        SourceTab.Videos => L["sc_tabs_videos"],
        SourceTab.Shorts => L["sc_tabs_shorts"],
        SourceTab.Streams => L["sc_tabs_streams"],
        SourceTab.Playlists => L["sc_tabs_playlists"],
        _ => "",
    };

    // ---- selection and the bottom bar
    [RelayCommand]
    private void SelectAll() { foreach (var r in Rows.Where(r => r.CanSelect)) r.Selected = true; }

    [RelayCommand]
    private void SelectNone() { foreach (var r in Rows.Concat(UnconfirmedRows)) r.Selected = false; }

    [RelayCommand]
    private void Invert() { foreach (var r in Rows.Where(r => r.CanSelect)) r.Selected = !r.Selected; }

    /// <summary>"Reiniciar": back to step 1. Asks first only when a search is running or videos are checked.</summary>
    [RelayCommand]
    private async Task Reset()
    {
        if ((IsSearching || Rows.Concat(UnconfirmedRows).Any(r => r.Selected))
            && !await _host.Ui.AskChoice(L["sc_reset_ask"], L["sc_reset_text"], L["sc_reset"], L["btn_cancel"]))
            return;
        _cts?.Cancel();
        _loadTicket++;
        _search.Clear();
        _extra.Clear();
        _lastTargets = [];
        Rows.Clear();
        UnconfirmedRows.Clear();
        ShowUnconfirmed = false;
        HasSearched = false;
        _reviewed = _passing = _datesA = _datesB = 0;
        _settingPreset = true;
        try
        {
            Filter.CopyFrom(new FilterInputs());
            Preset = 0;
        }
        finally
        {
            _settingPreset = false;
        }
        (TabVideos, TabShorts, TabStreams, TabPlaylists) = (true, false, false, false);
        _loadedSource = "";
        _probe = null;
        HasSource = false;
        Avatar = null;
        LoadNote = "";
        StatusLine = "";
        SourceText = "";
        UpdateResultsText();
        OnPropertyChanged(nameof(HasUnconfirmed));
        OnPropertyChanged(nameof(SelectedText));
        UpdateSummary();
    }

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
            var before = _extra.Count;
            var lister = _host.NewLister();
            var target = new SourceUrl.Target(SourceTab.Single, row.Entry.Url);
            var results = await Task.Run(() => lister.ListAsync([target], tools, _host.ListingCookies(),
                e => _host.Post(() => { if (_extra.All(x => x.Id != e.Id)) _extra.Add(e with { Order = 100_000 + e.Order }); }), _cts.Token));
            await _host.FlushPosts();
            row.Expanded = true;
            Refilter(filters);
            var r = results[0];
            StatusLine = r.Outcome == ListOutcome.Ok
                ? L.Format("sc_found", _extra.Count - before, Rows.Count(x => x.CanSelect))
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
        var picked = Rows.Concat(UnconfirmedRows).Where(r => r.Selected && r.CanSelect).ToList();
        if (picked.Count == 0)
        {
            await _host.Ui.ShowMessage(L["sc_nothing_selected"]);
            return;
        }
        _host.AddToQueue(picked.Select(r => (r.Entry.Url, r.Entry.Title)), Format.FormatKey, Format.Cap, Format.ConstantRate);
        foreach (var r in picked)
        {
            r.Selected = false;
            r.Status = L["sc_status_queued"];
        }
        StatusLine = L.Format("sc_sent", picked.Count);
    }

    // ---- opened rows (the same card as the Queue)
    public ExploreRow? OpenRowNow => Rows.Concat(UnconfirmedRows).FirstOrDefault(r => r.Expanded);

    /// <summary>Raised when a row opened or closed, so the table shows or hides its details.</summary>
    public event Action? ExpandedChanged;

    /// <summary>One row open at a time. Only the row that is opened asks for its description (one request, one at a time).</summary>
    public void OpenRow(ExploreRow row)
    {
        if (row.IsPlaylist) return;
        foreach (var r in Rows.Concat(UnconfirmedRows)) if (r != row && r.Expanded) r.Expanded = false;
        row.Card ??= NewCard(row);
        row.Expanded = true;
        ExpandedChanged?.Invoke();
        _host.RequestVideoInfo(row.Entry.Url);
        RefreshCard(row);
    }

    public void CloseRows()
    {
        foreach (var r in Rows.Concat(UnconfirmedRows).Where(r => r.Expanded)) r.Expanded = false;
        ExpandedChanged?.Invoke();
    }

    /// <summary>A click on the row that is already the only one selected: open it, or close it if open.</summary>
    public void ToggleRow(ExploreRow row)
    {
        if (row.Expanded) CloseRows();
        else OpenRow(row);
    }

    private QueueRowCard NewCard(ExploreRow row) => _host.NewCard(row.Entry.Url, () =>
    {
        _host.RetryVideoInfo(row.Entry.Url);
        RefreshCard(row);
    });

    private void RefreshCard(ExploreRow row)
    {
        if (row.Card is not { } card) return;
        var info = _host.FillCard(card, UrlParser.LinkKey(row.Entry.Url), row.Entry);
        card.Will = _host.WillText(Format.FormatKey, Format.Cap, card.Title);
        MainViewModel.SetCardState(card, info);
    }

    /// <summary>The questions a search asks (50 videos, cap of date confirmations) become dialogs.</summary>
    private sealed class Prompts(ExploreViewModel vm) : ISearchPrompts
    {
        private Loc L => Loc.Instance;

        public async Task<ManyKnownChoice> AskManyKnown(int total, int requests) =>
            await Ask(() => vm._host.Ui.AskThree(L["sc_search"], L.Format("sc_ask_many", total, requests), L["sc_btn_all"], L["sc_btn_limit50"], L["btn_cancel"])) switch
            {
                0 => ManyKnownChoice.SearchAll,
                1 => ManyKnownChoice.LimitTo50,
                _ => ManyKnownChoice.Cancel,
            };

        public async Task<Reached50Choice> AskReached50(int found) =>
            await Ask(() => vm._host.Ui.AskThree(L["sc_search"], L.Format("sc_ask_50", found), L["sc_btn_go"], L["sc_btn_stop_here"], L["btn_cancel"])) switch
            {
                0 => Reached50Choice.Continue,
                1 => Reached50Choice.StopHere,
                _ => Reached50Choice.Cancel,
            };

        public Task<bool> AskConfirmMore(int missing) =>
            Ask(() => vm._host.Ui.AskChoice(L["sc_search"], L.Format("sc_ask_more", missing), L["sc_btn_go"], L["sc_btn_stop"]));

        /// <summary>The search runs on a worker thread; the dialogs belong to the window's thread.</summary>
        private Task<T> Ask<T>(Func<Task<T>> ask)
        {
            var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            vm._host.Post(async () =>
            {
                try { done.SetResult(await ask()); }
                catch (Exception ex) { done.SetException(ex); }
            });
            return done.Task;
        }
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
