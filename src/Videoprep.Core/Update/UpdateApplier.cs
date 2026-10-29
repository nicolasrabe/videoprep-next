using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Videoprep.Core.Update;

/// <param name="Target">The installed videoprep.exe to replace.</param>
/// <param name="Staged">The new videoprep.exe (the one that is running this code).</param>
/// <param name="WaitPid">The old videoprep process to wait for.</param>
public sealed record ApplyRequest(string Target, string Staged, string ExpectedSha256, int WaitPid, string FromVersion, string ToVersion)
{
    public const string Flag = "--apply-update";

    /// <summary>The command line the old program uses to start the new one (no text is ever glued into a command string).</summary>
    public IReadOnlyList<string> ToArguments() =>
        [Flag, "--target", Target, "--sha256", ExpectedSha256, "--pid", WaitPid.ToString(CultureInfo.InvariantCulture), "--from", FromVersion, "--to", ToVersion];

    public static bool TryParse(IReadOnlyList<string> args, string stagedPath, out ApplyRequest request)
    {
        request = null!;
        if (args.Count == 0 || args[0] != Flag) return false;
        string? target = null, sha = null, from = null, to = null;
        int pid = -1;
        for (var i = 1; i + 1 < args.Count; i += 2)
            switch (args[i])
            {
                case "--target": target = args[i + 1]; break;
                case "--sha256": sha = args[i + 1]; break;
                case "--pid": int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out pid); break;
                case "--from": from = args[i + 1]; break;
                case "--to": to = args[i + 1]; break;
            }
        if (string.IsNullOrWhiteSpace(target) || !Path.IsPathRooted(target) || sha is not { Length: 64 } || pid <= 0 || from is null || to is null) return false;
        request = new ApplyRequest(target, stagedPath, sha.ToLowerInvariant(), pid, from, to);
        return true;
    }
}

public interface IRunningApp
{
    /// <summary>The exit code if the program ended within <paramref name="time"/>; null if it is still running.</summary>
    Task<int?> ExitCodeWithinAsync(TimeSpan time, CancellationToken ct);
}

/// <summary>What the applier needs from the operating system (tests replace it).</summary>
public interface IUpdateHost
{
    /// <summary>True when the process is gone (or never existed) before the timeout.</summary>
    Task<bool> WaitForExitAsync(int pid, TimeSpan timeout, CancellationToken ct);

    IRunningApp Launch(string exe);
}

public sealed class ProcessUpdateHost : IUpdateHost
{
    public async Task<bool> WaitForExitAsync(int pid, TimeSpan timeout, CancellationToken ct)
    {
        Process p;
        try { p = Process.GetProcessById(pid); }
        catch (ArgumentException) { return true; }
        using (p)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            try { await p.WaitForExitAsync(cts.Token).ConfigureAwait(false); return true; }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return false; }
        }
    }

    public IRunningApp Launch(string exe)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) ?? "" };
        return new Running(Process.Start(psi) ?? throw new IOException("the program did not start"));
    }

    private sealed class Running(Process process) : IRunningApp
    {
        public async Task<int?> ExitCodeWithinAsync(TimeSpan time, CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(time);
            try { await process.WaitForExitAsync(cts.Token).ConfigureAwait(false); return process.ExitCode; }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
        }
    }
}

/// <summary>What the last update attempt did; read once by the next start of the program, which tells the user.</summary>
public sealed record UpdateResult(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("from")] string From,
    [property: JsonPropertyName("to")] string To,
    [property: JsonPropertyName("failure")] [property: JsonConverter(typeof(JsonStringEnumConverter))] UpdateFailure? Failure,
    [property: JsonPropertyName("detail")] string Detail)
{
    public const string FileName = "last-result.json";

    public static void Write(string updatesDir, UpdateResult result)
    {
        Directory.CreateDirectory(updatesDir);
        File.WriteAllText(Path.Combine(updatesDir, FileName), JsonSerializer.Serialize(result));
    }

    /// <summary>Reads and deletes the file; null when there is none or it cannot be read.</summary>
    public static UpdateResult? Take(string updatesDir)
    {
        var path = Path.Combine(updatesDir, FileName);
        if (!File.Exists(path)) return null;
        try
        {
            var r = JsonSerializer.Deserialize<UpdateResult>(File.ReadAllText(path));
            File.Delete(path);
            return r;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return null;
        }
    }
}

/// <summary>Can videoprep.exe be replaced where it lives? Checked before anything is downloaded; administrator rights are never asked for.</summary>
public static class UpdatePermissions
{
    public static UpdateFailure? Check(string? exePath)
    {
        if (string.IsNullOrEmpty(exePath) || !string.Equals(Path.GetFileName(exePath), UpdateStager.ExeName, StringComparison.OrdinalIgnoreCase)) return UpdateFailure.NoInstaller;
        var dir = Path.GetDirectoryName(exePath);
        if (dir is null) return UpdateFailure.NoWritePermission;
        var probe = Path.Combine(dir, ".videoprep-write-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllText(probe, "x");
            File.Delete(probe);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return UpdateFailure.NoWritePermission;
        }
    }
}

