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
