using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Videoprep.App.Services;
using Videoprep.Core.Formats;
using Videoprep.Core.Models;
using Videoprep.Core.Profiles;

namespace Videoprep.App.ViewModels;

/// <summary>A choice with "(sin cambios)" as the first entry (Value null).</summary>
public sealed record Choice<T>(T? Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// "Cambiar N elementos de la cola" (F2): format, max resolution and names, each "(sin cambios)" by default.
/// The preview of the resulting names updates while typing; nothing changes until "Aplicar".
/// </summary>
public sealed partial class BatchEditViewModel : ObservableObject
{
    private readonly IReadOnlyList<QueueItem> _items;
    private readonly Func<QueueItem, string?> _title;
    private readonly Func<QueueItem, int> _firstSeriesNumber;

    public Loc L => Loc.Instance;
    public string Title => L.Format("batch_title", _items.Count);
    public string ApplyText => L.Format("batch_apply", _items.Count);
    /// <summary>"2 con candado no se cambian." or "".</summary>
    public string LockedNote { get; }
    public bool HasLockedNote => LockedNote.Length > 0;

    public IReadOnlyList<Choice<string>> Formats { get; }
    public IReadOnlyList<Choice<string>> Resolutions { get; }
    public IReadOnlyList<Choice<string>> NameModes { get; }

    /// <summary>F8: "(sin cambios)", "Sí", "No".</summary>
    public IReadOnlyList<Choice<string>> ConstantChoices { get; }
    [ObservableProperty] private Choice<string> _selectedConstant;

    /// <summary>"(sin cambios)" first, then the profiles (F6): picking one fills Formato, Resolución and Nombres below.</summary>
    public IReadOnlyList<ProfileChoice> Profiles { get; }
    [ObservableProperty] private ProfileChoice _selectedProfile;
    public bool HasProfiles => Profiles.Count > 1;

    partial void OnSelectedProfileChanged(ProfileChoice value)
    {
        if (value.Profile is not { } p) return;
        SelectedFormat = Formats.FirstOrDefault(f => f.Value == p.Format) ?? SelectedFormat;
        // A profile that asks for a constant rate turns it on; one that does not ask leaves the videos as they are.
        if (p.ConstantRate) SelectedConstant = ConstantChoices[1];
        SelectedResolution = Resolutions.FirstOrDefault(r => r.Value is { } v && FormatCatalog.Resolutions[v] == p.Cap) ?? SelectedResolution;
        if (p.IsSeries)
        {
            SelectedNames = NameModes.First(m => m.Value == nameof(NameChange.Series));
            if (p.Prefix.Length > 0) Prefix = p.Prefix;
        }
        else
        {
            // "Title of the video" in a profile must not wipe the names already typed in the queue: names stay as they are.
            SelectedNames = NameModes[0];
        }
    }

    [ObservableProperty] private Choice<string> _selectedFormat;
    [ObservableProperty] private Choice<string> _selectedResolution;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSeries), nameof(IsAddText), nameof(IsReplace))]
    private Choice<string> _selectedNames;

    [ObservableProperty] private string _prefix = "Video";
    [ObservableProperty] private string _before = "";
    [ObservableProperty] private string _after = "";
    [ObservableProperty] private string _find = "";
    [ObservableProperty] private string _replaceWith = "";

    public bool IsSeries => SelectedNames.Value == nameof(NameChange.Series);
    public bool IsAddText => SelectedNames.Value == nameof(NameChange.AddText);
    public bool IsReplace => SelectedNames.Value == nameof(NameChange.Replace);

    /// <summary>"old → new", one line per item.</summary>
    public ObservableCollection<string> Preview { get; } = [];

    /// <param name="items">The unlocked selected items, in queue order.</param>
    /// <param name="title">Video title when known.</param>
    /// <param name="firstSeriesNumber">First free number for a series prefix in the item's folder (1 when none).</param>
    public BatchEditViewModel(IReadOnlyList<QueueItem> items, int locked, Func<QueueItem, string?> title, Func<QueueItem, int> firstSeriesNumber,
                              IReadOnlyList<ProfileChoice>? profiles = null)
    {
        _items = items;
        _title = title;
        _firstSeriesNumber = firstSeriesNumber;
        LockedNote = locked > 0 ? L.Format("batch_locked", locked) : "";
        var same = L["batch_same"];
        Formats = [new(null, same), .. FormatCatalog.All.Select(f => new Choice<string>(f.Key, f.Label))];
        Resolutions = [new(null, same), .. FormatCatalog.Resolutions.Keys.Select(r => new Choice<string>(r, r))];
        NameModes =
        [
            new(null, same),
            new(nameof(NameChange.Series), L["batch_series"]),
            new(nameof(NameChange.AddText), L["batch_add"]),
            new(nameof(NameChange.Replace), L["batch_replace"]),
            new(nameof(NameChange.Original), L["batch_original"]),
        ];
        Profiles = [new ProfileChoice(null, same), .. profiles ?? []];
        _selectedProfile = Profiles[0];
        ConstantChoices = [new(null, same), new("yes", L["batch_cfr_yes"]), new("no", L["batch_cfr_no"])];
        _selectedConstant = ConstantChoices[0];
        _selectedFormat = Formats[0];
        _selectedResolution = Resolutions[0];
        _selectedNames = NameModes[0];
        UpdatePreview();
    }

    public BatchEdit Edit => new()
    {
        Format = SelectedFormat.Value,
        Cap = SelectedResolution.Value is { } r ? FormatCatalog.Resolutions[r] : null,
        ConstantRate = SelectedConstant.Value switch { "yes" => true, "no" => false, _ => null },
        Names = SelectedNames.Value is { } n ? Enum.Parse<NameChange>(n) : NameChange.None,
        Prefix = Prefix, Before = Before, After = After, Find = Find, ReplaceWith = ReplaceWith,
    };

    /// <summary>The items as they will be after "Aplicar".</summary>
    public IReadOnlyList<QueueItem> Result() => _items.Select(i => QueueEdits.Apply(i, Edit, _title(i))).ToList();

    partial void OnSelectedFormatChanged(Choice<string> value) => UpdatePreview();
    partial void OnSelectedNamesChanged(Choice<string> value) => UpdatePreview();
    partial void OnPrefixChanged(string value) => UpdatePreview();
    partial void OnBeforeChanged(string value) => UpdatePreview();
    partial void OnAfterChanged(string value) => UpdatePreview();
    partial void OnFindChanged(string value) => UpdatePreview();
    partial void OnReplaceWithChanged(string value) => UpdatePreview();

    private void UpdatePreview()
    {
        Preview.Clear();
        var after = Result();
        var next = new Dictionary<string, int>();
        foreach (var it in after.Where(i => i.Naming == NamingMode.Series))
            next.TryAdd(JobPlanner.SeriesKey(it), _firstSeriesNumber(it));
        var beforeNames = QueueEdits.OutputNames(_items, _title, SeriesStart(_items));
        var afterNames = QueueEdits.OutputNames(after, _title, next);
        var changesText = Edit.Names is NameChange.AddText or NameChange.Replace;
        var unknown = changesText ? L["batch_unknown"] : "(" + L["det_name_title"] + ")";
        var row = 0;
        for (var i = 0; i < _items.Count; i++)
        {
            var count = Math.Max(1, _items[i].Clips.Count);
            for (var c = 0; c < count; c++, row++)
            {
                var ext = FormatCatalog.Get(after[i].Format).Extension;
                var old = beforeNames[row] ?? _items[i].Url;
                var neu = afterNames[row] is { } n ? n + ext : changesText ? unknown : unknown + ext;
                Preview.Add($"{old}  →  {neu}");
            }
        }
    }

    private Dictionary<string, int> SeriesStart(IEnumerable<QueueItem> items)
    {
        var next = new Dictionary<string, int>();
        foreach (var it in items.Where(i => i.Naming == NamingMode.Series)) next.TryAdd(JobPlanner.SeriesKey(it), _firstSeriesNumber(it));
        return next;
    }
}
