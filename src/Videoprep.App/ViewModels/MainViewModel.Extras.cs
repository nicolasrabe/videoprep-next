using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Videoprep.App.Services;
using Videoprep.Core.Engine;
using Videoprep.Core.Formats;
using Videoprep.Core.Import;
using Videoprep.Core.Models;
using Videoprep.Core.Sources;
using Videoprep.Core.Text;

namespace Videoprep.App.ViewModels;

/// <summary>Round 2 of 0.3.0, part 1: the clipboard (F5) and the list of sources (F7).</summary>
public sealed partial class MainViewModel
{
    // ---------------- F5: clipboard ----------------

    private ClipboardWatcher _clipboard = null!;

    /// <summary>More than 20 links copied at once wait here until the user answers (never asked with the window out of sight).</summary>
    private List<string> _pendingClipboard = [];

    [ObservableProperty] private bool _clipboardWatch;

    /// <summary>Test hook: runs one check of the clipboard.</summary>
    public Task PollClipboardAsync() => _clipboard.PollOnceAsync();

    private void InitClipboard()
    {
        _clipboard = new ClipboardWatcher(_ui, _svc.Delay, OnClipboardLinks, _svc.ClipboardTimer);
        if (ClipboardWatch) _clipboard.Start();
    }

    partial void OnClipboardWatchChanged(bool value)
    {
        if (_loadingSettings) return;
        Settings.ClipboardWatch = value;
        SaveSettings();
        if (value)
        {
            _clipboard.Start();
        }
        else
        {
            _clipboard.Stop();
            _pendingClipboard = [];
            RefreshMini();
        }
    }

    /// <summary>Everything videoprep puts on the clipboard goes through here, so the watcher does not take it for a copy of the user.</summary>
    private async Task CopyToClipboardAsync(string text)
    {
        _clipboard.NoteOwnCopy(text);
        await _ui.SetClipboardText(text);
    }

    private bool InQueue(string url)
    {
        var key = LinkNormalizer.CompareKey(url);
        return Items.Any(i => LinkNormalizer.CompareKey(i.Url) == key);
    }

    private void OnClipboardLinks(IReadOnlyList<string> urls)
    {
        var fresh = urls.Where(u => !InQueue(u)).ToList();
        if (fresh.Count == 0) return;
        if (fresh.Count > ClipboardLinks.AskAbove)
        {
            _pendingClipboard = fresh;
            StatusText = L.Format("mini_pending", fresh.Count);
            RefreshMini();
            // Ask now only when the big window is in front; otherwise it waits (no window pops up over what the user is doing).
            if (_ui.IsMainActive) _ = ResolvePendingClipboardAsync();
            return;
        }
        AddClipboardLinks(fresh);
    }

    private void AddClipboardLinks(IReadOnlyList<string> urls)
    {
        PushUndo();
        // Whole videos, with what the Download tab has chosen now (format, resolution, names).
        var prefix = SafeName.Make(SeriesText.Trim().Length > 0 ? SeriesText.Trim() : "Video", "Video");
        foreach (var u in urls)
        {
            Items.Add(new QueueItem
            {
                Id = _nextItemId++, Url = u, Format = SelectedFormatKey, Cap = SelectedCap,
                Naming = NameSeries ? NamingMode.Series : NamingMode.Original, Prefix = prefix,
                ConstantRate = ConstantRate && FormatCatalog.Get(SelectedFormatKey).Kind == MediaKind.Video,
            });
            _svc.Preview.Request(u, null);   // the title, like a pasted link
        }
        SaveQueue();
        RebuildQueueRows();
        _svc.Log.Write($"Clipboard: {urls.Count} link(s) added to the queue.");   // how many, never what
        ShowNotice(urls.Count == 1 ? L["clip_added_one"] : L.Format("clip_added_many", urls.Count));
    }

    /// <summary>The big window is in front again (or was just restored): the question about a big copy can be asked.</summary>
    public async Task ResolvePendingClipboardAsync()
    {
        if (_pendingClipboard.Count == 0 || IsMiniActive || !_ui.IsMainActive) return;
        var pending = _pendingClipboard;
        _pendingClipboard = [];
        StatusText = IsRunning ? StatusText : L["st_ready"];
        RefreshMini();
        if (!await _ui.AskYesNo(L["clip_ask_title"], L.Format("clip_ask_many", pending.Count))) return;
        AddClipboardLinks(pending.Where(u => !InQueue(u)).ToList());
    }

    /// <summary>The big window came to the front.</summary>
    public void OnMainActivated() => _ = ResolvePendingClipboardAsync();

    // ---------------- F7: list of sources ----------------

    private sealed record SourceEntry(SourceRow Row, string Folder);

    /// <summary>One line per output of this session by job key; the list written after a batch and the manual export use them.</summary>
    private readonly Dictionary<string, SourceEntry> _sources = [];
    private List<string> _lastBatchKeys = [];

