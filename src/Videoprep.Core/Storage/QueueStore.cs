using System.Text.Json;
using System.Text.Json.Serialization;
using Videoprep.Core.Models;

namespace Videoprep.Core.Storage;

/// <summary>
/// The queue saved to queue.json after every change, so closing or a crash does not lose it (AUDIT H6).
/// Unknown formats or empty URLs are dropped on load (and reported) instead of breaking the app.
/// </summary>
public static class QueueStore
{
    private sealed record QueueFile(int Version, IReadOnlyList<QueueItem> Items);

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static void Save(string path, IEnumerable<QueueItem> items) =>
        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(new QueueFile(1, items.ToList()), Options));

    public static IReadOnlyList<QueueItem> Load(string path, out string? problem)
    {
        problem = null;
        if (!File.Exists(path)) return [];
        try
        {
            var file = JsonSerializer.Deserialize<QueueFile>(File.ReadAllText(path), Options);
            var all = file?.Items ?? [];
            var good = all.Where(i => !string.IsNullOrWhiteSpace(i.Url) && Formats.FormatCatalog.IsKnown(i.Format)).ToList();
            if (good.Count != all.Count) problem = $"{all.Count - good.Count} queue item(s) could not be read and were left out";
            return good;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            problem = $"queue file could not be read ({ex.GetType().Name}: {ex.Message}); starting with an empty queue";
            return [];
        }
    }
}
