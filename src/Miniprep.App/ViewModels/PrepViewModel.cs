using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Miniprep.App.Logic;
using Miniprep.App.Services;
using Videoprep.Core.Engine;
using Videoprep.Core.Import;
using Videoprep.Core.Models;
using Videoprep.Core.Preview;
using Videoprep.Core.Text;

namespace Miniprep.App.ViewModels;

/// <summary>
/// The Prep tab: one link in the box, its preview (picture, title, channel, description) and the two buttons.
/// "Descargar..." downloads only this link, with the Windows "Guardar como"; "Añadir a la cola" adds it without downloading.
/// </summary>
public sealed partial class PrepViewModel : ObservableObject
{
    private readonly MiniServices _svc;
    private readonly MiniSettings _settings;
    private readonly MiniQueue _queue;
    private readonly IMiniUi _ui;
    private readonly IDownloader _downloader;
    private readonly ToolsGate _tools;
    private readonly RunGate _gate;
    private readonly Action<Action> _post;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Action<string> _toast;

    private CancellationTokenSource? _typing;
    private CancellationTokenSource? _run;
    private Task _pending = Task.CompletedTask;
    private string _link = "";
    private string _key = "";
    private bool _useCookies;
    private DownloadRequest? _failedDownload;

    public PrepViewModel(MiniServices svc, MiniSettings settings, MiniQueue queue, IMiniUi ui, IDownloader downloader, ToolsGate tools, RunGate gate,
                         Action<string> toast, Action<Action>? post = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _svc = svc; _settings = settings; _queue = queue; _ui = ui; _downloader = downloader; _tools = tools; _gate = gate; _toast = toast;
        _post = post ?? (a => Avalonia.Threading.Dispatcher.UIThread.Post(a));
        _delay = delay ?? Task.Delay;
        svc.Info.Changed += key => _post(() => { if (key == _key) Refresh(); });
        _tools.Changed += () => _post(UpdateStatus);
    }

    public Loc L => Loc.Instance;

    [ObservableProperty] private string _url = "";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isReady;
    [ObservableProperty] private Bitmap? _thumb;
    [ObservableProperty] private string _duration = "";
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _channelLine = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _errorText = "";
    [ObservableProperty] private bool _canRetryCookies;
    [ObservableProperty] private string _noticeText = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private int _progress;
    [ObservableProperty] private bool _isDownloading;

    public bool HasThumb => Thumb is not null;
    public bool ShowPlaceholder => Thumb is null;
    public bool PreviewLoading => IsLoading && Thumb is null;
    public bool ShowDescriptionLoading => IsLoading;
    public bool HasDuration => Duration.Length > 0;
    public bool HasTitle => Title.Length > 0;
    public bool HasChannelLine => ChannelLine.Length > 0;
    public bool HasDescription => Description.Length > 0;
    public bool HasError => ErrorText.Length > 0;
    public bool HasNotice => NoticeText.Length > 0;
    public bool CanAct => IsReady && !IsDownloading;
    public bool ShowActions => !IsDownloading;

    partial void OnUrlChanged(string value)
    {
        _typing?.Cancel();
        _typing = new CancellationTokenSource();
        _pending = LookupAfterPauseAsync(value, _typing.Token);
    }

    /// <summary>Waits for the pending lookup of the link in the box (for tests).</summary>
    public Task Settled() => _pending;

