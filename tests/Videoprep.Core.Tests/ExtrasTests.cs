using System.Globalization;
using System.Text;
using Videoprep.Core.Engine;
using Videoprep.Core.Import;
using Videoprep.Core.Models;
using Videoprep.Core.Profiles;
using Videoprep.Core.Scraping;
using Videoprep.Core.Sources;
using Videoprep.Core.Storage;

namespace Videoprep.Core.Tests;

/// <summary>Round 2 of 0.3.0 in the engine: profiles (F6), the list of sources (F7), clipboard links (F5), mini placement (F4).</summary>
public sealed class ProfileTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("videoprep-profiles-").FullName;
    private string Path_ => Path.Combine(_dir, "profiles.json");

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); }
        catch (IOException) { }
    }

    [Fact]
    public void Built_in_examples_exist_and_cannot_be_changed()
    {
        var names = ProfileStore.BuiltIns.Select(p => p.Name).ToList();
        Assert.Equal(["Premiere 1080p", "ProRes 4K", "Audio (M4A)"], names);
        Assert.All(ProfileStore.BuiltIns, p => Assert.True(p.BuiltIn));
        Assert.Equal(("movp", 2160), (ProfileStore.BuiltIns[1].Format, ProfileStore.BuiltIns[1].Cap));
        var store = ProfileStore.Load(Path_, out _);
        Assert.False(store.Remove("builtin:prores-4k"));
        Assert.Null(store.Rename("builtin:prores-4k", "otro"));
        Assert.Null(store.ReplaceChoices("builtin:prores-4k", new FormatProfile { Format = "mp4" }));
        Assert.Equal(3, store.All.Count());
    }

    [Fact]
    public void Save_and_load_round_trip_with_a_version_and_no_leftover_temp_files()
    {
        var store = ProfileStore.Load(Path_, out var problem);
        Assert.Null(problem);
        var p = store.Add(new FormatProfile { Name = "  Entrevistas   4K ", Format = "movp", Cap = 2160, Naming = "series", Prefix = "Ent:", ConstantRate = true });
        Assert.Equal("Entrevistas 4K", p.Name);
        Assert.Equal("Ent", p.Prefix);                                         // safe file-name text
        Assert.StartsWith("p_", p.Id);

        var json = File.ReadAllText(Path_);
        Assert.Contains("\"version\": 1", json);
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        Assert.Empty(Directory.GetFiles(_dir, ".*"));

        var back = ProfileStore.Load(Path_, out _);
        var q = Assert.Single(back.User);
        Assert.Equal(p, q);
        Assert.True(q.ConstantRate);                                          // F8's flag is kept although no screen shows it
        Assert.Same(back.Find(p.Id), back.User[0]);
    }

    [Fact]
    public void A_file_that_cannot_be_read_is_kept_as_bad_and_the_store_starts_empty()
    {
        File.WriteAllText(Path_, "{ nope");
        var store = ProfileStore.Load(Path_, out var problem);
        Assert.Empty(store.User);
        Assert.Contains("profiles.json.bad", problem);
        Assert.True(File.Exists(Path_ + ".bad"));
        Assert.False(File.Exists(Path_));
    }

    [Fact]
    public void Odd_values_in_the_file_are_fixed_when_loading()
    {
        File.WriteAllText(Path_, """
            { "version": 1, "profiles": [
              { "id": "p_1", "name": "Bueno", "fmt": "no-such", "cap": 7, "naming": "weird", "prefix": "x" },
              { "id": "p_2", "name": "   " },
              { "id": "builtin:fake", "name": "Falso" },
              { "id": "p_1", "name": "Repetido id" }
            ] }
            """);
        var p = Assert.Single(ProfileStore.Load(Path_, out _).User);
        Assert.Equal(("Bueno", "mp4", 1080, "orig", ""), (p.Name, p.Format, p.Cap, p.Naming, p.Prefix));
    }

    [Fact]
    public void Names_are_unique_ignoring_case_and_also_against_the_examples()
    {
        var store = ProfileStore.InMemory();
        store.Add(new FormatProfile { Name = "Mi perfil" });
        Assert.True(store.NameTaken("mi PERFIL"));
        Assert.True(store.NameTaken("premiere 1080p"));
        Assert.False(store.NameTaken("Mi perfil", exceptId: store.User[0].Id));
        Assert.Equal("Mi perfil (2)", store.UniqueName("Mi perfil"));
        Assert.True(store.NameTaken("Solo audio (M4A)", null, ["Solo audio (M4A)"]));          // the translated example name
        Assert.Equal(FormatProfile.MaxNameLength, FormatProfile.CleanName(new string('a', 100)).Length);
        Assert.Equal("a b", FormatProfile.CleanName("a\n\tb"));
    }

    [Fact]
    public void Rename_replace_and_remove()
    {
        var store = ProfileStore.Load(Path_, out _);
        var p = store.Add(new FormatProfile { Name = "Uno", Format = "mp4", Cap = 720 });
        Assert.Equal("Dos", store.Rename(p.Id, " Dos ")!.Name);
        var r = store.ReplaceChoices(p.Id, new FormatProfile { Format = "mp3" })!;
        Assert.Equal(("Dos", "mp3", p.Id), (r.Name, r.Format, r.Id));
        Assert.True(store.Remove(p.Id));
        Assert.Empty(ProfileStore.Load(Path_, out _).User);
    }

    [Fact]
    public void Export_then_import_gives_the_same_profiles_with_new_unique_names()
    {
        var a = ProfileStore.Load(Path_, out _);
        a.Add(new FormatProfile { Name = "Uno", Format = "movp", Cap = 2160 });
        a.Add(new FormatProfile { Name = "Dos", Format = "m4a", Naming = "series", Prefix = "Voz" });
        var json = ProfileStore.Export(a.User);
        Assert.DoesNotContain("p_", json);                                                   // ids stay home

        var file = Path.Combine(_dir, "export.json");
        File.WriteAllText(file, json);
        var b = ProfileStore.InMemory([new FormatProfile { Id = "p_x", Name = "Uno" }]);
        var result = b.Import(file);
        Assert.Null(result.Problem);
        Assert.Equal(["Uno (2)", "Dos"], result.Added.Select(p => p.Name));                  // never replaces "Uno"
        Assert.Equal(3, b.User.Count);
        Assert.Equal(("m4a", "series", "Voz"), (b.User[2].Format, b.User[2].Naming, b.User[2].Prefix));
        Assert.Equal(3, b.User.Select(p => p.Id).Distinct().Count());
    }

    [Fact]
    public void Import_treats_the_file_as_untrusted()
    {
        var store = ProfileStore.InMemory();
        Assert.Equal("not a profiles file", store.ImportJson("[1,2,3]").Problem);
        Assert.Equal("not a profiles file", store.ImportJson("hola").Problem);

        var big = Path.Combine(_dir, "big.json");
        File.WriteAllText(big, new string(' ', (int)ProfileStore.MaxImportBytes + 10));
        Assert.Equal("too big", store.Import(big).Problem);
        Assert.Equal("missing", store.Import(Path.Combine(_dir, "nope.json")).Problem);

        var many = "{\"version\":1,\"profiles\":[" + string.Join(",", Enumerable.Range(0, 300).Select(i => $"{{\"name\":\"P{i}\",\"fmt\":\"zzz\",\"cap\":3}}")) + "]}";
        var r = store.ImportJson(many);
        Assert.Equal(ProfileStore.MaxProfiles, store.User.Count);
        Assert.Equal(100, r.Skipped);
        Assert.All(store.User, p => Assert.Equal(("mp4", 1080), (p.Format, p.Cap)));          // bad values fixed, not trusted
    }

    // ---------------- subscriptions ----------------

    private static Subscription Sub(string id, string? profileId, string fmt = "mp4", int cap = 1080) => new()
    {
        Id = id, Name = id, Url = "https://vimeo.com/x", Folder = "D",
        Profile = new SubscriptionProfile { Format = fmt, Cap = cap, ProfileId = profileId },
    };

    [Fact]
    public void A_subscription_follows_its_profile_and_falls_back_when_it_is_deleted()
    {
        var store = ProfileStore.InMemory();
        var p = store.Add(new FormatProfile { Name = "Uno", Format = "movp", Cap = 2160, Naming = "series", Prefix = "Toma" });
        var s = Sub("a", p.Id, "mp4", 720);

        var live = ProfileLinks.Effective(s.Profile, store);
        Assert.Equal(("movp", 2160, "series", "Toma"), (live.Format, live.Cap, live.Naming, live.Prefix));

        store.ReplaceChoices(p.Id, new FormatProfile { Format = "mp3" });
        Assert.Equal("mp3", ProfileLinks.Effective(s.Profile, store).Format);                // an edit counts at once

        var synced = ProfileLinks.Sync(s, store.Find(p.Id)!);                                // what the app stores when the profile is edited
        store.Remove(p.Id);
        var fallback = ProfileLinks.Effective(synced.Profile, store);
        Assert.Equal(("mp3", 1080), (fallback.Format, fallback.Cap));                         // the last values the profile had
        Assert.Equal("mp4", ProfileLinks.Effective(Sub("b", null).Profile, store).Format);

        Assert.Equal(["a"], ProfileLinks.Using(p.Id, [s, Sub("b", null), Sub("c", "other")]).Select(x => x.Id));
        Assert.Null(ProfileLinks.Detach(synced).Profile.ProfileId);
    }

    [Fact]
    public void Subscriptions_files_without_a_profile_id_still_load_and_the_id_is_saved_only_when_set()
    {
        var path = Path.Combine(_dir, "subscriptions.json");
        SubscriptionStore.Save(path, new SubscriptionFile { Subscriptions = [Sub("a", null), Sub("b", "p_1")] });
        var text = File.ReadAllText(path);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(text, "profileId"));
        var back = SubscriptionStore.Load(path, out var problem);
        Assert.Null(problem);
        Assert.Equal([null, "p_1"], back.Subscriptions.Select(s => s.Profile.ProfileId));
    }
}

