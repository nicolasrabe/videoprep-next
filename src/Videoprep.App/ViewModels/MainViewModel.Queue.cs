using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Videoprep.Core.Engine;
using Videoprep.Core.Formats;
using Videoprep.Core.Import;
using Videoprep.Core.Models;
using Videoprep.Core.Preview;
using Videoprep.Core.Storage;
using Videoprep.Core.Text;

namespace Videoprep.App.ViewModels;

/// <summary>One row of the queue table: a whole video or one clip of it (port of Update-QueueGrid rows).</summary>
public sealed partial class QueueRow : ObservableObject
{
    public required string Key { get; init; }
    public required int ItemId { get; init; }
    public required int Num { get; init; }
    public required string Url { get; init; }
    [ObservableProperty] private string _name = "";
    public required string Format { get; init; }
    public required string Clip { get; init; }
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _file = "";
    /// <summary>"Descargando" or "Listo": shown with a lock, cannot be edited (it can still be opened and removed).</summary>
    [ObservableProperty] private bool _isLocked;
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private QueueRowCard? _card;
    /// <summary>The text being typed while renaming in the row.</summary>
    [ObservableProperty] private string _editName = "";
    /// <summary>The planned file name without extension (null = the video title, not known yet).</summary>
    public string? PlannedName { get; set; }
    public string VideoKey => UrlParser.LinkKey(Url);
}

public enum CardState { Loading, Ready, Failed }

/// <summary>What an expanded queue row shows (F3).</summary>
public sealed partial class QueueRowCard : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoading), nameof(IsReady), nameof(IsFailed))]
    private CardState _state;

    public bool IsLoading => State == CardState.Loading;
    public bool IsReady => State == CardState.Ready;
    public bool IsFailed => State == CardState.Failed;

    [ObservableProperty] private Bitmap? _thumbnail;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDuration))]
    private string _duration = "";

    public bool HasDuration => Duration.Length > 0;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _meta = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _will = "";
    [ObservableProperty] private string _failText = "";
    public required string Link { get; init; }
    public required IRelayCommand OpenLinkCommand { get; init; }
    public required IRelayCommand CopyLinkCommand { get; init; }
    public required IRelayCommand RetryCommand { get; init; }
    internal string ThumbKey { get; set; } = "";
}

/// <summary>Queue tab: the table, editing (F2), expandable rows (F3), loading files with links (F1).</summary>
public sealed partial class MainViewModel
{
    public ObservableCollection<QueueRow> QueueRows { get; } = [];

    /// <summary>Row status, output file name and full path by job key "itemId|clipIndex" (legacy $RowStatus / $RowFile).</summary>
    private readonly Dictionary<string, JobStatus> _rowStatus = [];
    private readonly Dictionary<string, string> _rowFile = [];
    private readonly Dictionary<string, string> _rowPath = [];

    /// <summary>Rows selected in the table (the view keeps this in sync).</summary>
    private IReadOnlyList<QueueRow> _selectedRows = [];

    /// <summary>Raised after Up/Down so the view can select the moved item again.</summary>
    public event Action<int>? QueueItemMoved;

    /// <summary>Raised when a row opened or closed, so the table shows or hides its details.</summary>
    public event Action? ExpandedChanged;

    // ---------------- rows ----------------

    private static bool IsLockedStatus(JobStatus s) => s is JobStatus.Downloading or JobStatus.Cutting or JobStatus.OK;

    private JobStatus StatusOf(string key) => _rowStatus.TryGetValue(key, out var s) ? s : JobStatus.Queued;

    /// <summary>An item is locked when any of its rows is downloading or done.</summary>
    public bool IsItemLocked(QueueItem it)
    {
        var count = Math.Max(1, it.Clips.Count);
        for (var i = 0; i < count; i++) if (IsLockedStatus(StatusOf($"{it.Id}|{i}"))) return true;
        return false;
    }

