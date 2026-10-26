using System.Diagnostics;
using System.Globalization;

namespace Videoprep.App.Services;

/// <summary>
/// The Windows scheduled task that runs <c>videoprep.exe --check</c> while videoprep is closed (SCRAPING_SPEC 2):
/// current user, only when logged on (/IT), no admin rights. The user sees the exact command before it is created.
/// macOS (launchd) comes later.
/// </summary>
public static class ScheduledTask
{
    public const string Name = "videoprep-check";

    public static bool Supported => OperatingSystem.IsWindows();

    /// <summary>schtasks arguments: every N hours (1-23) or once a day (24).</summary>
    public static IReadOnlyList<string> CreateArgs(string exePath, int hours)
    {
        hours = Math.Clamp(hours, 1, 24);
        var schedule = hours >= 24 ? new[] { "/SC", "DAILY", "/MO", "1" } : ["/SC", "HOURLY", "/MO", hours.ToString(CultureInfo.InvariantCulture)];
        return ["/Create", "/TN", Name, "/TR", $"\"{exePath}\" --check", .. schedule, "/IT", "/F"];
    }

    public static IReadOnlyList<string> DeleteArgs() => ["/Delete", "/TN", Name, "/F"];

    /// <summary>The command as the user would type it, for the confirmation message.</summary>
    public static string Describe(IReadOnlyList<string> args) =>
        "schtasks " + string.Join(' ', args.Select(a => a.Contains(' ') || a.Contains('"') ? "\"" + a.Replace("\"", "\\\"") + "\"" : a));

    /// <summary>Runs schtasks; returns (exit code, output) for the log.</summary>
    public static async Task<(int Code, string Output)> RunAsync(IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo("schtasks.exe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var output = await p.StandardOutput.ReadToEndAsync() + await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return (p.ExitCode, output.Trim());
    }
}
