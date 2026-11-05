using System.Text.Json;
using System.Text.Json.Serialization;
using Videoprep.Core.Models;
using Videoprep.Core.Preview;
using Videoprep.Core.Storage;
using Videoprep.Core.Text;

namespace Miniprep.App.Logic;

/// <summary>
/// The queue of miniprep: a list of <see cref="QueueEntry"/> with every edit the screen offers. No UI here. It saves itself after each change
/// (<c>save</c>), and a row left "Descargando" by a crash comes back as "En cola". Safe to use from several threads (the background
/// completion of data and the window both edit it); <see cref="Changed"/> is raised outside the lock, on the thread that changed it.
/// </summary>
public sealed class MiniQueue
{
    private readonly object _gate = new();
    private readonly List<QueueEntry> _entries;
    private readonly Action<IReadOnlyList<QueueEntry>>? _save;
    private int _nextId;
    private bool _dirty;

    public MiniQueue(IEnumerable<QueueEntry>? entries = null, Action<IReadOnlyList<QueueEntry>>? save = null)
    {
        _entries = (entries ?? []).Select(e => e.Status == EntryStatus.Downloading ? e with { Status = EntryStatus.Queued } : e).ToList();
        _nextId = _entries.Count == 0 ? 1 : _entries.Max(e => e.Id) + 1;
        _save = save;
    }

    /// <summary>A copy of the rows, in order.</summary>
    public IReadOnlyList<QueueEntry> Entries { get { lock (_gate) return _entries.ToArray(); } }

    /// <summary>Raised after any change.</summary>
    public event Action? Changed;

    public QueueEntry? Get(int id) { lock (_gate) return _entries.FirstOrDefault(e => e.Id == id); }

    public int QueuedCount { get { lock (_gate) return _entries.Count(e => e.Status == EntryStatus.Queued); } }
    public int DoneCount { get { lock (_gate) return _entries.Count(e => e.Status == EntryStatus.Done); } }
    /// <summary>The number on the Cola tab: what is waiting or running.</summary>
    public int PendingCount { get { lock (_gate) return _entries.Count(e => e.Status is EntryStatus.Queued or EntryStatus.Downloading); } }

    public QueueEntry? NextQueued() { lock (_gate) return _entries.FirstOrDefault(e => e.Status == EntryStatus.Queued); }

    /// <summary>Runs an edit under the lock; if it changed something, the file is saved and <see cref="Changed"/> is raised after the lock.</summary>
    private T Locked<T>(Func<T> edit)
    {
        T result;
        bool raise;
        lock (_gate)
        {
            result = edit();
            raise = _dirty;
            _dirty = false;
        }
        if (raise) Changed?.Invoke();
        return result;
    }

    private void Touch()
    {
        _save?.Invoke(_entries.ToList());
        _dirty = true;
    }

    /// <summary>Adds a video without downloading it (MP4, 1080p). Known data (title, channel...) can come with it.</summary>
    public QueueEntry Add(string url, VideoInfo? info = null) => Locked(() =>
    {
        var item = new QueueItem { Id = _nextId++, Url = url, Format = "mp4", Cap = Qualities.Cap(Quality.P1080) };
        if (info is { State: InfoState.Ready }) item = WithInfo(item, info);
        var entry = new QueueEntry { Item = item };
        _entries.Add(entry);
        Touch();
        return entry;
    });

    /// <summary>The ✕ button and "Quitar de la cola". A row that is downloading is not removed (Detener first).</summary>
    public bool Remove(int id) => Locked(() =>
    {
        var e = _entries.FirstOrDefault(x => x.Id == id);
        if (e is null || e.Status == EntryStatus.Downloading) return false;
        _entries.Remove(e);
        Touch();
        return true;
    });

    /// <summary>"Quitar listos": returns how many were removed.</summary>
    public int RemoveDone() => Locked(() =>
    {
        var n = _entries.RemoveAll(e => e.Status == EntryStatus.Done);
        if (n > 0) Touch();
        return n;
    });