    private string RowStatusText(string key)
    {
        var s = StatusOf(key);
        return L["rs_" + s] + (IsLockedStatus(s) ? " 🔒" : "");
    }

    private void RebuildQueueRows()
    {
        QueueRows.Clear();
        var n = 0;
        foreach (var it in Items)
        {
            var count = Math.Max(1, it.Clips.Count);
            var locked = IsItemLocked(it);
            for (var i = 0; i < count; i++)
            {
                var key = $"{it.Id}|{i}";
                QueueRows.Add(new QueueRow
                {
                    Key = key, ItemId = it.Id, Num = ++n, Url = it.Url, Format = FormatText(it),
                    Clip = it.Clips.Count > 0 ? ClipText(it.Clips[i]) : L["clip_whole"],
                    Status = RowStatusText(key), File = _rowFile.GetValueOrDefault(key, ""), IsLocked = locked,
                });
            }
        }
        UpdateRowNames();
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(CanUndo));
    }

    private string FormatText(QueueItem it)
    {
        var f = FormatCatalog.Get(it.Format);
        var label = f.Kind == MediaKind.Audio ? f.Label : $"{f.Label} · {FormatCatalog.ResolutionLabel(it.Cap)}";
        return it.SubFolder.Length > 0 ? $"{label} · {it.SubFolder}{Path.DirectorySeparatorChar}" : label;
    }

    /// <summary>The video title when known: from the preview (YouTube) or from an opened row's card (other sites).</summary>
    private string? TitleOf(QueueItem it)
    {
        if (_svc.Preview.Get(it.VideoKey) is { Title.Length: > 0 } p) return p.Title;
        if (_svc.Info.Get(it.VideoKey) is { Title.Length: > 0 } i) return i.Title;
        return null;
    }

    private string ClipText(ClipRange c) =>
        $"{(c.Start.Length > 0 ? c.Start : L["clip_start"])} -> {(c.End.Length > 0 ? c.End : L["clip_end"])}";

    /// <summary>
    /// The Name column, like the files will be called: the name typed, the series number (continuing after the files
    /// already in the folder), or the title; the link while the title is not known.
    /// </summary>
    private void UpdateRowNames()
    {
        var names = QueueEdits.OutputNames(Items, TitleOf, FirstSeriesNumbers());
        for (var i = 0; i < QueueRows.Count && i < names.Count; i++)
        {
            QueueRows[i].PlannedName = names[i];
            QueueRows[i].Name = names[i] ?? QueueRows[i].Url;
        }
    }

    /// <summary>Port of Update-GridTitles: titles arrive later than the rows.</summary>
    private void RefreshQueueTitles()
    {
        UpdateRowNames();
        foreach (var row in QueueRows.Where(r => r.IsExpanded)) RefreshCard(row);
    }

    /// <summary>First free series number per series key, looking at the files already in each folder (local only).</summary>
    private Dictionary<string, int> FirstSeriesNumbers()
    {
        var next = new Dictionary<string, int>();
        foreach (var it in Items.Where(i => i.Naming == NamingMode.Series))
            next.TryAdd(JobPlanner.SeriesKey(it), FirstSeriesNumber(it));
        return next;
    }

    private int FirstSeriesNumber(QueueItem it)
    {
        var dest = Folder.Trim('"', ' ');
        if (dest.Length == 0) return 1;
        var dir = JobEngine.FolderFor(dest, it.SubFolder);
        try
        {
            if (!Directory.Exists(dir)) return 1;
            return FileNames.HighestSeriesNumber(it.Prefix, Directory.EnumerateFiles(dir).Select(f => Path.GetFileName(f))) + 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return 1;
        }
    }

    // ---------------- selection and expandable rows (F3) ----------------

    private List<int> SelectedItemIds() => _selectedRows.Select(r => r.ItemId).Distinct().ToList();

    private List<QueueItem> SelectedItems() => SelectedItemIds().Select(id => Items.FirstOrDefault(i => i.Id == id)).OfType<QueueItem>().ToList();

    /// <summary>
    /// The table's selection changed. One row selected = that row opens (the previous one closes); several = none is open.
    /// </summary>
    public void SetQueueSelection(IEnumerable<QueueRow> rows)
    {
        _selectedRows = rows.ToList();
        if (_selectedRows.Count == 1) Expand(_selectedRows[0]);
        else Collapse();
        NotifySelection();
    }

    /// <summary>A click on the row that is already the only one selected: open it, or close it if open.</summary>
    public void ToggleRow(QueueRow row)
    {
        if (row.IsExpanded) Collapse();
        else Expand(row);
    }

    public QueueRow? ExpandedRow => QueueRows.FirstOrDefault(r => r.IsExpanded);

    private void Collapse()
    {
        foreach (var r in QueueRows.Where(r => r.IsExpanded)) r.IsExpanded = false;
        ExpandedChanged?.Invoke();
    }

    private void Expand(QueueRow row)
    {
        foreach (var r in QueueRows) if (r != row && r.IsExpanded) r.IsExpanded = false;
        row.Card ??= NewCard(row);
        row.IsExpanded = true;
        ExpandedChanged?.Invoke();
        // Only the row that is opened asks for data, one at a time (VideoInfoService).
        if (_tools.YtDlp is null) RefreshTools();
        _svc.Info.Request(row.Url, new InfoRequest(_tools, CookieChain()));
        RefreshCard(row);
    }

    private QueueRowCard NewCard(QueueRow row) => new()
    {
        Link = row.Url,
        OpenLinkCommand = new RelayCommand(() => OpenLink(row.Url)),
        CopyLinkCommand = new AsyncRelayCommand(async () =>
        {
            await _ui.SetClipboardText(row.Url);
            StatusText = L["st_copied"];
        }),
        RetryCommand = new RelayCommand(() =>
        {
            _svc.Info.Retry(row.Url, new InfoRequest(_tools, CookieChain()));
            RefreshCard(row);
        }),
    };

    /// <summary>Only web links are opened, in the default browser.</summary>
    private void OpenLink(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp))
            _ui.OpenUrl(u.AbsoluteUri);
    }

    private void OnInfoChanged(string key)
    {
        var changedName = false;
        foreach (var row in QueueRows.Where(r => r.VideoKey == key))
        {
            if (row.IsExpanded) RefreshCard(row);
            changedName |= row.PlannedName is null;
        }
        if (changedName) UpdateRowNames();
    }

    private void RefreshCard(QueueRow row)
    {
        var card = row.Card;
        if (card is null) return;
        var info = _svc.Info.Get(row.VideoKey);
        var preview = _svc.Preview.Get(row.VideoKey);
        card.State = info?.State switch
        {
            InfoState.Ready => CardState.Ready,
            InfoState.Failed => CardState.Failed,
            _ => CardState.Loading,
        };
        card.Title = info?.Title is { Length: > 0 } t ? t : preview?.Title ?? "";
        var bits = new List<string>();
        var channel = info?.Channel is { Length: > 0 } c ? c : preview?.Author ?? "";
        if (channel.Length > 0) bits.Add(channel);
        if (info?.UploadDate is { Length: 8 } d && DateTime.TryParseExact(d, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            bits.Add(date.ToString("d MMM yyyy", L.Culture));
        if (info?.Views is { } v) bits.Add(L.Format("det_views", v.ToString("N0", L.Culture)));
        card.Meta = string.Join("  ·  ", bits);
        card.Duration = info?.Duration ?? "";
        card.Description = info?.Description ?? "";
        card.FailText = info?.State == InfoState.Failed ? L.Format("det_fail", ReasonText(info.FailReason)) : "";
        var bytes = info?.Thumbnail ?? preview?.Thumbnail;
        var thumbKey = bytes is null ? "" : row.VideoKey + bytes.Length;
        if (thumbKey != card.ThumbKey)
        {
            card.ThumbKey = thumbKey;
            card.Thumbnail = bytes is null ? null : DecodeBitmap(bytes);
        }
        var it = Items.FirstOrDefault(i => i.Id == row.ItemId);
        if (it is not null)
        {
            var ext = FormatCatalog.Get(it.Format).Extension;
            var name = row.PlannedName ?? (card.Title.Length > 0 ? SafeName.Make(card.Title) : "(" + L["det_name_title"] + ")");
            var file = (it.SubFolder.Length > 0 ? it.SubFolder + Path.DirectorySeparatorChar : "") + name + ext;
            card.Will = L.Format("det_will", $"{FormatText(it)}  ·  {file}");
        }
    }

    private static Bitmap? DecodeBitmap(byte[] bytes)
    {
        try { return new Bitmap(new MemoryStream(bytes)); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or NotSupportedException)
        {
            return null;
        }
    }

    // ---------------- what the selection allows (menu) ----------------

    public bool HasSelection => _selectedRows.Count > 0;
    public bool CanEditOne => IsIdle && SelectedItemIds().Count == 1 && !SelectedItems().Any(IsItemLocked);
    public bool CanEditSelection => IsIdle && SelectedItems().Any(i => !IsItemLocked(i));
    public bool CanChangeSelection => IsIdle && HasSelection;
    public bool CanRequeue => IsIdle && _selectedRows.Any(r => StatusOf(r.Key) == JobStatus.OK);
    public bool CanShowInFolder => _selectedRows.Count == 1 && _rowPath.ContainsKey(_selectedRows[0].Key) && StatusOf(_selectedRows[0].Key) == JobStatus.OK;

    private void NotifySelection()
    {
        foreach (var p in new[] { nameof(HasSelection), nameof(CanEditOne), nameof(CanEditSelection), nameof(CanChangeSelection), nameof(CanRequeue), nameof(CanShowInFolder) })
            OnPropertyChanged(p);
    }

    // ---------------- undo ----------------

    private readonly List<(List<QueueItem> Items, int NextId)> _undo = [];
    private const int UndoLevels = 20;

    public bool CanUndo => _undo.Count > 0 && IsIdle;

    /// <summary>Called before every queue edit: keeps a copy to go back to (items are immutable records).</summary>
    private void PushUndo()
    {
        _undo.Add(([.. Items], _nextItemId));
        if (_undo.Count > UndoLevels) _undo.RemoveAt(0);
        OnPropertyChanged(nameof(CanUndo));
    }

    [RelayCommand]
    public void Undo()
    {
        if (_undo.Count == 0 || IsRunning) return;
        var (items, _) = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        Items.Clear();
        foreach (var it in items) Items.Add(it);
        _selectedRows = [];
        SaveQueue();
        RebuildQueueRows();
        StatusText = L["st_undone"];
    }

    // ---------------- edits (F2) ----------------

    /// <summary>F2 or double-click: false (with a message) when the row is locked.</summary>
    public bool CanRename(QueueRow row)
    {
        if (IsRunning) return false;
        if (!row.IsLocked) return true;
        _ = _ui.ShowMessage(L["msg_locked"]);
        return false;
    }

    /// <summary>Text the rename box starts with: the name typed before, or the shown name.</summary>
    public string RenameStartText(QueueRow row)
    {
        var it = Items.FirstOrDefault(i => i.Id == row.ItemId);
        return it?.Naming == NamingMode.Custom ? it.Custom : row.PlannedName ?? "";
    }

    /// <summary>Enter after renaming in the row: the item gets that name (empty = back to the original title).</summary>
    public void Rename(QueueRow row, string name)
    {
        var idx = Items.ToList().FindIndex(i => i.Id == row.ItemId);
        if (idx < 0 || IsRunning || IsItemLocked(Items[idx])) return;
        var renamed = QueueEdits.Rename(Items[idx], name);
        if (renamed == Items[idx]) return;
        PushUndo();
        Items[idx] = renamed;
        SaveQueue();
        UpdateRowNames();
        foreach (var r in QueueRows.Where(r => r.IsExpanded)) RefreshCard(r);
    }

    [RelayCommand]
    private async Task BatchEdit()
    {
        var selected = SelectedItems();
        var editable = selected.Where(i => !IsItemLocked(i)).ToList();
        if (editable.Count == 0)
        {
            await _ui.ShowMessage(selected.Count == 0 ? L["msg_sel_one"] : L["msg_locked"]);
            return;
        }
        var vm = new BatchEditViewModel(editable, selected.Count - editable.Count, TitleOf, FirstSeriesNumber)
        {
            Prefix = SeriesText.Trim().Length > 0 ? SeriesText.Trim() : "Video",
        };
        if (!await _ui.EditBatch(vm)) return;
        var changed = vm.Result();
        PushUndo();
        for (var k = 0; k < editable.Count; k++)
        {
            var idx = Items.ToList().FindIndex(i => i.Id == editable[k].Id);
            if (idx >= 0) Items[idx] = changed[k];
        }
        SaveQueue();
        RebuildQueueRows();
    }

    [RelayCommand]
    private void Duplicate()
    {
        var selected = SelectedItems();
        if (selected.Count == 0 || IsRunning) return;
        PushUndo();
        foreach (var it in selected)
        {
            var idx = Items.ToList().FindIndex(i => i.Id == it.Id);
            Items.Insert(idx + 1, it with { Id = _nextItemId++ });
        }
        SaveQueue();
        RebuildQueueRows();
    }

    /// <summary>"Volver a descargar": finished rows go back to "En cola" (the next Download runs them again).</summary>
    [RelayCommand]
    private void Requeue()
    {
        foreach (var r in _selectedRows.Where(r => StatusOf(r.Key) == JobStatus.OK))
        {
            _rowStatus.Remove(r.Key);
            _rowFile.Remove(r.Key);
            _rowPath.Remove(r.Key);
        }
        RebuildQueueRows();
        NotifySelection();
    }

    /// <summary>"Mostrar en carpeta": the file explorer opens with the finished file selected.</summary>
    [RelayCommand]
    private async Task ShowInFolder()
    {
        if (_selectedRows.Count != 1 || !_rowPath.TryGetValue(_selectedRows[0].Key, out var path)) return;
        if (File.Exists(path)) _ui.ShowInFolder(path);
        else await _ui.ShowMessage(L["msg_nofile"]);
    }

    [RelayCommand]
    private void RemoveSelected()
    {
        var ids = SelectedItemIds();
        if (ids.Count == 0)
        {
            _ = _ui.ShowMessage(L["msg_sel_one"]);
            return;
        }
        RemoveItems(ids);
    }

    /// <summary>
    /// Delete key in the queue (backlog 0.1.2 #1): removes the selected rows' items without asking. Returns the index of the
    /// row to select afterwards (the one that took the place of the first removed row), or -1 when the queue is empty.
    /// </summary>
    public int DeleteKey()
    {
        var ids = SelectedItemIds();
        if (ids.Count == 0 || IsRunning) return -1;
        var firstIndex = QueueRows.ToList().FindIndex(r => ids.Contains(r.ItemId));
        RemoveItems(ids);
        return QueueRows.Count == 0 ? -1 : Math.Min(firstIndex, QueueRows.Count - 1);
    }

    private void RemoveItems(List<int> ids)
    {
        PushUndo();
        for (var i = Items.Count - 1; i >= 0; i--)
            if (ids.Contains(Items[i].Id)) Items.RemoveAt(i);
        _selectedRows = [];
        SaveQueue();
        RebuildQueueRows();
        NotifySelection();
    }

    [RelayCommand]
    private void MoveUp() => MoveSelected(-1);

    [RelayCommand]
    private void MoveDown() => MoveSelected(1);

    private void MoveSelected(int dir)
    {
        var ids = SelectedItemIds();
        if (ids.Count != 1) return;
        var idx = Items.ToList().FindIndex(i => i.Id == ids[0]);
        var j = idx + dir;
        if (idx < 0 || j < 0 || j >= Items.Count) return;
        PushUndo();
        (Items[idx], Items[j]) = (Items[j], Items[idx]);
        SaveQueue();
        RebuildQueueRows();
        QueueItemMoved?.Invoke(ids[0]);
    }

    /// <summary>Port of Edit-SelectedItem: the item goes back to the Download tab as the draft.</summary>
    [RelayCommand]
    private void EditSelected()
    {
        var ids = SelectedItemIds();
        if (ids.Count != 1)
        {
            _ = _ui.ShowMessage(L["msg_sel_one"]);
            return;
        }
        var it = Items.FirstOrDefault(i => i.Id == ids[0]);
        if (it is null) return;
        if (IsItemLocked(it))
        {
            _ = _ui.ShowMessage(L["msg_locked"]);
            return;
        }
        if (HasDraft && !SendDraft(interactive: false)) return;

        _draftUrl = it.Url;
        _draftKey = it.VideoKey;
        _editId = it.Id;
        _editSubFolder = it.SubFolder;
        OnPropertyChanged(nameof(HasDraft));
        Clips.Clear();
        IsAudio = it.Kind == MediaKind.Audio;
        RebuildFormatLists();
        SelectedFormat = FormatChoices.FirstOrDefault(f => f.Key == it.Format) ?? SelectedFormat;
        SelectedResolution = FormatCatalog.ResolutionLabel(it.Cap);
        switch (it.Naming)
        {
            case NamingMode.Custom: NameCustom = true; CustomText = it.Custom; break;
            case NamingMode.Series: NameSeries = true; SeriesText = it.Prefix; break;
            default: NameOriginal = true; break;
        }
        foreach (var c in it.Clips) AddClipRow(c.Start, c.End);
        UpdateHint();
        UpdateExample();
        SelectedTab = TabDownload;
        RequestPreview(it.Url);
    }

    [RelayCommand]
    private void ClearQueue()
    {
        if (Items.Count == 0) return;
        PushUndo();
        StopTitleLookup();
        Items.Clear();
        SaveQueue();
        RebuildQueueRows();
    }

    // ---------------- loading a file with links (F1) ----------------

    [RelayCommand]
    private async Task LoadTxt()
    {
        var path = await _ui.PickLinkFile();
        if (path is not null) await ImportLinksFromFileAsync(path);
    }

    /// <summary>"Cargar archivo..." and dropping a file: read it, show the review window, add what was checked.</summary>
    public async Task ImportLinksFromFileAsync(string path)
    {
        var result = await Task.Run(() => LinkFileReader.Read(path));
        var problem = result.Status switch
        {
            ReadStatus.NotYet => L.Format("msg_imp_notyet", result.Detail),
            ReadStatus.UnknownType => L["msg_imp_type"],
            ReadStatus.TooBig => L["msg_imp_big"],
            ReadStatus.Damaged => L["msg_imp_damaged"],
            ReadStatus.CannotOpen => L["msg_imp_cantopen"],
            _ => null,
        };
        if (problem is not null)
        {
            _svc.Log.Write($"could not read links from {Path.GetFileName(path)}: {result.Status} {result.Detail}");
            await _ui.ShowMessage(problem, warning: result.Status != ReadStatus.NotYet && result.Status != ReadStatus.UnknownType);
            return;
        }
        var candidates = ImportPlanner.Plan(result.Links, Items.Select(i => i.Url));
        if (candidates.Count == 0)
        {
            await _ui.ShowMessage(L["msg_nolinks"]);
            return;
        }
        var review = new ImportReviewViewModel(path, candidates, SelectedFormatKey, SelectedCap);
        if (!await _ui.ReviewImport(review)) return;
        var chosen = review.Chosen();
        if (chosen.Count == 0) return;

        PushUndo();
        var fmt = review.SelectedFormat;
        var prefix = SafeName.Make(SeriesText.Trim().Length > 0 ? SeriesText.Trim() : "Video", "Video");
        foreach (var (url, name) in chosen)
        {
            Items.Add(new QueueItem
            {
                Id = _nextItemId++, Url = url, Format = fmt.Key, Cap = fmt.Cap,
                Naming = name.Length > 0 ? NamingMode.Custom : NameSeries ? NamingMode.Series : NamingMode.Original,
                Custom = name, Prefix = prefix, SubFolder = review.UseSubfolder ? review.SubfolderName : "",
            });
        }
        SaveQueue();
        RebuildQueueRows();
        _svc.Log.Write($"Added {chosen.Count} link(s) from {Path.GetFileName(path)} to the queue ({result.Links.Count} found).");
        // Titles of YouTube links without a typed name, in the background (oEmbed only, one at a time, at most 100).
        StartTitleLookup(chosen.Where(c => c.Name.Length == 0).Select(c => c.Url).Where(u => UrlParser.IsYoutubeKey(UrlParser.LinkKey(u))).Take(MaxTitleLookups));
    }

    /// <summary>Files dropped on the Queue tab are loaded; text with links goes straight to the queue.</summary>
    public async Task DropOnQueueAsync(string? text, IReadOnlyList<string> files)
    {
        foreach (var f in files) await ImportLinksFromFileAsync(f);
        if (files.Count == 0 && text is not null)
        {
            var urls = UrlParser.LinksFromText(text);
            foreach (var u in urls) AddDefaultItem(u);
        }
    }

    // ---------------- background titles (oEmbed, one at a time) ----------------

    public const int MaxTitleLookups = 100;
    /// <summary>Pause between two title requests.</summary>
    public static readonly TimeSpan TitlePause = TimeSpan.FromSeconds(1.5);

    private readonly Queue<string> _titleQueue = new();
    private CancellationTokenSource? _titleCts;
    private int _titleDone, _titleTotal;

    /// <summary>Running while titles are being looked up (tests wait for it).</summary>
    public Task TitleLookup { get; private set; } = Task.CompletedTask;

    private void StartTitleLookup(IEnumerable<string> urls)
    {
        var added = 0;
        foreach (var u in urls)
        {
            _titleQueue.Enqueue(u);
            added++;
        }
        if (added == 0) return;
        _titleTotal += added;
        if (_titleCts is not null) return;   // the running lookup takes the new ones too
        _titleCts = new CancellationTokenSource();
        TitleLookup = RunTitleLookupAsync(_titleCts.Token);
    }

    private async Task RunTitleLookupAsync(CancellationToken ct)
    {
        var asked = false;
        try
        {
            while (_titleQueue.Count > 0 && !ct.IsCancellationRequested)
            {
                ShowTitleProgress();
                var url = _titleQueue.Dequeue();
                if (asked) await _svc.Delay(TitlePause, ct);
                asked = await _svc.Preview.LookupTitleAsync(url, ct);
                _titleDone++;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Queue emptied or the app is closing.
        }
        finally
        {
            _titleCts?.Dispose();
            _titleCts = null;
            _titleQueue.Clear();
            _titleDone = _titleTotal = 0;
            if (!IsRunning) StatusText = L["st_ready"];
        }
    }

    private void ShowTitleProgress()
    {
        if (!IsRunning) StatusText = L.Format("st_titles", _titleDone + 1, _titleTotal);
    }

    private void StopTitleLookup()
    {
        _titleCts?.Cancel();
        _titleQueue.Clear();
    }
}
