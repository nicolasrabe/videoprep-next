using System.Diagnostics;
using System.Text;

namespace Videoprep.Core.Engine;

/// <summary>What to run. Arguments are passed one by one (ProcessStartInfo.ArgumentList), never joined into a string.</summary>
public sealed record ProcessSpec(string Exe, IReadOnlyList<string> Args, string? WorkDir = null);

/// <summary>Exit code -1 means the process could not be started (see <see cref="StartError"/>).</summary>
public sealed record ProcessResult(int ExitCode, bool Cancelled, string? StartError = null);

public interface IProcessRunner
{
    /// <summary>
    /// Runs to the end. Every stdout/stderr line goes to <paramref name="onLine"/> (from a background thread).
    /// Cancelling kills the whole process tree (AUDIT L6) and returns with <see cref="ProcessResult.Cancelled"/>.
    /// </summary>
    Task<ProcessResult> RunAsync(ProcessSpec spec, Action<string> onLine, CancellationToken ct);
}

/// <summary>Real runner (replaces the legacy YtRunner + taskkill).</summary>
public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(ProcessSpec spec, Action<string> onLine, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(spec.Exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in spec.Args) psi.ArgumentList.Add(a);
        if (!string.IsNullOrEmpty(spec.WorkDir)) psi.WorkingDirectory = spec.WorkDir;
        // yt-dlp is Python: make it print UTF-8 (titles with accents) on every Windows code page.
        psi.Environment["PYTHONUTF8"] = "1";
        psi.Environment["PYTHONIOENCODING"] = "utf-8";

        using var proc = new Process { StartInfo = psi };
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) onLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) onLine(e.Data); };
        try
        {
            if (!proc.Start()) return new ProcessResult(-1, false, "the process did not start");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            return new ProcessResult(-1, false, ex.Message);
        }
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        // Kill right inside the cancel callback. Cancel() may be called from an onLine handler, i.e. on the thread that
        // reads the output: nothing here may block waiting for that thread (a blocking wait there hung for a minute).
        await using (ct.Register(() =>
        {
            try { proc.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* already exited */ }
            catch (System.ComponentModel.Win32Exception) { /* exiting right now */ }
        }))
        {
            // Also waits until both output streams are fully read.
            await proc.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        var cancelled = ct.IsCancellationRequested;
        return new ProcessResult(cancelled ? -1 : proc.ExitCode, cancelled);
    }
}
