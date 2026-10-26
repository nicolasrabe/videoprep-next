using System.Text;

namespace Videoprep.Core.Text;

/// <summary>
/// File-name text typed by the user (custom title, series prefix). Port of Get-SafeName plus AUDIT X1:
/// also removes control characters, trailing dots/spaces, and avoids Windows reserved names (CON, NUL, COM1...).
/// </summary>
public static class SafeName
{
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static string Make(string? text, string fallback = "video")
    {
        var sb = new StringBuilder();
        foreach (var c in text ?? "")
        {
            // Same characters as the legacy script (\/:*?"<>|%) plus control characters.
            if ("\\/:*?\"<>|%".Contains(c) || char.IsControl(c)) continue;
            sb.Append(c);
        }
        var n = sb.ToString().Trim().TrimEnd('.', ' ');
        if (n.Length == 0) return fallback;
        var stem = n.Split('.')[0].TrimEnd();
        if (Reserved.Contains(stem)) n = "_" + n;
        return n;
    }
}
