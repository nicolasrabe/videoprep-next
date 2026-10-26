using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Videoprep.Core.Engine;
using Videoprep.Core.Storage;
using Videoprep.Core.Themes;
using Videoprep.Core.Tools;

namespace Videoprep.App.ViewModels;

/// <summary>Settings tab: login cookies, tools, finishing options, theme and language.</summary>
public sealed partial class MainViewModel
{
    /// <summary>Automatic, each browser, cookies.txt file, none (same order as the legacy combo box).</summary>
    public ObservableCollection<string> CookieChoices { get; } = [];
    [ObservableProperty] private int _selectedCookieIndex;
    [ObservableProperty] private string _cookieFile = "";
    [ObservableProperty] private string _toolsStatus = "";
    [ObservableProperty] private bool _autoUpdate;
    [ObservableProperty] private bool _sound;
    [ObservableProperty] private bool _openDone;

    public ObservableCollection<string> ThemeChoices { get; } = [];
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomTheme))]
    private int _selectedThemeIndex;
    public bool IsCustomTheme => SelectedThemeIndex == Palette.Names.Count - 1;

    public IReadOnlyList<string> LanguageChoices { get; } = ["Español", "English"];
    [ObservableProperty] private int _selectedLanguageIndex;

    /// <summary>The App listens and repaints the window.</summary>
    public event Action<Palette>? PaletteChanged;

    private bool _loadingSettings;

    private void InitSettings()
    {
        _loadingSettings = true;
        CookieFile = Settings.CookieFile;
        AutoUpdate = Settings.AutoUpdate;
        Sound = Settings.Sound;
        OpenDone = Settings.OpenDone;
        SelectedLanguageIndex = L.Language == "es" ? 0 : 1;
        RebuildSettingsLists();
        _loadingSettings = false;
        StatusText = L["st_ready"];
    }

    private void RebuildSettingsLists()
    {
        var wasLoading = _loadingSettings;
        _loadingSettings = true;
        CookieChoices.Clear();
        CookieChoices.Add(L["ck_auto"]);
        foreach (var b in CookieStrategy.Browsers) CookieChoices.Add(b.Name);
        CookieChoices.Add(L["ck_file"]);
        CookieChoices.Add(L["ck_none"]);
        SelectedCookieIndex = Settings.CookieIndex(Settings.CookieSource);

        ThemeChoices.Clear();
        foreach (var n in Palette.Names) ThemeChoices.Add(L["th_" + n]);
        SelectedThemeIndex = Math.Max(0, Palette.Names.ToList().IndexOf(Settings.Theme));
        _loadingSettings = wasLoading;
    }

    partial void OnSelectedCookieIndexChanged(int value)
    {
        if (_loadingSettings || value < 0) return;
        Settings.CookieSource = Settings.CookieSourceFromIndex(value);
        SaveSettings();
    }

    partial void OnCookieFileChanged(string value)
    {
        if (_loadingSettings) return;
        Settings.CookieFile = value;
        SaveSettings();
    }

    partial void OnAutoUpdateChanged(bool value) { if (!_loadingSettings) { Settings.AutoUpdate = value; SaveSettings(); } }
    partial void OnSoundChanged(bool value) { if (!_loadingSettings) { Settings.Sound = value; SaveSettings(); } }
    partial void OnOpenDoneChanged(bool value) { if (!_loadingSettings) { Settings.OpenDone = value; SaveSettings(); } }

    partial void OnSelectedThemeIndexChanged(int value)
    {
        if (_loadingSettings || value < 0) return;
        Settings.Theme = Palette.Names[value];
        SaveSettings();
        PaletteChanged?.Invoke(PaletteFor(Settings));
    }

    partial void OnSelectedLanguageIndexChanged(int value)
    {
        if (_loadingSettings || value < 0) return;
        Settings.Lang = value == 0 ? "es" : "en";
        SaveSettings();
        L.Language = Settings.Lang;
    }

    [RelayCommand]
    private async Task PickColor()
    {
        var picked = await _ui.PickColor(Settings.CustomColor);
        if (picked is null) return;
        if (!Palette.TryParse(picked, out var c))
        {
            await _ui.ShowMessage(L["msg_bad_color"], warning: true);
            return;
        }
        Settings.CustomColor = Palette.ToHex(c.R, c.G, c.B);
        SaveSettings();
        PaletteChanged?.Invoke(PaletteFor(Settings));
    }

    [RelayCommand]
    private async Task BrowseCookieFile()
    {
        var path = await _ui.PickTextFile();
        if (path is null) return;
        CookieFile = path;
        SelectedCookieIndex = Settings.CookieIndex("file");
    }

    [RelayCommand]
    private void RecheckTools() => RefreshTools();

    /// <summary>Port of the Update tools button: yt-dlp updates itself; missing tools are downloaded.</summary>
    [RelayCommand]
    private Task UpdateTools() => UpdateToolsAsync(switchToLog: true);

    private async Task UpdateToolsAsync(bool switchToLog)
    {
        if (IsRunning) return;
        RefreshTools();
        if (_tools.YtDlp is null)
        {
            await _ui.ShowMessage(L["msg_tools_missing"]);
            return;
        }
        Settings.LastUpdate = DateTimeOffset.Now.ToString("o");
        SaveSettings();
        if (switchToLog) SelectedTab = TabLog;
        IsRunning = true;
        try
        {
            _svc.Log.Write("");
            _svc.Log.Write("=== Updating yt-dlp ===");
            var r = await _svc.Runner.RunAsync(ToolManager.UpdateYtDlp(_tools), _svc.Log.Write, CancellationToken.None);
            _svc.Log.Write($"--- Update finished (exit code {r.ExitCode}) ---");
            var missing = _tools.Missing().Where(m => m != "yt-dlp").ToList();
            if (missing.Count > 0) await InstallToolsAsync(missing);
        }
        finally
        {
            IsRunning = false;
            StatusText = L["st_ready"];
            RefreshTools();
        }
    }

    [RelayCommand]
    private async Task ClearArchive()
    {
        if (!await _ui.AskYesNo("videoprep", L["msg_cleararchive"])) return;
        try
        {
            if (File.Exists(_svc.Paths.ArchiveFile)) File.Delete(_svc.Paths.ArchiveFile);
            _svc.Log.Write("History of downloaded videos cleared.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _svc.Log.Write("could not clear the history: " + ex.Message);
        }
    }
}

