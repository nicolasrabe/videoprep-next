using CommunityToolkit.Mvvm.Input;
using Videoprep.Core.Preview;
using Videoprep.Core.Text;

namespace Videoprep.App.ViewModels;

/// <summary>RONDA 4 (spec D): "Descargar miniatura..." from a queue row, a Scraping row and the Prep preview.</summary>
public sealed partial class MainViewModel
{
    /// <summary>The queue row (first selected) -> its thumbnail.</summary>
    [RelayCommand]
    private Task SaveQueueThumbnail() =>
        _selectedRows.FirstOrDefault() is { } row ? SaveThumbnailAsync(row.Url, null) : Task.CompletedTask;

    /// <summary>The link in Prep -> its thumbnail.</summary>
    [RelayCommand]
    private Task SaveDraftThumbnail() => HasDraft ? SaveThumbnailAsync(_draftUrl, PreviewTitle) : Task.CompletedTask;

    /// <summary>
    /// The Save as dialog opens at once (suggested name = the sanitized title); the picture is looked for after the name is chosen:
    /// the best one known (cache), or the best one of the site. JPG/PNG: the same bytes when the format matches, ffmpeg when not.
    /// </summary>
    public async Task SaveThumbnailAsync(string url, string? knownTitle)
    {
        var key = UrlParser.LinkKey(url);
        var title = !string.IsNullOrWhiteSpace(knownTitle) ? knownTitle! : TitleOf(new Core.Models.QueueItem { Url = url }) ?? key;
        var path = await _ui.PickSaveThumbnail(SafeName.Make(title));
        if (string.IsNullOrEmpty(path)) return;
        StatusText = L.Format("loading_fmt", L["load_thumb"]);   // asked at the click when it was not loaded
        var bytes = await BestThumbnailBytesAsync(url, key);
        if (bytes is null)
        {
            StatusText = "";
            await _ui.ShowMessage(L["thumb_none"], warning: true);
            return;
        }
        if (_tools.Ffmpeg is null) RefreshTools();
        var error = await ThumbnailExporter.SaveAsync(bytes, path, ThumbnailExporter.FormatOf(path), _tools.Ffmpeg, _svc.Runner, _svc.Log);
        if (error.Length == 0) StatusText = L.Format("thumb_saved", Path.GetFileName(path));
        else
        {
            _svc.Log.Write("Thumbnail: not saved: " + error);
            StatusText = "";
            await _ui.ShowMessage(L["thumb_fail"], warning: true);
        }
    }

    /// <summary>The best picture known for a link; for sites other than YouTube, asks for the card data (one run) and waits for it.</summary>
    internal async Task<byte[]?> BestThumbnailBytesAsync(string url, string key)
    {
        if (_svc.Thumbs?.TryGet(key) is { } cached) return cached;
        if (UrlParser.IsYoutubeKey(key) && _svc.Thumbs is { } thumbs) return await thumbs.GetYoutubeAsync(key);
        if (_svc.Info.Get(key) is { Thumbnail: { } known }) return known;
        if (_svc.Preview.Get(key) is { Thumbnail: { } shown }) return shown;
        RefreshTools();
        var done = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged(string k)
        {
            if (k != key) return;
            var info = _svc.Info.Get(key);
            if (info is { Thumbnail: { } b }) done.TrySetResult(b);
            else if (info is { State: InfoState.Failed }) done.TrySetResult(null);
            else if (info is { State: InfoState.Ready })
                _ = Task.Delay(_svc.Thumbs is null ? 0 : 8000).ContinueWith(_ => done.TrySetResult(_svc.Info.Get(key)?.Thumbnail), TaskScheduler.Default);   // the picture comes a moment after the data
        }
        _svc.Info.Changed += OnChanged;
        try
        {
            _svc.Info.Request(url, new InfoRequest(_tools, CookieChain()));
            OnChanged(key);
            return await done.Task.WaitAsync(TimeSpan.FromSeconds(90)).ConfigureAwait(true);
        }
        catch (TimeoutException) { return null; }
        finally { _svc.Info.Changed -= OnChanged; }
    }
}
