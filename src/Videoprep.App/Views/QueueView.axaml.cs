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

    private void SelectItem(int itemId)
    {
        var row = _vm?.QueueRows.FirstOrDefault(r => r.ItemId == itemId);
        if (row is null) return;
        Grid.SelectedItem = row;
        Grid.ScrollIntoView(row, null);
    }
}
