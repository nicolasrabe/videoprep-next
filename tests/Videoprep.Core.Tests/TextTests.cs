using Videoprep.Core.Storage;
using Videoprep.Core.Text;

namespace Videoprep.Core.Tests;

public class UrlParserTests
{
    private const string Zoo = "https://www.youtube.com/watch?v=jNQXAC9IVRw";

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=jNQXAC9IVRw")]
    [InlineData("http://youtube.com/watch?v=jNQXAC9IVRw")]
    [InlineData("youtube.com/watch?feature=share&v=jNQXAC9IVRw&t=10")]
    [InlineData("https://m.youtube.com/watch?v=jNQXAC9IVRw")]
    [InlineData("https://music.youtube.com/watch?v=jNQXAC9IVRw&list=RD")]
    [InlineData("https://youtu.be/jNQXAC9IVRw?si=abc")]
    [InlineData("https://www.youtube.com/shorts/jNQXAC9IVRw")]
    [InlineData("https://www.youtube.com/live/jNQXAC9IVRw?feature=x")]
    [InlineData("https://www.youtube-nocookie.com/embed/jNQXAC9IVRw")]
    [InlineData("HTTPS://WWW.YOUTUBE.COM/watch?v=jNQXAC9IVRw")]   // PowerShell -match was case-insensitive
    [InlineData("jNQXAC9IVRw")]
    public void YouTube_forms_become_the_canonical_url(string text) => Assert.Equal(Zoo, UrlParser.ToYoutubeUrl(text));

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=jNQXAC9IVRwX")]   // 12 characters: not an ID
    [InlineData("https://youtu.be/short")]
    [InlineData("https://www.facebook.com/watch?v=12345678901")]  // Facebook numbers are not YouTube IDs (0.1.1 fix)
    [InlineData("https://www.youtube.com/@NASA")]
    public void Not_a_youtube_video(string text) => Assert.Null(UrlParser.ToYoutubeUrl(text));

    [Fact]
    public void Video_id_and_key()
    {
        Assert.Equal("jNQXAC9IVRw", UrlParser.VideoId(Zoo));
        Assert.Null(UrlParser.VideoId("https://www.youtube.com/watch?v=jNQXAC9IVRw&t=1"));
        Assert.Equal("jNQXAC9IVRw", UrlParser.LinkKey(Zoo));
        var other = UrlParser.LinkKey("https://www.twitch.tv/videos/1");
        Assert.Matches("^h[0-9a-f]{12}$", other);
        Assert.Equal(other, UrlParser.LinkKey("https://www.twitch.tv/videos/1"));
        Assert.False(UrlParser.IsYoutubeKey(other));
    }

    [Theory]
    [InlineData("twitch.tv/videos/123", "https://twitch.tv/videos/123")]
    [InlineData("clips.twitch.tv/Abc", "https://clips.twitch.tv/Abc")]
    [InlineData("www.instagram.com/reel/x", "https://www.instagram.com/reel/x")]
    [InlineData("https://vimeo.com/1", "https://vimeo.com/1")]
    [InlineData("HTTP://example.org/v", "HTTP://example.org/v")]
    public void Other_sites(string token, string expected) => Assert.Equal(expected, UrlParser.ToAnyUrl(token));

    [Theory]
    [InlineData("example.org/v")]   // unknown host without https:// is not a link
    [InlineData("hola")]
    public void Not_links(string token) => Assert.Null(UrlParser.ToAnyUrl(token));

    [Fact]
    public void Links_from_text_in_order()
    {
        var text = "mirá https://youtu.be/jNQXAC9IVRw y twitch.tv/videos/9\r\nhttps://vimeo.com/1";
        Assert.Equal([Zoo, "https://twitch.tv/videos/9", "https://vimeo.com/1"], UrlParser.LinksFromText(text));
    }

    [Fact]
    public void Bare_id_only_when_alone_on_its_line_AUDIT_M10()
    {
        Assert.Equal([Zoo], UrlParser.LinksFromText("jNQXAC9IVRw"));
        Assert.Equal([Zoo], UrlParser.LinksFromText("  jNQXAC9IVRw  \n"));
        // A .txt file with one ID per line still works.
        Assert.Equal(2, UrlParser.LinksFromText("jNQXAC9IVRw\r\nM7lc1UVf-VE\r\n").Count);
        // An 11-letter word in a sentence is not a video.
        Assert.Empty(UrlParser.LinksFromText("Hello_World is a test"));
        Assert.Empty(UrlParser.LinksFromText("ver jNQXAC9IVRw ahora"));
    }
}

public class SafeNameTests
{
    [Theory]
    [InlineData("Mi video: parte 1/2?", "Mi video parte 12")]
    [InlineData("  100% real  ", "100 real")]
    [InlineData("<>|*", "video")]
    [InlineData("", "video")]
    [InlineData("tab\there", "tabhere")]          // AUDIT X1: control characters
    [InlineData("final...", "final")]             // AUDIT X1: trailing dots
    [InlineData("CON", "_CON")]                   // AUDIT X1: reserved names
    [InlineData("nul.txt", "_nul.txt")]
    [InlineData("Console", "Console")]
    [InlineData("Fragmento ñandú", "Fragmento ñandú")]
    public void Makes_windows_safe_names(string input, string expected) => Assert.Equal(expected, SafeName.Make(input));

    [Fact]
    public void Custom_fallback() => Assert.Equal("Video", SafeName.Make("???", "Video"));
}

public class FileNamesTests
{
    [Fact]
    public void Unique_path_never_reuses_an_existing_name()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine("d", "clip.mp4"), Path.Combine("d", "clip_2.mp4"),
        };
        Assert.Equal(Path.Combine("d", "clip_3.mp4"), FileNames.UniquePath("d", "clip", ".mp4", taken.Contains));
        Assert.Equal(Path.Combine("d", "other.mp4"), FileNames.UniquePath("d", "other", ".mp4", taken.Contains));
    }

    [Fact]
    public void Unique_path_on_a_real_folder()
    {
        var dir = Directory.CreateTempSubdirectory("videoprep-test-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.mp4"), "");
            Assert.Equal(Path.Combine(dir, "a_2.mp4"), FileNames.UniquePath(dir, "a", ".mp4"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Series_numbering_continues_after_the_highest()
    {
        string[] files = ["Clip1.mp4", "Clip2.mov", "clip10.mp4", "Clip3", "Clipx.mp4", "MyClip4.mp4", "Clip 5.mp4"];
        Assert.Equal(10, FileNames.HighestSeriesNumber("Clip", files));
        Assert.Equal(0, FileNames.HighestSeriesNumber("Video", files));
        Assert.Equal(0, FileNames.HighestSeriesNumber("C.ip", files));   // prefix is literal, not a regex
    }
}
