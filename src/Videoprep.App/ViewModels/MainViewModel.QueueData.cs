using CommunityToolkit.Mvvm.Input;
using Videoprep.Core.Engine;
using Videoprep.Core.Models;
using Videoprep.Core.Preview;

namespace Videoprep.App.ViewModels;

/// <summary>
/// Data of the queue items (title, picture, description): completed in the background for items that have none (for example the ones
/// saved by an earlier session), one at a time with a short pause and stopping at the first "too many requests" answer; kept in the
/// queue file so they are not asked for again; "Actualizar datos" asks again for the selected rows.
/// </summary>
public sealed partial class MainViewModel
{
    private bool _enriching;
    private readonly Dictionary<string, string> _dataErrors = [];
    private readonly HashSet<string> _enrichGaveUp = [];

    /// <summary>Pause between two background requests (tests make it instant through <c>AppServices.Delay</c>).</summary>
    private static readonly TimeSpan EnrichPause = TimeSpan.FromSeconds(1.5);

    partial void OnSelectedTabChanged(int value)
    {
        if (value == TabQueue) StartEnrich();
    }

    /// <summary>The background completion that is running now (tests wait for it).</summary>
    public Task EnrichTask { get; private set; } = Task.CompletedTask;

    /// <summary>"Descargar miniatura..." works on any selected row, whatever its state (Listo, Descargando, En cola, error).</summary>
    public bool CanThumbnail => _selectedRows.Count > 0;

    private bool NeedsData(QueueItem it) => it.Title.Length == 0 && !_enrichGaveUp.Contains(it.VideoKey);

    /// <summary>Starts completing the data of the items that have none. Safe to call often.</summary>
    public void StartEnrich()
    {
        if (_enriching || IsRunning || !Items.Any(NeedsData)) return;
        _enriching = true;
        UpdateDataFlags();
        EnrichTask = EnrichLoopAsync();
    }

    private async Task EnrichLoopAsync()
    {
        try
        {
            while (!IsRunning)
            {
                // The open or selected rows first, then the rest from the top (the visible ones).
                var order = QueueRows.OrderBy(r => r.IsExpanded ? 0 : _selectedRows.Contains(r) ? 1 : 2).Select(r => r.ItemId).Distinct().ToList();
                var item = order.Select(id => Items.FirstOrDefault(i => i.Id == id)).FirstOrDefault(i => i is not null && NeedsData(i));
                if (item is null) break;
                RefreshTools();
                if (_tools.YtDlp is null) break;
                var info = await AskInfoAsync(item.Url, retry: false);
                OnInfoForQueueData(item.VideoKey);                          // keep it now, not when the posted notification arrives
                if (info.State == InfoState.Ready && Items.FirstOrDefault(i => i.Id == item.Id) is { Title.Length: 0 }) _enrichGaveUp.Add(item.VideoKey);   // nothing to keep: never loop on it
                if (info.State == InfoState.Failed)
                {
                    NoteDataFailure(item.VideoKey, item.Url, info.FailReason);
                    // The site asked for fewer requests or a verification: stop here, the rest waits for the next time.
                    if (CookieStrategy.BlockedOutput().IsMatch(info.FailReason)) break;
                }
                await _svc.Delay(EnrichPause, CancellationToken.None);
            }
        }
        finally
        {
            _enriching = false;
            UpdateDataFlags();
        }
    }

    /// <summary>Asks the info service for a link and waits until it answers (ready or failed).</summary>
    private async Task<VideoInfo> AskInfoAsync(string url, bool retry)
    {
        var key = Core.Text.UrlParser.LinkKey(url);
        var done = new TaskCompletionSource<VideoInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged(string k)
        {
            if (k == key && _svc.Info.Get(key) is { State: not InfoState.Loading } i) done.TrySetResult(i);
        }
        _svc.Info.Changed += OnChanged;
        try
        {
            var req = new InfoRequest(_tools, CookieChain());
            if (retry) _svc.Info.Retry(url, req);
            else _svc.Info.Request(url, req);
            OnChanged(key);
            return await done.Task.WaitAsync(TimeSpan.FromSeconds(120)).ConfigureAwait(true);
        }
        catch (TimeoutException)
        {
            return new VideoInfo { Key = key, Url = url, State = InfoState.Failed, FailReason = "timeout" };
        }
        finally { _svc.Info.Changed -= OnChanged; }
    }

