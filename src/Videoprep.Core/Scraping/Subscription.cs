using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Videoprep.Core.Formats;
using Videoprep.Core.Storage;

namespace Videoprep.Core.Scraping;

/// <summary>Filters of a subscription, as stored in subscriptions.json (SCRAPING_SPEC 2).</summary>
public sealed record SubscriptionFilters
{
    [JsonPropertyName("from")] public string? From { get; init; }
    [JsonPropertyName("to")] public string? To { get; init; }
    [JsonPropertyName("minSec")] public int? MinSec { get; init; }
    [JsonPropertyName("maxSec")] public int? MaxSec { get; init; }
    [JsonPropertyName("include")] public IReadOnlyList<string> Include { get; init; } = [];
    [JsonPropertyName("exclude")] public IReadOnlyList<string> Exclude { get; init; } = [];
    [JsonPropertyName("maxCount")] public int? MaxCount { get; init; }
    /// <summary>"newest" or "oldest".</summary>
    [JsonPropertyName("order")] public string Order { get; init; } = "newest";
    [JsonPropertyName("skipLive")] public bool SkipLive { get; init; } = true;
    [JsonPropertyName("skipMembers")] public bool SkipMembers { get; init; } = true;

    /// <summary>The same filters as Explorar; "already downloaded" always means this subscription's own history.</summary>
    public FilterSet ToFilterSet() => new()
    {
        From = Filters.TryParseDate(From, out var f) ? f : null,
        To = Filters.TryParseDate(To, out var t) ? t : null,
        MinSec = MinSec, MaxSec = MaxSec, Include = Include, Exclude = Exclude, MaxCount = MaxCount,
        OldestFirst = Order == "oldest", SkipLive = SkipLive, SkipMembers = SkipMembers, SkipDownloaded = true,
    };

    public static SubscriptionFilters FromFilterSet(FilterSet f) => new()
    {
        From = f.From?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        To = f.To?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        MinSec = f.MinSec, MaxSec = f.MaxSec, Include = f.Include, Exclude = f.Exclude, MaxCount = f.MaxCount,
        Order = f.OldestFirst ? "oldest" : "newest", SkipLive = f.SkipLive, SkipMembers = f.SkipMembers,
    };
}

/// <summary>How a subscription's videos are saved (the Descargar choices).</summary>
public sealed record SubscriptionProfile
{
    [JsonPropertyName("fmt")] public string Format { get; init; } = "mp4";
    [JsonPropertyName("cap")] public int Cap { get; init; } = FormatCatalog.DefaultCap;
    /// <summary>"orig" (title of the video) or "series" (prefix + number).</summary>
    [JsonPropertyName("naming")] public string Naming { get; init; } = "orig";
    [JsonPropertyName("prefix")] public string Prefix { get; init; } = "";
    /// <summary>
    /// The profile this subscription follows (F6), or null for its own values. The values above are always kept too:
    /// they are what is used when the profile is deleted.
    /// </summary>
    [JsonPropertyName("profileId")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ProfileId { get; init; }
    [JsonPropertyName("kind")] public string Kind => FormatCatalog.Get(Format).Kind == MediaKind.Audio ? "audio" : "video";
}

public sealed record Subscription
{
    public const string FirstRunMarkSeen = "markExistingAsSeen";
    public const string FirstRunBackfill = "backfillWithFilters";

    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("url")] public string Url { get; init; } = "";
    [JsonPropertyName("enabled")] public bool Enabled { get; init; } = true;
    /// <summary>"videos", "shorts", "streams" (YouTube channels; ignored for other links).</summary>
    [JsonPropertyName("tabs")] public IReadOnlyList<string> Tabs { get; init; } = ["videos"];
    [JsonPropertyName("filters")] public SubscriptionFilters Filters { get; init; } = new();
    [JsonPropertyName("profile")] public SubscriptionProfile Profile { get; init; } = new();
    [JsonPropertyName("folder")] public string Folder { get; init; } = "";
    [JsonPropertyName("subfolderPerSub")] public bool SubfolderPerSub { get; init; }
    [JsonPropertyName("firstRun")] public string FirstRun { get; init; } = FirstRunMarkSeen;
    [JsonPropertyName("lastCheck")] public string? LastCheck { get; init; }
    [JsonPropertyName("lastNew")] public int? LastNew { get; init; }
    [JsonPropertyName("lastError")] public string? LastError { get; init; }

