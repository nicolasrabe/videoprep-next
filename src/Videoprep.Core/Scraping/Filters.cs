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
    /// <summary>What the filters say about one entry.</summary>
    public enum Verdict
    {
        /// <summary>Does not pass (or is clearly outside the date range).</summary>
        Drop,
        /// <summary>Passes, and its date (when a date filter is set) is confirmed inside the range.</summary>
        Show,
        /// <summary>Passes everything else, but a date filter is set and its date is missing or only approximate and near the range:
        /// it is NOT shown; the exact date decides (<see cref="NeedsExact"/>) or it is listed apart as "sin fecha confirmada".</summary>
        Doubtful,
    }

    /// <summary>
    /// RONDA 1 spec 1.1: with a date filter a video is shown only if its date is CONFIRMED and inside the range, ends included.
    /// Approximate dates ("2 weeks ago") only decide when they are clearly outside the range; near the edges, or without a
    /// date at all, the entry is <see cref="Verdict.Doubtful"/>. Length is lenient: an unknown length still passes.
    /// </summary>
    public static Verdict Classify(ListingEntry e, FilterSet f, DateOnly today,
                                   IReadOnlyList<string>? include = null, IReadOnlyList<string>? exclude = null)
    {
        if (e.IsPlaylist) return Verdict.Show;
        if (f.SkipLive && e.IsLiveNow) return Verdict.Drop;
        if (f.SkipMembers && e.IsMembersOnly) return Verdict.Drop;
        if (e.DurationSec is { } s && (f.MinSec is { } min && s < min || f.MaxSec is { } max && s > max)) return Verdict.Drop;
        include ??= f.Include.Select(Normalize).Where(t => t.Length > 0).ToList();
        exclude ??= f.Exclude.Select(Normalize).Where(t => t.Length > 0).ToList();
        var title = Normalize(e.Title);
        if (include.Count > 0 && !include.Any(title.Contains)) return Verdict.Drop;
        if (exclude.Any(title.Contains)) return Verdict.Drop;
        if (!f.UsesDates) return Verdict.Show;
        if (e.Date is not { } d) return Verdict.Doubtful;
        if (!e.DateIsApprox)
            return f.From is { } from && d < from || f.To is { } to && d > to ? Verdict.Drop : Verdict.Show;
        // Approximate: how wrong can it be? Only what is clearly outside the range is dropped.
        var margin = ApproxMarginDays(d, today);
        if (f.From is { } from2 && d.AddDays(margin) < from2) return Verdict.Drop;
        if (f.To is { } to2 && d.AddDays(-margin) > to2) return Verdict.Drop;
        return Verdict.Doubtful;
    }

    /// <summary>Entries that pass the filters (shown), those whose date is not confirmed (apart), and how many were looked at.</summary>
    public sealed record Evaluation(IReadOnlyList<ListingEntry> Shown, IReadOnlyList<ListingEntry> Unconfirmed, int Reviewed);

    /// <summary>
    /// Shown = the entries that pass, in the chosen order, up to MaxCount (counted before "skip already downloaded").
    /// Unconfirmed = doubtful ones (see <see cref="Verdict.Doubtful"/>), never mixed with the shown ones. Playlists (rows that open
    /// a list) are kept as they are.
    /// </summary>
    public static Evaluation Evaluate(IReadOnlyList<ListingEntry> all, FilterSet f, Func<ListingEntry, bool> isDownloaded, DateOnly? todayOverride = null)
    {
        var today = todayOverride ?? DateOnly.FromDateTime(DateTime.Today);
        var include = f.Include.Select(Normalize).Where(s => s.Length > 0).ToList();
        var exclude = f.Exclude.Select(Normalize).Where(s => s.Length > 0).ToList();
        var kept = new List<ListingEntry>();
        var doubtful = new List<ListingEntry>();
        foreach (var e in all)
            switch (Classify(e, f, today, include, exclude))
            {
                case Verdict.Show: kept.Add(e); break;
                case Verdict.Doubtful: doubtful.Add(e); break;
            }
        // Newest first by date; equal or unknown dates keep the listing order (YouTube lists newest first).
        var ordered = f.OldestFirst
            ? kept.OrderBy(e => e.Date ?? DateOnly.MaxValue).ThenByDescending(e => e.Order)
            : kept.OrderByDescending(e => e.Date ?? DateOnly.MinValue).ThenBy(e => e.Order);
        var list = ordered.ToList();
        // "Cantidad máx. N" = the N newest (or oldest) that match; what is already downloaded is dropped AFTER that, so a
        // subscription with max 2 never walks back through the whole channel two videos per check (Nico, 2026-10-26).
        if (f.MaxCount is { } n && n >= 0) list = list.Take(n).ToList();
        var shown = f.SkipDownloaded ? list.Where(e => e.IsPlaylist || !isDownloaded(e)).ToList() : list;
        return new Evaluation(shown, doubtful.OrderBy(e => e.Order).ToList(), all.Count);
    }

    /// <summary>The entries that are shown (see <see cref="Evaluate"/>); the unconfirmed ones are left out.</summary>
    public static IReadOnlyList<ListingEntry> Apply(IEnumerable<ListingEntry> all, FilterSet f, Func<ListingEntry, bool> isDownloaded,
                                                    DateOnly? todayOverride = null) =>
        Evaluate(all as IReadOnlyList<ListingEntry> ?? all.ToList(), f, isDownloaded, todayOverride).Shown;

    /// <summary>How wrong an approximate date can be: "3 days ago" is close, "2 months ago" can be weeks off.</summary>
    public static int ApproxMarginDays(DateOnly approx, DateOnly today)
    {
        var age = today.DayNumber - approx.DayNumber;
        return age < 7 ? 1 : age < 31 ? 7 : age < 365 ? 31 : 366;
    }

    /// <summary>
    /// What the exact step has to look at: the doubtful entries (date missing or approximate near the range) and, with a length
    /// filter, the ones whose length is unknown. <paramref name="candidates"/> = shown + unconfirmed.
    /// </summary>
    public static IReadOnlyList<ListingEntry> NeedsExact(IEnumerable<ListingEntry> candidates, FilterSet f) =>
        candidates.Where(e => !e.IsPlaylist
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
