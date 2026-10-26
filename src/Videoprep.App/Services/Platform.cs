using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Videoprep.App.Services;

/// <summary>Small OS actions: open a folder in the file manager, play the "done" sound.</summary>
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