/// <summary>Log tab.</summary>
public sealed partial class MainViewModel
{
    private readonly StringBuilder _logText = new();

    public string LogText => _logText.ToString();

    private void AppendLog(string line)
    {
        _logText.AppendLine(line);
        // Same limit as the legacy Write-Log: keep the window responsive with very long logs.
        if (_logText.Length > 400_000) _logText.Remove(0, 200_000);
        OnPropertyChanged(nameof(LogText));
    }

    [RelayCommand]
    private Task CopyLog() => _ui.SetClipboardText(LogText);

    [RelayCommand]
    private async Task SaveLog()
    {
        var path = await _ui.PickSaveFile("videoprep-log.txt");
        if (path is null) return;
        try { await File.WriteAllTextAsync(path, LogText, new UTF8Encoding(false)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _svc.Log.Write("could not save the log: " + ex.Message);
        }
    }

    [RelayCommand]
    private void ClearLog()
    {
        _logText.Clear();
        OnPropertyChanged(nameof(LogText));
    }

    /// <summary>Port of Invoke-TestSetup.</summary>
    [RelayCommand]
    private async Task TestSetup()
    {
        SelectedTab = TabLog;
        var log = _svc.Log;
        log.Write("");
        log.Write($"=== Test setup (videoprep {Core.AppInfo.Version}) ===");
        RefreshTools();
        foreach (var l in ToolLines("found", "MISSING").Split(Environment.NewLine)) log.Write("  " + l);
        foreach (var (name, version) in await ToolVersionsAsync()) log.Write($"  {name} version: {version}");
        var browsers = _svc.InstalledBrowsers();
        log.Write("  browsers with a profile: " + (browsers.Count > 0 ? string.Join(", ", browsers.Select(BrowserName)) : "none found"));
        log.Write("  cookie source order: " + string.Join(" > ", CookieChain().Select(c => c.Label)));
        var folder = Folder.Trim('"', ' ');
        if (folder.Length > 0)
        {
            try
            {
                Directory.CreateDirectory(folder);
                var probe = Path.Combine(folder, ".__write_test");
                await File.WriteAllTextAsync(probe, "x");
                File.Delete(probe);
                log.Write("  save folder is writable: " + folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                log.Write("  PROBLEM: cannot write to " + folder);
            }
        }
        log.Write("=== End of test ===");
    }

    private async Task<List<(string Name, string Version)>> ToolVersionsAsync()
    {
        var list = new List<(string, string)>();
        foreach (var (name, path, arg) in new[] { ("yt-dlp", _tools.YtDlp, "--version"), ("ffmpeg", _tools.Ffmpeg, "-version"), ("deno", _tools.Deno, "--version") })
        {
            if (path is null) continue;
            var first = "";
            var r = await _svc.Runner.RunAsync(new ProcessSpec(path, [arg]), l => { if (first.Length == 0) first = l; }, CancellationToken.None);
            list.Add((name, r.StartError is null ? first : "(could not run)"));
        }
        return list;
    }

    /// <summary>"Copy diagnostics" (SCRAPING_SPEC 3): versions, tools, browsers, last 200 log lines; no cookies, no tokens.</summary>
    [RelayCommand]
    private async Task CopyDiagnostics()
    {
        RefreshTools();
        var versions = await ToolVersionsAsync();
        var tools = new[] { ("yt-dlp", _tools.YtDlp), ("ffmpeg", _tools.Ffmpeg), ("ffprobe", _tools.Ffprobe), ("deno", _tools.Deno) }
            .Select(t => (t.Item1, t.Item2, versions.FirstOrDefault(v => v.Name == t.Item1).Version ?? "")).ToList();
        var text = Core.Logging.Diagnostics.Build(Core.AppInfo.Version, tools, _svc.InstalledBrowsers(), _svc.Log.Recent(200));
        await _ui.SetClipboardText(text);
        await _ui.ShowMessage(L["msg_diag_copied"]);
    }
}