public sealed class SourceListTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("videoprep-sources-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); }
        catch (IOException) { }
    }

    private static readonly string[] Headers = ["Título", "Canal", "Enlace", "Nombre del archivo guardado", "Ruta", "Formato", "Fecha de descarga", "Estado"];

    private static SourceRow Row(string title = "Big Buck Bunny", string url = "https://www.youtube.com/watch?v=aqz-KE-bpKQ", string file = "Big Buck Bunny.mp4") =>
        new(title, "Blender", url, file, @"D:\Videos\" + file, "MP4 · 1080p", "2026-10-29 14:05", "Listo");

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=aqz-KE-bpKQ", "https://www.youtube.com/watch?v=aqz-KE-bpKQ")]
    [InlineData("https://user:pass@example.com/v/1?token=abc#frag", "https://example.com/v/1")]
    [InlineData("https://cdn.example.com/v.mp4?Signature=AbC&Expires=17&Key-Pair-Id=K1&id=77", "https://cdn.example.com/v.mp4?id=77")]
    [InlineData("https://example.com/v?x=0123456789abcdef0123456789abcdef&t=12", "https://example.com/v?t=12")]
    [InlineData("https://vimeo.com/76979871?auth_key=zzz&h=ab12", "https://vimeo.com/76979871?h=ab12")]
    [InlineData("https://example.com/watch?sessionid=9&v=abc", "https://example.com/watch?v=abc")]
    [InlineData("https://example.com:8443/a", "https://example.com:8443/a")]
    [InlineData("ftp://example.com/a", "")]
    [InlineData("file:///C:/x.mp4", "")]
    [InlineData("", "")]
    public void Links_lose_session_data(string url, string expected) => Assert.Equal(expected, SourceList.CleanUrl(url));

    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+54 11", "'+54 11")]
    [InlineData("-5 tips", "'-5 tips")]
    [InlineData("@usuario", "'@usuario")]
    [InlineData("\tx", "'\tx")]
    [InlineData("normal", "normal")]
    [InlineData("", "")]
    public void Text_a_spreadsheet_could_run_gets_an_apostrophe(string cell, string expected) => Assert.Equal(expected, SourceList.Guard(cell));

    [Fact]
    public void Separator_follows_the_language_of_windows()
    {
        Assert.Equal(';', SourceList.SeparatorFor(new CultureInfo("es-AR")));
        Assert.Equal(';', SourceList.SeparatorFor(new CultureInfo("de-DE")));
        Assert.Equal(',', SourceList.SeparatorFor(new CultureInfo("en-US")));
        Assert.Equal(',', SourceList.SeparatorFor(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Csv_quotes_cells_and_reads_back_with_the_same_reader_as_the_import()
    {
        var rows = new[]
        {
            Row("Hola; \"mundo\"\ny más", file: "a.mp4"),
            Row("=HYPERLINK(\"http://evil\")", "https://example.com/v?token=SECRET&v=1", "b.mp4"),
        };
        var csv = SourceList.Csv(rows, Headers, ';');
        Assert.StartsWith("Título;Canal;Enlace;Nombre del archivo guardado;Ruta;Formato;Fecha de descarga;Estado\r\n", csv);
        Assert.DoesNotContain("SECRET", csv);
        var back = MarkupLinks.CsvRows(csv).ToList();
        Assert.Equal(3, back.Count);
        Assert.All(back, r => Assert.Equal(8, r.Count));
        Assert.Equal("Hola; \"mundo\"\ny más", back[1][0]);
        Assert.Equal("'=HYPERLINK(\"http://evil\")", back[2][0]);
        Assert.Equal("https://example.com/v?v=1", back[2][2]);
        Assert.Equal("b.mp4", back[2][3]);

        Assert.Contains(',', SourceList.Csv([Row()], Headers, ',').Split("\r\n")[1]);
    }

    [Fact]
    public void Txt_has_one_line_per_file_with_tabs()
    {
        var txt = SourceList.Txt([Row("A\nB", file: "x.mp4")], Headers);
        var lines = txt.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Equal(8, lines[1].Split('\t').Length);
        Assert.StartsWith("A B\t", lines[1]);
    }

    [Fact]
    public void The_file_is_written_with_a_bom_a_dated_name_and_never_over_another_list()
    {
        var now = new DateTime(2026, 10, 29, 14, 5, 0);
        Assert.Equal("fuentes-20261029-1405.csv", SourceList.FileName(now, "csv"));
        Assert.Equal("fuentes-20261029-1405.txt", SourceList.FileName(now, "txt"));

        var sub = Path.Combine(_dir, "guion");
        var one = SourceList.Write(sub, now, "csv", [Row()], Headers, new CultureInfo("es-AR"));
        var two = SourceList.Write(sub, now, "csv", [Row()], Headers, new CultureInfo("es-AR"));
        Assert.Equal("fuentes-20261029-1405.csv", Path.GetFileName(one));
        Assert.Equal("fuentes-20261029-1405_2.csv", Path.GetFileName(two));
        var bytes = File.ReadAllBytes(one);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        Assert.Contains("Título;Canal", Encoding.UTF8.GetString(bytes));

        var txt = SourceList.Write(sub, now, "txt", [Row()], Headers, CultureInfo.InvariantCulture);
        Assert.EndsWith("fuentes-20261029-1405.txt", txt);
    }
}

public sealed class ClipboardAndMiniTests
{
    private const string Bunny = "https://www.youtube.com/watch?v=aqz-KE-bpKQ";

    [Fact]
    public void Only_clear_video_links_are_taken_from_copied_text()
    {
        var text = "mirá esto https://youtu.be/aqz-KE-bpKQ?si=x y también " +
                   "https://www.twitch.tv/ejemplo/clip/AbCdEf\n" +
                   "https://www.youtube.com/watch?v=aqz-KE-bpKQ&t=9 (repetido)\n" +
                   "https://www.youtube.com/@BlenderStudio\n" +                    // a channel
                   "https://www.lanacion.com.ar/politica/nota-nid123/\n" +         // a web page
                   "https://miempresa.com/informe.pdf\n" +
                   "https://cdn.example.org/clip.mp4\n" +                          // unknown site with a video file
                   "x.com/usuario/status/1234567890";
        Assert.Equal([Bunny, "https://www.twitch.tv/ejemplo/clip/AbCdEf", "https://x.com/usuario/status/1234567890"],
                     ClipboardLinks.Find(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("jNQXAC9IVRw")]                      // a bare ID is not a link in the clipboard
    [InlineData("hola, ¿cómo andás?")]
    public void Ordinary_text_has_no_links(string? text) => Assert.Empty(ClipboardLinks.Find(text));

    [Fact]
    public void A_huge_copy_is_ignored()
    {
        Assert.Empty(ClipboardLinks.Find(Bunny + new string(' ', ClipboardLinks.MaxChars)));
        Assert.Equal(20, ClipboardLinks.AskAbove);
    }

    [Theory]
    // saved, size, screens (x,y,w,h) -> where the window opens
    [InlineData(100, 200, 1920, 1080, 100, 200)]                    // still on the screen: stays
    [InlineData(null, null, 1920, 1080, 1920 - 300 - 24, 24)]       // never moved: top right of the main screen
    [InlineData(4000, 200, 1920, 1080, 1920 - 300 - 24, 24)]        // the second monitor is gone
    [InlineData(-900, 5, 1920, 1080, 1920 - 300 - 24, 24)]
    [InlineData(1700, 1060, 1920, 1080, 1700, 1060)]                // 220 x 20 px still visible: reachable
    [InlineData(1890, 1070, 1920, 1080, 1920 - 300 - 24, 24)]       // only a corner: lost
    public void Mini_opens_where_it_was_left_or_back_on_screen(int? x, int? y, int sw, int sh, int expectX, int expectY)
    {
        var (px, py) = MiniPlacement.Place(x, y, 300, 28, [new PixelBox(0, 0, sw, sh)]);
        Assert.Equal((expectX, expectY), (px, py));
    }

    [Fact]
    public void Mini_can_be_on_any_of_several_screens()
    {
        PixelBox[] screens = [new(0, 0, 1920, 1040), new(1920, 0, 1280, 1024)];
        Assert.Equal((2500, 400), MiniPlacement.Place(2500, 400, 300, 28, screens));
        Assert.Equal((100, 5), MiniPlacement.Place(100, 5, 300, 28, [screens[0]]));
        Assert.Equal((2500 - 0, 400), MiniPlacement.Place(2500, 400, 300, 28, screens));
    }
}

/// <summary>The engine keeps what the download itself says about a video (title, channel) for the sources list.</summary>
[Collection("engine")]   // process-wide environment variables of the fake yt-dlp
public sealed class EngineMetaTests : IDisposable
{
    private readonly FakeYtDlpRig _rig = new();

    public void Dispose() => _rig.Dispose();

    [Fact]
    public async Task Title_and_channel_come_from_the_same_download_run()
    {
        var job = FakeYtDlpRig.Job("ok", "m1");
        var result = await _rig.Engine.RunAsync([job], _rig.Context(), TestContext.Current.CancellationToken);
        var meta = Assert.Contains(job.GroupId, result.Metas!);
        Assert.Equal(("Fake video m1", "Fake channel", job.Url), (meta.Title, meta.Channel, meta.Url));
        var call = Assert.Single(_rig.Calls());
        Assert.Equal(1, call.Count(a => a == YtDlpArgs.MetaPrint));
        Assert.Equal(2, call.Count(a => a == "--print-to-file"));                     // the path file and the meta file
        Assert.Empty(Directory.GetFiles(Path.GetTempPath(), "videoprep-meta-*.txt"));  // the temp file is deleted
    }

    [Fact]
    public async Task A_failed_download_has_no_meta_and_the_batch_still_reports_it()
    {
        var job = FakeYtDlpRig.Job("error", "m2");
        var result = await _rig.Engine.RunAsync([job], _rig.Context(), TestContext.Current.CancellationToken);
        Assert.Empty(result.Metas!);
        Assert.Equal(1, result.Fail);
    }

    [Theory]
    [InlineData("{\"title\": \"T\", \"channel\": null, \"uploader\": \"U\", \"webpage_url\": \"https://x/y\"}", "T", "U")]
    [InlineData("{\"title\": \"T\", \"channel\": \"C\"}", "T", "C")]
    public void Meta_line_is_read_with_the_uploader_as_the_channel_when_there_is_none(string json, string title, string channel)
    {
        var file = Path.Combine(_rig.Dir, "meta.txt");
        File.WriteAllText(file, "ruido\n" + json + "\n");
        var meta = JobEngine.ReadMeta(file)!;
        Assert.Equal((title, channel), (meta.Title, meta.Channel));
        Assert.False(File.Exists(file));
        Assert.Null(JobEngine.ReadMeta(Path.Combine(_rig.Dir, "missing.txt")));
        File.WriteAllText(file, "{ roto");
        Assert.Null(JobEngine.ReadMeta(file));
    }
}

public sealed class SettingsAdditionsTests
{
    [Fact]
    public void New_settings_have_safe_defaults_and_old_files_still_load()
    {
        var s = new Settings();
        Assert.True(s.MiniOnTop);
        Assert.False(s.ClipboardWatch);                       // reads nothing by default
        Assert.False(s.SourceList);
        Assert.Equal("csv", s.SourceListFormat);
        Assert.Null(s.MiniX);

        var dir = Directory.CreateTempSubdirectory("videoprep-set-").FullName;
        var path = Path.Combine(dir, "settings.json");
        File.WriteAllText(path, """{ "version": 2, "folder": "D:\\Videos", "theme": "dark", "sourceListFormat": "xml" }""");
        var back = SettingsStore.Load(path, out var problem);
        Assert.Null(problem);
        Assert.Equal(("D:\\Videos", "dark", true, false), (back.Folder, back.Theme, back.MiniOnTop, back.ClipboardWatch));
        Assert.Equal("csv", back.SourceListFormat);           // an unknown value goes back to the default
        Directory.Delete(dir, true);
    }

    [Fact]
    public void The_home_variable_moves_every_folder_for_checks_that_must_not_touch_the_users_files()
    {
        var home = Path.Combine(Path.GetTempPath(), "videoprep-home-test");
        Environment.SetEnvironmentVariable(AppPaths.HomeVariable, home);
        try
        {
            var p = AppPaths.ForCurrentUser();
            Assert.Equal(Path.Combine(home, "roaming", "settings.json"), p.SettingsFile);
            Assert.Equal(Path.Combine(home, "local", "tools"), p.ToolsDir);
            Assert.Equal(Path.Combine(home, "roaming", "profiles.json"), p.ProfilesFile);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppPaths.HomeVariable, null);
        }
    }
}
