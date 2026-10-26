using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Videoprep.Core.Engine;
using Videoprep.Core.Formats;
using Videoprep.Core.Models;
using Videoprep.Core.Preview;
using Videoprep.Core.Text;

namespace Videoprep.App.ViewModels;

/// <summary>Download tab: the link being prepared ("draft"), its format, clips, names and preview.</summary>
public sealed partial class MainViewModel
{
    // ---- draft (legacy $Draft)
    [ObservableProperty] private string _linkText = "";
    private string _draftUrl = "";
    private string _draftKey = "";
    private int _editId;

    public bool HasDraft => _draftUrl.Length > 0;

    // ---- format
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVideo))]
    private bool _isAudio;

    public bool IsVideo
    {
        get => !IsAudio;
        set => IsAudio = !value;
    }

    public ObservableCollection<FormatChoice> FormatChoices { get; } = [];
    [ObservableProperty] private FormatChoice? _selectedFormat;
    public ObservableCollection<string> ResolutionChoices { get; } = [];
    [ObservableProperty] private string? _selectedResolution;
    [ObservableProperty] private string _hintText = "";
    public bool ResolutionEnabled => !IsAudio && !IsRunning;

    // ---- clips
    public ObservableCollection<ClipRowViewModel> Clips { get; } = [];

    // ---- names
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CustomEnabled))]
    private bool _nameOriginal = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CustomEnabled))]
    private bool _nameCustom;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SeriesEnabled))]
    private bool _nameSeries;

    [ObservableProperty] private string _customText = "";
    [ObservableProperty] private string _seriesText = "Video";
    [ObservableProperty] private string _exampleText = "";
    public bool CustomEnabled => NameCustom && !IsRunning;
    public bool SeriesEnabled => NameSeries && !IsRunning;

    // ---- preview
    [ObservableProperty] private string _previewTitle = "";
    [ObservableProperty] private string _previewMeta = "";
    [ObservableProperty] private Bitmap? _previewImage;

    // ---- folder
    [ObservableProperty] private string _folder = "";

    private string SelectedFormatKey => SelectedFormat?.Key ?? "mp4";

    private int SelectedCap =>
        SelectedResolution is not null && FormatCatalog.Resolutions.TryGetValue(SelectedResolution, out var c) ? c : FormatCatalog.DefaultCap;

    private void InitDownload()
    {
        Folder = Settings.Folder.Length > 0 ? Settings.Folder : DefaultFolder();
        SeriesText = Settings.Prefix;
        if (Settings.Naming == "series") NameSeries = true; else NameOriginal = true;
        RebuildFormatLists();
        ClearPreviewPanel();
        Clips.CollectionChanged += (_, _) => UpdateExample();
    }

    private static string DefaultFolder()
    {
        var v = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        return v.Length > 0 ? v : Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
    }

    // ---------------- format lists (port of Update-FormatList / Update-ResList / Update-Hint) ----------------

    private bool _buildingLists;

    private void RebuildFormatLists()
    {
        _buildingLists = true;
        var keep = SelectedFormat?.Key;
        FormatChoices.Clear();
        foreach (var f in FormatCatalog.OfKind(IsAudio ? MediaKind.Audio : MediaKind.Video)) FormatChoices.Add(new FormatChoice(f.Key, f.Label));
        SelectedFormat = FormatChoices.FirstOrDefault(f => f.Key == keep) ?? FormatChoices[0];
        _buildingLists = false;
        RebuildResolutions();
        UpdateHint();
        UpdateExample();
    }

    private void RebuildResolutions()
    {
        _buildingLists = true;
        var keep = SelectedResolution;
        ResolutionChoices.Clear();
        foreach (var r in FormatCatalog.ResolutionChoices(SelectedFormatKey)) ResolutionChoices.Add(r);
        SelectedResolution = keep is not null && ResolutionChoices.Contains(keep) ? keep : "1080p";
        _buildingLists = false;
        OnPropertyChanged(nameof(ResolutionEnabled));
    }

    private void UpdateHint() => HintText = L[FormatCatalog.Get(SelectedFormatKey).HintKey];

    partial void OnIsAudioChanged(bool value)
    {
        RebuildFormatLists();
        OnFormatChanged();
    }

    partial void OnSelectedFormatChanged(FormatChoice? value)
    {
        if (_buildingLists) return;
        RebuildResolutions();
        UpdateHint();
        UpdateExample();
        OnFormatChanged();
    }

    partial void OnSelectedResolutionChanged(string? value)
    {
        if (_buildingLists) return;
        OnFormatChanged();
    }

    /// <summary>Port of On-FormatChanged: re-check the quality only when the format group changes.</summary>
    private void OnFormatChanged()
    {
        if (_draftKey.Length == 0) return;
        if (!IsRunning) _svc.Preview.Refresh(_draftKey, DetailRequestNow());
        ShowPreview();
    }

    // ---------------- names (port of Update-Example / Update-NameControls) ----------------

    // The three name options exclude each other here too (not only through the radio buttons' group).
    partial void OnNameOriginalChanged(bool value)
    {
        if (value) { NameCustom = false; NameSeries = false; }
        UpdateExample();
    }

    partial void OnNameCustomChanged(bool value)
    {
        if (value) { NameOriginal = false; NameSeries = false; }
        UpdateExample();
    }

    partial void OnNameSeriesChanged(bool value)
    {
        if (value) { NameOriginal = false; NameCustom = false; }
        UpdateExample();
    }
    partial void OnCustomTextChanged(string value) => UpdateExample();
    partial void OnSeriesTextChanged(string value) => UpdateExample();

    private void UpdateExample()
    {
        var ext = FormatCatalog.Get(SelectedFormatKey).Extension;
        string text;
        if (NameSeries)
        {
            text = $"{SeriesText}1{ext}, {SeriesText}2{ext}, {SeriesText}3{ext}";
        }
        else
        {
            var name = NameCustom ? CustomText : L["ex_orig_title"];
            // AUDIT X5: numbers only when there are several clips.
            text = Clips.Count > 1 ? $"{name} - 1{ext}, {name} - 2{ext}" : name + ext;
        }
        ExampleText = L["ex_label"] + text;
    }

    // ---------------- clips ----------------

    [RelayCommand]
    private void AddClip() => AddClipRow("", "");

    private void AddClipRow(string start, string end)
    {
        var row = new ClipRowViewModel(r => Clips.Remove(r)) { Start = start, End = end };
        row.PropertyChanged += (_, _) => UpdateExample();
        Clips.Add(row);
    }

    // ---------------- links (port of Invoke-SmartPaste, Add-PastedLinks, Accept-Link, Invoke-LinkEnter) ----------------

    /// <summary>
    /// Text pasted into the link box. Returns true when it contained links (handled here); false means "paste it as
    /// normal text" (the legacy put non-link text into the box).
    /// </summary>
    public bool SmartPaste(string? text)
    {
        var urls = UrlParser.LinksFromText(text);
        if (urls.Count == 0) return false;
        AddLinks(urls);
        return true;
    }

    /// <summary>Enter in the link box.</summary>
    public void LinkEnter()
    {
        var urls = UrlParser.LinksFromText(LinkText.Trim());
        if (urls.Count > 0) AddLinks(urls);
    }

    /// <summary>One link becomes the draft (the previous draft goes to the queue); several go straight to the queue.</summary>
    public void AddLinks(IReadOnlyList<string> urls)
    {
        if (urls.Count == 1)
        {
            AcceptLink(urls[0]);
            return;
        }
        if (HasDraft && !SendDraft(interactive: false)) return;
        foreach (var u in urls) AddDefaultItem(u);
        LinkText = "";
        ClearPreviewPanel();
    }

    private void AcceptLink(string url)
    {
        if (HasDraft && !SendDraft(interactive: false)) return;
        LinkText = "";
        _draftUrl = url;
        _draftKey = UrlParser.LinkKey(url);
        _editId = 0;
        OnPropertyChanged(nameof(HasDraft));
        RequestPreview(url);
    }

    private void AddDefaultItem(string url)
    {
        var prefix = SafeName.Make(SeriesText.Trim().Length > 0 ? SeriesText.Trim() : "Video", "Video");
        AddItem(new QueueItem { Url = url, Naming = NameSeries ? NamingMode.Series : NamingMode.Original, Prefix = prefix });
        // Like the legacy Add-ItemToQueue: start the YouTube title lookup so the queue shows names.
        _svc.Preview.Request(url, null);
    }

    private void AddItem(QueueItem item)
    {
        Items.Add(item with { Id = _nextItemId++ });
        SaveQueue();
        RebuildQueueRows();
    }

    /// <summary>Port of Send-Draft. Interactive = the user pressed the button (messages); else silent fallbacks.</summary>
    public bool SendDraft(bool interactive)
    {
        if (!HasDraft)
        {
            if (interactive) _ = _ui.ShowMessage(L["msg_nolink_draft"]);
            return false;
        }
        var clips = new List<ClipRange>();
        var n = 0;
        foreach (var row in Clips)
        {
            n++;
            if (row.IsEmpty) continue;
            foreach (var text in new[] { row.Start.Trim(), row.End.Trim() })
            {
                var tc = Timecode.Parse(text);
                if (!tc.Blank && !tc.Valid)
                {
                    var nl = Environment.NewLine + Environment.NewLine;
                    _ = _ui.ShowMessage(string.Format(L.Culture, L["msg_badtc"], n, text, nl), warning: true);
                    return false;
                }
            }
            clips.Add(new ClipRange(row.Start.Trim(), row.End.Trim()));
        }

        var naming = NameCustom ? NamingMode.Custom : NameSeries ? NamingMode.Series : NamingMode.Original;
        if (naming == NamingMode.Custom && CustomText.Trim().Length == 0)
        {
            if (interactive) { _ = _ui.ShowMessage(L["msg_custom"], warning: true); return false; }
            naming = NamingMode.Original;
        }
        var prefix = SeriesText.Trim();
        if (naming == NamingMode.Series && prefix.Length == 0)
        {
            if (interactive) { _ = _ui.ShowMessage(L["msg_prefix"], warning: true); return false; }
            prefix = "Video";
        }

        var item = new QueueItem
        {
            Url = _draftUrl, Format = SelectedFormatKey, Cap = SelectedCap, Naming = naming, Custom = CustomText.Trim(),
            Prefix = SafeName.Make(prefix.Length > 0 ? prefix : "Video", "Video"), Clips = clips,
            Fps = _svc.Preview.Get(_draftKey)?.Detail?.Fps,
        };
        var index = _editId > 0 ? Items.ToList().FindIndex(i => i.Id == _editId) : -1;
        if (index >= 0)
        {
            Items[index] = item with { Id = _editId };
            SaveQueue();
            RebuildQueueRows();
        }
        else
        {
            AddItem(item);
        }

        _draftUrl = "";
        _draftKey = "";
        _editId = 0;
        OnPropertyChanged(nameof(HasDraft));
        LinkText = "";
        ResetDraftFields();
        ClearPreviewPanel();
        return true;
    }

    [RelayCommand]
    private void SendToQueue() => SendDraft(interactive: true);

    private void ResetDraftFields()
    {
        Clips.Clear();
        IsAudio = false;
        SelectedFormat = FormatChoices.FirstOrDefault();
        SelectedResolution = "1080p";
        CustomText = "";
        if (NameCustom) NameOriginal = true;
        UpdateHint();
        UpdateExample();
    }

    /// <summary>Port of Import-LinksFromFile (Load .txt and dropping a file).</summary>
    public async Task ImportLinksFromFileAsync(string path)
    {
        string text;
        try { text = await File.ReadAllTextAsync(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _svc.Log.Write($"could not read {path}: {ex.Message}");
            text = "";
        }
        var urls = UrlParser.LinksFromText(text);
        if (urls.Count == 0)
        {
            await _ui.ShowMessage(L["msg_nolinks"]);
            return;
        }
        foreach (var u in urls) AddDefaultItem(u);
    }

    /// <summary>Something dropped on the Download tab: text with links, or .txt files.</summary>
    public async Task DropAsync(string? text, IReadOnlyList<string> files)
    {
        foreach (var f in files) await ImportLinksFromFileAsync(f);
        if (files.Count == 0 && text is not null)
        {
            var urls = UrlParser.LinksFromText(text);
            if (urls.Count > 0) AddLinks(urls);
        }
    }

    // ---------------- preview (port of Request-Preview / Show-Preview / Get-WillText) ----------------

    private DetailRequest DetailRequestNow() =>
        new(SelectedFormatKey, SelectedCap, _tools, CookieChain());

    private void RequestPreview(string url)
    {
        _svc.Preview.Request(url, IsRunning ? null : DetailRequestNow());
        ShowPreview();
    }

    private void OnPreviewChanged(string key)
    {
        if (key == _draftKey) ShowPreview();
        RefreshQueueTitles();
    }

    private void ClearPreviewPanel()
    {
        PreviewImage = null;
        PreviewTitle = L["prev_hint"];
        PreviewMeta = "";
    }

    private string? _shownImageKey;

    private void ShowPreview()
    {
        if (_draftKey.Length == 0 || _svc.Preview.Get(_draftKey) is not { } p)
        {
            ClearPreviewPanel();
            return;
        }
        if (p.Thumbnail is { } bytes && _shownImageKey != p.Key + bytes.Length)
        {
            try
            {
                PreviewImage = new Bitmap(new MemoryStream(bytes));
                _shownImageKey = p.Key + bytes.Length;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or NotSupportedException)
            {
                PreviewImage = null;
            }
        }
        else if (p.Thumbnail is null)
        {
            PreviewImage = null;
            _shownImageKey = null;
        }

        if (p.Meta == MetaState.Loading || (!p.IsYoutube && p.Details == DetailState.Loading && p.Title.Length == 0))
        {
            PreviewTitle = L["prev_loading"];
            PreviewMeta = "";
            return;
        }
        if (p.Meta == MetaState.NotFound || p.Title.Length == 0)
        {
            PreviewTitle = L["prev_none"];
            PreviewMeta = L["prev_none2"] + (p.FailReason.Length > 0 ? Environment.NewLine + ReasonText(p.FailReason) : "");
            return;
        }
        PreviewTitle = p.Title;
        var bits = new List<string>();
        if (p.Author.Length > 0) bits.Add(p.Author);
        if (p.Detail is { } d)
        {
            if (d.Duration.Length > 0) bits.Add(d.Duration);
            if (d.Date.Length > 0) bits.Add(d.Date);
        }
        var line2 = p.Details switch
        {
            DetailState.Loading => L["prev_check"],
            DetailState.Done => WillText(p.Detail!),
            DetailState.NoFormat => L["prev_noformat"],
            DetailState.Failed => L.Format("prev_fail_reason", ReasonText(p.FailReason)),
            _ => "",
        };
        PreviewMeta = string.Join("  |  ", bits) + Environment.NewLine + line2;
    }

    private string ReasonText(string reason) => reason switch
    {
        "timeout" => L["prev_reason_timeout"],
        "yt-dlp is not installed" => L["prev_reason_notools"],
        _ => reason,   // a yt-dlp error line: technical, kept as it is (the same text is in the log)
    };

    private string WillText(PreviewDetails d)
    {
        var fmt = FormatCatalog.Get(SelectedFormatKey);
        if (fmt.Kind == MediaKind.Audio) return L.Format("prev_audio", fmt.Label);
        string will;
        if (fmt.Key == "movp")
        {
            will = "ProRes 422 HQ" + (d.Resolution.Length > 0 ? $" ({d.Resolution})" : "");
        }
        else
        {
            will = string.Join(' ', new[] { d.Resolution, d.VideoLabel }.Where(x => x.Length > 0));
            if (d.AudioLabel.Length > 0) will += " + " + d.AudioLabel;
        }
        if (fmt.IsH264 && d.VideoLabel != "H.264") will += L["prev_conv"];
        return L.Format("prev_will", will.Trim());
    }

    // ---------------- folder ----------------

    [RelayCommand]
    private async Task BrowseFolder()
    {
        var picked = await _ui.PickFolder(Directory.Exists(Folder) ? Folder : null);
        if (picked is not null) Folder = picked;
    }

    [RelayCommand]
    private void OpenSaveFolder()
    {
        var f = Folder.Trim('"', ' ');
        if (f.Length > 0 && Directory.Exists(f)) _ui.OpenFolder(f);
        else _ = _ui.ShowMessage(L["msg_nofolder"]);
    }

    [RelayCommand]
    private async Task ShowOptions()
    {
        var vm = OptionsViewModel.From(Settings);
        if (await _ui.ShowOptions(vm))
        {
            vm.ApplyTo(Settings);
            SaveSettings();
            if (_draftKey.Length > 0 && !IsRunning) _svc.Preview.Refresh(_draftKey, DetailRequestNow());
        }
    }
}
