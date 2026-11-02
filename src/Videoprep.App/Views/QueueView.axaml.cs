using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Videoprep.App.ViewModels;

namespace Videoprep.App.Views;

public partial class QueueView : UserControl
{
    private MainViewModel? _vm;
    /// <summary>The table only starts editing a name when we ask (F2, double-click, menu), never on a plain click.</summary>
    private bool _allowEdit;
    private QueueRow? _pressedRow;
    private bool _pressedSoleSelection;

    /// <summary>The Name column (columns get no generated field).</summary>
    private DataGridColumn NameColumn => Grid.Columns[1];

    public QueueView()
    {
        InitializeComponent();
        Grid.SelectionChanged += (_, _) => _vm?.SetQueueSelection(Grid.SelectedItems.OfType<QueueRow>());
        Grid.LoadingRow += (_, e) => e.Row.AreDetailsVisible = e.Row.DataContext is QueueRow { IsExpanded: true };
        Grid.BeginningEdit += (_, e) => { if (!_allowEdit) e.Cancel = true; };
        Grid.PreparingCellForEdit += (_, e) =>
        {
            if (e.EditingElement is TextBox box)
            {
                box.Focus();
                box.SelectAll();
            }
            else if (e.EditingElement.GetVisualDescendants().OfType<TextBox>().FirstOrDefault() is { } inner)
            {
                inner.Focus();
                inner.SelectAll();
            }
        };
        Grid.CellEditEnded += (_, e) =>
        {
            if (e.EditAction == DataGridEditAction.Commit && e.Column == NameColumn && e.Row.DataContext is QueueRow row)
                _vm?.Rename(row, row.EditName);
        };
        // Tunnel: before the table handles keys and clicks itself.
        Grid.AddHandler(KeyDownEvent, OnGridKeyDown, RoutingStrategies.Tunnel);
        Grid.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        Grid.AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        Grid.AddHandler(PointerMovedEvent, OnPointerMovedForDrag, RoutingStrategies.Tunnel, handledEventsToo: true);
        Grid.AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Escape && _dragIds is not null) { EndDrag(false); e.Handled = true; } }, RoutingStrategies.Tunnel);
        MenuRename.Click += (_, _) => { if (Grid.SelectedItem is QueueRow r) BeginRename(r); };
        AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = DragDropEffects.Copy);
        AddHandler(DragDrop.DropEvent, OnDrop);
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null)
            {
                _vm.QueueItemMoved -= SelectItem;
                _vm.ExpandedChanged -= SyncDetails;
            }
            _vm = DataContext as MainViewModel;
            if (_vm is not null)
            {
                _vm.QueueItemMoved += SelectItem;
                _vm.ExpandedChanged += SyncDetails;
            }
        };
    }

    /// <summary>Shows the details of the row the view model opened and hides the others.</summary>
    private void SyncDetails()
    {
        foreach (var r in Grid.GetVisualDescendants().OfType<DataGridRow>())
            r.AreDetailsVisible = r.DataContext is QueueRow { IsExpanded: true };
    }

    private bool IsEditing => Grid.GetVisualDescendants().OfType<TextBox>().Any(t => t.IsFocused);

    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is null) return;
        if (IsEditing)
        {
            // Enter confirms the new name and stays on the row (the table would move to the next row and open it).
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                Grid.CommitEdit();
            }
            return;
        }
        if (e.Key == Key.F2 && Grid.SelectedItem is QueueRow row && Grid.SelectedItems.Count == 1)
        {
            e.Handled = true;
            BeginRename(row);
        }
        else if (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            _vm.Undo();
        }
        else if (e.Key == Key.Delete)
        {
            // Delete key removes the selected rows (backlog 0.1.2 #1), no confirmation, and keeps a sane selection.
            e.Handled = true;
            var next = _vm.DeleteKey();
            if (next >= 0 && next < _vm.QueueRows.Count)
            {
                Grid.SelectedItem = _vm.QueueRows[next];
                Grid.ScrollIntoView(Grid.SelectedItem, null);
            }
        }
    }

    private static QueueRow? RowAt(object? source, out bool inDetails)
    {
        var v = source as Visual;
        inDetails = v?.FindAncestorOfType<DataGridDetailsPresenter>(includeSelf: true) is not null;
        return v?.FindAncestorOfType<DataGridRow>(includeSelf: true)?.DataContext as QueueRow;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _pressedRow = null;
        if (TryStartDrag(e)) return;
        var row = RowAt(e.Source, out var inDetails);
        if (row is null || inDetails) return;
        var point = e.GetCurrentPoint(Grid).Properties;
        if (point.IsRightButtonPressed)
        {
            // Right-click on a row outside the selection selects it, so the menu acts on what was clicked.
            if (!Grid.SelectedItems.Contains(row)) Grid.SelectedItem = row;
            return;
        }
        if (!point.IsLeftButtonPressed) return;
        if (e.ClickCount == 2)
        {
            Dispatcher.UIThread.Post(() => BeginRename(row));
            return;
        }
        _pressedRow = row;
        _pressedSoleSelection = e.KeyModifiers == KeyModifiers.None && Grid.SelectedItems.Count == 1 && Grid.SelectedItem == row;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragIds is not null) { EndDrag(true); return; }
        // A click on the row that was already the only one selected opens or closes it (selecting it opened it already).
        if (e.InitialPressMouseButton != MouseButton.Left || _pressedRow is null) return;
        var row = RowAt(e.Source, out _);
        if (row == _pressedRow && _pressedSoleSelection) _vm?.ToggleRow(row);
        _pressedRow = null;
    }

    private void BeginRename(QueueRow row)
    {
        if (_vm is null || !_vm.CanRename(row)) return;
        row.EditName = _vm.RenameStartText(row);
        if (Grid.SelectedItem != row) Grid.SelectedItem = row;
        Grid.ScrollIntoView(row, NameColumn);
        Grid.CurrentColumn = NameColumn;
        _allowEdit = true;
        try { Grid.BeginEdit(); }
        finally { _allowEdit = false; }
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        try
        {
            if (_vm is null) return;
            var files = (e.DataTransfer.TryGetFiles() ?? [])
                .Select(f => f.TryGetLocalPath()).OfType<string>().Where(File.Exists).ToList();
            await _vm.DropOnQueueAsync(e.DataTransfer.TryGetText(), files);
        }
        catch (Exception ex)
        {
            Program.Log.Crash(ex, "drag and drop on the queue");
        }
    }

    // ---------------- reorder by dragging the grip (RONDA 4, spec E) ----------------
    // Done with the pointer itself (not the system drag and drop, which the window already uses to receive files).

    private List<int>? _dragIds;
    private int _dropIndex;

    /// <summary>The press landed on the grip of a row that can move: the drag starts (the selection is left alone).</summary>
    private bool TryStartDrag(PointerPressedEventArgs e)
    {
        if (_vm is null || !e.GetCurrentPoint(Grid).Properties.IsLeftButtonPressed) return false;
        if (e.Source is not Visual v || v.FindAncestorOfType<Control>(includeSelf: true) is null) return false;
        if (!v.GetSelfAndVisualAncestors().OfType<Control>().Any(c => c.Classes.Contains("grip"))) return false;
        var row = RowAt(e.Source, out var inDetails);
        if (row is null || inDetails || !_vm.CanDrag(row)) return false;
        // The row is part of the selection: the whole selection travels; if not, only this row.
        var rows = Grid.SelectedItems.Contains(row) ? Grid.SelectedItems.OfType<QueueRow>().Where(_vm.CanDrag).ToList() : [row];
        _dragIds = rows.Select(r => r.ItemId).Distinct().ToList();
        _dropIndex = _vm.QueueRows.IndexOf(row);
        e.Pointer.Capture(Grid);
        e.Handled = true;
        return true;
    }

    private void OnPointerMovedForDrag(object? sender, PointerEventArgs e)
    {
        if (_dragIds is null || _vm is null) return;
        var y = e.GetPosition(Grid).Y;
        var (index, lineY) = DropTarget(y);
        _dropIndex = index;
        DropLine.IsVisible = true;
        DropLine.Margin = new Thickness(0, Math.Max(0, lineY - 1), 0, 0);
        // Near the top or bottom edge: bring the next row into view.
        var rows = _vm.QueueRows;
        if (y < 28 && index > 0) Grid.ScrollIntoView(rows[index - 1], null);
        else if (y > Grid.Bounds.Height - 20 && index < rows.Count) Grid.ScrollIntoView(rows[Math.Min(index, rows.Count - 1)], null);
    }

    /// <summary>Where a drop at height <paramref name="y"/> would land: the row index (count = at the end) and the height of the line to draw. Never above a locked row.</summary>
    internal (int Index, double LineY) DropTarget(double y)
    {
        var rows = _vm!.QueueRows;
        var visuals = Grid.GetVisualDescendants().OfType<DataGridRow>().Where(r => r.DataContext is QueueRow)
            .Select(r => (Row: r, Index: rows.IndexOf((QueueRow)r.DataContext!), Top: r.TranslatePoint(new Point(0, 0), Grid)?.Y ?? 0))
            .Where(t => t.Index >= 0).OrderBy(t => t.Index).ToList();
        var index = rows.Count;
        var lineY = visuals.Count > 0 ? visuals[^1].Top + visuals[^1].Row.Bounds.Height : 0;
        foreach (var t in visuals)
        {
            var mid = t.Top + Math.Min(t.Row.Bounds.Height, 30) / 2;
            if (y < mid) { index = t.Index; lineY = t.Top; break; }
        }
        var lastLocked = -1;
        for (var i = 0; i < rows.Count; i++) if (rows[i].IsLocked) lastLocked = i;
        if (index <= lastLocked)
        {
            index = lastLocked + 1;
            var after = visuals.FirstOrDefault(t => t.Index == lastLocked);
            if (after.Row is not null) lineY = after.Top + after.Row.Bounds.Height;
        }
        return (index, lineY);
    }

    private void EndDrag(bool drop)
    {
        var ids = _dragIds;
        _dragIds = null;
        DropLine.IsVisible = false;
        if (!drop || ids is null || _vm is null) return;
        if (_vm.MoveItemsBefore(ids, _dropIndex))
        {
            var moved = _vm.QueueRows.Where(r => ids.Contains(r.ItemId)).ToList();
            Grid.SelectedItems.Clear();
            foreach (var r in moved) Grid.SelectedItems.Add(r);
        }
    }

    private void SelectItem(int itemId)
    {
        var row = _vm?.QueueRows.FirstOrDefault(r => r.ItemId == itemId);
        if (row is null) return;
        Grid.SelectedItem = row;
        Grid.ScrollIntoView(row, null);
    }
}
