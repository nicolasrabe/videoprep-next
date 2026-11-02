using Videoprep.Core.Engine;
using Videoprep.Core.Logging;

namespace Videoprep.Core.Preview;

public enum ThumbFormat { Jpg, Png }

/// <summary>
/// RONDA 4 (spec D): saves a thumbnail the person asked for. If the picture already is in the format asked for, its bytes are
/// written as they are (no second compression); otherwise it is converted with the app's own ffmpeg (JPG quality 2, PNG lossless).
/// </summary>
public static class ThumbnailExporter
{
    /// <summary>The format of the picture by its first bytes; null when it is neither JPEG nor PNG (WebP, for example).</summary>
    public static ThumbFormat? Detect(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return ThumbFormat.Jpg;
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A) return ThumbFormat.Png;
        return null;
    }

    public static string Extension(ThumbFormat f) => f == ThumbFormat.Jpg ? ".jpg" : ".png";

    /// <summary>Which format a chosen file name asks for (.png = PNG, anything else = JPG).</summary>
    public static ThumbFormat FormatOf(string path) =>
        Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase) ? ThumbFormat.Png : ThumbFormat.Jpg;

    /// <summary>Writes the picture to <paramref name="path"/>. Returns an empty string when it worked, or why it did not (English, for the log).</summary>
    public static async Task<string> SaveAsync(byte[] source, string path, ThumbFormat want, string? ffmpeg, IProcessRunner runner, Log? log, CancellationToken ct = default)
    {
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            if (Detect(source) == want)
            {
                await File.WriteAllBytesAsync(path, source, ct).ConfigureAwait(false);
                return "";
            }
            if (ffmpeg is null) return "ffmpeg is not installed";
            var tmpIn = Path.Combine(Path.GetTempPath(), "videoprep-thumb-" + Guid.NewGuid().ToString("N")[..8]);
            var tmpOut = tmpIn + Extension(want);
            try
            {
                await File.WriteAllBytesAsync(tmpIn, source, ct).ConfigureAwait(false);
                var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y", "-i", tmpIn, "-frames:v", "1" };
                if (want == ThumbFormat.Jpg) args.AddRange(["-q:v", "2"]);
                args.Add(tmpOut);
                var lines = new List<string>();
                var r = await runner.RunAsync(new ProcessSpec(ffmpeg, args), l => lines.Add(l), ct).ConfigureAwait(false);
                if (r.ExitCode != 0 || !File.Exists(tmpOut) || new FileInfo(tmpOut).Length == 0)
                    return r.StartError ?? $"ffmpeg exit code {r.ExitCode}: {lines.LastOrDefault()}";
                File.Move(tmpOut, path, overwrite: true);
                return "";
            }
            finally
            {
                TryDelete(tmpIn);
                TryDelete(tmpOut);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            log?.Write("Thumbnail: could not save: " + ex.Message);
            return ex.Message;
        }
    }

    private static void TryDelete(string f)
    {
        try { File.Delete(f); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
