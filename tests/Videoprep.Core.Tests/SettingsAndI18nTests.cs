using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Videoprep.Core.I18n;
using Videoprep.Core.Storage;

namespace Videoprep.Core.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("videoprep-settings-").FullName;
    private string FilePath => Path.Combine(_dir, "settings.json");

    public void Dispose() => Directory.Delete(_dir, true);

    // Exactly what 0.1.1 writes (Save-GuiSettings, PowerShell 5.1 ConvertTo-Json + Set-Content -Encoding UTF8 = with BOM).
    private const string LegacyJson = """
    {
        "folder":  "D:\\Videos\\Cliente",
        "naming":  "series",
        "prefix":  "Toma",
        "cookie":  1,
        "cookieFile":  "",
        "subs":  true,
        "subLangs":  "es",
        "thumbs":  false,
        "archive":  true,
        "fallback":  false,
        "keepFull":  true,
        "crf":  18,
        "limit":  0,
        "sleep":  2,
        "theme":  "dark",
        "customColor":  "#d9e1ea",
        "lang":  "es",
        "autoUpdate":  true,
        "lastUpdate":  "2026-10-01T10:00:00.0000000-03:00",
        "sound":  true,
        "openDone":  false
    }
    """;

    [Fact]
    public void Reads_a_0_1_1_file_and_migrates_the_cookie_index()
    {
        File.WriteAllText(FilePath, LegacyJson, new System.Text.UTF8Encoding(true));
        var s = SettingsStore.Load(FilePath, out var problem);
        Assert.Null(problem);
        Assert.Equal(@"D:\Videos\Cliente", s.Folder);
        Assert.Equal(("series", "Toma"), (s.Naming, s.Prefix));
        Assert.Equal("firefox", s.CookieSource);
        Assert.True(s.Subs && s.Archive && s.KeepFull && s.AutoUpdate && s.Sound);
        Assert.False(s.Fallback);
        Assert.Equal((18, 0, 2), (s.Crf, s.Limit, s.Sleep));
        Assert.Equal(("dark", "es"), (s.Theme, s.Lang));
        Assert.Equal(Settings.CurrentVersion, s.Version);
    }

    [Fact]
    public void Saves_atomically_and_keeps_the_legacy_cookie_index_for_0_1_1()
    {
        var s = new Settings { CookieSource = "none", Folder = @"C:\x", PreferredBrowser = "edge" };
        SettingsStore.Save(FilePath, s);
        Assert.Single(Directory.GetFiles(_dir));   // no temp file left behind

        using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
        Assert.Equal(10, doc.RootElement.GetProperty("cookie").GetInt32());   // 0.1.1 index for "none"
        Assert.Equal("none", doc.RootElement.GetProperty("cookieSource").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("version").GetInt32());

        var back = SettingsStore.Load(FilePath, out _);
        Assert.Equal(("none", @"C:\x", "edge"), (back.CookieSource, back.Folder, back.PreferredBrowser));
    }

    [Fact]
    public void Out_of_range_values_fall_back_to_defaults()
    {
        File.WriteAllText(FilePath, """{"version":2,"crf":99,"limit":-1,"sleep":31,"theme":"neon","lang":"fr","cookieSource":"netscape","naming":"custom","prefix":" ","preferredBrowser":"ie"}""");
        var s = SettingsStore.Load(FilePath, out _);
        Assert.Equal((14, 0, 1), (s.Crf, s.Limit, s.Sleep));
        Assert.Equal(("light", "", "auto", "orig", "Video"), (s.Theme, s.Lang, s.CookieSource, s.Naming, s.Prefix));
        Assert.Null(s.PreferredBrowser);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("")]
    public void Broken_file_gives_defaults_and_a_reason(string content)
    {
        File.WriteAllText(FilePath, content);
        var s = SettingsStore.Load(FilePath, out var problem);
        Assert.NotNull(problem);
        Assert.Equal("auto", s.CookieSource);
        Assert.True(s.Fallback);
    }

    [Fact]
    public void Missing_file_gives_defaults_without_a_problem()
    {
        var s = SettingsStore.Load(Path.Combine(_dir, "nope.json"), out var problem);
        Assert.Null(problem);
        Assert.Equal(14, s.Crf);
    }

    [Theory]
    [InlineData(0, "auto")]
    [InlineData(1, "firefox")]
    [InlineData(8, "whale")]
    [InlineData(9, "file")]
    [InlineData(10, "none")]
    [InlineData(42, "auto")]
    public void Cookie_index_round_trip(int index, string source)
    {
        Assert.Equal(source, Settings.CookieSourceFromIndex(index));
        if (index <= 10) Assert.Equal(index, Settings.CookieIndex(source));
    }

    [Fact]
    public void Video_downloader_folder_is_copied_once()
    {
        var oldDir = Path.Combine(_dir, "VideoDownloader");
        Directory.CreateDirectory(oldDir);
        File.WriteAllText(Path.Combine(oldDir, "downloaded.txt"), "youtube jNQXAC9IVRw\n");
        var paths = new AppPaths(Path.Combine(_dir, "videoprep"), Path.Combine(_dir, "local"));
        Assert.True(paths.MigrateFromVideoDownloader(oldDir));
        Assert.True(File.Exists(paths.ArchiveFile));
        Assert.True(Directory.Exists(oldDir));            // never deleted
        Assert.False(paths.MigrateFromVideoDownloader(oldDir));
        Assert.EndsWith(Path.Combine("local", "tools"), paths.ToolsDir);
    }
}

