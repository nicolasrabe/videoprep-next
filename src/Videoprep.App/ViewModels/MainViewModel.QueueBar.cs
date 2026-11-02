using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Videoprep.Core.Engine;
using Videoprep.Core.Models;

namespace Videoprep.App.ViewModels;

/// <summary>
/// RONDA 4 (spec E): the bar on top of the queue (Iniciar cola, Pausar, Detener and the counter) and dragging rows to reorder.
/// "Iniciar cola" and "Detener" are the SAME commands as Descargar and Detener in Prep (<c>DownloadCommand</c>, <c>StopCommand</c>):
/// one source of truth for what is running.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>"Pausar" was pressed: the video that is downloading finishes and the batch stops before the next one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPause))]
    private bool _pauseRequested;

    public bool CanPause => IsRunning && !PauseRequested;

    /// <summary>"1 descargando · 3 en cola · 1 lista".</summary>
    public string QueueCounter
    {
        get
        {
            int working = 0, waiting = 0, done = 0;
            foreach (var r in QueueRows)
                switch (StatusOf(r.Key))
                {
                    case JobStatus.Downloading or JobStatus.Cutting: working++; break;
                    case JobStatus.OK: done++; break;
                    case JobStatus.Queued: waiting++; break;
                }
            return L.Format("qbar_counter", working, waiting, done, L[done == 1 ? "qbar_done_one" : "qbar_done_many"]);
        }
    }

    [RelayCommand]
    private void PauseQueue()
    {
        if (!IsRunning || PauseRequested) return;
        PauseRequested = true;
        StatusText = L["st_pausing"];
    }

    /// <summary>
    /// Drop of dragged rows (spec E): <paramref name="itemIds"/> go just before the row at <paramref name="beforeRowIndex"/> of the table
    /// (the row count = at the end). Only waiting items move; the order is saved in the queue file. Returns true when it changed.
    /// </summary>
    public bool MoveItemsBefore(IReadOnlyCollection<int> itemIds, int beforeRowIndex)
    {
        var beforeItem = beforeRowIndex >= 0 && beforeRowIndex < QueueRows.Count ? QueueRows[beforeRowIndex].ItemId : -1;
        var at = beforeItem < 0 ? Items.Count : Math.Max(0, Items.ToList().FindIndex(i => i.Id == beforeItem));
        var snapshot = Items.ToList();
        if (!QueueOrder.Move(snapshot, itemIds, at, IsItemLocked)) return false;
        PushUndo();
        for (var i = 0; i < snapshot.Count; i++) Items[i] = snapshot[i];
        SaveQueue();
        RebuildQueueRows();
        return true;
    }

    /// <summary>Can this row be picked up (only the ones still waiting)?</summary>
    public bool CanDrag(QueueRow row) => !row.IsLocked && !IsRunning;
}
