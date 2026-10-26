using CommunityToolkit.Mvvm.ComponentModel;
using Videoprep.App.Services;

namespace Videoprep.App.ViewModels;

/// <summary>
/// The Scraping tab as an approved-mockup preview (Phase 4). Sample data only; nothing here talks to the engine.
/// Phase 5 replaces it with the real Explore / Subscriptions view models (SCRAPING_SPEC.md).
/// </summary>
public sealed partial class ScrapingSampleViewModel : ObservableObject
{
    public sealed record VideoRow(bool Selected, string Title, string Date, string Duration, string Views, string StatusKey);

    public sealed record SubscriptionRow(bool Enabled, string Channel, string What, string Filters, string Format, string Last, string New);

    /// <summary>0 = Explorar, 1 = Suscripciones.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowExplore), nameof(ShowSubscriptions))]
    private int _subView;

    public bool ShowExplore => SubView == 0;
    public bool ShowSubscriptions => SubView == 1;

    public string SourceUrl => "https://www.youtube.com/@canal-de-ejemplo";

    public IReadOnlyList<VideoRow> Videos { get; } =
    [
        new(true, "Entrevista completa con el invitado", "2026-10-21", "38:12", "41 mil", "sc_status_new"),
        new(true, "Review del equipo de edición", "2026-10-14", "12:40", "18 mil", "sc_status_new"),
        new(true, "Entrevista: detrás de escena", "2026-10-02", "24:05", "9,3 mil", "sc_status_new"),
        new(false, "Review del mes pasado", "2026-09-18", "09:51", "22 mil", "sc_status_done"),
        new(false, "Entrevista con el equipo", "2026-09-03", "31:27", "15 mil", "sc_status_done"),
    ];

    public IReadOnlyList<SubscriptionRow> Subscriptions { get; } =
    [
        new(true, "@canal-de-ejemplo", "Videos + Directos", "desde 2026-09-01, 2:00-45:00", "MP4 1080p", "hoy 09:14", "2"),
        new(true, "Lista: Clips de abril", "Lista completa", "sin filtros", "MOV ProRes", "ayer 21:02", "0"),
        new(false, "twitch.tv/ejemplo", "Clips", "máx. 20, más nuevos", "MP4 720p", "nunca", "-"),
    ];

    public ScrapingSampleViewModel() =>
        Loc.Instance.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(SelectedText));
            OnPropertyChanged(nameof(CheckEveryText));
        };

    public string SelectedText => Loc.Instance.Format("sc_selected", Videos.Count(v => v.Selected), Videos.Count);
    public string CheckEveryText => Loc.Instance.Format("sc_check_every", 6);

    public void ShowExploreView() => SubView = 0;
    public void ShowSubscriptionsView() => SubView = 1;
}
