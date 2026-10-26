namespace Videoprep.Core.Storage;

/// <summary>
/// Where videoprep keeps its files. Roaming data (settings, subscriptions, archives) stays in %APPDATA%\videoprep like
/// 0.1.1; tools, logs and caches go to %LOCALAPPDATA%\videoprep (Phase 0 decision). On macOS both are
/// ~/Library/Application Support/videoprep (.NET maps the special folders there).
/// The roots are injectable so tests never touch the real profile.
/// </summary>
public sealed class AppPaths(string roamingRoot, string localRoot)
{
    public static AppPaths ForCurrentUser() => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "videoprep"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "videoprep"));

    public string Roaming { get; } = roamingRoot;
    public string Local { get; } = localRoot;

    public string SettingsFile => Path.Combine(Roaming, "settings.json");
    public string ArchiveFile => Path.Combine(Roaming, "downloaded.txt");
    public string QueueFile => Path.Combine(Roaming, "queue.json");
    public string SubscriptionsFile => Path.Combine(Roaming, "subscriptions.json");
    public string ArchivesDir => Path.Combine(Roaming, "archives");
    public string ToolsDir => Path.Combine(Local, "tools");
    public string LogsDir => Path.Combine(Local, "logs");
    public string CacheDir => Path.Combine(Local, "cache");

    /// <summary>
    /// One-time copy of the pre-0.0.3 folder (%APPDATA%\VideoDownloader), as the legacy script did.
    /// Returns true when something was copied. Never deletes the old folder.
    /// </summary>
    public bool MigrateFromVideoDownloader(string oldRoamingRoot)
    {
        if (Directory.Exists(Roaming) || !Directory.Exists(oldRoamingRoot)) return false;
        CopyDir(oldRoamingRoot, Roaming);
        return true;
    }

    private static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), overwrite: false);
        foreach (var d in Directory.GetDirectories(from)) CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
    }
}
