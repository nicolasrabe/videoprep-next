using System.Globalization;
using System.Text;
using Videoprep.Core.Formats;
using Videoprep.Core.Logging;
using Videoprep.Core.Media;
using Videoprep.Core.Models;
using Videoprep.Core.Storage;

namespace Videoprep.Core.Engine;

/// <summary>
/// Runs a batch of jobs: download each video once, cut/convert every output from it, verify, move, clean up.
/// Port of the legacy timer state machine (Start-NextJob, Start-Attempt, Finish-Attempt, Complete-Download,
/// Start-Post, Finish-Post, Finish-Job, Complete-Batch) as plain async code with no UI references (AUDIT H1).
/// Changes from 0.1.1: the downloaded file comes from <c>--print after_move:filepath</c> (H2); outputs are checked
/// with ffprobe before they are moved and before the source is deleted (H3); a file that existed before the run is
/// never deleted (X6); yt-dlp exit code 101 is not an error.
/// </summary>
public sealed class JobEngine(IProcessRunner runner, Log log)
{
    /// <summary>yt-dlp: "stopped early on purpose" (--break-on-existing, --max-downloads...). Not an error (docs/VERIFY.md).</summary>
    public const int ExitStoppedEarly = 101;

    public event Action<EngineEvent>? Event;

    /// <summary>Browser that worked after the first one failed; the UI saves it as the preferred source (AUDIT X3).</summary>
    public string? PreferredBrowser { get; private set; }

    /// <summary>Port of Start-Retry: the failed jobs again; the first job of each video downloads it again.</summary>
    public static IReadOnlyList<Job> RetryJobs(IReadOnlyList<Job> lastJobs, IReadOnlyDictionary<string, JobStatus> statuses)
    {
        var seen = new HashSet<int>();
        return lastJobs
            .Where(j => statuses.TryGetValue(j.Key, out var s) && s == JobStatus.ERROR)
            .Select(j => j with { Download = seen.Add(j.GroupId) })
            .ToList();
    }

    /// <summary>
    /// Where a job's files go: the batch folder, or a subfolder of it. The subfolder name is cleaned again here, so a
    /// queue file edited by hand cannot point outside the batch folder ("..", "C:\", "\\server").
    /// </summary>
    public static string FolderFor(string batchFolder, Job job) => FolderFor(batchFolder, job.SubFolder);

    public static string FolderFor(string batchFolder, string subFolder)
    {
        var sub = Text.SafeName.Make(subFolder, "");
        return sub.Length > 0 ? Path.Combine(batchFolder, sub) : batchFolder;
    }

    public async Task<BatchResult> RunAsync(IReadOnlyList<Job> jobs, EngineContext ctx, CancellationToken ct)
    {
        var run = new Run(this, jobs, ctx, ct);
        return await run.ExecuteAsync().ConfigureAwait(false);
    }

    private void Raise(EngineEvent e) => Event?.Invoke(e);

    private enum DownloadOutcome { Ok, Skipped, ExistsWithError, Error, Cancelled }

    private sealed record DownloadResult(DownloadOutcome Outcome, string? Path = null, bool PreExisting = false);

    /// <summary>State of one batch (the legacy $S table, now private to the run).</summary>
    private sealed class Run(JobEngine engine, IReadOnlyList<Job> jobs, EngineContext ctx, CancellationToken ct)
    {
        private readonly Log _log = engine._log;
        private readonly Dictionary<int, VideoGroup> _groups = jobs.GroupBy(j => j.GroupId).ToDictionary(
            g => g.Key, g => new VideoGroup(g.Count(), g.Any(j => j.IsRange)));
        private readonly Dictionary<string, JobStatus> _status = jobs.ToDictionary(j => j.Key, _ => JobStatus.Queued);
        private readonly Dictionary<string, string> _files = [];
        private readonly Dictionary<string, string> _reasons = [];
        private readonly HashSet<int> _skippedGroups = [];
        private readonly List<string> _failed = [];
        private IReadOnlyList<CookieSource> _chain = ctx.Cookies.Count > 0 ? ctx.Cookies : [CookieSource.NoCookies];
        private int _ok, _warn, _fail, _idx;
        private bool _cancelled, _blocked;