    /// <summary>Windows' regional list separator decides ";" or "," in the CSV (a test can set another language).</summary>
    public CultureInfo ListCulture { get; set; } = CultureInfo.CurrentCulture;

    private void RememberSources(IReadOnlyList<Job> jobs, BatchResult result, string folder)
    {
        _lastBatchKeys = [];
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        foreach (var job in jobs)
        {
            if (!result.Statuses.TryGetValue(job.Key, out var status)) continue;   // never reached (the batch was stopped)
            var item = Items.FirstOrDefault(i => i.Id == job.GroupId);
            if (item is null) continue;
            var meta = result.Metas?.GetValueOrDefault(job.GroupId);
            var preview = _svc.Preview.Get(item.VideoKey);
            var title = meta?.Title is { Length: > 0 } t ? t : TitleOf(item) ?? "";
            var channel = meta?.Channel is { Length: > 0 } c ? c : preview?.Author is { Length: > 0 } a ? a : _svc.Info.Get(item.VideoKey)?.Channel ?? "";
            var file = result.Files.GetValueOrDefault(job.Key, "");
            var dir = JobEngine.FolderFor(folder, job);
            var row = new SourceRow(title, channel, meta?.Url is { Length: > 0 } u ? u : item.Url, file,
                                    file.Length > 0 ? Path.Combine(dir, file) : "", FormatLabel(item), now, RowStatusLabel(status));
            _sources[job.Key] = new SourceEntry(row, dir);
            _lastBatchKeys.Add(job.Key);
        }
    }

    private string RowStatusLabel(JobStatus s) => L["rs_" + s];

    private string[] SourceHeaders() =>
        [L["src_col_title"], L["src_col_channel"], L["src_col_link"], L["src_col_file"], L["src_col_path"], L["src_col_format"], L["src_col_date"], L["src_col_status"]];

    /// <summary>"Guardar una lista de fuentes al terminar cada tanda": one list per folder the batch wrote to.</summary>
    private void WriteSourceListAfterBatch()
    {
        if (!Settings.SourceList || _lastBatchKeys.Count == 0) return;
        var saved = new List<string>();
        try
        {
            foreach (var group in _lastBatchKeys.Select(k => _sources[k]).GroupBy(e => e.Folder))
            {
                var path = SourceList.Write(group.Key, DateTime.Now, Settings.SourceListFormat, group.Select(e => e.Row).ToList(), SourceHeaders(), ListCulture);
                saved.Add(Path.GetFileName(path));
                _svc.Log.Write("Sources list saved: " + path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _svc.Log.Write("WARN  could not save the sources list: " + ex.Message);
            StatusText += "  ·  " + L["msg_sources_failed"];
            return;
        }
        if (saved.Count > 0) StatusText += "  ·  " + L.Format("msg_sources_saved", string.Join(", ", saved));
    }

    /// <summary>"Exportar lista de fuentes...": the finished rows that are selected, or every finished row when none is.</summary>
    [RelayCommand]
    private async Task ExportSources()
    {
        var keys = (_selectedRows.Count > 0 ? _selectedRows.Select(r => r.Key) : QueueRows.Select(r => r.Key))
            .Where(k => StatusOf(k) == JobStatus.OK && _sources.ContainsKey(k)).ToList();
        if (keys.Count == 0)
        {
            await _ui.ShowMessage(L["msg_sources_none"]);
            return;
        }
        var format = Settings.SourceListFormat;
        var path = await _ui.PickSaveFileAs(SourceList.FileName(DateTime.Now, format), "CSV / TXT", ["*.csv", "*.txt"]);
        if (path is null) return;
        var asTxt = path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
        try
        {
            SourceList.WriteTo(path, asTxt ? "txt" : "csv", keys.Select(k => _sources[k].Row).ToList(), SourceHeaders(), ListCulture);
            _svc.Log.Write("Sources list exported: " + path);
            StatusText = L.Format("msg_sources_saved", Path.GetFileName(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _svc.Log.Write("WARN  could not export the sources list: " + ex.Message);
            await _ui.ShowMessage(L["msg_sources_failed"], warning: true);
        }
    }

    // ---------------- settings of this round ----------------

    [ObservableProperty] private bool _sourceListOn;
    [ObservableProperty] private int _sourceListFormatIndex;

    partial void OnSourceListOnChanged(bool value)
    {
        if (_loadingSettings) return;
        Settings.SourceList = value;
        SaveSettings();
    }

    partial void OnSourceListFormatIndexChanged(int value)
    {
        if (_loadingSettings || value < 0) return;
        Settings.SourceListFormat = value == 1 ? "txt" : "csv";
        SaveSettings();
    }

    public IReadOnlyList<string> SourceListFormats => [L["src_csv"], L["src_txt"]];

    private void InitExtrasSettings()
    {
        MiniOnTop = Settings.MiniOnTop;
        ClipboardWatch = Settings.ClipboardWatch;
        SourceListOn = Settings.SourceList;
        SourceListFormatIndex = Settings.SourceListFormat == "txt" ? 1 : 0;
    }
}
