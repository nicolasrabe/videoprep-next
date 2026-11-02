using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;

namespace Videoprep.App.Views;

/// <summary>
/// RONDA 4 (principle 14: compact and dynamic). A card with a clickable header (chevron + title, always-visible header content and,
/// only while folded, a one-line summary) and a body that folds. With <see cref="Grows"/> the card fills the free height its parent
/// gives it (parent row = "*") and the body fills the card; without it the card is as tall as its body.
/// The template lives in App.axaml (style "CollapsibleSection").
/// </summary>
public class CollapsibleSection : ContentControl
{
    public static readonly StyledProperty<string> TitleProperty = AvaloniaProperty.Register<CollapsibleSection, string>(nameof(Title), "");
    public static readonly StyledProperty<string> SummaryProperty = AvaloniaProperty.Register<CollapsibleSection, string>(nameof(Summary), "");
    public static readonly StyledProperty<object?> HeaderContentProperty = AvaloniaProperty.Register<CollapsibleSection, object?>(nameof(HeaderContent));
    public static readonly StyledProperty<bool> IsExpandedProperty = AvaloniaProperty.Register<CollapsibleSection, bool>(nameof(IsExpanded), true, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);
    public static readonly StyledProperty<bool> GrowsProperty = AvaloniaProperty.Register<CollapsibleSection, bool>(nameof(Grows));
    public static readonly DirectProperty<CollapsibleSection, bool> SummaryVisibleProperty =
        AvaloniaProperty.RegisterDirect<CollapsibleSection, bool>(nameof(SummaryVisible), o => o.SummaryVisible);

    private bool _summaryVisible;
    private Control? _body;
    private Control? _summary;

    static CollapsibleSection()
    {
        IsExpandedProperty.Changed.AddClassHandler<CollapsibleSection>((s, _) => s.Refresh());
        SummaryProperty.Changed.AddClassHandler<CollapsibleSection>((s, _) => s.Refresh());
        GrowsProperty.Changed.AddClassHandler<CollapsibleSection>((s, _) => s.Refresh());
    }

    public CollapsibleSection() => Refresh();

    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    /// <summary>One line shown next to the title only while the section is folded (what is applied inside).</summary>
    public string Summary { get => GetValue(SummaryProperty); set => SetValue(SummaryProperty, value); }
    /// <summary>Always visible in the header, folded or not (for example the channel's avatar and name).</summary>
    public object? HeaderContent { get => GetValue(HeaderContentProperty); set => SetValue(HeaderContentProperty, value); }
    public bool IsExpanded { get => GetValue(IsExpandedProperty); set => SetValue(IsExpandedProperty, value); }
    /// <summary>Takes all the free height of its parent while it is open.</summary>
    public bool Grows { get => GetValue(GrowsProperty); set => SetValue(GrowsProperty, value); }
    public bool SummaryVisible => _summaryVisible;

    private void Refresh()
    {
        var visible = !IsExpanded && !string.IsNullOrWhiteSpace(Summary);
        SetAndRaise(SummaryVisibleProperty, ref _summaryVisible, visible);
        // A folded card is only its header: it must not keep the free height.
        VerticalAlignment = Grows && IsExpanded ? VerticalAlignment.Stretch : VerticalAlignment.Top;
        PseudoClasses.Set(":folded", !IsExpanded);
        if (_body is not null) _body.IsVisible = IsExpanded;
        if (_summary is not null) _summary.IsVisible = visible;
    }

    /// <summary>The body is built even while the card is hidden or folded, so what lives inside it (named boxes, tables) always exists.</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ApplyTemplate();
        (_body as Avalonia.Controls.Presenters.ContentPresenter)?.UpdateChild();   // a hidden card has no layout pass: build the body now
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _body = e.NameScope.Find<Control>("PART_ContentPresenter");
        _summary = e.NameScope.Find<Control>("PART_Summary");
        Refresh();
        if (e.NameScope.Find<Button>("PART_Header") is { } header)
        {
            header.Click -= OnHeaderClick;
            header.Click += OnHeaderClick;
        }
    }

    private void OnHeaderClick(object? sender, RoutedEventArgs e) => IsExpanded = !IsExpanded;
}