    private async Task LookupAfterPauseAsync(string text, CancellationToken ct)
    {
        try { await _delay(TimeSpan.FromMilliseconds(350), ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }
        await LookupAsync(text.Trim()).ConfigureAwait(false);
    }

    private void Clear(bool keepNotice = false)
    {
        _key = ""; _link = ""; _useCookies = false; _failedDownload = null; _finalNote = "";
        _post(() =>
        {
            IsLoading = false; IsReady = false; Thumb = null; Duration = ""; Title = ""; ChannelLine = ""; Description = "";
            ErrorText = ""; CanRetryCookies = false;
            if (!keepNotice) NoticeText = "";
            UpdateStatus();
        });
    }

    private async Task LookupAsync(string text)
    {
        if (IsDownloading) return;
        Clear();
        if (text.Length == 0) return;
        var link = UrlParser.ToAnyUrl(text);
        if (link is null) { _post(() => NoticeText = L["mp_not_link"]); return; }
        switch (LinkClassifier.Classify(link).Kind)
        {
            case LinkKind.Playlist or LinkKind.Channel: _post(() => NoticeText = L["mp_list_notice"]); return;
            case LinkKind.NotVideo: _post(() => NoticeText = L["mp_not_link"]); return;
        }
        _link = link;
        _key = UrlParser.LinkKey(link);
        await RequestInfoAsync();
    }

    private async Task RequestInfoAsync()
    {
        _gate.InfoBusy = true;
        _post(() => { IsLoading = true; ErrorText = ""; CanRetryCookies = false; UpdateStatus(); });
        var paths = await _tools.GetAsync().ConfigureAwait(false);
        if (paths is null)
        {
            _gate.InfoBusy = false;
            _post(() => { IsLoading = false; ErrorText = L["mp_err_tools"]; UpdateStatus(); });
            return;
        }
        var req = new InfoRequest(paths, _svc.Cookies(_useCookies));
        var key = _key;
        var info = _svc.Info.Request(_link, req);
        _post(() => { if (key == _key) Show(info); });
    }

    private void Refresh()
    {
        if (_svc.Info.Get(_key) is { } info) Show(info);
    }

    private void Show(VideoInfo info)
    {
        if (info.Thumbnail is { Length: > 0 } bytes && Thumb is null) Thumb = ToBitmap(bytes);
        Title = info.Title;
        Duration = info.Duration;
        ChannelLine = Display.ChannelLine(info.Channel, info.UploadDate, info.Views, L.Language);
        Description = info.Description;
        switch (info.State)
        {
            case InfoState.Loading:
                IsLoading = true; IsReady = false;
                break;
            case InfoState.Ready:
                IsLoading = false; IsReady = true; ErrorText = ""; CanRetryCookies = false;
                _gate.InfoBusy = false;
                break;
            case InfoState.Failed:
                IsLoading = false; IsReady = false;
                _gate.InfoBusy = false;
                ShowFailure(info.FailReason, readingFailed: true);
                break;
        }
        UpdateStatus();
    }

    private void ShowFailure(string reason, bool readingFailed)
    {
        var login = CookieStrategy.AsksForLogin(reason);
        ErrorText = login ? L["mp_err_login"] : L.Format(readingFailed ? "mp_err_read_fmt" : "mp_err_dl_fmt", Short(reason));
        CanRetryCookies = login && !_useCookies;
    }

    private static string Short(string reason)
    {
        var line = reason.Split('\n', '\r').LastOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "";
        return line.Length > 160 ? line[..160] + "…" : line;
    }

    private static Bitmap? ToBitmap(byte[] bytes)
    {
        try { return new Bitmap(new MemoryStream(bytes)); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException) { return null; }
    }

    private string _finalNote = "";

    private void UpdateStatus()
    {
        if (IsDownloading) return;
        StatusText = _tools.Installing ? L["mp_tools_install"]
            : IsLoading ? L["mp_reading"]
            : IsReady ? (_finalNote.Length > 0 ? _finalNote : L["mp_ready"])
            : "";
    }

    // ---------------- buttons ----------------

    [RelayCommand]
    private async Task Paste()
    {
        var text = await _ui.GetClipboardText();
        if (string.IsNullOrWhiteSpace(text)) return;
        var first = UrlParser.LinksFromText(text).FirstOrDefault() ?? text.Trim();
        Url = first;
        await _pending;
    }

    [RelayCommand]
    private async Task Retry()
    {
        if (_failedDownload is { } d)
        {
            await RunDownloadAsync(d);
            return;
        }
        if (_link.Length == 0) return;
        var paths = await _tools.GetAsync();
        if (paths is null) { ErrorText = L["mp_err_tools"]; return; }
        _gate.InfoBusy = true;
        IsLoading = true; ErrorText = ""; CanRetryCookies = false;
        UpdateStatus();
        _svc.Info.Retry(_link, new InfoRequest(paths, _svc.Cookies(_useCookies)));
    }

    /// <summary>Only after the person pressed it: the browser's cookies are read for this link.</summary>
    [RelayCommand]
    private async Task RetryWithCookies()
    {
        _useCookies = true;
        if (_failedDownload is { } d) _failedDownload = d with { BrowserCookies = true };
        await Retry();
    }

    [RelayCommand]
    private void Add()
    {
        if (!CanAct || _link.Length == 0) return;
        _queue.Add(_link, _svc.Info.Get(_key));
        _toast(L["mp_added"]);
    }

    [RelayCommand]
    private async Task Download()
    {
        if (!CanAct) return;
        if (_gate.Busy) { await _ui.ShowMessage(L["mp_busy"]); return; }
        var start = _settings.LastSaveFolder.Length > 0 ? _settings.LastSaveFolder : _settings.EffectiveFolder();
        var path = await _ui.PickSaveFile(Names.Suggested(Title), start);
        if (path is null) return;
        var (folder, name, format) = Names.FromSavePath(path);
        _settings.LastSaveFolder = folder;
        try { _settings.Save(_svc.Paths.SettingsFile); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _svc.Log.Write("WARN  settings could not be saved: " + ex.Message); }
        var item = new QueueItem { Id = 0, Url = _link, Format = format, Cap = Qualities.Cap(Quality.P1080), Naming = NamingMode.Custom, Custom = name };
        await RunDownloadAsync(new DownloadRequest(item, folder, _useCookies));
    }

    private async Task RunDownloadAsync(DownloadRequest request)
    {
        if (_gate.Busy) return;
        _gate.Busy = true;
        _failedDownload = null;
        ErrorText = ""; CanRetryCookies = false;
        IsDownloading = true; Progress = 0;
        StatusText = L.Format("mp_downloading_pct", 0);
        _run = new CancellationTokenSource();
        var progress = new SyncProgress(p => _post(() =>
        {
            Progress = p.Percent;
            StatusText = p.Speed.Length > 0 ? L.Format("mp_downloading_fmt", p.Percent, p.Speed) : L.Format("mp_downloading_pct", p.Percent);
        }));
        DownloadResult result;
        try { result = await _downloader.RunAsync(request, progress, _run.Token); }
        finally { _run.Dispose(); _run = null; _gate.Busy = false; }
        _post(() =>
        {
            IsDownloading = false; Progress = 0;
            if (result.Cancelled) { _finalNote = L["mp_dl_stopped"]; StatusText = _finalNote; }
            else if (result.Ok) { _finalNote = L["mp_dl_done"]; StatusText = _finalNote; _toast(_finalNote); }
            else
            {
                _failedDownload = request;
                StatusText = L["mp_ready"];
                ShowFailure(result.Reason, readingFailed: false);
                CanRetryCookies &= !request.BrowserCookies;
            }
            OnPropertyChanged(nameof(CanAct));
        });
    }

    [RelayCommand]
    private void Stop() => _run?.Cancel();

    // ---------------- derived properties for the screen ----------------

    partial void OnThumbChanged(Bitmap? value) { OnPropertyChanged(nameof(HasThumb)); OnPropertyChanged(nameof(ShowPlaceholder)); OnPropertyChanged(nameof(PreviewLoading)); }
    partial void OnIsLoadingChanged(bool value) { OnPropertyChanged(nameof(PreviewLoading)); OnPropertyChanged(nameof(ShowDescriptionLoading)); OnPropertyChanged(nameof(CanAct)); }
    partial void OnIsReadyChanged(bool value) => OnPropertyChanged(nameof(CanAct));
    partial void OnIsDownloadingChanged(bool value) { OnPropertyChanged(nameof(CanAct)); OnPropertyChanged(nameof(ShowActions)); }
    partial void OnDurationChanged(string value) => OnPropertyChanged(nameof(HasDuration));
    partial void OnTitleChanged(string value) => OnPropertyChanged(nameof(HasTitle));
    partial void OnChannelLineChanged(string value) => OnPropertyChanged(nameof(HasChannelLine));
    partial void OnDescriptionChanged(string value) => OnPropertyChanged(nameof(HasDescription));
    partial void OnErrorTextChanged(string value) => OnPropertyChanged(nameof(HasError));
    partial void OnNoticeTextChanged(string value) => OnPropertyChanged(nameof(HasNotice));
}

/// <summary>An <see cref="IProgress{T}"/> that calls back on the same thread that reports (no SynchronizationContext surprises).</summary>
internal sealed class SyncProgress(Action<DownloadProgress> on) : IProgress<DownloadProgress>
{
    public void Report(DownloadProgress value) => on(value);
}
