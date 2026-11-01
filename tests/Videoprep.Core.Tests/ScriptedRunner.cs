using Videoprep.Core.Engine;

namespace Videoprep.Core.Tests;

/// <summary>
/// An in-memory yt-dlp: the test says what each call prints. Counts calls (requests), honours cancellation between lines
/// like the real runner (the process is killed and the result says Cancelled).
/// </summary>
internal sealed class ScriptedRunner(Func<IReadOnlyList<string>, IEnumerable<string>> script, int exitCode = 0) : IProcessRunner
{
    private readonly object _lock = new();
    public List<IReadOnlyList<string>> Calls { get; } = [];

    public int CallCount { get { lock (_lock) return Calls.Count; } }

    /// <summary>Lines handed to the caller (a listing cut early delivers fewer than it could).</summary>
    public int LinesDelivered { get; private set; }

    public Task<ProcessResult> RunAsync(ProcessSpec spec, Action<string> onLine, CancellationToken ct)
    {
        lock (_lock) Calls.Add(spec.Args);
        foreach (var line in script(spec.Args))
        {
            if (ct.IsCancellationRequested) return Task.FromResult(new ProcessResult(-1, true));
            LinesDelivered++;
            onLine(line);
        }
        return Task.FromResult(new ProcessResult(exitCode, ct.IsCancellationRequested));
    }
}

/// <summary>Builds listing / exact-data lines the way yt-dlp prints them.</summary>
internal static class Lines
{
    public static string Entry(string id, string? date, string title = "t", int? duration = 600, string? live = null) =>
        "{\"id\":\"" + id + "\",\"title\":\"" + title + "\",\"url\":\"https://www.youtube.com/watch?v=" + id + "\",\"ie_key\":\"Youtube\"" +
        (duration is null ? "" : ",\"duration\":" + duration) +
        (date is null ? "" : ",\"upload_date\":\"" + date + "\"") +
        (live is null ? "" : ",\"live_status\":\"" + live + "\"") + "}";

    public static string Exact(string id, string? date, int duration = 600) => $"EX|{id}|{date ?? "NA"}|{duration}|not_live|public";
}
