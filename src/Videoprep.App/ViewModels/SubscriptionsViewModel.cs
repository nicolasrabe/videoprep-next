using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Videoprep.App.Services;
using Videoprep.Core.Engine;
using Videoprep.Core.Formats;
using Videoprep.Core.Profiles;
using Videoprep.Core.Scraping;
using Videoprep.Core.Storage;

namespace Videoprep.App.ViewModels;

/// <summary>One row of the Suscripciones table.</summary>
public sealed partial class SubscriptionRow(Subscription sub, Action<SubscriptionRow> enabledChanged, Func<string?, string?>? profileName = null) : ObservableObject
{
    public Subscription Sub { get; set; } = sub;
    [ObservableProperty] private bool _enabled = sub.Enabled;
    partial void OnEnabledChanged(bool value) => enabledChanged(this);

    private static Loc L => Loc.Instance;

    public string Name => Sub.Name;

    public string What => SourceUrl.ChannelBase(Sub.Url) is null
        ? L["sc_what_link"]
        : string.Join(" + ", Sub.SourceTabs().Select(t => L["sc_tabs_" + SourceUrl.TabPath(t)]));

    public string FiltersText
    {
        get
        {
            var f = Sub.Filters;
            var parts = new List<string>();
            if (f.From is not null || f.To is not null)
                parts.Add($"{(f.From is null ? "" : L["sc_from"].ToLower(L.Culture) + " " + f.From)} {(f.To is null ? "" : L["sc_to"] + " " + f.To)}".Trim());
            if (f.MinSec is not null || f.MaxSec is not null)
                parts.Add($"{Filters.FormatDuration(f.MinSec)}-{Filters.FormatDuration(f.MaxSec)}");
            if (f.Include.Count > 0) parts.Add("+" + string.Join(", +", f.Include));
            if (f.Exclude.Count > 0) parts.Add("-" + string.Join(", -", f.Exclude));
            if (f.MaxCount is { } m) parts.Add(L["sc_max"].ToLower(L.Culture) + " " + m);
            if (f.Order == "oldest") parts.Add(L["sc_order_old"].ToLower(L.Culture));
            return parts.Count > 0 ? string.Join(", ", parts) : L["sc_nofilters"];
        }
    }

    /// <summary>The profile it follows ("Premiere 1080p"), or "(valores propios)" (also when its profile was deleted).</summary>
    public string ProfileText => profileName?.Invoke(Sub.Profile.ProfileId) ?? L["prof_own"];

    public string FormatText => FormatCatalog.Get(Sub.Profile.Format).Label.Split(" - ")[0] + " " + FormatCatalog.ResolutionLabel(Sub.Profile.Cap);

    public string LastText => DateTimeOffset.TryParse(Sub.LastCheck, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t)
        ? t.ToLocalTime().ToString("dd/MM HH:mm", CultureInfo.InvariantCulture)
        : L["sc_never"];

    public string NewText => Sub.LastNew?.ToString(CultureInfo.InvariantCulture) ?? "-";

    public string? Error => Sub.LastError;
    public bool HasError => Sub.LastError is not null;

    public void Refresh(Subscription s)
    {
        Sub = s;
        Enabled = s.Enabled;
        OnPropertyChanged(string.Empty);
    }
}

/// <summary>
/// Suscripciones ("subscribe and forget", SCRAPING_SPEC 2): the table, Agregar/Editar/Quitar, Revisar ahora, on start,
/// every N hours while open, and the Windows scheduled task. Data lives in subscriptions.json.
/// </summary>
public sealed partial class SubscriptionsViewModel : ObservableObject
{
    private readonly MainViewModel _host;
    private SubscriptionFile _file = new();
    private bool _loading;
    private CancellationTokenSource? _cts;
    private DispatcherTimer? _timer;

