using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Miniprep.App.Logic;
using Miniprep.App.Services;
using Videoprep.Core.Preview;
using Videoprep.Core.Text;

namespace Miniprep.App.ViewModels;

/// <summary>One row of the Cola tab.</summary>
public sealed partial class QueueRowViewModel(QueueEntry entry) : ObservableObject
{
    public int Id { get; } = entry.Id;

    [ObservableProperty] private QueueEntry _entry = entry;
    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private int _progress;
    [ObservableProperty] private Bitmap? _thumb;
    /// <summary>The line where a dragged row would land is drawn above this row.</summary>
    [ObservableProperty] private bool _dropAbove;
    [ObservableProperty] private bool _dragging;

    public Loc L => Loc.Instance;

    public string Title => Entry.FileName.Length > 0 ? Entry.FileName : Entry.Item.Url;
    public string Meta => Display.RowMeta(Entry, L.Language);
    public string StatusText => Display.StatusText(Entry, Progress, L.Language);
    public bool IsDownloading => Entry.Status == EntryStatus.Downloading;
    public bool IsDone => Entry.Status == EntryStatus.Done;
    /// <summary>Green status text: downloading or finished.</summary>
    public bool IsGreen => Entry.Status is EntryStatus.Downloading or EntryStatus.Done;
    public bool IsError => Entry.Status == EntryStatus.Error;
    public bool CanDrag => Entry.Status == EntryStatus.Queued;
    public string Description => Entry.Item.Description;
    public bool HasDescription => Entry.Item.Description.Length > 0;
    public string ErrorText => Entry.Error;

    partial void OnEntryChanged(QueueEntry value) => RefreshAll();
    partial void OnProgressChanged(int value) => OnPropertyChanged(nameof(StatusText));

    public void RefreshAll()
    {
        foreach (var n in new[] { nameof(Title), nameof(Meta), nameof(StatusText), nameof(IsDownloading), nameof(IsDone), nameof(IsGreen), nameof(IsError), nameof(CanDrag),
                                  nameof(Description), nameof(HasDescription), nameof(ErrorText) })
            OnPropertyChanged(n);
    }
}

/// <summary>
/// The Cola tab: the list, the toolbar (start / stop, remove finished), the one destination folder and the right-click menu of the rows.
/// The queue lives on disk; what a row misses (title, picture, description) is completed in the background, one link at a time.
/// </summary>
public sealed partial class QueueViewModel : ObservableObject
{
    private readonly MiniServices _svc;
    private readonly MiniSettings _settings;
    private readonly IMiniUi _ui;
    private readonly IDownloader _downloader;
    private readonly ToolsGate _tools;
    private readonly RunGate _gate;
    private readonly Action<Action> _post;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Action<string> _toast;
    private readonly HashSet<int> _attempted = [];
    private CancellationTokenSource? _run;
    private bool _filling;

    public QueueViewModel(MiniServices svc, MiniSettings settings, MiniQueue queue, IMiniUi ui, IDownloader downloader, ToolsGate tools, RunGate gate,
                          Action<string> toast, Action<Action>? post = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _svc = svc; _settings = settings; Queue = queue; _ui = ui; _downloader = downloader; _tools = tools; _gate = gate; _toast = toast;
        _post = post ?? (a => Avalonia.Threading.Dispatcher.UIThread.Post(a));
        _delay = delay ?? Task.Delay;
        Folder = settings.EffectiveFolder();
        queue.Changed += () => _post(Rebuild);
        Rebuild();
    }

    public MiniQueue Queue { get; }
    public Loc L => Loc.Instance;
    public ObservableCollection<QueueRowViewModel> Rows { get; } = [];

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _folder = "";
    [ObservableProperty] private string _counter = "";
    [ObservableProperty] private bool _isEmpty = true;

