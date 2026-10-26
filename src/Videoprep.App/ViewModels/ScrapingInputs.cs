using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Videoprep.App.Services;
using Videoprep.Core.Formats;
using Videoprep.Core.Scraping;

namespace Videoprep.App.ViewModels;

/// <summary>The filter boxes shared by Explorar and the subscription dialog (SCRAPING_SPEC 1).</summary>
public sealed partial class FilterInputs : ObservableObject
{
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(FromBad))] private string _fromText = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ToBad))] private string _toText = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(MinBad))] private string _minText = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(MaxBad))] private string _maxText = "";
    [ObservableProperty] private string _includeText = "";
    [ObservableProperty] private string _excludeText = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(MaxCountBad))] private string _maxCountText = "";
    [ObservableProperty] private int _orderIndex;
    [ObservableProperty] private bool _skipLive = true;
    [ObservableProperty] private bool _skipMembers = true;
    [ObservableProperty] private bool _skipDone = true;

    public bool FromBad => !Filters.TryParseDate(FromText, out _);
    public bool ToBad => !Filters.TryParseDate(ToText, out _);
    public bool MinBad => !Filters.TryParseDuration(MinText, out _);
    public bool MaxBad => !Filters.TryParseDuration(MaxText, out _);
    public bool MaxCountBad => MaxCountText.Trim().Length > 0 && !(int.TryParse(MaxCountText.Trim(), out var n) && n >= 0);

    /// <summary>Reads the boxes; null (and a message) when one is not valid.</summary>
    public FilterSet? Read(out string? problem)
    {
        var L = Loc.Instance;
        problem = null;
        if (!Filters.TryParseDate(FromText, out var from)) problem = L.Format("sc_bad_date", FromText.Trim());
        else if (!Filters.TryParseDate(ToText, out var to)) problem = L.Format("sc_bad_date", ToText.Trim());
        else if (!Filters.TryParseDuration(MinText, out var min)) problem = L.Format("sc_bad_duration", MinText.Trim());
        else if (!Filters.TryParseDuration(MaxText, out var max)) problem = L.Format("sc_bad_duration", MaxText.Trim());
        else if (MaxCountBad) problem = L["sc_bad_max"];
        else
        {
            return new FilterSet
            {
                From = from, To = to, MinSec = min, MaxSec = max,
                Include = Filters.SplitTerms(IncludeText), Exclude = Filters.SplitTerms(ExcludeText),
                MaxCount = int.TryParse(MaxCountText.Trim(), out var n) ? n : null,
                OldestFirst = OrderIndex == 1, SkipLive = SkipLive, SkipMembers = SkipMembers, SkipDownloaded = SkipDone,
            };
        }
        return null;
    }

    public void CopyFrom(FilterInputs o)
    {
        (FromText, ToText, MinText, MaxText) = (o.FromText, o.ToText, o.MinText, o.MaxText);
        (IncludeText, ExcludeText, MaxCountText, OrderIndex) = (o.IncludeText, o.ExcludeText, o.MaxCountText, o.OrderIndex);
        (SkipLive, SkipMembers, SkipDone) = (o.SkipLive, o.SkipMembers, o.SkipDone);
    }

    public static FilterInputs From(SubscriptionFilters f) => new()
    {
        FromText = f.From ?? "", ToText = f.To ?? "",
        MinText = f.MinSec is { } a ? Filters.FormatDuration(a) : "", MaxText = f.MaxSec is { } b ? Filters.FormatDuration(b) : "",
        IncludeText = string.Join(", ", f.Include), ExcludeText = string.Join(", ", f.Exclude),
        MaxCountText = f.MaxCount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
        OrderIndex = f.Order == "oldest" ? 1 : 0, SkipLive = f.SkipLive, SkipMembers = f.SkipMembers,
    };
}

/// <summary>Format + resolution combo boxes (the Descargar choices), shared by Explorar and the subscription dialog.</summary>
public sealed partial class FormatPicker : ObservableObject
{
    public ObservableCollection<FormatChoice> FormatChoices { get; } = [];
    [ObservableProperty] private FormatChoice? _selectedFormat;
    public ObservableCollection<string> ResolutionChoices { get; } = [];
    [ObservableProperty] private string? _selectedResolution;

    public FormatPicker(string formatKey = "mp4", int cap = FormatCatalog.DefaultCap)
    {
        foreach (var f in FormatCatalog.All) FormatChoices.Add(new FormatChoice(f.Key, f.Label));
        SelectedFormat = FormatChoices.FirstOrDefault(f => f.Key == formatKey) ?? FormatChoices[0];
        var label = FormatCatalog.ResolutionLabel(cap);
        if (ResolutionChoices.Contains(label)) SelectedResolution = label;
    }

    partial void OnSelectedFormatChanged(FormatChoice? value)
    {
        var keep = SelectedResolution;
        ResolutionChoices.Clear();
        foreach (var r in FormatCatalog.ResolutionChoices(value?.Key ?? "mp4")) ResolutionChoices.Add(r);
        SelectedResolution = keep is not null && ResolutionChoices.Contains(keep) ? keep : "1080p";
    }

    public string FormatKey => SelectedFormat?.Key ?? "mp4";

    public int Cap => SelectedResolution is not null && FormatCatalog.Resolutions.TryGetValue(SelectedResolution, out var c) ? c : FormatCatalog.DefaultCap;
}
