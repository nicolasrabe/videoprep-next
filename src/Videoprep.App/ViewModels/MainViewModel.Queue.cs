using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Videoprep.Core.Formats;
using Videoprep.Core.Models;

namespace Videoprep.App.ViewModels;

/// <summary>One row of the queue table: a whole video or one clip of it (port of Update-QueueGrid rows).</summary>
public sealed partial class QueueRow : ObservableObject
{
    public required string Key { get; init; }
    public required int ItemId { get; init; }
    public required int Num { get; init; }
    [ObservableProperty] private string _name = "";
    public required string Format { get; init; }
    public required string Clip { get; init; }
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _file = "";
}

/// <summary>Queue tab.</summary>
public sealed partial class MainViewModel
{
    public ObservableCollection<QueueRow> QueueRows { get; } = [];

    /// <summary>Row status and output file by job key "itemId|clipIndex" (legacy $RowStatus / $RowFile).</summary>
    private readonly Dictionary<string, JobStatus> _rowStatus = [];
    private readonly Dictionary<string, string> _rowFile = [];

    /// <summary>Rows selected in the table (the view keeps this in sync).</summary>
    private IReadOnlyList<QueueRow> _selectedRows = [];

    public void SetQueueSelection(IEnumerable<QueueRow> rows) => _selectedRows = rows.ToList();

    /// <summary>Raised after Up/Down so the view can select the moved item again.</summary>
    public event Action<int>? QueueItemMoved;

    private void RebuildQueueRows()
    {
        QueueRows.Clear();
        var n = 0;
        foreach (var it in Items)
        {
            var count = Math.Max(1, it.Clips.Count);
            for (var i = 0; i < count; i++)
            {
                var key = $"{it.Id}|{i}";
                QueueRows.Add(new QueueRow
                {
                    Key = key, ItemId = it.Id, Num = ++n, Name = TitleFor(it), Format = FormatCatalog.Get(it.Format).Label,
                    Clip = it.Clips.Count > 0 ? ClipText(it.Clips[i]) : L["clip_whole"],
                    Status = L["rs_" + (_rowStatus.TryGetValue(key, out var s) ? s : JobStatus.Queued)],
                    File = _rowFile.GetValueOrDefault(key, ""),
                });
            }
        }
        OnPropertyChanged(nameof(CanRetry));
    }

    private string TitleFor(QueueItem it) =>
        _svc.Preview.Get(it.VideoKey) is { Title.Length: > 0 } p ? p.Title : it.Url;

    private string ClipText(ClipRange c) =>
        $"{(c.Start.Length > 0 ? c.Start : L["clip_start"])} -> {(c.End.Length > 0 ? c.End : L["clip_end"])}";

    /// <summary>Port of Update-GridTitles: titles arrive later than the rows.</summary>
    private void RefreshQueueTitles()
    {
        foreach (var row in QueueRows)
        {
            var it = Items.FirstOrDefault(i => i.Id == row.ItemId);
            if (it is not null) row.Name = TitleFor(it);
        }
    }

    private List<int> SelectedItemIds() => _selectedRows.Select(r => r.ItemId).Distinct().ToList();

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
        for (var i = Items.Count - 1; i >= 0; i--)
            if (ids.Contains(Items[i].Id)) Items.RemoveAt(i);
        _selectedRows = [];
        SaveQueue();
        RebuildQueueRows();
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
        if (HasDraft && !SendDraft(interactive: false)) return;

        _draftUrl = it.Url;
        _draftKey = it.VideoKey;
        _editId = it.Id;
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
    private async Task LoadTxt()
    {
        var path = await _ui.PickTextFile();
        if (path is not null) await ImportLinksFromFileAsync(path);
    }

    [RelayCommand]
    private void ClearQueue()
    {
        Items.Clear();
        SaveQueue();
        RebuildQueueRows();
    }
}
