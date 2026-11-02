using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Videoprep.App.ViewModels;

namespace Videoprep.App.Views;

public partial class ScrapingView : UserControl
{
    private ExploreViewModel? _explore;
    private ExploreRow? _pressedRow;
    private bool _menuOnRow;
    private bool _pressedSoleSelection;

    public ScrapingView()
    {
        InitializeComponent();
        ExploreButton.Click += (_, _) => (DataContext as ScrapingViewModel)?.ShowExploreView();
        SubsButton.Click += (_, _) => (DataContext as ScrapingViewModel)?.ShowSubscriptionsView();

        // Enter in any filter box searches; Enter in the link box loads it.
        FiltersCard.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter && e.Source is TextBox)
            {
                e.Handled = true;
                _explore?.SearchCommand.Execute(null);
            }
        }, RoutingStrategies.Tunnel);
        SourceBox.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            _explore?.LoadCommand.Execute(null);
        }, RoutingStrategies.Tunnel);

        // Same behaviour as the Queue table: one row open at a time; a single selected row opens, a click on it closes it.
        ResultsGrid.LoadingRow += (_, e) => e.Row.AreDetailsVisible = e.Row.DataContext is ExploreRow { Expanded: true };
        ResultsGrid.SelectionChanged += (_, _) =>
        {
            if (_explore is null) return;
            if (ResultsGrid.SelectedItems.Count == 1 && ResultsGrid.SelectedItem is ExploreRow row && !row.IsPlaylist) _explore.OpenRow(row);
            else _explore.CloseRows();
        };
        // Right-click on a subscription selects it and shows the subscription menu; on the empty space, the other one.
        SubsGrid.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(SubsGrid).Properties.IsRightButtonPressed || DataContext is not ScrapingViewModel vm) return;
            var row = (e.Source as Visual)?.FindAncestorOfType<DataGridRow>(includeSelf: true)?.DataContext as SubscriptionRow;
            vm.Subscriptions.MenuOnRow = row is not null;
            if (row is not null) SubsGrid.SelectedItem = row;
            else SubsGrid.SelectedItem = null;
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        ResultsGrid.ContextMenu!.Opening += (_, e) => e.Cancel = !_menuOnRow;   // only on a row
        ResultsGrid.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        ResultsGrid.AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        DataContextChanged += (_, _) =>
        {
            if (_explore is not null) _explore.ExpandedChanged -= SyncDetails;
            _explore = (DataContext as ScrapingViewModel)?.Explore;
            if (_explore is not null) _explore.ExpandedChanged += SyncDetails;
        };
    }

    /// <summary>Shows the details of the row the view model opened and hides the others.</summary>
    private void SyncDetails()
    {
        foreach (var r in ResultsGrid.GetVisualDescendants().OfType<DataGridRow>())
            r.AreDetailsVisible = r.DataContext is ExploreRow { Expanded: true };
    }

    private static ExploreRow? RowAt(object? source, out bool inDetails)
    {
        var v = source as Visual;
        inDetails = v?.FindAncestorOfType<DataGridDetailsPresenter>(includeSelf: true) is not null;
        // A click on a button or a check box of the row is theirs, not a click on the row.
        var onControl = v?.FindAncestorOfType<Button>(includeSelf: true) is not null || v?.FindAncestorOfType<CheckBox>(includeSelf: true) is not null;
        if (onControl) inDetails = true;
        return v?.FindAncestorOfType<DataGridRow>(includeSelf: true)?.DataContext as ExploreRow;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _pressedRow = null;
        var row = RowAt(e.Source, out var inDetails);
        _menuOnRow = row is not null && !inDetails;
        // Right-click on a row selects it, so the menu ("Descargar miniatura...") acts on what was clicked.
        if (row is not null && !inDetails && e.GetCurrentPoint(ResultsGrid).Properties.IsRightButtonPressed && ResultsGrid.SelectedItem != row)
            ResultsGrid.SelectedItem = row;
        if (row is null || inDetails || !e.GetCurrentPoint(ResultsGrid).Properties.IsLeftButtonPressed) return;
        _pressedRow = row;
        _pressedSoleSelection = e.KeyModifiers == KeyModifiers.None && ResultsGrid.SelectedItems.Count == 1 && ResultsGrid.SelectedItem == row;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // A click on the row that was already the only one selected opens or closes it (selecting it opened it already).
        if (e.InitialPressMouseButton != MouseButton.Left || _pressedRow is null) return;
        var row = RowAt(e.Source, out _);
        if (row == _pressedRow && _pressedSoleSelection) _explore?.ToggleRow(row);
        _pressedRow = null;
    }
}
