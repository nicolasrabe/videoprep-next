namespace Miniprep.App.Logic;

public enum MenuKind { Header, Item, Separator }

/// <summary>One line of the right-click menu of a queue row. <see cref="Label"/> is an i18n key.</summary>
public sealed record MenuLine(MenuKind Kind, string Label = "", string Command = "", bool Checked = false, bool Enabled = true, bool Danger = false);

/// <summary>The right-click menu of a queue row, as data (the window draws it). Only rows have a menu, never the empty space.</summary>
public static class RowMenu
{
    public static IReadOnlyList<MenuLine> For(QueueEntry e)
    {
        var editable = e.Status is EntryStatus.Queued or EntryStatus.Error;
        var audio = e.IsAudio;
        var qEnabled = editable && !audio;
        MenuLine Q(string key, Quality q) => new(MenuKind.Item, key, "q:" + Qualities.Command(q), !audio && e.Quality == q, qEnabled);
        return
        [
            new(MenuKind.Item, "mp_m_rename", "rename", Enabled: editable),
            new(MenuKind.Separator),
            new(MenuKind.Header, "mp_m_format"),
            new(MenuKind.Item, "mp_m_mp4", "fmt:mp4", !audio, editable),
            new(MenuKind.Item, "mp_m_mp3", "fmt:mp3", audio, editable),
            new(MenuKind.Separator),
            new(MenuKind.Header, "mp_m_quality"),
            Q("mp_q_best", Quality.Best), Q("mp_q_1080", Quality.P1080), Q("mp_q_720", Quality.P720), Q("mp_q_480", Quality.P480),
            new(MenuKind.Separator),
            new(MenuKind.Item, "mp_m_thumb", "thumb"),
            new(MenuKind.Item, "mp_m_open", "open"),
            new(MenuKind.Item, "mp_m_copy", "copy"),
            new(MenuKind.Separator),
            new(MenuKind.Item, "mp_m_remove", "remove", Enabled: e.Status != EntryStatus.Downloading, Danger: true),
        ];
    }
}
