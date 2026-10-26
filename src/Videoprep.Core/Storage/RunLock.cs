namespace Videoprep.Core.Storage;

/// <summary>
/// "Only one check/download run at a time" (SCRAPING_SPEC): an exclusive lock file shared by the window and
/// <c>videoprep --check</c>. The OS releases it if the process dies, so a crash never leaves it stuck.
/// <c>--check</c> exits with code 3 when the lock is taken (Phase 0 decision).
/// </summary>
public sealed class RunLock : IDisposable
{
    public const int ExitCodeBusy = 3;

    private readonly FileStream _stream;

    private RunLock(FileStream stream) => _stream = stream;

    /// <summary>Takes the lock, or returns null when another run holds it.</summary>
    public static RunLock? TryAcquire(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        try
        {
            var fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            return new RunLock(fs);
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Dispose() => _stream.Dispose();
}