    private void NoteDataFailure(string key, string url, string reason)
    {
        _enrichGaveUp.Add(key);
        var text = ReasonText(reason);
        _dataErrors[key] = text;
        _svc.Log.Write($"Queue data: could not read {url}: {reason}");
        UpdateDataFlags();
    }

    /// <summary>The info service has news: keep what a ready card says in the queue items that lack it.</summary>
    private void OnInfoForQueueData(string key)
    {
        if (_svc.Info.Get(key) is not { State: InfoState.Ready } info || info.Title.Length == 0) return;
        var changed = false;
        for (var i = 0; i < Items.Count; i++)
        {
            var it = Items[i];
            if (it.VideoKey != key) continue;
            if (it.Title == info.Title && it.Description == info.Description) continue;
            Items[i] = it with
            {
                Title = info.Title, Channel = info.Channel, UploadDate = info.UploadDate, Views = info.Views,
                Duration = info.Duration, Description = info.Description,
            };
            changed = true;
        }
        _dataErrors.Remove(key);
        _enrichGaveUp.Remove(key);
        if (changed) SaveQueue();
        UpdateDataFlags();
    }

    /// <summary>Marks the rows that are waiting for data or that failed (the row shows the link plus "Cargando los datos..." or the reason).</summary>
    private void UpdateDataFlags()
    {
        foreach (var r in QueueRows)
        {
            var item = Items.FirstOrDefault(i => i.Id == r.ItemId);
            r.DataLoading = item is not null && item.Title.Length == 0 && !_dataErrors.ContainsKey(r.VideoKey) && (_enriching || _refreshing.Contains(r.VideoKey));
            r.DataError = _dataErrors.GetValueOrDefault(r.VideoKey, "");
        }
    }

    private readonly HashSet<string> _refreshing = [];

    /// <summary>"Actualizar datos": asks again for title, picture and description of the selected rows, one at a time.</summary>
    [RelayCommand]
    private async Task RefreshQueueData()
    {
        var items = SelectedItems().GroupBy(i => i.VideoKey).Select(g => g.First()).ToList();
        if (items.Count == 0) return;
        RefreshTools();
        if (_tools.YtDlp is null) { await _ui.ShowMessage(L["msg_tools_missing"], warning: true); return; }
        foreach (var it in items)
        {
            _refreshing.Add(it.VideoKey);
            _dataErrors.Remove(it.VideoKey);
            _enrichGaveUp.Remove(it.VideoKey);
            _svc.Thumbs?.Forget(it.VideoKey);
            for (var i = 0; i < Items.Count; i++)
                if (Items[i].VideoKey == it.VideoKey) Items[i] = Items[i] with { Title = "", Channel = "", UploadDate = "", Views = null, Duration = "", Description = "" };
            UpdateDataFlags();
        }
        foreach (var it in items)
        {
            var info = await AskInfoAsync(it.Url, retry: true);
            _refreshing.Remove(it.VideoKey);
            OnInfoForQueueData(it.VideoKey);
            if (info.State == InfoState.Failed)
            {
                NoteDataFailure(it.VideoKey, it.Url, info.FailReason);
                if (CookieStrategy.BlockedOutput().IsMatch(info.FailReason)) break;
            }
            if (it != items[^1]) await _svc.Delay(EnrichPause, CancellationToken.None);
        }
        _refreshing.Clear();
        UpdateRowNames();
        UpdateDataFlags();
        SaveQueue();
    }
}
