using Avalonia.Threading;
using Videoprep.Core.Import;

namespace Videoprep.App.Services;

/// <summary>
/// What the clipboard watcher needs from the system (F5). The window implements it for real; tests answer with
/// whatever the "user" copied. Nothing here reads the clipboard unless <see cref="ClipboardWatcher"/> asks.
/// </summary>
public interface IClipboardSource
{
    /// <summary>A number that changes at every copy (asking for it reads nothing); 0 = this system cannot say.</summary>
    long ClipboardSequence { get; }

    /// <summary>The copied text, or null when there is none or the clipboard is busy. Text only.</summary>
    Task<string?> ReadClipboardTextAsync();
}

/// <summary>
/// "Reconocer y agregar a la cola automáticamente los enlaces que copio" (F5). Off unless the user turned it on: when off
/// there is no timer and nothing is read. When on it checks twice a second whether the clipboard changed (a number, not
/// the content); only after a change, and a short pause so the copy settles, it reads the TEXT and looks for clear
/// video links. What was already copied when it started is ignored. What videoprep copied itself is ignored.
/// The copied text is never stored or logged.
/// </summary>
public sealed class ClipboardWatcher(IClipboardSource source, Func<TimeSpan, CancellationToken, Task> delay,
                                     Action<IReadOnlyList<string>> onLinks, bool useTimer = true)
{
    private DispatcherTimer? _timer;
    private bool _running;
    private long _lastSequence;
    private string? _lastText;
    private string? _own;
    private bool _busy;

    public TimeSpan Interval { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Wait after a change before reading (several copies in a row, or a program still filling the clipboard).</summary>
    public TimeSpan Debounce { get; init; } = TimeSpan.FromMilliseconds(600);

    public bool IsRunning => _running;

    /// <summary>Starts watching from now on: the current content is the starting point, not something to add.</summary>
    public void Start()
    {
        if (_running) return;
        _running = true;
        _lastSequence = source.ClipboardSequence;
        _lastText = null;
        _own = null;
        if (!useTimer) return;   // tests ask for each check themselves
        _timer = new DispatcherTimer { Interval = Interval };
        _timer.Tick += async (_, _) =>
        {
            try { await PollOnceAsync(); }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or System.Runtime.InteropServices.COMException)
            {
                // A busy clipboard: try again at the next tick. Nothing about the content is kept.
            }
        };
        _timer.Start();
    }

    public void Stop()
    {
        _running = false;
        _timer?.Stop();
        _timer = null;
        _lastText = null;
        _own = null;
    }

    /// <summary>Call right before videoprep puts text on the clipboard: that copy is not the user's.</summary>
    public void NoteOwnCopy(string text) => _own = text;

    /// <summary>One check (the timer calls it; tests call it directly).</summary>
    public async Task PollOnceAsync(CancellationToken ct = default)
    {
        if (_busy || !_running) return;
        _busy = true;
        try
        {
            var seq = source.ClipboardSequence;
            if (seq != 0 && seq == _lastSequence) return;
            if (seq != 0)
            {
                await delay(Debounce, ct).ConfigureAwait(true);
                if (source.ClipboardSequence != seq) return;   // copied again meanwhile: the next check takes it
            }
            var text = await source.ReadClipboardTextAsync().ConfigureAwait(true);
            if (seq == 0)
            {
                if (text == _lastText) return;                  // no change number on this system: compare the text
                _lastText = text;
            }
            _lastSequence = seq;
            if (string.IsNullOrEmpty(text)) return;
            if (_own is { } own)
            {
                _own = null;
                if (text == own) return;
            }
            var links = ClipboardLinks.Find(text);
            if (links.Count > 0) onLinks(links);
        }
        finally
        {
            _busy = false;
        }
    }
}
