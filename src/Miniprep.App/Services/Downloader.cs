using Videoprep.Core.Engine;
using Videoprep.Core.Models;
using Videoprep.Core.Tools;

namespace Miniprep.App.Services;

/// <summary>What one download needs. <see cref="Item"/> carries the link, the format, the quality and the file name (Naming = Custom).</summary>
public sealed record DownloadRequest(QueueItem Item, string Folder, bool BrowserCookies);

/// <summary>How a download ended. <see cref="Reason"/> is the engine's English text for a failure.</summary>
public sealed record DownloadResult(bool Ok, bool Cancelled, string File, string Reason);

public readonly record struct DownloadProgress(int Percent, string Speed);

/// <summary>Runs one download (tests replace it with a fake that never starts a process).</summary>
public interface IDownloader
{
    Task<DownloadResult> RunAsync(DownloadRequest request, IProgress<DownloadProgress>? progress, CancellationToken ct);
}

/// <summary>The real thing: the same <see cref="JobEngine"/> videoprep uses, with one job at a time.</summary>
public sealed class Downloader(MiniServices svc, ToolsGate tools) : IDownloader
{
    public async Task<DownloadResult> RunAsync(DownloadRequest request, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var paths = await tools.GetAsync(ct).ConfigureAwait(false);
        if (paths is null) return new DownloadResult(false, false, "", "the download tools are not installed");

        var job = JobPlanner.Build([request.Item], new Dictionary<string, int>())[0];
        var engine = new JobEngine(svc.Runner, svc.Log);
        engine.Event += e =>
        {
            if (e is ProgressEvent p) progress?.Report(new DownloadProgress((int)Math.Clamp(p.Progress.Percent, 0, 100), p.Progress.Speed));
        };
        var ctx = new EngineContext
        {
            Tools = paths,
            Options = new DownloadOptions(),
            Folder = request.Folder,
            Cookies = svc.Cookies(request.BrowserCookies),
            SessionCookieFile = request.BrowserCookies ? svc.Paths.SessionCookieFile : null,
        };
        var result = await Task.Run(() => engine.RunAsync([job], ctx, ct), CancellationToken.None).ConfigureAwait(false);
        if (result.Cancelled || ct.IsCancellationRequested) return new DownloadResult(false, true, "", "");
        var status = result.Statuses.GetValueOrDefault(job.Key, JobStatus.ERROR);
        var file = result.Files.TryGetValue(job.Key, out var name) && name.Length > 0 ? Path.Combine(JobEngine.FolderFor(request.Folder, job), name) : "";
        var ok = status is JobStatus.OK or JobStatus.WARN or JobStatus.REVIEW;
        return new DownloadResult(ok, false, file, ok ? "" : result.Reasons.GetValueOrDefault(job.Key, "") is { Length: > 0 } r ? r : "unknown error");
    }
}

/// <summary>
/// The download tools, prepared once: found if they are there (videoprep's folder or miniprep's own), otherwise downloaded the first
/// time they are needed. Everything that needs yt-dlp or ffmpeg asks here.
/// </summary>
public sealed class ToolsGate(ToolManager manager)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ToolPaths? _paths;

    /// <summary>True while the tools are being downloaded (the window shows "Preparando las herramientas...").</summary>
    public bool Installing { get; private set; }
    public event Action? Changed;

    public ToolPaths? Paths => _paths;

    /// <summary>The tools, or null if they are missing and could not be installed.</summary>
    public async Task<ToolPaths?> GetAsync(CancellationToken ct = default)
    {
        if (_paths is not null) return _paths;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_paths is not null) return _paths;
            var found = manager.Resolve();
            if (found.Missing().Count > 0)
            {
                Installing = true;
                Changed?.Invoke();
                try { await manager.InstallAsync(found.Missing(), null, ct).ConfigureAwait(false); }
                catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException or InvalidDataException)
                {
                    return null;
                }
                finally
                {
                    Installing = false;
                    Changed?.Invoke();
                }
                found = manager.Resolve();
            }
            return found.Missing().Count == 0 ? _paths = found : null;
        }
        finally
        {
            _gate.Release();
        }
    }
}
