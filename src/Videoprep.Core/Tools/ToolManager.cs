using System.IO.Compression;
using Videoprep.Core.Engine;

namespace Videoprep.Core.Tools;

/// <summary>Download progress: which tool, bytes so far, total bytes (null when the server does not say).</summary>
public sealed record ToolProgress(string Tool, long Bytes, long? TotalBytes);

/// <summary>
/// Finds, downloads and updates yt-dlp, FFmpeg (ffmpeg + ffprobe) and Deno (port of the -SetupTools branch,
/// Resolve-Tool and Update-Tools). Tools live in %LOCALAPPDATA%\videoprep\tools; the folder of the exe and PATH are
/// also searched, in that order. Downloads go to a temp file first and only replace the tool when complete.
/// Windows only for now (macOS after 0.2.0: different URLs and no FFmpeg build from yt-dlp).
/// </summary>
public sealed class ToolManager(string toolsDir, HttpClient http, string? exeDir = null, string? pathVariable = null)
{
    public const string YtDlpUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
    public const string FfmpegUrl = "https://github.com/yt-dlp/FFmpeg-Builds/releases/latest/download/ffmpeg-master-latest-win64-gpl.zip";
    public const string DenoUrl = "https://github.com/denoland/deno/releases/latest/download/deno-x86_64-pc-windows-msvc.zip";

    public string ToolsDir { get; } = toolsDir;

    private static string Exe(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;

    /// <summary>Absolute paths of the tools found now (null = missing).</summary>
    public ToolPaths Resolve() => new(Find("yt-dlp"), Find("ffmpeg"), Find("ffprobe"), Find("deno"));

    private string? Find(string name)
    {
        var file = Exe(name);
        foreach (var dir in new[] { ToolsDir, exeDir ?? AppContext.BaseDirectory })
        {
            var p = Path.Combine(dir, file);
            if (File.Exists(p)) return Path.GetFullPath(p);
        }
        var path = pathVariable ?? Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var p = Path.Combine(dir.Trim('"'), file);
                if (File.Exists(p)) return Path.GetFullPath(p);
            }
            catch (ArgumentException)
            {
                // A malformed PATH entry is skipped.
            }
        }
        return null;
    }

    /// <summary>Installs the missing tools ("yt-dlp", "ffmpeg", "deno") into <see cref="ToolsDir"/>.</summary>
    public async Task InstallAsync(IEnumerable<string> missing, IProgress<ToolProgress>? progress, CancellationToken ct)
    {
        // The URLs are the Windows builds; the macOS build (after 0.2.0) will choose its own.
        Directory.CreateDirectory(ToolsDir);
        foreach (var tool in missing.Distinct())
        {
            switch (tool)
            {
                case "yt-dlp":
                    await DownloadFileAsync("yt-dlp", YtDlpUrl, Path.Combine(ToolsDir, "yt-dlp.exe"), progress, ct).ConfigureAwait(false);
                    break;
                case "ffmpeg":
                    await DownloadZipAsync("ffmpeg", FfmpegUrl, ["ffmpeg.exe", "ffprobe.exe"], progress, ct).ConfigureAwait(false);
                    break;
                case "deno":
                    await DownloadZipAsync("deno", DenoUrl, ["deno.exe"], progress, ct).ConfigureAwait(false);
                    break;
                default:
                    throw new ArgumentException("unknown tool: " + tool, nameof(missing));
            }
        }
    }

    /// <summary>yt-dlp updates itself (<c>yt-dlp -U</c>); FFmpeg and Deno are refreshed by downloading them again.</summary>
    public static ProcessSpec UpdateYtDlp(ToolPaths tools) =>
        new(tools.YtDlp ?? throw new InvalidOperationException("yt-dlp is not installed"), YtDlpArgs.Update(),
            Path.GetDirectoryName(tools.YtDlp));

    private async Task DownloadFileAsync(string tool, string url, string target, IProgress<ToolProgress>? progress, CancellationToken ct)
    {
        var tmp = target + ".download";
        try
        {
            await FetchAsync(tool, url, tmp, progress, ct).ConfigureAwait(false);
            File.Move(tmp, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    private async Task DownloadZipAsync(string tool, string url, string[] wanted, IProgress<ToolProgress>? progress, CancellationToken ct)
    {
        var tmpZip = Path.Combine(ToolsDir, tool + ".zip.download");
        var staged = new List<(string Tmp, string Final)>();
        try
        {
            await FetchAsync(tool, url, tmpZip, progress, ct).ConfigureAwait(false);
            using (var zip = ZipFile.OpenRead(tmpZip))
            {
                foreach (var name in wanted)
                {
                    // Only the files we need, by file name, wherever they are in the zip (never trust zip paths).
                    var entry = zip.Entries.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))
                                ?? throw new InvalidDataException($"{name} is not in the {tool} download");
                    var final = Path.Combine(ToolsDir, name);
                    var tmp = final + ".download";
                    entry.ExtractToFile(tmp, overwrite: true);
                    staged.Add((tmp, final));
                }
            }
            foreach (var (tmp, final) in staged) File.Move(tmp, final, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmpZip)) File.Delete(tmpZip);
            foreach (var (tmp, _) in staged) if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    private async Task FetchAsync(string tool, string url, string target, IProgress<ToolProgress>? progress, CancellationToken ct)
    {
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength;
        await using var input = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var output = File.Create(target);
        var buffer = new byte[81920];
        long done = 0;
        int n;
        while ((n = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
            done += n;
            progress?.Report(new ToolProgress(tool, done, total));
        }
    }
}
