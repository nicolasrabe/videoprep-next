using System.Globalization;
using System.Text.Json;

namespace Videoprep.Core.Scraping;

/// <summary>Channel tabs that can be listed (SCRAPING_SPEC 1). <see cref="Single"/> = a playlist or another site's page.</summary>
public enum SourceTab { Videos, Shorts, Streams, Playlists, Single }

/// <summary>
/// One row of a listing, read from <c>yt-dlp --flat-playlist -j</c> (docs/VERIFY.md, phase 5).
/// Dates from the quick listing are approximate (<see cref="DateIsApprox"/>); Shorts come without date or duration.
/// </summary>
public sealed record ListingEntry
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Url { get; init; }
    /// <summary>yt-dlp extractor key in lower case ("youtube"); with <see cref="Id"/> it makes the archive line.</summary>
    public string Extractor { get; init; } = "";
    public int? DurationSec { get; init; }
    public long? Views { get; init; }
    public DateOnly? Date { get; init; }
    public bool DateIsApprox { get; init; }
    /// <summary>"is_live", "is_upcoming", "was_live", "not_live" or null.</summary>
    public string? LiveStatus { get; init; }
    /// <summary>"public", "subscriber_only", "premium_only", "needs_auth"... or null (unknown in the quick listing).</summary>
    public string? Availability { get; init; }
    public SourceTab Tab { get; init; }
    /// <summary>The row is a playlist (Playlists tab), not a video.</summary>
    public bool IsPlaylist { get; init; }
    /// <summary>Position in the listing (YouTube lists newest first); keeps order stable when dates are equal or missing.</summary>
    public int Order { get; init; }

    /// <summary>Line in yt-dlp's --download-archive format.</summary>
    public string ArchiveKey => $"{Extractor} {Id}";

    public bool IsLiveNow => LiveStatus is "is_live" or "is_upcoming";
    public bool IsMembersOnly => Availability is "subscriber_only" or "premium_only";

    /// <summary>Reads one JSON line; null for lines that are not an entry (warnings, empty lines).</summary>
    public static ListingEntry? Parse(string jsonLine, SourceTab tab, int order)
    {
        if (jsonLine.Length == 0 || jsonLine[0] != '{') return null;
        try
        {
            using var doc = JsonDocument.Parse(jsonLine);
            var e = doc.RootElement;
            var id = Str(e, "id");
            if (id is null) return null;
            var ie = Str(e, "ie_key") ?? Str(e, "extractor_key") ?? "";
            var isPlaylist = ie == "YoutubeTab" || Str(e, "_type") == "playlist";
            var date = DateFrom(Str(e, "upload_date")) ?? DateFrom(Long(e, "timestamp"));
            return new ListingEntry
            {
                Id = id,
                Title = Str(e, "title") ?? id,
                Url = Str(e, "url") is { } u && u.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? u : Str(e, "webpage_url") ?? id,
                Extractor = ie.ToLowerInvariant(),
                DurationSec = Long(e, "duration") is { } d ? (int)d : null,
                Views = Long(e, "view_count"),
                Date = date,
                DateIsApprox = date is not null,   // the quick listing never has the exact date
                LiveStatus = Str(e, "live_status"),
                Availability = Str(e, "availability"),
                Tab = tab,
                IsPlaylist = isPlaylist,
                Order = order,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static long? Long(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? (long)v.GetDouble() : null;

    private static DateOnly? DateFrom(string? yyyymmdd) =>
        yyyymmdd is { Length: 8 } && DateOnly.TryParseExact(yyyymmdd, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private static DateOnly? DateFrom(long? unix) =>
        unix is { } t ? DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(t).UtcDateTime) : null;
}

/// <summary>Exact data for one video, from the slower full extraction (only when a filter needs it).</summary>
public sealed record ExactInfo(string Id, DateOnly? Date, int? DurationSec, string? LiveStatus, string? Availability)
{
    public const string PrintTemplate = "EX|%(id)s|%(upload_date)s|%(duration)s|%(live_status)s|%(availability)s";

    public static ExactInfo? Parse(string line)
    {
        if (!line.StartsWith("EX|", StringComparison.Ordinal)) return null;
        var f = line.Split('|');
        if (f.Length < 6) return null;
        static string? Na(string s) => s is "NA" or "" ? null : s;
        DateOnly? date = DateOnly.TryParseExact(f[2], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
        int? dur = double.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? (int)s : null;
        return new ExactInfo(f[1], date, dur, Na(f[4]), Na(f[5]));
    }

    public ListingEntry ApplyTo(ListingEntry e) => e with
    {
        Date = Date ?? e.Date,
        DateIsApprox = Date is null && e.DateIsApprox,
        DurationSec = DurationSec ?? e.DurationSec,
        LiveStatus = LiveStatus ?? e.LiveStatus,
        Availability = Availability ?? e.Availability,
    };
}