        private int Number => _idx + 1;
        private int Total => jobs.Count;

        public async Task<BatchResult> ExecuteAsync()
        {
            _log.Write("");
            _log.Write($"=== Starting: {jobs.Count} file(s) -> {ctx.Folder} ===");
            for (_idx = 0; _idx < jobs.Count; _idx++)
            {
                if (ct.IsCancellationRequested) { _cancelled = true; break; }
                if (_blocked && ctx.StopWhenBlocked)
                {
                    _log.Write("Stopped: the site asked for verification (bot check) or for fewer requests. The rest stays queued.");
                    break;
                }
                await RunJobAsync(jobs[_idx]).ConfigureAwait(false);
                if (_cancelled) break;
            }

            _log.Write("");
            var word = _cancelled ? "Stopped" : "Done";
            _log.Write($"=== {word}: {_ok} OK, {_warn} to review, {_fail} failed ===");
            foreach (var f in _failed) _log.Write("  FAILED: " + f);
            return new BatchResult(_ok, _warn, _fail, _failed, _cancelled, _blocked, _status, _files, _reasons);
        }

        private async Task RunJobAsync(Job job)
        {
            var g = _groups[job.GroupId];
            if (_skippedGroups.Contains(job.GroupId))
            {
                _log.Write($"[{Number}/{Total}] skipped: this video was not downloaded (already in the history)");
                g.OutputFinished();
                _warn++;
                SetStatus(job, JobStatus.Skipped);
                return;
            }
            if (g.Failed)
            {
                _log.Write("");
                _log.Write($"[{Number}/{Total}] skipped: the download of this video failed");
                AddFailure(job);
                return;
            }

            if (job.Download)
            {
                SetStatus(job, JobStatus.Downloading);
                var dl = await DownloadAsync(job, g).ConfigureAwait(false);
                switch (dl.Outcome)
                {
                    case DownloadOutcome.Cancelled:
                        _cancelled = true;
                        return;
                    case DownloadOutcome.Ok:
                        g.SetDownloaded(dl.Path!, dl.PreExisting);
                        if (dl.PreExisting) _log.Write($"    the file was already in the folder, it is reused (and never deleted): {Path.GetFileName(dl.Path)}");
                        await CompleteAsync(job, dl.Path!, g).ConfigureAwait(false);
                        return;
                    case DownloadOutcome.Skipped:
                        g.OutputFinished();
                        _skippedGroups.Add(job.GroupId);
                        _warn++;
                        _log.Write("SKIPPED  nothing was downloaded: this video is in the download history (or outside the date range).");
                        SetStatus(job, JobStatus.Skipped);
                        return;
                    case DownloadOutcome.ExistsWithError:
                        g.OutputFinished();
                        g.MarkFailed();
                        _warn++;
                        SetStatus(job, JobStatus.REVIEW, Path.GetFileName(dl.Path!), "yt-dlp reported an error after writing the file");
                        return;
                    default:
                        g.MarkFailed();
                        AddFailure(job);
                        return;
                }
            }

            _log.Write("");
            _log.Write($"[{Number}/{Total}] next clip of an already downloaded video");
            if (g.FullPath is null || !File.Exists(g.FullPath))
            {
                _log.Write("ERROR  the full video is missing, so this clip cannot be cut.");
                AddFailure(job);
                return;
            }
            await CompleteAsync(job, g.FullPath, g).ConfigureAwait(false);
        }

        // ---------------- download (port of Start-Attempt + Finish-Attempt) ----------------

