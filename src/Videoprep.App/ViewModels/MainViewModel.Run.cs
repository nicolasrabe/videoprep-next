using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Videoprep.Core.Engine;
using Videoprep.Core.Models;
using Videoprep.Core.Storage;
using Videoprep.Core.Tools;

namespace Videoprep.App.ViewModels;

/// <summary>Running the queue: tools check/setup, series numbering, the engine, Stop, Retry (port of Start-Batch and friends).</summary>
public sealed partial class MainViewModel
{
    private ToolPaths _tools = new(null, null, null, null);
    private CancellationTokenSource? _runCts;
    private IReadOnlyList<Job> _lastJobs = [];
    private IReadOnlyDictionary<string, JobStatus> _lastStatuses = new Dictionary<string, JobStatus>();
    /// <summary>Destination of the run in progress (for the exact path of each finished file).</summary>
    private string? _runFolder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(ResolutionEnabled), nameof(CustomEnabled), nameof(SeriesEnabled), nameof(CanRetry))]
    private bool _isRunning;

    public bool IsIdle => !IsRunning;

    partial void OnIsRunningChanged(bool value)
    {
        NotifySelection();
        OnPropertyChanged(nameof(CanUndo));
        RefreshMini();
    }

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _progressIndeterminate;

    public bool CanRetry => !IsRunning && _lastStatuses.Values.Any(s => s == JobStatus.ERROR);

    private void RefreshTools()
    {
        _tools = _svc.Tools.Resolve();
        ToolsStatus = ToolLines(L["tool_found"], L["tool_missing"]);
    }

    /// <summary>One line per tool; the Settings tab shows it translated, the log gets it in English.</summary>
    private string ToolLines(string found, string missing) => string.Join(Environment.NewLine, new[]
    {
        ("yt-dlp", _tools.YtDlp), ("FFmpeg", _tools.Ffmpeg), ("FFprobe", _tools.Ffprobe), ("Deno", _tools.Deno),
    }.Select(t => t.Item2 is null ? $"{t.Item1}: {missing}" : $"{t.Item1}: {found}  ({t.Item2})"));

    private IReadOnlyList<CookieSource> CookieChain() => CookieStrategy.Chain(Settings, _svc.InstalledBrowsers());

    [RelayCommand]
    private Task Download() => StartBatchAsync();

    /// <summary>Port of Start-Batch.</summary>
    public async Task StartBatchAsync()
    {
        if (IsRunning) return;
        var folder = Folder.Trim('"', ' ');
        if (folder.Length == 0)
        {
            await _ui.ShowMessage(L["msg_nofolder_sel"], warning: true);
            return;
        }
        // A link still being prepared goes to the queue first.
        if (HasDraft && !SendDraft(interactive: false)) return;
        if (Items.Count == 0)
        {
            await _ui.ShowMessage(L["msg_empty"]);
            return;
        }

        RefreshTools();
        var missing = _tools.Missing();
        if (missing.Count > 0)
        {
            var nl = Environment.NewLine + Environment.NewLine;
            if (!await _ui.AskYesNo(L["msg_setup_title"], string.Format(L.Culture, L["msg_setup"], string.Join(", ", missing), nl))) return;
            if (!await InstallToolsAsync(missing)) return;
        }

        _svc.Preview.CancelDetails();
        try { Directory.CreateDirectory(folder); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            await _ui.ShowMessage(string.Format(L.Culture, L["msg_cantcreate"], Environment.NewLine + folder), warning: true);
            return;
        }

        // Rows already finished ("Listo") are not downloaded again; "Volver a descargar" puts them back.
        bool Done(string key) => _rowStatus.TryGetValue(key, out var st) && st == JobStatus.OK;
        var todo = Items.Where(it => Enumerable.Range(0, Math.Max(1, it.Clips.Count)).Any(i => !Done($"{it.Id}|{i}"))).ToList();
        if (todo.Count == 0)
        {
            await _ui.ShowMessage(L["msg_all_done"]);
            return;
        }

        // Series numbering continues after the files already in each folder (asks first; never overwrites).
        var next = new Dictionary<string, int>();
        foreach (var (sub, prefix) in JobPlanner.SeriesTargets(todo))
        {
            var dir = JobEngine.FolderFor(folder, sub);
            var existing = Directory.Exists(dir) ? Directory.EnumerateFiles(dir).Select(f => Path.GetFileName(f)).ToList() : [];
            var max = FileNames.HighestSeriesNumber(prefix, existing);
            if (max > 0)
            {
                var text = string.Format(L.Culture, L["msg_exists"], prefix, max, Environment.NewLine, max + 1);
                if (!await _ui.AskYesNo(L["msg_exists_title"], text)) return;
            }
            next[JobPlanner.SeriesKey(new QueueItem { Url = "", Prefix = prefix, SubFolder = sub })] = max + 1;
        }

        var jobs = JobPlanner.WithoutDone(JobPlanner.Build(todo, next), Done);
        foreach (var key in _rowStatus.Keys.Where(k => !Done(k)).ToList())
        {
            _rowStatus.Remove(key);
            _rowFile.Remove(key);
            _rowPath.Remove(key);
        }
        await RunJobsAsync(jobs, folder);
    }

    [RelayCommand]
    private async Task RetryFailed()
    {
        if (IsRunning) return;
        var failed = JobEngine.RetryJobs(_lastJobs, _lastStatuses);
        if (failed.Count == 0) return;
        RefreshTools();
        if (_tools.Missing().Count > 0)
        {
            await _ui.ShowMessage(L["msg_tools_missing"], warning: true);
            return;
        }
        _svc.Preview.CancelDetails();
        foreach (var j in failed) _rowStatus[j.Key] = JobStatus.Queued;
        await RunJobsAsync(failed, Folder.Trim('"', ' '));
    }

    [RelayCommand]
    private void Stop()
    {
        if (_runCts is null) return;
        _runCts.Cancel();
        StatusText = L["st_stopping"];
    }

    private async Task RunJobsAsync(IReadOnlyList<Job> jobs, string folder)
    {
        // Only one check/download run at a time: the scheduled "videoprep --check" may be running (SCRAPING_SPEC 2).
        using var runLock = RunLock.TryAcquire(_svc.Paths.RunLockFile);
        if (runLock is null)
        {
            await _ui.ShowMessage(L["msg_busy"], warning: true);
            return;
        }
        SaveSettings();
        ResetMiniRun();
        IsRunning = true;
        _runFolder = folder;
        RebuildQueueRows();
        Progress = 0;
        _runCts = new CancellationTokenSource();
        var engine = new JobEngine(_svc.Runner, _svc.Log);
        engine.Event += e => _post(() => OnEngineEvent(e));
        var ctx = new EngineContext
        {
            Tools = _tools,
            Options = OptionsViewModel.ToDownloadOptions(Settings),
            Folder = folder,
            Cookies = CookieChain(),
            ArchiveFile = _svc.Paths.ArchiveFile,
            SessionCookieFile = _svc.Paths.SessionCookieFile,
        };
        BatchResult result;
        try
        {
            result = await Task.Run(() => engine.RunAsync(jobs, ctx, _runCts.Token));
        }
        finally
        {
            _runCts.Dispose();
            _runCts = null;
        }

        // Port of Complete-Batch.
        _lastJobs = jobs;
        _lastStatuses = result.Statuses;
        var byKey = jobs.ToDictionary(j => j.Key);
        foreach (var (key, status) in result.Statuses) _rowStatus[key] = status;
        foreach (var (key, file) in result.Files)
        {
            _rowFile[key] = file;
            if (byKey.TryGetValue(key, out var job)) _rowPath[key] = Path.Combine(JobEngine.FolderFor(folder, job), file);
        }
        if (engine.PreferredBrowser is { } browser)
        {
            Settings.PreferredBrowser = browser;
            SaveSettings();
        }
        IsRunning = false;
        ProgressIndeterminate = false;
        Progress = 0;
        RebuildQueueRows();
        StatusText = result.Cancelled
            ? L["st_stopped"]
            : string.Format(L.Culture, L["st_done"], result.Ok, result.Warn, result.Fail);
        MiniRunFinished(result);
        RememberSources(jobs, result, folder);
        if (!result.Cancelled)
        {
            WriteSourceListAfterBatch();
            if (Settings.Sound) _ui.PlayDoneSound();
            if (Settings.OpenDone && Directory.Exists(folder)) _ui.OpenFolder(folder);
        }
    }

    private void OnEngineEvent(EngineEvent e)
    {
        switch (e)
        {
            case StatusEvent s:
                _rowStatus[s.Job.Key] = s.Status;
                if (s.File.Length > 0)
                {
                    _rowFile[s.Job.Key] = s.File;
                    if (_runFolder is not null) _rowPath[s.Job.Key] = Path.Combine(JobEngine.FolderFor(_runFolder, s.Job), s.File);
                }
                var row = QueueRows.FirstOrDefault(r => r.Key == s.Job.Key);
                if (row is not null)
                {
                    row.Status = RowStatusText(s.Job.Key);
                    if (s.File.Length > 0) row.File = s.File;
                    var locked = IsLockedStatus(s.Status);
                    foreach (var r in QueueRows.Where(r => r.ItemId == row.ItemId)) r.IsLocked |= locked;
                }
                break;
            case StepEvent { Step: EngineStep.Downloading } st:
                MiniJobStarted(st.Job, st.Number, st.Total);
                ProgressIndeterminate = false;
                Progress = 0;
                StatusText = string.Format(L.Culture, L["st_starting"], st.Number, st.Total);
                break;
            case StepEvent { Step: EngineStep.Processing or EngineStep.Verifying } st:
                MiniJobStarted(st.Job, st.Number, st.Total);
                ProgressIndeterminate = true;
                StatusText = string.Format(L.Culture, L["st_cutting"], st.Number, st.Total);
                break;
            case ProgressEvent p:
                ProgressIndeterminate = false;
                Progress = p.Progress.Percent;
                StatusText = string.Format(L.Culture, L["st_progress"], p.Number, p.Total,
                    p.Progress.Percent.ToString("0.0", L.Culture), p.Progress.Speed, p.Progress.Eta);
                break;
        }
    }

    /// <summary>First-time setup (port of Start-Setup): downloads the missing tools with progress in the status bar.</summary>
    private async Task<bool> InstallToolsAsync(IReadOnlyList<string> missing)
    {
        IsRunning = true;
        ProgressIndeterminate = false;
        StatusText = L["st_setup"];
        _svc.Log.Write("");
        _svc.Log.Write("=== First-time setup: downloading tools ===");
        var ok = false;
        try
        {
            var progress = new Progress<ToolProgress>(p =>
            {
                if (p.TotalBytes is > 0) Progress = p.Bytes * 100.0 / p.TotalBytes.Value;
                var amount = p.TotalBytes is > 0 ? $"{p.Bytes * 100 / p.TotalBytes.Value} %" : $"{p.Bytes / 1048576} MB";
                StatusText = string.Format(L.Culture, L["st_tools_progress"], p.Tool, amount);
            });
            await _svc.Tools.InstallAsync(missing, progress, CancellationToken.None);
            RefreshTools();
            ok = _tools.Missing().Count == 0;
            _svc.Log.Write(ok ? "=== Setup complete ===" : "Setup did not finish. Still missing: " + string.Join(", ", _tools.Missing()));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException or TaskCanceledException)
        {
            _svc.Log.Write("SETUP ERROR: " + ex.Message);
            _svc.Log.Write("Check your internet connection and press Download to try again.");
        }
        finally
        {
            IsRunning = false;
            Progress = 0;
        }
        StatusText = ok ? L["st_ready"] : L["st_setupfail"];
        return ok;
    }
}
