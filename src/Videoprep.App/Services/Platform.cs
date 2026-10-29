using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Videoprep.App.Services;

/// <summary>Small OS actions: open a folder (or show a file) in the file manager, open a web link, play the "done" sound.</summary>
internal static partial class Platform
{
    public static void OpenFolder(string path)
    {
        var psi = OperatingSystem.IsWindows() ? new ProcessStartInfo("explorer.exe")
            : OperatingSystem.IsMacOS() ? new ProcessStartInfo("open")
            : new ProcessStartInfo("xdg-open");
        psi.ArgumentList.Add(path);
        psi.UseShellExecute = false;
        try { Process.Start(psi)?.Dispose(); }
        catch (System.ComponentModel.Win32Exception) { /* no file manager: nothing to do */ }
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHParseDisplayName(string name, IntPtr bindingContext, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);

    [LibraryImport("shell32.dll")]
    private static partial int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint cidl, IntPtr apidl, uint flags);

    [LibraryImport("ole32.dll")]
    private static partial void CoTaskMemFree(IntPtr pv);

    /// <summary>
    /// "Mostrar en carpeta": Windows opens Explorer with the file selected, through the shell API (no command line is
    /// built, so names with commas or quotes are safe). Elsewhere, or if that fails, the folder is opened.
    /// </summary>
    public static void ShowInFolder(string file)
    {
        if (OperatingSystem.IsWindows() && SHParseDisplayName(file, IntPtr.Zero, out var pidl, 0, out _) == 0 && pidl != IntPtr.Zero)
        {
            try
            {
                // cidl = 0: the item itself is selected in its parent folder.
                if (SHOpenFolderAndSelectItems(pidl, 0, IntPtr.Zero, 0) == 0) return;
            }
            finally
            {
                CoTaskMemFree(pidl);
            }
        }
        if (Path.GetDirectoryName(file) is { Length: > 0 } dir) OpenFolder(dir);
    }

    /// <summary>A web link in the default browser. Anything that is not http/https is ignored.</summary>
    public static void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeHttp)) return;
        var psi = OperatingSystem.IsWindows() ? new ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true }
            : new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open") { UseShellExecute = false, ArgumentList = { u.AbsoluteUri } };
        try { Process.Start(psi)?.Dispose(); }
        catch (System.ComponentModel.Win32Exception) { /* no browser: nothing to do */ }
    }

    // ---------------- mini window and clipboard (F4, F5): everything that depends on the operating system ----------------

    private const int GwlExStyle = -20;
    private const long WsExNoActivate = 0x08000000L;

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindow(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    private static partial uint GetClipboardSequenceNumber();

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int virtualKey);

    /// <summary>
    /// Tells Windows this window never takes the keyboard focus, not even when clicked (the "do not activate" window
    /// style): the program where the user is typing keeps the cursor. No effect on other systems.
    /// </summary>
    public static void MakeNoActivate(IntPtr hwnd)
    {
        if (!OperatingSystem.IsWindows() || hwnd == IntPtr.Zero) return;
        var style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        if ((style & WsExNoActivate) == 0) SetWindowLongPtr(hwnd, GwlExStyle, (IntPtr)(style | WsExNoActivate));
    }

    /// <summary>The window that has the keyboard focus now (0 = unknown).</summary>
    public static IntPtr ForegroundWindow() => OperatingSystem.IsWindows() ? GetForegroundWindow() : IntPtr.Zero;

    /// <summary>Gives the focus back to a window (after a menu closed), only if it is still there and it lost it.</summary>
    public static void RestoreForeground(IntPtr hwnd)
    {
        if (!OperatingSystem.IsWindows() || hwnd == IntPtr.Zero || !IsWindow(hwnd) || GetForegroundWindow() == hwnd) return;
        SetForegroundWindow(hwnd);
    }

    /// <summary>
    /// A number that changes every time anything is copied (Windows keeps it for the clipboard). Asking for it reads
    /// nothing from the clipboard. 0 = this system cannot say; the caller then compares the text itself.
    /// </summary>
    public static long ClipboardSequence() => OperatingSystem.IsWindows() ? GetClipboardSequenceNumber() : 0;

    /// <summary>Is this key or mouse button down right now, in any program? (A no-activate window gets no key events.)</summary>
    public static bool IsKeyDown(int virtualKey) => OperatingSystem.IsWindows() && (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out CursorPoint point);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint
    {
        public int X, Y;
    }

    /// <summary>Where the mouse pointer is, in screen pixels.</summary>
    public static (int X, int Y)? CursorPosition() =>
        OperatingSystem.IsWindows() && GetCursorPos(out var p) ? (p.X, p.Y) : null;

    /// <summary>Does this window belong to videoprep itself?</summary>
    public static bool IsOwnWindow(IntPtr hwnd) =>
        OperatingSystem.IsWindows() && hwnd != IntPtr.Zero && GetWindowThreadProcessId(hwnd, out var pid) != 0 && pid == (uint)Environment.ProcessId;

    public const int VkEscape = 0x1B, VkLeftButton = 0x01, VkRightButton = 0x02;

    private const uint MbIconAsterisk = 0x40;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool MessageBeep(uint type);

    /// <summary>The legacy script played SystemSounds.Asterisk; this is the same Windows sound.</summary>
    public static void PlayDoneSound()
    {
        if (OperatingSystem.IsWindows()) MessageBeep(MbIconAsterisk);
    }
}
