using Avalonia.Controls;
using Avalonia.Input;
using Videoprep.App.ViewModels;

namespace Videoprep.App.Views;

public partial class QueueView : UserControl
{
    private MainViewModel? _vm;

    public QueueView()
    {
        InitializeComponent();
        Grid.SelectionChanged += (_, _) => _vm?.SetQueueSelection(Grid.SelectedItems.OfType<QueueRow>());
        // Delete key removes the selected rows (backlog 0.1.2 #1), no confirmation, and keeps a sane selection.
        Grid.KeyDown += OnGridKeyDown;
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null) _vm.QueueItemMoved -= SelectItem;
            _vm = DataContext as MainViewModel;
            if (_vm is not null) _vm.QueueItemMoved += SelectItem;
        };
    }

    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete || _vm is null) return;
        e.Handled = true;
        var next = _vm.DeleteKey();
        if (next >= 0 && next < _vm.QueueRows.Count)
        {
            Grid.SelectedItem = _vm.QueueRows[next];
            Grid.ScrollIntoView(Grid.SelectedItem, null);
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
