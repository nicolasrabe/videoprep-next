using System.Text.RegularExpressions;

namespace Videoprep.Core.Storage;

/// <summary>File-name rules that look at what already exists in a folder. Never overwrite anything.</summary>
public static class FileNames
{
    /// <summary>
    /// Port of Get-UniquePath: "base.ext", then "base_2.ext", "base_3.ext"... until a name is free.
    /// <paramref name="exists"/> is injectable for tests (defaults to File.Exists || Directory.Exists).
    /// </summary>
    public static string UniquePath(string dir, string baseName, string ext, Func<string, bool>? exists = null)
    {
        exists ??= p => File.Exists(p) || Directory.Exists(p);
        var p = Path.Combine(dir, baseName + ext);
        for (var n = 2; exists(p); n++) p = Path.Combine(dir, $"{baseName}_{n}{ext}");
        return p;
    }

    /// <summary>
    /// Series numbering (from Start-Batch): highest N among files named "prefixN.anything" in the folder.
    /// Returns 0 when there are none, so numbering starts at 1. The UI asks before continuing after an existing number.
    /// Matching is case-insensitive like the legacy PowerShell -match.
    /// </summary>
    public static int HighestSeriesNumber(string prefix, IEnumerable<string> fileNames)
    {
        var rx = new Regex("^" + Regex.Escape(prefix) + @"(\d+)\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var max = 0;
        foreach (var name in fileNames)
        {
            var m = rx.Match(name);
            if (m.Success && int.TryParse(m.Groups[1].Value, out var n) && n > max) max = n;
        }
        return max;
    }
}
