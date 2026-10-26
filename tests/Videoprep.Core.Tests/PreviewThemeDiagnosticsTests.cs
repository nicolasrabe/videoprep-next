using System.Net;
using System.Text;
using Videoprep.Core.Engine;
using Videoprep.Core.Logging;
using Videoprep.Core.Preview;
using Videoprep.Core.Themes;

namespace Videoprep.Core.Tests;

public class PaletteTests
{
    [Fact]
    public void Six_themes_with_the_legacy_colors()
    {
        Assert.Equal(["light", "dark", "beige", "gray", "slate", "custom"], Palette.Names);
        Assert.Equal("#f1f1ee", Palette.For("light", "").Bg);
        Assert.Equal("#1e1f22", Palette.For("dark", "").Bg);
        Assert.True(Palette.For("dark", "").IsDark);
        Assert.False(Palette.For("beige", "").IsDark);
        Assert.Equal("#f1f1ee", Palette.For("neon", "").Bg);   // unknown -> light
    }

    [Fact]
    public void Custom_light_color_mixes_with_white_and_black()
    {
        var p = Palette.For("custom", "#d9e1ea");
        Assert.Equal("#d9e1ea", p.Bg);
        Assert.False(p.IsDark);
        Assert.Equal(Palette.Mix("#d9e1ea", "#ffffff", 0.6), p.Card);
        Assert.Equal(Palette.Mix("#d9e1ea", "#000000", 0.15), p.Bd);
        Assert.Equal("#1f1f1d", p.Tx);   // light base
    }

    [Fact]
    public void Custom_dark_color_uses_the_dark_base()
    {
        var p = Palette.For("custom", "#203040");
        Assert.True(p.IsDark);
        Assert.Equal("#e8e8e6", p.Tx);
        Assert.Equal(Palette.Mix("#203040", "#ffffff", 0.08), p.Card);
    }

    [Fact]
    public void Bad_custom_color_falls_back_to_light() => Assert.Equal("#f1f1ee", Palette.For("custom", "nope").Bg);

    [Fact]
    public void Mix_math() => Assert.Equal("#808080", Palette.Mix("#000000", "#ffffff", 0.5));
}

public class DiagnosticsTests
{
    [Theory]
    [InlineData("[1/1] https://www.youtube.com/watch?v=jNQXAC9IVRw&t=5&pp=secret", "[1/1] https://www.youtube.com/watch?v=jNQXAC9IVRw")]
    [InlineData("GET https://rr3.googlevideo.com/videoplayback?expire=1&sig=ABC&ip=1.2.3.4", "GET https://rr3.googlevideo.com/videoplayback?…")]
    [InlineData("https://twitch.tv/videos/1", "https://twitch.tv/videos/1")]
    [InlineData(".youtube.com\tTRUE\t/\tTRUE\t0\tSID\tabc", "[line removed: looked like cookie data]")]
    [InlineData("OK    clip.mp4  ->  video h264", "OK    clip.mp4  ->  video h264")]
    public void Lines_are_safe_to_paste(string line, string expected) => Assert.Equal(expected, Diagnostics.SanitizeLine(line));

    [Fact]
    public void Report_has_versions_tools_browsers_and_log()
    {
        var text = Diagnostics.Build("0.2.0", [("yt-dlp", @"C:\t\yt-dlp.exe", "2026.10.21"), ("deno", null, "")], ["firefox"],
                                     ["line one", "https://x.test/a?token=1"]);
        Assert.Contains("videoprep 0.2.0", text);
        Assert.Contains("yt-dlp: 2026.10.21", text);
        Assert.Contains("deno: MISSING", text);
        Assert.Contains("Browsers with a profile: firefox", text);
        Assert.Contains("https://x.test/a?…", text);
        Assert.DoesNotContain("token=1", text);
    }
}

[Collection("engine")]
public sealed class PreviewServiceTests : IDisposable
{
    private readonly FakeYtDlpRig _rig = new();
    private readonly FakeWeb _web = new();

    public void Dispose() => _rig.Dispose();

