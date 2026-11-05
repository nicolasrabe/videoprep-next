using Miniprep.App.Logic;
using Miniprep.App.Services;
using Videoprep.Core.Engine;

namespace Miniprep.App.Tests;

public class TextAndPathsTests
{
    [Theory]
    [InlineData("Entrevista: parte 1/2?", "Entrevista")]
    [InlineData("", "video")]
    public void Suggested_name_is_a_valid_windows_name(string title, string startsWith)
    {
        var name = Names.Suggested(title);
        Assert.StartsWith(startsWith, name);
        Assert.DoesNotContain(name, c => "<>:\"/\\|?*".Contains(c));
    }

    [Theory]
    [InlineData(@"C:\Videos\Mi video.mp3", "mp3")]
    [InlineData(@"C:\Videos\Mi video.MP3", "mp3")]
    [InlineData(@"C:\Videos\Mi video.mp4", "mp4")]
    [InlineData(@"C:\Videos\Mi video", "mp4")]
    public void The_file_type_chosen_in_Guardar_como_sets_the_format(string path, string format)
    {
        var (folder, name, fmt) = Names.FromSavePath(path);
        Assert.Equal(@"C:\Videos", folder);
        Assert.Equal("Mi video", name);
        Assert.Equal(format, fmt);
    }

    [Fact]
    public void Dates_views_and_the_channel_line_follow_the_language()
    {
        Assert.Equal("21 oct 2026", Display.Date("20261021", "es"));
        Assert.Equal("Oct 21, 2026", Display.Date("20261021", "en"));
        Assert.Equal("", Display.Date("NA", "es"));
        Assert.Equal("41 mil vistas", Display.Views(41_000, "es"));
        Assert.Equal("41K views", Display.Views(41_000, "en"));
        Assert.Equal("1 vista", Display.Views(1, "es"));
        Assert.Equal("812 vistas", Display.Views(812, "es"));
        Assert.Equal("Canal de ejemplo · 21 oct 2026 · 41 mil vistas", Display.ChannelLine("Canal de ejemplo", "20261021", 41_000, "es"));
        Assert.Equal("Canal", Display.ChannelLine("Canal", "", null, "es"));
    }

    [Fact]
    public void A_row_reads_like_the_mockup()
    {
        var q = new MiniQueue();
        var e = q.Add("https://www.youtube.com/watch?v=aaaaaaaaaaa");
        e = e with { Item = e.Item with { Channel = "Canal de ejemplo", Duration = "38:12" } };
        Assert.Equal("Canal de ejemplo · 38:12 · MP4 1080p", Display.RowMeta(e, "es"));
        Assert.Equal("Canal de ejemplo · 38:12 · MP4 Mejor", Display.RowMeta(e with { Quality = Quality.Best }, "es"));
        Assert.Equal("Canal de ejemplo · 38:12 · MP3 audio", Display.RowMeta(e with { Item = e.Item with { Format = "mp3" } }, "es"));
        Assert.Equal("En cola", Display.StatusText(e, 0, "es"));
        Assert.Equal("Descargando 37%", Display.StatusText(e with { Status = EntryStatus.Downloading }, 37, "es"));
        Assert.Equal("Listo", Display.StatusText(e with { Status = EntryStatus.Done }, 0, "es"));
        Assert.Equal("3 en cola · 1 listo", Display.Counter(3, 1, 4, "es"));
        Assert.Equal("", Display.Counter(0, 0, 0, "es"));
    }

    [Fact]
    public void Tools_are_videoprep_s_when_that_folder_exists_otherwise_miniprep_s_own()
    {
        var root = Directory.CreateTempSubdirectory("miniprep-paths-").FullName;
        var videoprepTools = Path.Combine(root, "videoprep", "tools");
        var own = MiniPaths.For(Path.Combine(root, "miniprep"), videoprepTools);
        Assert.Equal(Path.Combine(root, "miniprep", "tools"), own.ToolsDir);   // videoprep has not installed anything yet

        Directory.CreateDirectory(videoprepTools);
        var shared = MiniPaths.For(Path.Combine(root, "miniprep"), videoprepTools);
        Assert.Equal(videoprepTools, shared.ToolsDir);
        Assert.Equal(Path.Combine(root, "miniprep", "queue.json"), shared.QueueFile);   // data stays miniprep's own
        Assert.Equal(Path.Combine(root, "miniprep", "settings.json"), shared.SettingsFile);
    }

    [Fact]
    public void Browser_cookies_are_never_used_unless_asked()
    {
        var rig = new MiniRig(browsers: ["firefox", "edge"]);
        try
        {
            Assert.Equal([CookieSource.NoCookies], rig.Services.Cookies(useBrowser: false));
            var asked = rig.Services.Cookies(useBrowser: true);
            Assert.Contains(CookieSource.FromBrowser("firefox"), asked);
            Assert.Equal(CookieSource.NoCookies, asked[^1]);   // and finally without cookies
        }
        finally { rig.Dispose(); }
    }

    [Fact]
    public void Every_miniprep_text_exists_in_both_languages_with_the_same_placeholders()
    {
        var es = Videoprep.Core.I18n.Localizer.Table("es").Where(p => p.Key.StartsWith("mp_")).ToList();
        var en = Videoprep.Core.I18n.Localizer.Table("en");
        Assert.True(es.Count > 40);
        foreach (var (key, text) in es)
        {
            Assert.True(en.ContainsKey(key), key);
            Assert.Equal(System.Text.RegularExpressions.Regex.Matches(text, @"\{\d\}").Count, System.Text.RegularExpressions.Regex.Matches(en[key], @"\{\d\}").Count);
        }
    }
}
