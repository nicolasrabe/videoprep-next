using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Videoprep.App.Services;
using Videoprep.Core.Formats;
using Videoprep.Core.Profiles;
using Videoprep.Core.Scraping;

namespace Videoprep.App.ViewModels;

/// <summary>
/// The Agregar / Editar dialog of Suscripciones (SCRAPING_SPEC 2): the Explorar fields plus folder, format and the
/// first-run choice. Edits a copy; <see cref="Build"/> gives the subscription to save, or a message saying what is wrong.
/// </summary>
public sealed partial class SubscriptionEditViewModel : ObservableObject
{
    private readonly Subscription? _original;
    private readonly Func<string?, Task<string?>> _pickFolder;

    private readonly bool _ready;
    private bool _applyingProfile;

    public SubscriptionEditViewModel(Subscription? original, Func<string?, Task<string?>> pickFolder, IReadOnlyList<ProfileChoice>? profiles = null)
    {
        _original = original;
        _pickFolder = pickFolder;
        Profiles = [new ProfileChoice(null, Loc.Instance["prof_own"]), .. profiles ?? []];
        var s = original ?? new Subscription();
        Name = s.Name;
        Url = s.Url;
        TabVideos = s.Tabs.Contains("videos");
        TabShorts = s.Tabs.Contains("shorts");
        TabStreams = s.Tabs.Contains("streams");
        Filter = FilterInputs.From(s.Filters);
        Format = new FormatPicker(s.Profile.Format, s.Profile.Cap, s.Profile.ConstantRate);
        NameSeries = s.Profile.Naming == "series";
        Prefix = s.Profile.Prefix;
        Folder = s.Folder;
        SubfolderPerSub = s.SubfolderPerSub;
        Backfill = s.FirstRun == Subscription.FirstRunBackfill;
        Enabled = s.Enabled;
        // F6: the profile it follows (if it still exists); a change of format or names by hand means "own values".
        SelectedProfile = s.Profile.ProfileId is { } id ? Profiles.FirstOrDefault(c => c.Profile?.Id == id) ?? Profiles[0] : Profiles[0];
        Format.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(FormatPicker.SelectedFormat) or nameof(FormatPicker.SelectedResolution) or nameof(FormatPicker.ConstantRate)) FollowOwnValues();
        };
        _ready = true;
    }

    /// <summary>"(valores propios)" first, then the profiles.</summary>
    public IReadOnlyList<ProfileChoice> Profiles { get; }
    [ObservableProperty] private ProfileChoice? _selectedProfile;

    partial void OnSelectedProfileChanged(ProfileChoice? value)
    {
        if (!_ready || value?.Profile is not { } p) return;
        _applyingProfile = true;
        try
        {
            Format.Apply(p);
            NameSeries = p.IsSeries;
            if (p.IsSeries && p.Prefix.Length > 0) Prefix = p.Prefix;
        }
        finally
        {
            _applyingProfile = false;
        }
    }

    private void FollowOwnValues()
    {
        if (_ready && !_applyingProfile && SelectedProfile?.Profile is not null) SelectedProfile = Profiles[0];
    }

    partial void OnNameSeriesChanged(bool value) => FollowOwnValues();

    /// <summary>New subscriptions ask what to do the first time; existing ones already did it.</summary>
    public bool IsNew => _original is null;

    public string Title => Loc.Instance[IsNew ? "sc_add" : "sc_edit"];

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _url = "";
    [ObservableProperty] private bool _tabVideos = true;
    [ObservableProperty] private bool _tabShorts;
    [ObservableProperty] private bool _tabStreams;
    public FilterInputs Filter { get; }
    public FormatPicker Format { get; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(NameOriginal))] private bool _nameSeries;
    public bool NameOriginal { get => !NameSeries; set => NameSeries = !value; }
    [ObservableProperty] private string _prefix = "";
    [ObservableProperty] private string _folder = "";
    [ObservableProperty] private bool _subfolderPerSub;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(OnlyNew))] private bool _backfill;
    public bool OnlyNew { get => !Backfill; set => Backfill = !value; }
    [ObservableProperty] private bool _enabled = true;

    [RelayCommand]
    private async Task PickFolder()
    {
        if (await _pickFolder(Folder.Length > 0 ? Folder : null) is { } f) Folder = f;
    }

    /// <summary>A short name from the link when the user typed none ("@canal", "Lista", the site's last path part).</summary>
    public static string NameFromUrl(string url)
    {
        if (SourceUrl.ChannelBase(url) is { } ch) return ch[(ch.LastIndexOf('/') + 1)..];
        if (Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u))
        {
            var parts = u.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 ? u.Host.Replace("www.", "") + "/" + parts[0] : u.Host;
        }
        return url.Trim();
    }

    /// <summary>The profile it follows: only when the values on screen are still exactly the profile's.</summary>
    private string? FollowedProfileId() =>
        SelectedProfile?.Profile is { } p && p.Format == Format.FormatKey && p.Cap == Format.Cap && p.IsSeries == NameSeries
        && p.ConstantRate == Format.ConstantRate ? p.Id : null;

    /// <summary>The subscription to save, or null and a message for the user.</summary>
    public Subscription? Build(out string? problem)
    {
        var L = Loc.Instance;
        problem = null;
        var url = Url.Trim();
        if (SourceUrl.UnsupportedProfile(url) is { } site) { problem = L.Format("sc_profile_unsupported", site); return null; }
        var tabs = new List<SourceTab>();
        if (TabVideos) tabs.Add(SourceTab.Videos);
        if (TabShorts) tabs.Add(SourceTab.Shorts);
        if (TabStreams) tabs.Add(SourceTab.Streams);
        if (SourceUrl.Targets(url, tabs).Count == 0) { problem = L["sc_bad_url"]; return null; }
        if (Filter.Read(out problem) is not { } filters) return null;
        if (Folder.Trim().Length == 0) { problem = L["sc_need_folder"]; return null; }
        var name = Name.Trim().Length > 0 ? Name.Trim() : NameFromUrl(url);
        var baseSub = _original ?? new Subscription { Id = Subscription.NewId() };
        return baseSub with
        {
            Name = name, Url = url, Enabled = Enabled,
            Tabs = tabs.Count > 0 ? Subscription.TabNames(tabs) : ["videos"],
            Filters = SubscriptionFilters.FromFilterSet(filters),
            Profile = new SubscriptionProfile
            {
                Format = Format.FormatKey, Cap = Format.Cap, Naming = NameSeries ? "series" : "orig",
                Prefix = NameSeries ? (Prefix.Trim().Length > 0 ? Prefix.Trim() : name) : "",
                ConstantRate = Format.ConstantRate && FormatCatalog.Get(Format.FormatKey).Kind == MediaKind.Video,
                ProfileId = FollowedProfileId(),
            },
            Folder = Folder.Trim().Trim('"'), SubfolderPerSub = SubfolderPerSub,
            FirstRun = IsNew ? (Backfill ? Subscription.FirstRunBackfill : Subscription.FirstRunMarkSeen) : baseSub.FirstRun,
        };
    }
}
