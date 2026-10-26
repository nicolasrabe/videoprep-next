using System.Globalization;
using System.Text;

namespace Videoprep.Core.Scraping;

/// <summary>The Explorar filters (SCRAPING_SPEC 1). All optional; applied locally, so "Aplicar filtros" needs no new requests.</summary>
public sealed record FilterSet
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public int? MinSec { get; init; }
    public int? MaxSec { get; init; }
    /// <summary>Title must contain ANY of these (case- and accent-insensitive).</summary>
    public IReadOnlyList<string> Include { get; init; } = [];
    /// <summary>Title must contain NONE of these.</summary>
    public IReadOnlyList<string> Exclude { get; init; } = [];
    public int? MaxCount { get; init; }
    public bool OldestFirst { get; init; }
    public bool SkipLive { get; init; } = true;
    public bool SkipMembers { get; init; } = true;
    public bool SkipDownloaded { get; init; } = true;

    public bool UsesDates => From is not null || To is not null;
    public bool UsesDuration => MinSec is not null || MaxSec is not null;
}

public static class Filters
{
    /// <summary>
    /// Keeps the entries that pass, in the chosen order, up to MaxCount. Playlists (rows that open a list) are kept as
    /// they are. An entry whose date (or length) is unknown passes that filter: the exact data is fetched afterwards
    /// for those (<see cref="NeedsExact"/>), and dates are checked again when downloading.
    /// </summary>
    public static IReadOnlyList<ListingEntry> Apply(IEnumerable<ListingEntry> all, FilterSet f, Func<ListingEntry, bool> isDownloaded,
                                                    DateOnly? todayOverride = null)
    {
        var today = todayOverride ?? DateOnly.FromDateTime(DateTime.Today);
        var include = f.Include.Select(Normalize).Where(s => s.Length > 0).ToList();
        var exclude = f.Exclude.Select(Normalize).Where(s => s.Length > 0).ToList();
        var kept = all.Where(e =>
        {
            if (e.IsPlaylist) return true;
            if (f.SkipLive && e.IsLiveNow) return false;
            if (f.SkipMembers && e.IsMembersOnly) return false;
            if (f.SkipDownloaded && isDownloaded(e)) return false;
            if (e.Date is { } d)
            {
                // Approximate dates come from "2 weeks ago"-style texts: only drop what is clearly outside the range.
                var margin = e.DateIsApprox ? ApproxMarginDays(d, today) : 0;
                if (f.From is { } from && d.AddDays(margin) < from) return false;
                if (f.To is { } to && d.AddDays(-margin) > to) return false;
            }
            if (e.DurationSec is { } s && (f.MinSec is { } min && s < min || f.MaxSec is { } max && s > max)) return false;
            var title = Normalize(e.Title);
            if (include.Count > 0 && !include.Any(title.Contains)) return false;
            if (exclude.Any(title.Contains)) return false;
            return true;
        });
        // Newest first by date; equal or unknown dates keep the listing order (YouTube lists newest first).
        var ordered = f.OldestFirst
            ? kept.OrderBy(e => e.Date ?? DateOnly.MaxValue).ThenByDescending(e => e.Order)
            : kept.OrderByDescending(e => e.Date ?? DateOnly.MinValue).ThenBy(e => e.Order);
        var list = ordered.ToList();
        return f.MaxCount is { } n && n >= 0 ? list.Take(n).ToList() : list;
    }

    /// <summary>How wrong an approximate date can be: "3 days ago" is close, "2 months ago" can be weeks off.</summary>
    public static int ApproxMarginDays(DateOnly approx, DateOnly today)
    {
        var age = today.DayNumber - approx.DayNumber;
        return age < 7 ? 1 : age < 31 ? 7 : age < 365 ? 31 : 366;
    }

    /// <summary>Entries that passed but miss data a filter needs (date or length): candidates for the exact step.</summary>
    public static IReadOnlyList<ListingEntry> NeedsExact(IEnumerable<ListingEntry> passed, FilterSet f) =>
        passed.Where(e => !e.IsPlaylist
                          && (f.UsesDates && (e.Date is null || e.DateIsApprox) || f.UsesDuration && e.DurationSec is null))
              .ToList();

    /// <summary>Lower case without accents ("Entrevísta" -> "entrevista").</summary>
    public static string Normalize(string s)
    {
        var d = s.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>"entrevista, review" -> ["entrevista", "review"].</summary>
    public static IReadOnlyList<string> SplitTerms(string? text) =>
        (text ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    /// <summary>"YYYY-MM-DD"; empty gives null with ok = true.</summary>
    public static bool TryParseDate(string? text, out DateOnly? date)
    {
        date = null;
        var t = (text ?? "").Trim();
        if (t.Length == 0) return true;
        if (!DateOnly.TryParseExact(t, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return false;
        date = d;
        return true;
    }

    /// <summary>"SS", "M:SS" or "H:MM:SS"; empty gives null with ok = true.</summary>
    public static bool TryParseDuration(string? text, out int? seconds)
    {
        seconds = null;
        var t = (text ?? "").Trim();
        if (t.Length == 0) return true;
        var parts = t.Split(':');
        if (parts.Length > 3) return false;
        var total = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var v)) return false;
            if (i > 0 && v >= 60) return false;
            total = total * 60 + v;
        }
        seconds = total;
        return true;
    }

    /// <summary>"4:05", "1:02:03".</summary>
    public static string FormatDuration(int? seconds)
    {
        if (seconds is not { } s) return "";
        var t = TimeSpan.FromSeconds(s);
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    /// <summary>"41 mil" / "41K", "1,2 M" / "1.2M".</summary>
    public static string FormatViews(long? views, string language)
    {
        if (views is not { } v) return "";
        var es = language == "es";
        var c = CultureInfo.GetCultureInfo(es ? "es-AR" : "en-US");
        if (v < 1000) return v.ToString(c);
        if (v < 1_000_000) return (v / 1000.0).ToString("0.#", c) + (es ? " mil" : "K");
        return (v / 1_000_000.0).ToString("0.#", c) + (es ? " M" : "M");
    }
}