    public SubscriptionsViewModel(MainViewModel host)
    {
        _host = host;
        Load();
        Loc.Instance.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != "Item[]") return;
            foreach (var r in Rows) r.Refresh(r.Sub);
        };
    }

    private static Loc L => Loc.Instance;

    public ObservableCollection<SubscriptionRow> Rows { get; } = [];
    [ObservableProperty] private SubscriptionRow? _selected;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsIdle))] private bool _isChecking;
    public bool IsIdle => !IsChecking;
    [ObservableProperty] private string _statusLine = "";

    [ObservableProperty] private bool _checkOnStart = true;
    [ObservableProperty] private bool _checkEvery;
    [ObservableProperty] private decimal _everyHours = 6;
    [ObservableProperty] private bool _taskInstalled;

    partial void OnCheckOnStartChanged(bool value) => SaveSettings();
    partial void OnCheckEveryChanged(bool value) { SaveSettings(); RestartTimer(); }
    partial void OnEveryHoursChanged(decimal value) { SaveSettings(); RestartTimer(); }

    private int Hours => (int)Math.Clamp(EveryHours, 1, 24);

    /// <summary>Reads subscriptions.json again (after a check wrote its results).</summary>
    public void Load()
    {
        _file = SubscriptionStore.Load(_host.Paths.SubscriptionsFile, out var problem);
        if (problem is not null) _host.LogLine("WARN  " + problem);
        _loading = true;
        CheckOnStart = _file.Settings.CheckOnStart;
        CheckEvery = _file.Settings.EveryHours > 0;
        if (_file.Settings.EveryHours > 0) EveryHours = _file.Settings.EveryHours;
        TaskInstalled = _file.Settings.ScheduledTask;
        _loading = false;
        var selectedId = Selected?.Sub.Id;
        Rows.Clear();
        foreach (var s in _file.Subscriptions) Rows.Add(new SubscriptionRow(s, OnRowEnabledChanged, _host.ProfileNameOf));
        Selected = Rows.FirstOrDefault(r => r.Sub.Id == selectedId);
    }

    private void OnRowEnabledChanged(SubscriptionRow row)
    {
        if (_loading || row.Sub.Enabled == row.Enabled) return;
        row.Sub = row.Sub with { Enabled = row.Enabled };
        Save();
    }

    private void SaveSettings()
    {
        if (_loading) return;
        Save();
    }

    private void Save()
    {
        // Results a check wrote meanwhile (lastCheck...) are kept: only the user's own fields come from the rows.
        var onDisk = SubscriptionStore.Load(_host.Paths.SubscriptionsFile, out _).Subscriptions.ToDictionary(s => s.Id);
        var subs = Rows.Select(r => onDisk.TryGetValue(r.Sub.Id, out var d)
            ? r.Sub with { LastCheck = d.LastCheck, LastNew = d.LastNew, LastError = d.LastError }
            : r.Sub).ToList();
        _file = _file with
        {
            Settings = _file.Settings with
            {
                CheckOnStart = CheckOnStart, EveryHours = CheckEvery ? Hours : 0, ScheduledTask = TaskInstalled,
            },
            Subscriptions = subs,
        };
        try { SubscriptionStore.Save(_host.Paths.SubscriptionsFile, _file); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _host.LogLine("WARN  could not save the subscriptions: " + ex.Message);
        }
    }

    private SubscriptionEditViewModel NewEditor(Subscription? s) => new(s, start => _host.Ui.PickFolder(start), _host.ProfileList());

    // ---- profiles (F6): which subscriptions follow a profile, and what happens to them when it changes

    /// <summary>Names of the subscriptions that follow this profile (rows in memory, the ones the user sees).</summary>
    public IReadOnlyList<string> NamesUsing(string profileId) =>
        ProfileLinks.Using(profileId, Rows.Select(r => r.Sub)).Select(s => s.Name).ToList();

    /// <summary>The profile was edited: the subscriptions that follow it keep its new values as their own too.</summary>
    public void SyncProfile(FormatProfile profile)
    {
        var changed = false;
        foreach (var r in Rows.Where(r => r.Sub.Profile.ProfileId == profile.Id).ToList())
        {
            r.Refresh(ProfileLinks.Sync(r.Sub, profile));
            changed = true;
        }
        if (changed) Save();
    }

    /// <summary>The profile is being deleted: its subscriptions stop following it and keep the values they have.</summary>
    public void DetachProfile(string profileId)
    {
        var changed = false;
        foreach (var r in Rows.Where(r => r.Sub.Profile.ProfileId == profileId).ToList())
        {
            r.Refresh(ProfileLinks.Detach(r.Sub));
            changed = true;
        }
        if (changed) Save();
    }

    /// <summary>A profile was renamed or deleted: the table shows the current names.</summary>
    public void RefreshProfileNames()
    {
        foreach (var r in Rows) r.Refresh(r.Sub);
    }

    [RelayCommand]
    private async Task Add()
    {
        var vm = NewEditor(null);
        vm.Folder = _host.CurrentFolder;
        await AddWith(vm);
    }

    /// <summary>"Suscribirme a esto..." in Explorar: the same link, tabs, filters and format.</summary>
    public async Task AddFromExplore(ExploreViewModel ex)
    {
        var vm = NewEditor(null);
        vm.Url = ex.SourceText.Trim();
        vm.TabVideos = ex.TabVideos;
        vm.TabShorts = ex.TabShorts;
        vm.TabStreams = ex.TabStreams;
        vm.Filter.CopyFrom(ex.Filter);
        vm.Format.SelectedFormat = vm.Format.FormatChoices.FirstOrDefault(f => f.Key == ex.Format.FormatKey) ?? vm.Format.SelectedFormat;
        vm.Format.SelectedResolution = ex.Format.SelectedResolution;
        vm.Folder = _host.CurrentFolder;
        await AddWith(vm);
    }

    private async Task AddWith(SubscriptionEditViewModel vm)
    {
        if (!await _host.Ui.EditSubscription(vm) || vm.Build(out _) is not { } sub) return;
        var row = new SubscriptionRow(sub, OnRowEnabledChanged, _host.ProfileNameOf);
        Rows.Add(row);
        Selected = row;
        Save();
    }

    [RelayCommand]
    private async Task Edit()
    {
        if (Selected is not { } row) { await _host.Ui.ShowMessage(L["sc_select_sub"]); return; }
        var vm = NewEditor(row.Sub);
        if (!await _host.Ui.EditSubscription(vm) || vm.Build(out _) is not { } sub) return;
        row.Refresh(sub);
        Save();
    }

    [RelayCommand]
    private async Task Remove()
    {
        if (Selected is not { } row) { await _host.Ui.ShowMessage(L["sc_select_sub"]); return; }
        if (!await _host.Ui.AskYesNo("videoprep", L.Format("sc_remove_ask", row.Name))) return;
        Rows.Remove(row);
        Selected = null;
        Save();
        // The history stays in archives\<id>.txt on purpose: harmless, and it lets a user recover by hand.
    }

    [RelayCommand]
    private async Task CheckSelected()
    {
        if (Selected is not { } row) { await _host.Ui.ShowMessage(L["sc_select_sub"]); return; }
        await CheckAsync([row.Sub.Id], interactive: true);
    }

    [RelayCommand]
    private Task CheckAll() => CheckAsync(null, interactive: true);

    [RelayCommand]
    private void StopCheck() => _cts?.Cancel();

    /// <summary>Runs a check (button, on start, timer). Returns null when it could not start.</summary>
    public async Task<CheckResult?> CheckAsync(IReadOnlyCollection<string>? ids, bool interactive)
    {
        if (IsChecking) return null;
        if (ids is null && !Rows.Any(r => r.Enabled)) return null;
        var tools = _host.CurrentTools();
        if (tools.YtDlp is null || tools.Ffmpeg is null || tools.Ffprobe is null)
        {
            if (interactive) await _host.Ui.ShowMessage(L["msg_tools_missing"], warning: true);
            else _host.LogLine("Subscriptions: not checked, tools are missing.");
            return null;
        }
        using var runLock = RunLock.TryAcquire(_host.Paths.RunLockFile);
        if (runLock is null)
        {
            if (interactive) await _host.Ui.ShowMessage(L["msg_busy"], warning: true);
            else _host.LogLine("Subscriptions: not checked now, another download or check is running.");
            return null;
        }
        if (interactive && !_host.Settings.ScrapingLegalShown)
        {
            await _host.Ui.ShowMessage(L["sc_legal"]);
            _host.Settings.ScrapingLegalShown = true;
            _host.SaveSettings();
        }

        IsChecking = true;
        StatusLine = L["sc_checking"];
        _cts = new CancellationTokenSource();
        var runner = _host.NewCheckRunner();
        runner.SubscriptionStarted += (name, n, total) => _host.Post(() => StatusLine = L.Format("sc_checking_sub", name, n, total));
        runner.EngineEvent += e =>
        {
            if (e is StepEvent { Step: EngineStep.Downloading } st)
                _host.Post(() => StatusLine = L.Format("sc_downloading", st.Number, st.Total));
        };
        CheckResult result;
        try
        {
            var options = _host.CheckOptions(tools);
            result = await Task.Run(() => runner.RunAsync(ids, options, _cts.Token));
            await _host.FlushPosts();
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            IsChecking = false;
        }
        Load();
        StatusLine = Summary(result);
        if (result.Blocked && interactive) await _host.Ui.ShowMessage(L["sc_blocked"], warning: true);
        if (result.NewTotal > 0 && _host.Settings.Sound) _host.Ui.PlayDoneSound();
        return result;
    }

    private static string Summary(CheckResult r)
    {
        if (r.Blocked) return L["sc_blocked"];
        var parts = new List<string>();
        foreach (var s in r.Subs)
        {
            if (s.Error is not null) parts.Add(L.Format("sc_sub_failed", s.Name));
            else if (s.MarkedSeen > 0) parts.Add(L.Format("sc_marked_seen", s.Name, s.MarkedSeen));
            else if (s.Ok > 0) parts.Add(L.Format("sc_found_new", s.Ok, s.Name));
        }
        return parts.Count == 0 ? L["sc_no_new"] : L.Format("sc_check_done", string.Join(" ", parts));
    }

    /// <summary>"Revisar al abrir videoprep" (called once the window is visible).</summary>
    public async Task OnShownAsync()
    {
        RestartTimer();
        if (CheckOnStart && Rows.Any(r => r.Enabled)) await CheckAsync(null, interactive: false);
    }

    private void RestartTimer()
    {
        if (_loading) return;
        _timer?.Stop();
        _timer = null;
        if (!CheckEvery) return;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromHours(Hours) };
        _timer.Tick += async (_, _) => await CheckAsync(null, interactive: false);
        _timer.Start();
    }

    [RelayCommand]
    private async Task ConfigureTask()
    {
        if (!ScheduledTask.Supported) { await _host.Ui.ShowMessage(L["sc_task_unsupported"]); return; }
        var args = ScheduledTask.CreateArgs(_host.ExePath, Hours);
        var nl = Environment.NewLine;
        if (!await _host.Ui.AskYesNo("videoprep", L.Format("sc_task_ask", nl, ScheduledTask.Describe(args), Hours))) return;
        var (code, output) = await _host.RunScheduledTask(args);
        _host.LogLine($"schtasks create -> exit {code}: {output}");
        if (code == 0)
        {
            TaskInstalled = true;
            Save();
            await _host.Ui.ShowMessage(L["sc_task_done"]);
        }
        else await _host.Ui.ShowMessage(L.Format("sc_task_failed", output), warning: true);
    }

    [RelayCommand]
    private async Task RemoveTask()
    {
        if (!ScheduledTask.Supported) { await _host.Ui.ShowMessage(L["sc_task_unsupported"]); return; }
        var (code, output) = await _host.RunScheduledTask(ScheduledTask.DeleteArgs());
        _host.LogLine($"schtasks delete -> exit {code}: {output}");
        TaskInstalled = false;
        Save();
        await _host.Ui.ShowMessage(code == 0 ? L["sc_task_removed"] : L.Format("sc_task_failed", output), warning: code != 0);
    }
}
