using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Miniprep.App.Logic;
using Miniprep.App.ViewModels;

namespace Miniprep.App.Views;

/// <summary>
/// The Cola tab. Everything the rows do lives here, as the list is one control: click opens the description, ✕ removes, dragging ⋮⋮ reorders
/// the "En cola" rows, and right-click opens the menu of the row (never on the empty space).
/// </summary>
public partial class QueueView : UserControl
{
    private QueueRowViewModel? _dragging;
    private int _dropIndex = -1;

    public QueueView()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Bubble);
        AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Bubble);
        AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Bubble);
        AddHandler(PointerCaptureLostEvent, (_, _) => EndDrag(apply: false), RoutingStrategies.Bubble);
        AddHandler(ContextRequestedEvent, OnContextRequested, RoutingStrategies.Tunnel);
        AddHandler(Button.ClickEvent, OnButtonClick, RoutingStrategies.Bubble);
    }

    private QueueViewModel? Vm => DataContext as QueueViewModel;

    private static QueueRowViewModel? RowOf(object? source) =>
        (source as Visual)?.GetSelfAndVisualAncestors().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("qrow"))?.DataContext as QueueRowViewModel;

    private static bool Inside(object? source, string cssClass) =>
        (source as Visual)?.GetSelfAndVisualAncestors().OfType<Control>().Any(c => c.Classes.Contains(cssClass)) == true;

    private void OnButtonClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is Control c && c.GetSelfAndVisualAncestors().OfType<Button>().FirstOrDefault(b => b.Classes.Contains("remove")) is { } b
            && RowOf(b) is { } row)
        {
            e.Handled = true;
            Vm?.Remove(row);
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm is not { } vm || RowOf(e.Source) is not { } row) return;
        var props = e.GetCurrentPoint(this).Properties;
        if (!props.IsLeftButtonPressed || Inside(e.Source, "remove")) return;
        if (Inside(e.Source, "grip"))
        {
            if (!row.CanDrag) return;
            _dragging = row;
            _dropIndex = -1;
            row.Dragging = true;
            e.Pointer.Capture(List);
            e.Handled = true;
            return;
        }
        vm.ToggleOpen(row);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragging is null || Vm is not { } vm) return;
        var y = e.GetPosition(List).Y;
        var target = vm.Rows.Count;
        var rows = RowBorders();
        for (var i = 0; i < rows.Count; i++)
        {
            var top = rows[i].TranslatePoint(new Point(0, 0), List)?.Y ?? 0;
            if (y < top + rows[i].Bounds.Height / 2) { target = i; break; }
        }
        _dropIndex = target;
        for (var i = 0; i < vm.Rows.Count; i++) vm.Rows[i].DropAbove = i == target && vm.Rows[i].CanDrag && !ReferenceEquals(vm.Rows[i], _dragging);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragging is null) return;
        e.Pointer.Capture(null);
        EndDrag(apply: true);
    }

    private void EndDrag(bool apply)
    {
        if (_dragging is not { } row) return;
        var target = _dropIndex;
        _dragging = null;
        _dropIndex = -1;
        row.Dragging = false;
        if (Vm is not { } vm) return;
        foreach (var r in vm.Rows) r.DropAbove = false;
        if (apply && target >= 0) vm.MoveRow(row, target);
    }

    private List<Border> RowBorders() =>
        List.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("qrow")).ToList();

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        // Only a row has a menu. On the empty space nothing opens.
        e.Handled = true;
        if (Vm is not { } vm || RowOf(e.Source) is not { } row) return;
        var host = (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("qrow"));
        if (host is null) return;
        var menu = new ContextMenu();
        foreach (var line in RowMenu.For(row.Entry))
        {
            switch (line.Kind)
            {
                case MenuKind.Separator:
                    menu.Items.Add(new Separator());
                    break;
                case MenuKind.Header:
                    var header = new MenuItem { Header = Loc.Instance[line.Label], IsEnabled = false };
                    header.Classes.Add("header");
                    menu.Items.Add(header);
                    break;
                default:
                    var item = new MenuItem { Header = Loc.Instance[line.Label], IsEnabled = line.Enabled };
                    if (line.Checked) item.Icon = new TextBlock { Text = "✓", Foreground = (Avalonia.Media.IBrush?)this.FindResource("Ok") };
                    if (line.Danger) item.Classes.Add("danger");
                    var command = line.Command;
                    item.Click += async (_, _) => await vm.RunMenuCommand(row, command);
                    menu.Items.Add(item);
                    break;
            }
        }
        menu.Open(host);
    }
}
