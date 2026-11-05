using System.Globalization;
using Videoprep.Core.I18n;
using Videoprep.Core.Text;

namespace Miniprep.App.Logic;

/// <summary>File names for "Guardar como" and for the queue.</summary>
public static class Names
{
    /// <summary>The name offered in "Guardar como": the title, cleaned for Windows file names.</summary>
    public static string Suggested(string? title) => SafeName.Make(title, "video");

    /// <summary>What the person chose in "Guardar como": folder, name without extension, and the format by the file type (.mp3 = audio, anything else = MP4).</summary>
    public static (string Folder, string Name, string Format) FromSavePath(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var name = SafeName.Make(Path.GetFileNameWithoutExtension(path), "video");
        return (Path.GetDirectoryName(path) ?? "", name, ext == ".mp3" ? "mp3" : "mp4");
    }
}

/// <summary>Texts the screens show about a video: "Canal · 21 oct 2026 · 41 mil vistas", "Canal · 38:12 · MP4 1080p"...</summary>
public static class Display
{
    private static CultureInfo Culture(string lang) => CultureInfo.GetCultureInfo(lang == "es" ? "es-AR" : "en-US");

    /// <summary>yyyymmdd (as yt-dlp gives it) to "21 oct 2026" / "Oct 21, 2026"; empty when it is not a date.</summary>
    public static string Date(string yyyymmdd, string lang)
    {
        if (!DateTime.TryParseExact(yyyymmdd, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return "";
        var c = Culture(lang);
        return lang == "es"
            ? $"{d.Day} {c.DateTimeFormat.GetAbbreviatedMonthName(d.Month).TrimEnd('.')} {d.Year}"
            : d.ToString("MMM d, yyyy", c);
    }

    public static string Views(long? views, string lang)
    {
        if (views is not { } v) return "";
        var c = Culture(lang);
        if (v == 1) return Localizer.Get(lang, "mp_views_one");
        if (v < 1000) return string.Format(c, Localizer.Get(lang, "mp_views_fmt"), v);
        if (v < 1_000_000) return string.Format(c, Localizer.Get(lang, "mp_views_k"), v / 1000);
        return string.Format(c, Localizer.Get(lang, "mp_views_m"), v / 100_000 / 10.0);
    }

    /// <summary>"Canal · 21 oct 2026 · 41 mil vistas" without the parts that are not known.</summary>
    public static string ChannelLine(string channel, string uploadDate, long? views, string lang) =>
        string.Join(" · ", new[] { channel, Date(uploadDate, lang), Views(views, lang) }.Where(s => s.Length > 0));

    /// <summary>"MP4 1080p", "MP4 Mejor" or "MP3 audio".</summary>
    public static string FormatQuality(QueueEntry e, string lang)
    {
        if (e.IsAudio) return Localizer.Get(lang, "mp_meta_audio");
        return "MP4 " + (e.Quality == Quality.Best ? Localizer.Get(lang, "mp_q_best_short") : Qualities.Label(e.Quality));
    }

    /// <summary>The line under the title of a queue row: "Canal · 38:12 · MP4 1080p".</summary>
    public static string RowMeta(QueueEntry e, string lang) =>
        string.Join(" · ", new[] { e.Item.Channel, e.Item.Duration, FormatQuality(e, lang) }.Where(s => s.Length > 0));

    public static string StatusText(QueueEntry e, int percent, string lang) => e.Status switch
    {
        EntryStatus.Queued => Localizer.Get(lang, "mp_st_queued"),
        EntryStatus.Downloading => string.Format(Culture(lang), Localizer.Get(lang, "mp_st_dl_fmt"), percent),
        EntryStatus.Done => Localizer.Get(lang, "mp_st_done"),
        _ => Localizer.Get(lang, "mp_st_error"),
    };

    /// <summary>"3 en cola · 1 listo" under the toolbar; empty when the queue is empty.</summary>
    public static string Counter(int queued, int done, int total, string lang)
    {
        if (total == 0) return "";
        return string.Format(Culture(lang), Localizer.Get(lang, done == 1 ? "mp_count_fmt_one" : "mp_count_fmt"), queued, done);
    }
}