    /// <summary>oEmbed and thumbnails without the internet.</summary>
    private sealed class FakeWeb : HttpMessageHandler
    {
        public bool OembedFound { get; set; } = true;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            if (url.StartsWith("https://www.youtube.com/oembed", StringComparison.Ordinal))
                return Task.FromResult(OembedFound
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"title":"Me at the zoo","author_name":"jawed"}""", Encoding.UTF8) }
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
            if (url.Contains("ytimg.com") || url.Contains("/thumb/"))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([0xff, 0xd8, 0xff]) });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private PreviewService Service(TimeSpan? timeout = null) =>
        new(new ProcessRunner(), new HttpClient(_web), _rig.Log) { DetailTimeout = timeout ?? TimeSpan.FromSeconds(45) };

    private DetailRequest Req(string fmt = "mp4", params CookieSource[] cookies) =>
        new(fmt, 1080, new ToolPaths(FakeYtDlpRig.FakeExe, null, null, null), cookies.Length > 0 ? cookies : [CookieSource.NoCookies]);

    private static async Task<PreviewInfo> WaitFor(PreviewService s, string key, Func<PreviewInfo, bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (s.Get(key) is { } p && done(p)) return p;
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
        throw new TimeoutException("preview did not finish: " + s.Get(key));
    }

    [Fact]
    public async Task Youtube_gets_oembed_then_the_quality_check()
    {
        var s = Service();
        var first = s.Request("https://www.youtube.com/watch?v=jNQXAC9IVRw", Req());
        Assert.Equal(MetaState.Loading, first.Meta);
        var p = await WaitFor(s, "jNQXAC9IVRw", x => x.Details == DetailState.Done);
        Assert.Equal(("Me at the zoo", "jawed"), (p.Title, p.Author));
        Assert.NotNull(p.Thumbnail);
        Assert.Equal("1080p30", p.Detail!.Resolution);
        Assert.Equal("h264", p.DetailGroup);
    }

    [Fact]
    public async Task Youtube_not_found_stops_without_running_yt_dlp()
    {
        _web.OembedFound = false;
        var s = Service();
        s.Request("https://www.youtube.com/watch?v=jNQXAC9IVRw", Req());
        var p = await WaitFor(s, "jNQXAC9IVRw", x => x.Meta != MetaState.Loading);
        Assert.Equal(MetaState.NotFound, p.Meta);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        Assert.Empty(_rig.Calls());
    }

    [Fact]
    public async Task Other_sites_take_title_and_thumbnail_from_yt_dlp()
    {
        var s = Service();
        var url = "https://fake.test/ok/clip9";
        s.Request(url, Req());
        var p = await WaitFor(s, Text.UrlParser.LinkKey(url), x => x.Details == DetailState.Done && x.Thumbnail is not null);
        Assert.Equal(MetaState.Ready, p.Meta);
        Assert.Equal(("Fake video clip9", "Fake uploader"), (p.Title, p.Author));
    }

    [Fact]
    public async Task Youtube_without_h264_is_its_own_state()
    {
        Environment.SetEnvironmentVariable("FAKE_YTDLP_SCENARIO", "noformat");
        try
        {
            var s = Service();
            s.Request("https://www.youtube.com/watch?v=jNQXAC9IVRw", Req());
            var p = await WaitFor(s, "jNQXAC9IVRw", x => x.Details is DetailState.NoFormat or DetailState.Failed);
            Assert.Equal(DetailState.NoFormat, p.Details);
            Assert.Equal(MetaState.Ready, p.Meta);
        }
        finally { Environment.SetEnvironmentVariable("FAKE_YTDLP_SCENARIO", null); }
    }

    [Fact]
    public async Task Other_site_failure_keeps_the_yt_dlp_error_as_reason()
    {
        var s = Service();
        var url = "https://fake.test/error/x";
        s.Request(url, Req());
        var p = await WaitFor(s, Text.UrlParser.LinkKey(url), x => x.Details == DetailState.Failed);
        Assert.Contains("HTTP Error 404", p.FailReason);
        Assert.Equal(MetaState.NotFound, p.Meta);
        Assert.Single(_rig.Calls());   // already without cookies: no extra try
    }

    [Fact]
    public async Task Bot_check_tries_next_source_then_once_without_cookies_AUDIT_M11()
    {
        var s = Service();
        var url = "https://fake.test/bot/zz";
        s.Request(url, Req("mp4", CookieSource.FromBrowser("firefox"), CookieSource.FromBrowser("chrome")));
        var p = await WaitFor(s, Text.UrlParser.LinkKey(url), x => x.Details == DetailState.Failed);
        Assert.Contains("not a bot", p.FailReason);
        var used = _rig.Calls().Select(c => Array.IndexOf(c, "--cookies-from-browser") is var i and >= 0 ? c[i + 1] : "none").ToList();
        Assert.Equal(["firefox", "chrome", "none"], used);
        Assert.Contains("Preview: yt-dlp failed", _rig.LogText);
    }

    [Fact]
    public async Task Slow_run_ends_with_a_timeout_state_instead_of_loading_forever()
    {
        var s = Service(TimeSpan.FromSeconds(1));
        var url = "https://fake.test/hang/h1";
        s.Request(url, Req());
        var p = await WaitFor(s, Text.UrlParser.LinkKey(url), x => x.Details == DetailState.Failed);
        Assert.Equal("timeout", p.FailReason);
        Assert.Contains("took more than 1 s", _rig.LogText);
    }

    [Fact]
    public async Task Changing_to_a_format_of_another_group_checks_again()
    {
        var s = Service();
        var url = "https://fake.test/ok/g1";
        var key = Text.UrlParser.LinkKey(url);
        s.Request(url, Req("mp4"));
        await WaitFor(s, key, x => x.Details == DetailState.Done);
        s.Refresh(key, Req("mp3"));                          // same group (h264): no new run
        await Task.Delay(300, TestContext.Current.CancellationToken);
        Assert.Single(_rig.Calls());
        s.Refresh(key, Req("webm"));                         // other group: new run
        var p = await WaitFor(s, key, x => x.Details == DetailState.Done && x.DetailGroup == "any");
        Assert.Equal(2, _rig.Calls().Count);
        Assert.Equal("any", p.DetailGroup);
    }
}
