using System.Globalization;
using System.Text;

namespace Videoprep.Core.Logging;

/// <summary>
/// Engine log: English lines (users paste them in bug reports, AUDIT L5). Keeps the last lines in memory for the
/// Log tab and "Copy diagnostics", and appends to a daily file when a folder is given. Thread-safe.
/// </summary>
public sealed class Log
{
    private readonly object _gate = new();
    private readonly Queue<string> _recent = new();
    private readonly int _keep;
    private readonly string? _dir;
    private readonly string _prefix;

    /// <param name="filePrefix">Daily file name start: "videoprep" for the window, "check" for <c>videoprep --check</c>.</param>
    public Log(string? logDir = null, int keepLines = 2000, string filePrefix = "videoprep")
    {
        _dir = logDir;
        _prefix = filePrefix;
        _keep = keepLines;
    }

    /// <summary>Raised for every line (from any thread; the UI must marshal it).</summary>
    public event Action<string>? LineAdded;

    /// <summary>Path of today's log file, or null when not writing to disk.</summary>
    public string? FilePath => _dir is null ? null : Path.Combine(_dir, $"{_prefix}-{DateTime.Now:yyyyMMdd}.log");

    public void Write(string line)
    {
        lock (_gate)
        {
            _recent.Enqueue(line);
            while (_recent.Count > _keep) _recent.Dequeue();
            if (FilePath is { } path)
            {
                try
                {
                    Directory.CreateDirectory(_dir!);
                    File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss ", CultureInfo.InvariantCulture) + line + Environment.NewLine,
                                       new UTF8Encoding(false));
                }
                catch (IOException)
                {
                    // A locked or full disk must never stop a download; the line still reaches the Log tab.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
        LineAdded?.Invoke(line);
    }

    public IReadOnlyList<string> Recent(int max = int.MaxValue)
    {
        lock (_gate) return _recent.Skip(Math.Max(0, _recent.Count - max)).ToList();
    }

    /// <summary>
    /// Writes an unexpected error with its full stack trace (AUDIT H4) to the log and to a crash file.
    /// Returns the crash file path (null if it could not be written).
    /// </summary>
    public string? Crash(Exception ex, string where)
    {
        Write($"INTERNAL ERROR in {where}: {ex.GetType().Name}: {ex.Message}");
        foreach (var l in ex.ToString().Split('\n')) Write("    " + l.TrimEnd('\r'));
        if (_dir is null) return null;
        try
        {
            Directory.CreateDirectory(_dir);
            var path = Path.Combine(_dir, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(path, $"videoprep {AppInfo.Version}\n{DateTime.Now:o}\n{where}\n\n{ex}\n");
            return path;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
