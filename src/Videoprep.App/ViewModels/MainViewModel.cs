using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Videoprep.App.Services;
using Videoprep.Core;
using Videoprep.Core.Engine;
using Videoprep.Core.I18n;
using Videoprep.Core.Models;
using Videoprep.Core.Storage;
using Videoprep.Core.Themes;

namespace Videoprep.App.ViewModels;

/// <summary>
/// Everything behind the main window. Split by tab: MainViewModel.Download.cs, .Queue.cs, .Run.cs, .Settings.cs,
/// .Log.cs. One object (like the legacy script's shared state) keeps the cross-tab rules simple: pasting a second link
/// sends the first to the queue, Edit goes back to Download, Download runs the queue, and so on.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppServices _svc;
    private readonly IUiServices _ui;
    private readonly Action<Action> _post;

    public Loc L => Loc.Instance;
    public Settings Settings { get; }

    /// <summary>The queue (legacy $Items). Saved to queue.json after every change (AUDIT H6).</summary>
    public ObservableCollection<QueueItem> Items { get; } = [];
    private int _nextItemId = 1;

    /// <summary>0 Download, 1 Queue, 2 Scraping, 3 Settings, 4 Log.</summary>
    [ObservableProperty] private int _selectedTab;

    public const int TabDownload = 0, TabQueue = 1, TabScraping = 2, TabSettings = 3, TabLog = 4;

    public MainViewModel(AppServices services, IUiServices ui, Action<Action>? post = null)
    {
        _svc = services;
        _ui = ui;
        _post = post ?? (a => Dispatcher.UIThread.Post(a));

        _svc.Paths.MigrateFromVideoDownloader(Path.Combine(Path.GetDirectoryName(_svc.Paths.Roaming)!, "VideoDownloader"));
        Settings = SettingsStore.Load(_svc.Paths.SettingsFile, out var problem);
        if (problem is not null) _svc.Log.Write("WARN  " + problem);
        Loc.Instance.Language = Settings.Lang.Length > 0 ? Settings.Lang : Localizer.DefaultLanguage();

        _svc.Log.LineAdded += line => _post(() => AppendLog(line));
        _svc.Preview.Changed += key => _post(() => OnPreviewChanged(key));
        _svc.Info.Changed += key => _post(() => OnInfoChanged(key));

        var saved = QueueStore.Load(_svc.Paths.QueueFile, out var qProblem);
        foreach (var it in saved)
        {
            Items.Add(it);
            _nextItemId = Math.Max(_nextItemId, it.Id + 1);
        }
        if (qProblem is not null) _svc.Log.Write("WARN  " + qProblem);

        InitDownload();
        InitSettings();
        InitClipboard();
        InitUpdates();
        InitProfiles();
        Scraping = new ScrapingViewModel(this);
        RebuildProfileChoices();   // the Scraping boxes get the profiles too
        RebuildQueueRows();
        Loc.Instance.PropertyChanged += (_, e) => { if (e.PropertyName == "Item[]") OnLanguageChanged(); };
    }

    /// <summary>Called once the window is visible (port of the form's Shown handler).</summary>
    public async Task OnShownAsync()
    {
        RefreshTools();
        var missing = _tools.Missing();
        _svc.Log.Write(missing.Count > 0
            ? $"First run: press Download and the app will fetch what it needs ({string.Join(", ", missing)}, about 200 MB, once)."
            : "Ready. Paste a link, choose the format and press Send to queue, then Download.");
        var browsers = _svc.InstalledBrowsers();
        if (browsers.Count > 0) _svc.Log.Write("Browsers found on this PC: " + string.Join(", ", browsers.Select(BrowserName)));
        if (Items.Count > 0) _svc.Log.Write($"Queue restored: {Items.Count} item(s).");
        // "Revisar al abrir videoprep": in the background, the window stays usable.
        _ = Scraping.Subscriptions.OnShownAsync();
        await OnShownUpdatesAsync();

        if (Settings.AutoUpdate && missing.Count == 0 && UpdateIsDue(Settings.LastUpdate, DateTimeOffset.Now))
        {
            Settings.LastUpdate = DateTimeOffset.Now.ToString("o");
            SaveSettings();
            await UpdateToolsAsync(switchToLog: false);
        }
    }

    /// <summary>Weekly yt-dlp update (legacy: 7 days since the last one).</summary>
    public static bool UpdateIsDue(string lastUpdate, DateTimeOffset now) =>
        !DateTimeOffset.TryParse(lastUpdate, System.Globalization.CultureInfo.InvariantCulture,
                                 System.Globalization.DateTimeStyles.RoundtripKind, out var last)
        || (now - last).TotalDays >= 7;

    /// <summary>Window closing (port of FormClosing): confirm if something runs, stop it, save, delete the session cookie copy.</summary>
    public async Task<bool> CanCloseAsync()
    {
        if (IsRunning)
        {
            if (!await _ui.AskYesNo("videoprep", L["msg_running"])) return false;
            _runCts?.Cancel();
        }
        _svc.Preview.CancelDetails();
        StopTitleLookup();
        _svc.Info.Stop();
        _clipboard.Stop();
        SaveSettings();
        SaveQueue();
        try
        {
            if (File.Exists(_svc.Paths.SessionCookieFile)) File.Delete(_svc.Paths.SessionCookieFile);
        }
        catch (IOException ex) { _svc.Log.Write("could not delete the session cookie copy: " + ex.Message); }
        return true;
    }

    public void SaveSettings()
    {
        Settings.Folder = Folder;
        Settings.Naming = NameSeries ? "series" : "orig";
        Settings.Prefix = SeriesText;
        try { SettingsStore.Save(_svc.Paths.SettingsFile, Settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _svc.Log.Write("WARN  could not save the settings: " + ex.Message);
        }
    }

    private void SaveQueue()
    {
        try { QueueStore.Save(_svc.Paths.QueueFile, Items); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _svc.Log.Write("WARN  could not save the queue: " + ex.Message);
        }
    }

    private void OnLanguageChanged()
    {
        RebuildFormatLists();
        RebuildSettingsLists();
        RebuildQueueRows();
        RebuildProfileChoices();
        RefreshMini();
        OnPropertyChanged(nameof(SourceListFormats));
        UpdateExample();
        ShowPreview();
        if (!IsRunning) StatusText = L["st_ready"];
        ToolsStatus = ToolLines(L["tool_found"], L["tool_missing"]);
        OnPropertyChanged(nameof(AboutText));
        OnPropertyChanged(nameof(UpdateBannerText));
        OnPropertyChanged(nameof(InstalledVersionText));
    }

    private static string BrowserName(string key) => CookieStrategy.Browsers.FirstOrDefault(b => b.Key == key)?.Name ?? key;

    public static Palette PaletteFor(Settings s) => Palette.For(s.Theme, s.CustomColor);

    public string AboutText => string.Format(Loc.Instance.Culture, L["txt_about"], AppInfo.Version);
}