    public static string NewId() => "sub_" + Guid.NewGuid().ToString("N")[..6];

    public IReadOnlyList<SourceTab> SourceTabs() => Tabs.Select(t => t switch
    {
        "shorts" => SourceTab.Shorts,
        "streams" => SourceTab.Streams,
        _ => SourceTab.Videos,
    }).Distinct().ToList();

    public static IReadOnlyList<string> TabNames(IEnumerable<SourceTab> tabs) =>
        tabs.Where(t => t is SourceTab.Videos or SourceTab.Shorts or SourceTab.Streams).Select(SourceUrl.TabPath).Distinct().ToList();

    /// <summary>Where the videos go: the folder, plus a folder named after the subscription when asked.</summary>
    public string TargetFolder() =>
        SubfolderPerSub ? Path.Combine(Folder, Text.SafeName.Make(Name, Id)) : Folder;
}

public sealed record SubscriptionSettings
{
    [JsonPropertyName("checkOnStart")] public bool CheckOnStart { get; init; } = true;
    /// <summary>0 = off.</summary>
    [JsonPropertyName("everyHours")] public int EveryHours { get; init; }
    [JsonPropertyName("scheduledTask")] public bool ScheduledTask { get; init; }
    [JsonPropertyName("maxNewPerCheck")] public int MaxNewPerCheck { get; init; } = 25;
    [JsonPropertyName("pauseBetweenSubsSec")] public IReadOnlyList<int> PauseBetweenSubsSec { get; init; } = [5, 15];
}

public sealed record SubscriptionFile
{
    [JsonPropertyName("version")] public int Version { get; init; } = 1;
    [JsonPropertyName("settings")] public SubscriptionSettings Settings { get; init; } = new();
    [JsonPropertyName("subscriptions")] public IReadOnlyList<Subscription> Subscriptions { get; init; } = [];
}

/// <summary>subscriptions.json (SCRAPING_SPEC 2), written atomically; each subscription's history is archives/&lt;id&gt;.txt.</summary>
public static class SubscriptionStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        IgnoreReadOnlyProperties = false,
    };

    public static SubscriptionFile Load(string path, out string? problem)
    {
        problem = null;
        if (!File.Exists(path)) return new SubscriptionFile();
        try
        {
            var file = JsonSerializer.Deserialize<SubscriptionFile>(File.ReadAllText(path), Options) ?? new SubscriptionFile();
            var good = file.Subscriptions.Where(s => s.Id.Length > 0 && s.Url.Length > 0).ToList();
            if (good.Count != file.Subscriptions.Count) problem = $"{file.Subscriptions.Count - good.Count} subscription(s) could not be read and were left out";
            return file with { Subscriptions = good };
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Keep a copy so a later save never wipes subscriptions the user could still recover by hand.
            var copy = path + ".unreadable-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            try { File.Copy(path, copy, overwrite: true); }
            catch (Exception copyEx) when (copyEx is IOException or UnauthorizedAccessException) { copy = "(no copy: " + copyEx.Message + ")"; }
            problem = $"subscriptions file could not be read ({ex.GetType().Name}: {ex.Message}); a copy was kept: {copy}";
            return new SubscriptionFile();
        }
    }

    public static void Save(string path, SubscriptionFile file) =>
        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(file, Options));

    public static string ArchiveFor(AppPaths paths, Subscription s) => Path.Combine(paths.ArchivesDir, s.Id + ".txt");
}