    public string StartText => IsRunning ? L["mp_stop_queue"] : L["mp_start"];
    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(StartText));

    /// <summary>Redraws the list from the queue, keeping the row objects (open state, progress, picture) of the rows that stay.</summary>
    public void Rebuild()
    {
        var old = Rows.ToDictionary(r => r.Id);
        var fresh = new List<QueueRowViewModel>();
        foreach (var e in Queue.Entries)
        {
            if (old.TryGetValue(e.Id, out var row)) { row.Entry = e; }
            else row = new QueueRowViewModel(e);
            row.Thumb ??= ThumbFor(e);
            fresh.Add(row);
        }
        for (var i = 0; i < fresh.Count; i++)
        {
            if (i < Rows.Count && ReferenceEquals(Rows[i], fresh[i])) continue;
            var at = Rows.IndexOf(fresh[i]);
            if (at >= 0) Rows.Move(at, i); else Rows.Insert(i, fresh[i]);
        }
        while (Rows.Count > fresh.Count) Rows.RemoveAt(Rows.Count - 1);
        IsEmpty = Rows.Count == 0;
        Counter = Display.Counter(Queue.QueuedCount, Queue.DoneCount, Rows.Count, L.Language);
        if (!_gate.Busy) StartFill();
    }

    private Bitmap? ThumbFor(QueueEntry e)
    {
        var bytes = _svc.Info.Get(e.Item.VideoKey)?.Thumbnail ?? _svc.Thumbs.TryGet(e.Item.VideoKey);
        if (bytes is not { Length: > 0 }) return null;
        try { return new Bitmap(new MemoryStream(bytes)); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException) { return null; }
    }

    // ---------------- toolbar ----------------

    [RelayCommand]
    private async Task ToggleRun()
    {
        if (IsRunning) { _run?.Cancel(); return; }
        await RunAsync();
    }

    [RelayCommand]
    private void ClearDone() => Queue.RemoveDone();

    [RelayCommand]
    private async Task ChangeFolder()
    {
        var picked = await _ui.PickFolder(Folder);
        if (string.IsNullOrWhiteSpace(picked)) return;
        _settings.Folder = picked;
        Folder = picked;
        try { _settings.Save(_svc.Paths.SettingsFile); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _svc.Log.Write("WARN  settings could not be saved: " + ex.Message); }
    }

    /// <summary>Runs the queue from the top, one video at a time, every file named like its title (or the name the person chose).</summary>
    public async Task RunAsync()
    {
        if (_gate.Busy) { await _ui.ShowMessage(L["mp_busy"]); return; }
        foreach (var failed in Queue.Entries.Where(e => e.Status == EntryStatus.Error).ToList()) Queue.SetStatus(failed.Id, EntryStatus.Queued);
        if (Queue.NextQueued() is null) return;
        _gate.Busy = true;
        IsRunning = true;
        _run = new CancellationTokenSource();
        var ct = _run.Token;
        try
        {
            while (!ct.IsCancellationRequested && Queue.NextQueued() is { } e)
            {
                Queue.SetStatus(e.Id, EntryStatus.Downloading);
                var row = Rows.FirstOrDefault(r => r.Id == e.Id);
                if (row is not null) row.Progress = 0;
                var folder = _settings.EffectiveFolder();
                DownloadResult result;
                try
                {
                    Directory.CreateDirectory(folder);
                    var progress = new SyncProgress(p => _post(() => { if (row is not null) row.Progress = p.Percent; }));
                    result = await _downloader.RunAsync(new DownloadRequest(e.Item, folder, BrowserCookies: false), progress, ct);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    result = new DownloadResult(false, false, "", ex.Message);
                }
                if (result.Cancelled) { Queue.SetStatus(e.Id, EntryStatus.Queued); break; }
                Queue.SetStatus(e.Id, result.Ok ? EntryStatus.Done : EntryStatus.Error, result.Reason, result.File);
            }
        }
        finally
        {
            _run.Dispose();
            _run = null;
            _gate.Busy = false;
            IsRunning = false;
            Rebuild();
        }
    }

    // ---------------- rows ----------------

    /// <summary>Clic on a row: its description opens; only one row is open at a time.</summary>
    public void ToggleOpen(QueueRowViewModel row)
    {
        var open = !row.IsOpen;
        foreach (var r in Rows) r.IsOpen = false;
        row.IsOpen = open;
    }

    /// <summary>The ✕ button.</summary>
    public void Remove(QueueRowViewModel row) => Queue.Remove(row.Id);

    /// <summary>Drag from ⋮⋮: the row lands before the row at <paramref name="insertBefore"/> (Rows.Count = at the end). Only "En cola" rows move.</summary>
    public bool MoveRow(QueueRowViewModel row, int insertBefore) => Queue.Move(row.Id, insertBefore);

    /// <summary>What the right-click menu of a row does (<see cref="RowMenu"/> commands).</summary>
    public async Task RunMenuCommand(QueueRowViewModel row, string command)
    {
        var id = row.Id;
        var entry = Queue.Get(id);
        if (entry is null) return;
        switch (command)
        {
            case "rename":
                var name = await _ui.AskText(L["mp_rename_title"], L["mp_rename_title"], entry.FileName);
                if (name is not null) Queue.Rename(id, name);
                break;
            case "fmt:mp4" or "fmt:mp3":
                Queue.SetFormat(id, command[4..]);
                break;
            case "open":
                _ui.OpenUrl(entry.Item.Url);
                break;
            case "copy":
                await _ui.SetClipboardText(entry.Item.Url);
                _toast(L["mp_copied"]);
                break;
            case "remove":
                Queue.Remove(id);
                break;
            case "thumb":
                await SaveThumbnailAsync(entry);
                break;
            default:
                if (command.StartsWith("q:", StringComparison.Ordinal) && Qualities.FromCommand(command[2..]) is { } q) Queue.SetQuality(id, q);
                break;
        }
    }

    private async Task SaveThumbnailAsync(QueueEntry entry)
    {
        var bytes = _svc.Info.Get(entry.Item.VideoKey)?.Thumbnail ?? _svc.Thumbs.TryGet(entry.Item.VideoKey);
        if (bytes is not { Length: > 0 }) { await _ui.ShowMessage(L["mp_thumb_none"]); return; }
        var path = await _ui.PickSaveThumbnail(Names.Suggested(entry.FileName));
        if (path is null) return;
        var paths = _tools.Paths;
        try
        {
            await ThumbnailExporter.SaveAsync(bytes, path, ThumbnailExporter.FormatOf(path), paths?.Ffmpeg, _svc.Runner, _svc.Log);
            _toast(L["mp_thumb_saved"]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await _ui.ShowMessage(ex.Message);
        }
    }

    // ---------------- completing data in the background ----------------

    public static bool NeedsData(QueueEntry e) =>
        e.Status != EntryStatus.Done && (e.Item.Title.Length == 0 || e.Item.Description.Length == 0);

    /// <summary>Waits for the background completion to finish (for tests).</summary>
    public Task FillTask { get; private set; } = Task.CompletedTask;

    private void StartFill()
    {
        if (_filling) return;
        if (!Queue.Entries.Any(e => NeedsData(e) && !_attempted.Contains(e.Id))) return;
        _filling = true;
        FillTask = Task.Run(FillAsync);
    }

    /// <summary>One link at a time, gently: waits while Prep reads a link or a download runs, never asks twice about the same row.</summary>
    private async Task FillAsync()
    {
        try
        {
            while (true)
            {
                while (_gate.InfoBusy || _gate.Busy) await _delay(TimeSpan.FromMilliseconds(500), CancellationToken.None).ConfigureAwait(false);
                var next = Queue.Entries.FirstOrDefault(e => NeedsData(e) && !_attempted.Contains(e.Id));
                if (next is null) break;
                _attempted.Add(next.Id);
                var paths = await _tools.GetAsync().ConfigureAwait(false);
                if (paths is null) break;
                var info = await WaitForInfoAsync(next.Item.Url, new InfoRequest(paths, _svc.Cookies(false))).ConfigureAwait(false);
                if (info is { State: InfoState.Ready })
                {
                    var id = next.Id;
                    _post(() => { Queue.FillData(id, info); if (Rows.FirstOrDefault(r => r.Id == id) is { } r) r.Thumb ??= ThumbFor(Queue.Get(id)!); });
                }
                await _delay(TimeSpan.FromSeconds(1), CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            _filling = false;
        }
    }

    private async Task<VideoInfo?> WaitForInfoAsync(string url, InfoRequest req)
    {
        var key = UrlParser.LinkKey(url);
        var done = new TaskCompletionSource<VideoInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Check(string k)
        {
            if (k == key && _svc.Info.Get(key) is { State: not InfoState.Loading } i) done.TrySetResult(i);
        }
        _svc.Info.Changed += Check;
        try
        {
            var first = _svc.Info.Request(url, req);
            if (first.State != InfoState.Loading) return first;
            var finished = await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromSeconds(60))).ConfigureAwait(false);
            return finished == done.Task ? done.Task.Result : null;
        }
        finally
        {
            _svc.Info.Changed -= Check;
        }
    }
}