        private async Task<DownloadResult> DownloadAsync(Job job, VideoGroup g)
        {
            if (ctx.Tools.YtDlp is null)
            {
                _log.Write("ERROR  yt-dlp is not installed.");
                return new DownloadResult(DownloadOutcome.Error);
            }
            var fmt = FormatCatalog.Get(job.Format);
            try { Directory.CreateDirectory(FolderFor(ctx.Folder, job)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                _log.Write($"ERROR  could not create the folder {FolderFor(ctx.Folder, job)}: {ex.Message}");
                return new DownloadResult(DownloadOutcome.Error);
            }
            for (var attempt = 0; attempt < _chain.Count; attempt++)
            {
                var src = _chain[attempt];
                if (attempt == 0)
                {
                    _log.Write("");
                    var tag = job.Label is null ? "" : job.Label + "  ";
                    _log.Write($"[{Number}/{Total}] {tag}{job.Url}");
                    _log.Write($"    format: {fmt.Label}   max {job.Cap}p");
                    if (g.HasClips) _log.Write($"    {g.Total} clip(s) will be cut from this video");
                }
                _log.Write("    login: " + src.Label);
                engine.Raise(new StepEvent(job, Number, Total, EngineStep.Downloading, src.Label));

                var dump = ctx.SessionCookieFile is not null && src.Kind == CookieKind.Browser ? ctx.SessionCookieFile : null;
                var pathFile = Path.Combine(Path.GetTempPath(), $"videoprep-final-{Guid.NewGuid():N}.txt");
                var folder = FolderFor(ctx.Folder, job);
                var args = YtDlpArgs.Download(job, ctx.Options, ctx.Tools, src, folder, g.HasClips, ctx.ArchiveFile,
                                              ctx.ForceArchive, dump, pathFile);
                var before = SnapshotFolder(folder);
                var output = new StringBuilder();
                var printed = new List<string>();
                var r = await engine._runner.RunAsync(new ProcessSpec(ctx.Tools.YtDlp, args, folder), line =>
                {
                    if (ProgressLine.TryParse(line) is { } p)
                    {
                        engine.Raise(new ProgressEvent(job, Number, Total, p));
                        return;
                    }
                    lock (output) output.AppendLine(line);
                    if (LooksLikeFinalPath(line)) lock (printed) printed.Add(line.Trim());
                    else _log.Write(line);
                }, ct).ConfigureAwait(false);

                printed.AddRange(ReadFinalPaths(pathFile));   // last = preferred over the console copy
                if (r.Cancelled)
                {
                    _log.Write("Stopped by user. Partial files (.part) may remain in the folder.");
                    return new DownloadResult(DownloadOutcome.Cancelled);
                }
                if (r.StartError is not null)
                {
                    _log.Write("ERROR: could not start yt-dlp: " + r.StartError);
                    return new DownloadResult(DownloadOutcome.Error);
                }

                var exit = r.ExitCode == ExitStoppedEarly ? 0 : r.ExitCode;
                var text = output.ToString();
                if (CookieStrategy.BlockedOutput().IsMatch(text)) _blocked = true;

                // Cookie problem or bot check with this source -> try the next one.
                if (exit != 0 && attempt + 1 < _chain.Count && CookieStrategy.RetryOutput().IsMatch(text))
                {
                    _log.Write($"    \"{src.Label}\" did not work - trying \"{_chain[attempt + 1].Label}\"...");
                    continue;
                }
                if (exit == 0) _blocked = false;   // a later source worked: not blocked after all

                var file = printed.LastOrDefault(File.Exists);
                if (exit == 0 && file is not null)
                {
                    RememberWorkingSource(src, attempt);
                    var pre = before.Contains(Path.GetFileName(file));
                    return new DownloadResult(DownloadOutcome.Ok, file, pre);
                }
                if (file is not null)
                {
                    _log.Write($"REVIEW  {Path.GetFileName(file)} exists but yt-dlp returned code {r.ExitCode}.");
                    return new DownloadResult(DownloadOutcome.ExistsWithError, file);
                }
                if (exit == 0) return new DownloadResult(DownloadOutcome.Skipped);

                _log.Write($"ERROR  no file produced (exit code {r.ExitCode}).");
                foreach (var hint in Hints(text, job.Site)) _log.Write("    Hint: " + hint);
                return new DownloadResult(DownloadOutcome.Error);
            }
            return new DownloadResult(DownloadOutcome.Error);
        }

        private void RememberWorkingSource(CookieSource src, int attempt)
        {
            if (attempt > 0)
            {
                _chain = CookieStrategy.MoveToFront(_chain, src);
                if (src.Kind == CookieKind.Browser) engine.PreferredBrowser = src.Value;
            }
            // AUDIT M5: after a browser worked, the next videos read the saved copy first.
            if (src.Kind == CookieKind.Browser && ctx.SessionCookieFile is { } jar && File.Exists(jar))
            {
                var copy = CookieSource.FromFile(jar);
                _chain = [copy, .. _chain.Where(c => c != copy)];
            }
        }

        /// <summary>Exact final paths from --print-to-file (UTF-8, one per line); the temp file is deleted.</summary>
        private static IReadOnlyList<string> ReadFinalPaths(string pathFile)
        {
            try
            {
                if (!File.Exists(pathFile)) return [];
                var lines = File.ReadAllLines(pathFile, Encoding.UTF8).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
                File.Delete(pathFile);
                return lines;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return [];
            }
        }

        private static bool LooksLikeFinalPath(string line)
        {
            var t = line.Trim();
            return t.Length > 3 && Path.IsPathFullyQualified(t) && !t.Contains('\t');
        }

        private static HashSet<string> SnapshotFolder(string folder)
        {
            try
            {
                return Directory.Exists(folder)
                    ? Directory.EnumerateFiles(folder).Select(f => Path.GetFileName(f)).ToHashSet(StringComparer.OrdinalIgnoreCase)
                    : [];
            }
            catch (IOException) { return []; }
            catch (UnauthorizedAccessException) { return []; }
        }

        /// <summary>Port of the hints at the end of Finish-Attempt.</summary>
        private static IEnumerable<string> Hints(string text, Site site)
        {
            if (text.Contains("Sign in to confirm", StringComparison.OrdinalIgnoreCase))
                yield return "log into YouTube in Firefox, keep the login source on Automatic (Settings tab), then press Update tools and try again.";
            else if (site == Site.Other && System.Text.RegularExpressions.Regex.IsMatch(text,
                         "login|log in|cookie|private|authenticat|restricted|rate.?limit|not available",
                         System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                yield return "this site may need you to be logged in. Log in to it in Firefox and keep the login source on Automatic (Settings tab). Public posts usually work without it. Press Update tools if the site changed.";
            else if (text.Contains("Requested format is not available", StringComparison.OrdinalIgnoreCase))
                yield return "YouTube did not offer this format. Turn on the H.264 conversion in Options, or press Update tools.";
            else if (text.Contains("DPAPI", StringComparison.OrdinalIgnoreCase) || text.Contains("Could not copy", StringComparison.OrdinalIgnoreCase))
                yield return "Chrome/Edge cookies are often unreadable on Windows. Firefox works best, or close the browser completely.";
        }

        // ---------------- after the download (port of Complete-Download / Start-Post / Finish-Post) ----------------

        private async Task CompleteAsync(Job job, string file, VideoGroup g)
        {
            var fmt = FormatCatalog.Get(job.Format);
            var needPost = job.IsRange;
            if (!needPost)
            {
                if (job.Format == "movp") needPost = true;
                else if (job.Kind == MediaKind.Video && fmt.IsH264 && (ctx.Options.ConvertFallback || job.Site == Site.Other)
                         && ctx.Tools.Ffprobe is not null)
                {
                    var info0 = await ProbeAsync(file).ConfigureAwait(false);
                    if (info0 is not null)
                    {
                        var t0 = SourceTiming.FromVideo(info0);
                        if (t0.Ok && (t0.VideoCodec != "h264" || t0.AudioCodec != "aac"
                                      || !string.Equals(Path.GetExtension(file), fmt.Extension, StringComparison.OrdinalIgnoreCase)))
                            needPost = true;
                    }
                }
            }
            if (needPost)
            {
                if (ctx.Tools.Ffmpeg is not null && ctx.Tools.Ffprobe is not null)
                {
                    await PostAsync(job, file, g, job.IsRange).ConfigureAwait(false);
                    return;
                }
                _log.Write("WARN  ffmpeg not found - cannot cut or convert. The downloaded file was kept.");
            }
            g.OutputFinished();
            await FinishJobAsync(job, file).ConfigureAwait(false);
        }

        private async Task PostAsync(Job job, string file, VideoGroup g, bool isRange)
        {
            var fmt = FormatCatalog.Get(job.Format);
            var dir = Path.GetDirectoryName(file)!;
            var baseName = job.Label ?? (isRange && job.Count > 1 ? $"{g.Stem} - {job.Index + 1}" : g.Stem);
            var tmp = Path.Combine(dir, baseName + ".__tmp" + fmt.Extension);

            var src = await ProbeAsync(file).ConfigureAwait(false);
            if (src is null)
            {
                SkipPost(job, file, g, "ffprobe could not read the downloaded file");
                return;
            }
            var timing = job.Kind == MediaKind.Audio ? SourceTiming.FromAudio(src, job.Fps) : SourceTiming.FromVideo(src);
            CutPlan? plan = null;
            var note = "";
            if (isRange)
            {
                if (!timing.Ok) { SkipPost(job, file, g, timing.Error); return; }
                plan = CutPlanner.Plan(job.Start, job.End, timing);
                if (!plan.Ok) { SkipPost(job, file, g, plan.Error); return; }
                note = string.Create(CultureInfo.InvariantCulture,
                    $"clip: frames {plan.StartFrame} to {plan.EndFrame} = {plan.Frames} frames at {plan.Fps:0.###} fps");
                if (plan.Note.Length > 0) note += "  (" + plan.Note + ")";
                if (job.Kind == MediaKind.Audio && SourceTiming.FpsWasGuessed(job.Fps))
                    note += "  (frame rate of the video unknown: 30 fps assumed)";
                if (timing.VariableFrameRate)
                    _log.Write("WARN  this video has a variable frame rate: the cut may not be frame-accurate.");
            }
            else if (!timing.Ok)
            {
                SkipPost(job, file, g, timing.Error);
                return;
            }

            _log.Write(isRange ? "    cutting " + note : $"    converting to {fmt.Label}...");
            SetStatus(job, JobStatus.Cutting);
            engine.Raise(new StepEvent(job, Number, Total, EngineStep.Processing, note, plan));

            var args = FfmpegArgs.Encode(job, plan, timing, ctx.Options.Crf, file, tmp);
            var r = await engine._runner.RunAsync(new ProcessSpec(ctx.Tools.Ffmpeg!, args, dir), _log.Write, ct).ConfigureAwait(false);
            if (r.Cancelled)
            {
                TryDelete(tmp);
                _log.Write("Stopped by user.");
                _cancelled = true;
                return;
            }

            g.OutputFinished();
            if (r.ExitCode != 0 || !File.Exists(tmp))
            {
                TryDelete(tmp);
                g.MarkKeep();
                _warn++;
                var why = r.StartError ?? $"exit code {r.ExitCode}";
                _log.Write($"REVIEW  ffmpeg failed ({why}). The downloaded video was kept: {Path.GetFileName(file)}");
                SetStatus(job, JobStatus.REVIEW, Path.GetFileName(file), $"ffmpeg failed ({why})");
                return;
            }

            // AUDIT H3: check the result BEFORE moving it and before anything is deleted.
            engine.Raise(new StepEvent(job, Number, Total, EngineStep.Verifying));
            var outInfo = await ProbeAsync(tmp).ConfigureAwait(false);
            var check = outInfo is null
                ? new VerifyResult(false, "ffprobe could not read the result")
                : OutputVerifier.Check(outInfo, job.Kind, plan, src.FormatDuration, src.Audio is not null);
            var seconds = outInfo is null ? null : job.Kind == MediaKind.Video ? outInfo.Video?.Duration ?? outInfo.FormatDuration : outInfo.FormatDuration;
            engine.Raise(new VerifiedEvent(job, tmp, outInfo?.Video?.FrameCount, seconds, check.Ok, check.Reason));
            if (!check.Ok)
            {
                TryDelete(tmp);
                g.MarkKeep();
                _warn++;
                _log.Write($"REVIEW  the result did not pass the check: {check.Reason}. The downloaded video was kept: {Path.GetFileName(file)}");
                SetStatus(job, JobStatus.REVIEW, Path.GetFileName(file), "the result did not pass the check: " + check.Reason);
                return;
            }

            string final;
            try
            {
                final = FileNames.UniquePath(dir, baseName, fmt.Extension);
                File.Move(tmp, final);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                g.MarkKeep();
                _warn++;
                _log.Write($"REVIEW  could not give the result its final name ({ex.Message}). Kept: {Path.GetFileName(tmp)} and {Path.GetFileName(file)}");
                SetStatus(job, JobStatus.REVIEW, Path.GetFileName(tmp), "could not give the result its final name: " + ex.Message);
                return;
            }

            await FinishJobAsync(job, final, outInfo).ConfigureAwait(false);
            if (note.Length > 0) _log.Write("      " + note);

            // Only now, with the result verified and in place, may the source go (and never a file the user already had).
            if (g.ShouldDeleteFull(ctx.Options.KeepFull))
            {
                if (TryDelete(g.FullPath!)) _log.Write("      the downloaded full video was deleted: " + Path.GetFileName(g.FullPath));
            }
            else if (g.IsLast && g.HasClips)
            {
                _log.Write("      the full video was kept: " + Path.GetFileName(g.FullPath));
            }
        }

        private void SkipPost(Job job, string file, VideoGroup g, string why)
        {
            g.MarkKeep();
            g.OutputFinished();
            _warn++;
            _log.Write($"REVIEW  not processed: {why}. The downloaded file was kept: {Path.GetFileName(file)}");
            SetStatus(job, JobStatus.REVIEW, Path.GetFileName(file), why);
        }

        /// <summary>Port of Finish-Job: check the codecs of the final file.</summary>
        private async Task FinishJobAsync(Job job, string file, MediaInfo? known = null)
        {
            var exp = FormatCatalog.Get(job.Format).Expected;
            var name = Path.GetFileName(file);
            _files[job.Key] = name;
            MediaInfo? info = known;
            if (info is null && exp is not null && ctx.Tools.Ffprobe is not null) info = await ProbeAsync(file).ConfigureAwait(false);

            if (exp is not null && info is not null && job.Kind == MediaKind.Video
                && (info.Video?.Codec != exp.Video || (info.Audio is not null && info.Audio.Codec != exp.Audio)))
            {
                _warn++;
                _log.Write($"WARN  {name}  ->  {info.Summary}");
                _log.Write($"      Not the expected codecs ({exp.Video} + {exp.Audio}).");
                SetStatus(job, JobStatus.WARN, name);
                return;
            }
            _ok++;
            _log.Write(info is null ? $"OK    {name}" : $"OK    {name}  ->  {info.Summary}");
            SetStatus(job, JobStatus.OK, name);
        }

        private async Task<MediaInfo?> ProbeAsync(string path)
        {
            if (ctx.Tools.Ffprobe is null) return null;
            var json = new StringBuilder();
            var r = await engine._runner.RunAsync(new ProcessSpec(ctx.Tools.Ffprobe, FfmpegArgs.Probe(path)),
                                                  l => { lock (json) json.AppendLine(l); }, CancellationToken.None).ConfigureAwait(false);
            if (r.ExitCode != 0) return null;
            try { return MediaInfo.ParseFfprobeJson(json.ToString()); }
            catch (System.Text.Json.JsonException) { return null; }
        }

        private void SetStatus(Job job, JobStatus status, string file = "", string reason = "")
        {
            _status[job.Key] = status;
            if (file.Length > 0) _files[job.Key] = file;
            if (reason.Length > 0) _reasons[job.Key] = reason;
            engine.Raise(new StatusEvent(job, status, file, reason));
        }

        private void AddFailure(Job job)
        {
            _fail++;
            _failed.Add($"{job.Label ?? job.Url}  {job.Url}");
            SetStatus(job, JobStatus.ERROR);
        }

        private bool TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.Write($"      could not delete {Path.GetFileName(path)}: {ex.Message}");
                return false;
            }
        }
    }

    private readonly IProcessRunner _runner = runner;
    private readonly Log _log = log;
}
