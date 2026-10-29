using Videoprep.Core.Formats;
using Videoprep.Core.Text;

namespace Videoprep.Core.Models;

/// <summary>"Nombres" in the "Cambiar N elementos" dialog.</summary>
public enum NameChange { None, Series, AddText, Replace, Original }

/// <summary>What the "Cambiar N elementos" dialog changes; null / <see cref="NameChange.None"/> = "(sin cambios)".</summary>
public sealed record BatchEdit
{
    public string? Format { get; init; }
    public int? Cap { get; init; }
    /// <summary>F8: turn "velocidad constante" on or off for the videos; null = leave as it is.</summary>
    public bool? ConstantRate { get; init; }
    public NameChange Names { get; init; }
    public string Prefix { get; init; } = "";
    public string Before { get; init; } = "";
    public string After { get; init; } = "";
    public string Find { get; init; } = "";
    public string ReplaceWith { get; init; } = "";
}

/// <summary>Editing items already in the queue (F2). Pure functions: the queue is replaced item by item, so Undo is a copy.</summary>
public static class QueueEdits
{
    /// <summary>Rename in the row: the item's own name (empty = back to the original title).</summary>
    public static QueueItem Rename(QueueItem it, string name)
    {
        var n = name.Trim();
        return n.Length == 0
            ? it with { Naming = NamingMode.Original, Custom = "" }
            : it with { Naming = NamingMode.Custom, Custom = n };
    }

    /// <summary>
    /// The base name the user sees for an item: the custom name, the series prefix, or the title when it is known.
    /// Null when the item uses the original title and the title is not known yet.
    /// </summary>
    public static string? BaseName(QueueItem it, string? title) => it.Naming switch
    {
        NamingMode.Custom => it.Custom,
        NamingMode.Series => it.Prefix,
        _ => title is { Length: > 0 } ? title : null,
    };

    /// <summary>
    /// Applies the dialog to one item. <paramref name="title"/> is the video title when known (needed to add text to
    /// or replace text in an original-title name). Items whose name cannot be computed keep their name.
    /// </summary>
    public static QueueItem Apply(QueueItem it, BatchEdit e, string? title)
    {
        var r = it;
        if (e.Format is { } f && FormatCatalog.IsKnown(f)) r = r with { Format = f };
        if (e.Cap is { } cap && FormatCatalog.Get(r.Format).Kind == MediaKind.Video) r = r with { Cap = cap };
        if (e.ConstantRate is { } cfr) r = r with { ConstantRate = cfr && FormatCatalog.Get(r.Format).Kind == MediaKind.Video };
        switch (e.Names)
        {
            case NameChange.Series when e.Prefix.Trim().Length > 0:
                r = r with { Naming = NamingMode.Series, Prefix = SafeName.Make(e.Prefix.Trim(), "Video") };
                break;
            case NameChange.Original:
                r = r with { Naming = NamingMode.Original, Custom = "" };
                break;
            case NameChange.AddText or NameChange.Replace:
                if (BaseName(it, title) is not { } baseName) break;
                var changed = e.Names == NameChange.AddText
                    ? e.Before + baseName + e.After
                    : e.Find.Length > 0 ? baseName.Replace(e.Find, e.ReplaceWith, StringComparison.CurrentCultureIgnoreCase) : baseName;
                if (changed.Trim().Length == 0 || changed == baseName) break;
                r = it.Naming == NamingMode.Series
                    ? r with { Prefix = SafeName.Make(changed.Trim(), "Video") }
                    : r with { Naming = NamingMode.Custom, Custom = changed.Trim() };
                break;
        }
        return r;
    }

    /// <summary>
    /// Names shown for queue rows, like the files will be called (without extension): custom names ("Name - 2" for
    /// several clips), series numbers continuing from <paramref name="nextSeriesNumber"/>, or the title (null when it
    /// is not known yet). One entry per output, in queue order.
    /// </summary>
    public static IReadOnlyList<string?> OutputNames(IEnumerable<QueueItem> items, Func<QueueItem, string?> title,
                                                     IDictionary<string, int> nextSeriesNumber)
    {
        var names = new List<string?>();
        foreach (var it in items)
        {
            var count = Math.Max(1, it.Clips.Count);
            for (var i = 0; i < count; i++)
            {
                switch (it.Naming)
                {
                    case NamingMode.Series:
                        var key = JobPlanner.SeriesKey(it);
                        var n = nextSeriesNumber.TryGetValue(key, out var v) ? v : 1;
                        nextSeriesNumber[key] = n + 1;
                        names.Add(it.Prefix + n);
                        break;
                    case NamingMode.Custom:
                        names.Add(SafeName.Make(it.Custom) + (count > 1 ? " - " + (i + 1) : ""));
                        break;
                    default:
                        names.Add(title(it) is { Length: > 0 } t ? t + (it.Clips.Count > 1 ? " - " + (i + 1) : "") : null);
                        break;
                }
            }
        }
        return names;
    }

    /// <summary>
    /// "El video dura 38:12": the clip's start or end is beyond the known duration. Negative starts count from the end.
    /// Frames use <paramref name="fps"/> (30 when unknown). Never blocks: only a warning.
    /// </summary>
    public static bool BeyondDuration(string timecode, double durationSeconds, double? fps, bool isEnd)
    {
        var tc = Timecode.Parse(timecode);
        if (tc.Blank || !tc.Valid || durationSeconds <= 0) return false;
        var rate = fps is > 0 ? fps.Value : 30;
        var seconds = tc.H * 3600.0 + tc.M * 60 + tc.S + tc.F / Math.Round(rate);
        var oneFrame = 1 / rate;
        if (tc.Negative) return seconds > durationSeconds + oneFrame;
        return isEnd ? seconds > durationSeconds + oneFrame : seconds >= durationSeconds;
    }

    /// <summary>yt-dlp's duration_string ("19", "4:05", "1:02:03") in seconds; null when it is not one.</summary>
    public static double? ParseDuration(string text)
    {
        var parts = text.Trim().Split(':');
        if (parts.Length is < 1 or > 3) return null;
        double total = 0;
        foreach (var p in parts)
        {
            if (!int.TryParse(p, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n)) return null;
            total = total * 60 + n;
        }
        return total;
    }
}