public partial class LocalizerTests
{
    [GeneratedRegex(@"\{(\d+)\}")]
    private static partial Regex Placeholder();

    [Fact]
    public void Both_languages_have_the_same_keys()
    {
        var es = Localizer.Table("es");
        var en = Localizer.Table("en");
        Assert.Empty(es.Keys.Except(en.Keys));
        Assert.Empty(en.Keys.Except(es.Keys));
        Assert.True(es.Count >= 200, $"only {es.Count} keys");
    }

    [Fact]
    public void Same_placeholders_and_no_empty_texts()
    {
        foreach (var (key, esText) in Localizer.Table("es"))
        {
            var enText = Localizer.Table("en")[key];
            Assert.False(string.IsNullOrWhiteSpace(esText), key);
            Assert.False(string.IsNullOrWhiteSpace(enText), key);
            var a = Placeholder().Matches(esText).Select(m => m.Value).Distinct().Order();
            var b = Placeholder().Matches(enText).Select(m => m.Value).Distinct().Order();
            Assert.True(a.SequenceEqual(b), $"placeholders differ in '{key}'");
        }
    }

    [Fact]
    public void Legacy_keys_and_scraping_keys_are_present()
    {
        foreach (var k in new[] { "tab_dl", "tip_tc", "msg_exists", "rs_Skipped", "h_movp", "tab_scraping", "sc_selected", "sc_blocked", "btn_diag" })
            Assert.True(Localizer.Table("es").ContainsKey(k), k);
        Assert.Equal("Enviar a la cola", new Localizer("es")["btn_send"]);
        Assert.Equal("Send to queue", new Localizer("en")["btn_send"]);
        Assert.Equal("3 de 5 seleccionados", new Localizer("es").Format("sc_selected", 3, 5));
    }

    [Fact]
    public void Falls_back_to_english_then_to_the_key()
    {
        Assert.Equal("en", new Localizer("fr").Language);
        Assert.Equal("missing_key", new Localizer("es")["missing_key"]);
    }

    [Fact]
    public void Default_language_follows_the_system()
    {
        Assert.Equal("es", Localizer.DefaultLanguage(new CultureInfo("es-AR")));
        Assert.Equal("en", Localizer.DefaultLanguage(new CultureInfo("pt-BR")));
    }

    [Fact]
    public void Spanish_texts_avoid_anglicisms_and_keep_the_name_lowercase()
    {
        string[] banned = ["link", "queue", "frame", "timecode", "download", "clip"];
        foreach (var (key, text) in Localizer.Table("es"))
        {
            foreach (var w in banned)
                Assert.False(text.Contains(w, StringComparison.OrdinalIgnoreCase), $"'{w}' in es:{key}");
            Assert.DoesNotContain("Videoprep", text);
        }
        foreach (var (key, text) in Localizer.Table("en")) Assert.False(text.Contains("Videoprep"), key);
    }
}
