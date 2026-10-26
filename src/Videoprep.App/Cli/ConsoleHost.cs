using System.Runtime.InteropServices;
using System.Text;

namespace Videoprep.App.Cli;

/// <summary>
/// videoprep.exe is a window app, so on Windows it has no console of its own. For command-line modes
/// (--demo, later --check) it borrows the console of the terminal that started it, when there is one.
/// </summary>
internal static partial class ConsoleHost
{
    private const int AttachParentProcess = -1;
    private const int StdOutputHandle = -11;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(int processId);

    [LibraryImport("kernel32.dll")]
    private static partial nint GetStdHandle(int handle);

    public static void Attach()
    {
        if (OperatingSystem.IsWindows() && GetStdHandle(StdOutputHandle) is 0 or -1)
            AttachConsole(AttachParentProcess);
        try { Console.OutputEncoding = Encoding.UTF8; }
        catch (IOException) { /* no console at all: output is lost, the log file still has everything */ }
    }
}
