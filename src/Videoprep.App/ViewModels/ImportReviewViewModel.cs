using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Videoprep.App.Services;
using Videoprep.Core.Formats;
using Videoprep.Core.Import;
using Videoprep.Core.Text;

namespace Videoprep.App.ViewModels;

/// <summary>One row of "Enlaces encontrados en ...".</summary>
public sealed partial class ImportRow : ObservableObject
{
    public required ImportCandidate Candidate { get; init; }
    public required string ShownUrl { get; init; }
    public required string Context { get; init; }
    public required string How { get; init; }
    public required string State { get; init; }
    /// <summary>Why the row starts unchecked ("" for new videos).</summary>
    public required string Explanation { get; init; }
    /// <summary>"ok" (green pill), "warn" (orange) or "" (plain).</summary>
    public required string Pill { get; init; }
    public bool IsOk => Pill == "ok";
    public bool IsWarn => Pill == "warn";
    public bool IsDim => !Candidate.CheckedByDefault;

    [ObservableProperty] private bool _checked;
    [ObservableProperty] private string _name = "";
}

/// <summary>A choice in the review window's "Formato" box: the Download tab's choice first, then every format.</summary>
public sealed record ImportFormat(string Key, int Cap, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Review window for links read from a file (F1). Nothing is added until "Agregar N a la cola"; links that are
/// probably not single videos start unchecked and say why. Always whole videos: no clips from the document.
/// </summary>
public sealed partial class ImportReviewViewModel : ObservableObject
{
    public Loc L => Loc.Instance;
    public string FileName { get; }
    public string Title => L.Format("imp_title", FileName);
    public string Summary { get; }
    public ObservableCollection<ImportRow> Rows { get; } = [];
    public IReadOnlyList<ImportFormat> Formats { get; }
    [ObservableProperty] private ImportFormat _selectedFormat;
    [ObservableProperty] private bool _useSubfolder;
    /// <summary>Clean folder name made from the file name.</summary>
    public string SubfolderName { get; }
    public string SubfolderText => L.Format("imp_subfolder", SubfolderName);

    public ImportReviewViewModel(string filePath, IReadOnlyList<ImportCandidate> candidates, string tabFormatKey, int tabCap)
    {
        FileName = Path.GetFileName(filePath);
        SubfolderName = SafeName.Make(Path.GetFileNameWithoutExtension(filePath), "enlaces");
        foreach (var c in candidates)
        {
            var row = new ImportRow
            {
                Candidate = c,
                ShownUrl = LinkNormalizer.Short(c.Url),
                Context = c.Context.Length > 0 ? c.Context : L["imp_ctx_alone"],
                How = L[c.How == LinkHow.Hyperlink ? "how_link" : "how_text"],
                State = c.State switch
                {
                    CandidateState.New => c.Site,
                    CandidateState.Repeated => L["imp_st_repeated"],
                    CandidateState.InQueue => L["imp_st_inqueue"],
                    CandidateState.Channel => L["imp_st_channel"] + " · " + c.Site,
                    CandidateState.Playlist => L["imp_st_playlist"] + " · " + c.Site,
                    CandidateState.NotVideo => L["imp_st_notvideo"],
                    _ => L["imp_st_unknown"],
                },
                Explanation = c.State switch
                {
                    CandidateState.Repeated => L["imp_tip_repeated"],
                    CandidateState.InQueue => L["imp_tip_inqueue"],
                    CandidateState.Channel => L["imp_tip_channel"],
                    CandidateState.Playlist => L["imp_tip_playlist"],
                    CandidateState.NotVideo => L["imp_tip_notvideo"],
                    CandidateState.Unknown => L["imp_tip_unknown"],
                    _ => "",
                },
                Pill = c.State switch
                {
                    CandidateState.New => "ok",
                    CandidateState.Repeated or CandidateState.InQueue => "warn",
                    _ => "",
                },
                Checked = c.CheckedByDefault,
            };
            row.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ImportRow.Checked)) OnCheckedChanged(); };
            Rows.Add(row);
        }
        var news = candidates.Count(c => c.State == CandidateState.New);
        var repeats = candidates.Count(c => c.State is CandidateState.Repeated or CandidateState.InQueue);
        Summary = L.Format("imp_summary", candidates.Count, news, repeats, candidates.Count - news - repeats);

        var tab = FormatCatalog.Get(tabFormatKey);
        var formats = new List<ImportFormat>
        {
            new(tab.Key, tabCap, L.Format("imp_fmt_tab", FormatLabel(tab.Key, tabCap))),
        };
        formats.AddRange(FormatCatalog.All.Select(f => new ImportFormat(f.Key, f.Kind == MediaKind.Video ? Math.Min(tabCap, MaxCap(f.Key)) : tabCap, f.Label)));
        Formats = formats;
        _selectedFormat = formats[0];
    }

    /// <summary>"MP4 - H.264 + AAC · 1080p" (audio formats without resolution).</summary>
    public static string FormatLabel(string key, int cap)
    {
        var f = FormatCatalog.Get(key);
        return f.Kind == MediaKind.Audio ? f.Label : $"{f.Label} · {FormatCatalog.ResolutionLabel(cap)}";
    }

    private static int MaxCap(string key) => FormatCatalog.ResolutionChoices(key).Select(r => FormatCatalog.Resolutions[r]).Max();

    public int CheckedCount => Rows.Count(r => r.Checked);
    public string SelectedText => L.Format("imp_selected", CheckedCount);
    public string AddText => L.Format("imp_add", CheckedCount);
    public bool CanAdd => CheckedCount > 0;

    private void OnCheckedChanged()
    {
        OnPropertyChanged(nameof(CheckedCount));
        OnPropertyChanged(nameof(SelectedText));
        OnPropertyChanged(nameof(AddText));
        OnPropertyChanged(nameof(CanAdd));
    }

    [RelayCommand]
    private void All() { foreach (var r in Rows) r.Checked = true; }

    [RelayCommand]
    private void None() { foreach (var r in Rows) r.Checked = false; }

    [RelayCommand]
    private void Invert() { foreach (var r in Rows) r.Checked = !r.Checked; }

    /// <summary>What to add: checked rows in file order, with the name typed for each ("" = original title).</summary>
    public IReadOnlyList<(string Url, string Name)> Chosen() =>
        Rows.Where(r => r.Checked).Select(r => (r.Candidate.Url, r.Name.Trim())).ToList();
}
