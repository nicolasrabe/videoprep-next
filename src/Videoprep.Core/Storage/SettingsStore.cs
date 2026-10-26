using System.Text.Json;

namespace Videoprep.Core.Storage;

/// <summary>Loads and saves <see cref="Settings"/> as JSON, atomically (AUDIT M8).</summary>
public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Reads the file; missing or unreadable gives defaults (<paramref name="problem"/> says why, for the log).
    /// A file without "version" is a 0.1.x file: its cookie index is migrated.
    /// </summary>
    public static Settings Load(string path, out string? problem)
    {
        problem = null;
        if (!File.Exists(path)) return Defaults();
        try
        {
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var legacy = !doc.RootElement.TryGetProperty("version", out _);
            var s = doc.RootElement.Deserialize<Settings>(Options) ?? new Settings();
            s.Normalize(fromLegacyFile: legacy);
            return s;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            problem = $"settings file could not be read ({ex.GetType().Name}: {ex.Message}); using defaults";
            return Defaults();
        }
    }

    /// <summary>Writes to a temp file next to the target, then replaces it, so a crash never leaves half a file.</summary>
    public static void Save(string path, Settings settings)
    {
        settings.Normalize(fromLegacyFile: false);
        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(settings, Options));
    }

    private static Settings Defaults()
    {
        var s = new Settings();
        s.Normalize(fromLegacyFile: false);
        return s;
    }
}

/// <summary>Write-then-rename helper used for every JSON file videoprep owns.</summary>
public static class AtomicFile
{
    public static void WriteAllText(string path, string text)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);
        var tmp = Path.Combine(dir, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp");
        try
        {
            File.WriteAllText(tmp, text, new System.Text.UTF8Encoding(false));
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }
}