/// <summary>
/// Replaces videoprep.exe with the new one. Runs from the NEW program, after the old one closed:
/// copy to videoprep.exe.new, check its SHA-256, old -> videoprep.exe.old, new -> videoprep.exe, start it and watch it.
/// Every file step is retried for a while (antivirus and Windows keep files busy for a moment) and every failure puts things back.
/// The old copy stays as videoprep.exe.old so the user can go back.
/// </summary>
public sealed class UpdateApplier(IUpdateHost host, Func<TimeSpan, CancellationToken, Task>? delay = null, int retryAttempts = 20,
                                  TimeSpan? retryStep = null, TimeSpan? watch = null, Action<string>? log = null,
                                  Action<UpdateResult>? saveResult = null)
{
    public static readonly TimeSpan WaitForOldProgram = TimeSpan.FromSeconds(60);

    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;
    private readonly TimeSpan _step = retryStep ?? TimeSpan.FromMilliseconds(500);
    private readonly TimeSpan _watch = watch ?? TimeSpan.FromSeconds(15);
    private readonly Action<string> _log = log ?? (_ => { });
    private readonly Action<UpdateResult> _save = saveResult ?? (_ => { });
    private string _lastError = "";

    public async Task<UpdateResult> RunAsync(ApplyRequest req, CancellationToken ct)
    {
        var target = req.Target;
        var fresh = target + ".new";
        var old = target + ".old";
        UpdateResult Fail(UpdateFailure f, string detail)
        {
            _log($"update failed ({f}): {detail}");
            return new UpdateResult(false, req.FromVersion, req.ToVersion, f, detail);
        }

        _log($"waiting for videoprep (process {req.WaitPid}) to close");
        if (!await host.WaitForExitAsync(req.WaitPid, WaitForOldProgram, ct).ConfigureAwait(false))
        {
            var r = Fail(UpdateFailure.StillRunning, "the old program did not close in time; nothing was changed");
            _save(r);
            return r;
        }

        // The result is saved BEFORE any program is opened, so the one that opens can tell the person.
        UpdateResult Done(UpdateResult r) { _save(r); StartAgain(target); return r; }

        if (!File.Exists(req.Staged) || Sha256Sums.HashFile(req.Staged) != req.ExpectedSha256)
            return Done(Fail(UpdateFailure.ChecksumMismatch, "the new program is not the one that was checked"));

        _log("copying the new program next to the old one");
        if (!await TryAsync(() => File.Copy(req.Staged, fresh, true), ct).ConfigureAwait(false))
            return Done(Fail(UpdateFailure.FilesLocked, "could not write " + Path.GetFileName(fresh) + ": " + _lastError));
        if (Sha256Sums.HashFile(fresh) != req.ExpectedSha256)
        {
            await TryAsync(() => File.Delete(fresh), ct).ConfigureAwait(false);
            return Done(Fail(UpdateFailure.ChecksumMismatch, "the copy is not identical to the checked file"));
        }

        var hadOld = File.Exists(target);
        if (hadOld)
        {
            if (File.Exists(old) && !await TryAsync(() => File.Delete(old), ct).ConfigureAwait(false))
            {
                await TryAsync(() => File.Delete(fresh), ct).ConfigureAwait(false);
                return Done(Fail(UpdateFailure.FilesLocked, "could not remove the older backup: " + _lastError));
            }
            if (!await TryAsync(() => File.Move(target, old), ct).ConfigureAwait(false))
            {
                await TryAsync(() => File.Delete(fresh), ct).ConfigureAwait(false);
                return Done(Fail(UpdateFailure.FilesLocked, "could not move the old program aside: " + _lastError));
            }
        }
        if (!await TryAsync(() => File.Move(fresh, target), ct).ConfigureAwait(false))
        {
            var error = _lastError;
            await RestoreAsync(target, old, hadOld, ct).ConfigureAwait(false);
            return Done(Fail(UpdateFailure.FilesLocked, "could not put the new program in place: " + error));
        }
        if (Sha256Sums.HashFile(target) != req.ExpectedSha256)
        {
            await RestoreAsync(target, old, hadOld, ct).ConfigureAwait(false);
            return Done(Fail(UpdateFailure.ChecksumMismatch, "the installed file does not match after copying"));
        }

        _log("starting the new version and watching it for " + _watch.TotalSeconds.ToString("0", CultureInfo.InvariantCulture) + " s");
        IRunningApp app;
        var success = new UpdateResult(true, req.FromVersion, req.ToVersion, null, "");
        _save(success);   // the new program reads this when it starts; a failure below replaces it before the old one is opened again
        try { app = host.Launch(target); }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            await RestoreAsync(target, old, hadOld, ct).ConfigureAwait(false);
            return Done(Fail(UpdateFailure.NewVersionDidNotStart, "it could not be started: " + ex.Message));
        }
        var code = await app.ExitCodeWithinAsync(_watch, ct).ConfigureAwait(false);
        if (code is int c && c != 0)
        {
            await RestoreAsync(target, old, hadOld, ct).ConfigureAwait(false);
            return Done(Fail(UpdateFailure.NewVersionDidNotStart, $"the new version closed by itself with code {c}; the old one was put back"));
        }
        _log("the new version is running");
        return success;
    }

    private async Task RestoreAsync(string target, string old, bool hadOld, CancellationToken ct)
    {
        if (!hadOld) return;
        _log("putting the old version back");
        if (File.Exists(target)) await TryAsync(() => File.Delete(target), ct).ConfigureAwait(false);
        if (File.Exists(old)) await TryAsync(() => File.Move(old, target), ct).ConfigureAwait(false);
    }

    /// <summary>Opens videoprep again after a failure so the person is never left without a program.</summary>
    private void StartAgain(string target)
    {
        try { if (File.Exists(target)) host.Launch(target); }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException) { _log("could not start videoprep again: " + ex.Message); }
    }

    private async Task<bool> TryAsync(Action op, CancellationToken ct)
    {
        for (var i = 0; i < retryAttempts; i++)
        {
            try { op(); return true; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _lastError = ex.Message; }
            await _delay(_step, ct).ConfigureAwait(false);
        }
        return false;
    }
}
