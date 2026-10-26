namespace Videoprep.Core.Scraping;

/// <summary>
/// Reads and writes yt-dlp's --download-archive files ("youtube &lt;id&gt;" per line, docs/VERIFY.md). Used for
/// "skip already downloaded" in Explorar and, in tanda B, for the per-subscription history.
/// </summary>
public sealed class ArchiveStore
{
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);

    public static ArchiveStore Load(string path)
    {
        var a = new ArchiveStore();
        if (!File.Exists(path)) return a;
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                var t = line.Trim();
                if (t.Length > 0) a._keys.Add(Normalize(t));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable archive means "nothing known as downloaded"; yt-dlp still checks it when downloading.
        }
        return a;
    }

    public int Count => _keys.Count;

    public bool Contains(ListingEntry e) => _keys.Contains(Normalize(e.ArchiveKey));

    public bool Contains(string extractor, string id) => _keys.Contains(Normalize($"{extractor} {id}"));

    /// <summary>Adds lines (only new ones) to an archive file, creating it if needed.</summary>
    public static void Append(string path, IEnumerable<string> archiveKeys)
    {
        var existing = Load(path);
        var add = archiveKeys.Select(Normalize).Where(k => !existing._keys.Contains(k)).Distinct().ToList();
        if (add.Count == 0) return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.AppendAllLines(path, add);
    }

    // The extractor part is lower case in yt-dlp's archive; ids are case-sensitive.
    private static string Normalize(string line)
    {
        var cut = line.IndexOf(' ');
        return cut < 0 ? line : line[..cut].ToLowerInvariant() + " " + line[(cut + 1)..].Trim();
    }
}
