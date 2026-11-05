using System.Text.Json;
using System.Text.Json.Serialization;
using Videoprep.Core.Engine;
using Videoprep.Core.Logging;
using Videoprep.Core.Preview;
using Videoprep.Core.Storage;
using Videoprep.Core.Tools;

namespace Miniprep.App.Services;

/// <summary>
/// Where miniprep keeps its files: %LOCALAPPDATA%\miniprep (settings, queue, logs, caches). The tools (yt-dlp, ffmpeg, deno) are
/// videoprep's own folder when it already exists, so they are not downloaded twice; otherwise miniprep keeps its own.
/// </summary>
public sealed class MiniPaths(string root, string toolsDir)
{
    /// <summary>Moves every miniprep folder under one root (checks and tests; not shown to people).</summary>
    public const string HomeVariable = "MINIPREP_HOME";

    public static MiniPaths ForCurrentUser()
    {
        if (Environment.GetEnvironmentVariable(HomeVariable) is { Length: > 0 } home) return For(home, null);
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "miniprep");
        return For(local, AppPaths.ForCurrentUser().ToolsDir);
    }

    /// <summary>The tools folder of videoprep is used only if it exists (<paramref name="videoprepTools"/> null = not looked for).</summary>
    public static MiniPaths For(string root, string? videoprepTools) =>
        new(root, videoprepTools is not null && Directory.Exists(videoprepTools) ? videoprepTools : Path.Combine(root, "tools"));

    public string Root { get; } = root;
    public string ToolsDir { get; } = toolsDir;
    public string SettingsFile => Path.Combine(Root, "settings.json");
    public string QueueFile => Path.Combine(Root, "queue.json");
    public string LogsDir => Path.Combine(Root, "logs");
    public string CacheDir => Path.Combine(Root, "cache");
    public string SessionCookieFile => Path.Combine(Root, "session-cookies.txt");
}

/// <summary>What miniprep remembers between runs.</summary>
public sealed class MiniSettings
{
    /// <summary>The one destination folder of the whole queue ("" = the Videos folder).</summary>
    [JsonPropertyName("folder")] public string Folder { get; set; } = "";
    /// <summary>The folder of the last "Guardar como" of Prep.</summary>
    [JsonPropertyName("lastSaveFolder")] public string LastSaveFolder { get; set; } = "";

    public string EffectiveFolder() =>
        Folder.Length > 0 ? Folder : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

    public static MiniSettings Load(string path)
    {
        try
        {
            if (File.Exists(path)) return JsonSerializer.Deserialize<MiniSettings>(File.ReadAllText(path)) ?? new();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { /* damaged file: start again */ }
        return new();
    }

    public void Save(string path) => AtomicFile.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
}

/// <summary>The engine-side objects the windows use (tests build one with their own folders).</summary>
public sealed class MiniServices
{
    public required MiniPaths Paths { get; init; }
    public required Log Log { get; init; }
    public required IProcessRunner Runner { get; init; }
    public required ToolManager Tools { get; init; }
    public required VideoInfoService Info { get; init; }
    public required ThumbnailService Thumbs { get; init; }
    public Func<IReadOnlyList<string>> InstalledBrowsers { get; init; } = () => CookieStrategy.InstalledBrowsers();

    public static MiniServices CreateDefault(Log log, MiniPaths paths)
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("miniprep/" + Videoprep.Core.AppInfo.Version);
        var runner = new ProcessRunner();
        var thumbs = new ThumbnailService(http, paths.CacheDir, log);
        return new MiniServices
        {
            Paths = paths, Log = log, Runner = runner, Thumbs = thumbs,
            Tools = new ToolManager(paths.ToolsDir, http),
            Info = new VideoInfoService(runner, http, log, thumbs, paths.CacheDir),
        };
    }

    /// <summary>
    /// Where yt-dlp reads cookies from. By default nowhere: the browser's cookies are read only after the person pressed
    /// "Reintentar con las cookies de tu navegador" (then every installed browser is tried, and finally no cookies).
    /// </summary>
    public IReadOnlyList<CookieSource> Cookies(bool useBrowser)
    {
        if (!useBrowser) return [CookieSource.NoCookies];
        var s = new Settings { CookieSource = "auto" };
        s.Normalize(fromLegacyFile: false);
        return CookieStrategy.Chain(s, InstalledBrowsers());
    }
}