    /// <summary>
    /// Reorders by dragging: only "En cola" rows move, and they cannot land above a row that is downloading or finished.
    /// <paramref name="insertBefore"/> is an index in the list as it is now (Count = at the end).
    /// </summary>
    public bool Move(int id, int insertBefore) => Locked(() =>
    {
        var items = _entries.Select(e => e.Item).ToList();
        var status = _entries.ToDictionary(e => e.Id, e => e.Status);
        if (!QueueOrder.Move(items, [id], insertBefore, i => status[i.Id] != EntryStatus.Queued)) return false;
        var byId = _entries.ToDictionary(e => e.Id);
        for (var i = 0; i < items.Count; i++) _entries[i] = byId[items[i].Id];
        Touch();
        return true;
    });

    /// <summary>"Renombrar...": the file will be named like this (a clean name; empty = back to the video's title).</summary>
    public bool Rename(int id, string name)
    {
        var clean = (name ?? "").Trim().Length == 0 ? "" : SafeName.Make(name);
        return Locked(() => Edit(id, e => e with { Item = clean.Length == 0 ? e.Item with { Naming = NamingMode.Original, Custom = "" } : e.Item with { Naming = NamingMode.Custom, Custom = clean } }));
    }

    public bool SetFormat(int id, string format) =>
        format is "mp4" or "mp3" && Locked(() => Edit(id, e => e with { Item = e.Item with { Format = format } }));

    public bool SetQuality(int id, Quality q) =>
        Locked(() => Edit(id, e => e with { Quality = q, Item = e.Item with { Cap = Qualities.Cap(q) } }));

    public void SetStatus(int id, EntryStatus status, string error = "", string file = "") =>
        Locked(() => Edit(id, e => e with { Status = status, Error = status == EntryStatus.Error ? error : "", File = status == EntryStatus.Done ? file : e.File }));

    /// <summary>Title, channel, picture data... that were missing, filled in the background. Never overwrites what is known.</summary>
    public bool FillData(int id, VideoInfo info) =>
        info.State == InfoState.Ready && Locked(() => Edit(id, e => e with { Item = WithInfo(e.Item, info, onlyMissing: true) }, onlyIfChanged: true));

    private static QueueItem WithInfo(QueueItem item, VideoInfo info, bool onlyMissing = false)
    {
        string Pick(string current, string fresh) => onlyMissing && current.Length > 0 ? current : fresh.Length > 0 ? fresh : current;
        return item with
        {
            Title = Pick(item.Title, info.Title), Channel = Pick(item.Channel, info.Channel), UploadDate = Pick(item.UploadDate, info.UploadDate),
            Duration = Pick(item.Duration, info.Duration), Description = Pick(item.Description, info.Description),
            Views = onlyMissing && item.Views is not null ? item.Views : info.Views ?? item.Views,
        };
    }

    private bool Edit(int id, Func<QueueEntry, QueueEntry> change, bool onlyIfChanged = false)
    {
        var i = _entries.FindIndex(e => e.Id == id);
        if (i < 0) return false;
        var old = _entries[i];
        var now = change(old);
        if (now == old && onlyIfChanged) return false;
        _entries[i] = now;
        Touch();
        return true;
    }

    // ---------------- file ----------------

    private sealed record FileData(int Version, IReadOnlyList<QueueEntry> Entries);

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static void Save(string path, IEnumerable<QueueEntry> entries) =>
        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(new FileData(1, entries.ToList()), Json));

    /// <summary>Reads the queue file; a missing or damaged file gives an empty queue (<paramref name="problem"/> says why, for the log).</summary>
    public static MiniQueue Load(string path, out string? problem)
    {
        problem = null;
        if (File.Exists(path))
        {
            try
            {
                var data = JsonSerializer.Deserialize<FileData>(File.ReadAllText(path), Json);
                var all = data?.Entries ?? [];
                var good = all.Where(e => !string.IsNullOrWhiteSpace(e.Item.Url) && Videoprep.Core.Formats.FormatCatalog.IsKnown(e.Item.Format)).ToList();
                if (good.Count != all.Count) problem = $"{all.Count - good.Count} queue item(s) could not be read and were left out";
                return new MiniQueue(good, list => Save(path, list));
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                problem = $"queue file could not be read ({ex.GetType().Name}: {ex.Message}); starting with an empty queue";
            }
        }
        return new MiniQueue(null, list => Save(path, list));
    }
}
