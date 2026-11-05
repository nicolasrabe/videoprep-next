using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Videoprep.Core.Themes;

namespace Videoprep.Controls;

/// <summary>
/// Paints the title bar of every window with the colors of the current theme (Windows: DWM). Dark or light mode where the
/// system has it (Windows 10 build 19041+), and the caption and text colors on Windows 11. Never fails where unsupported.
/// </summary>
public static partial class WindowTitleBar
{
    private const int DwmaUseImmersiveDarkMode = 20, DwmaCaptionColor = 35, DwmaTextColor = 36;

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private static TitleBarStyle? _style;

    public static void Set(IntPtr hwnd, TitleBarStyle style)
    {
        if (!OperatingSystem.IsWindows() || hwnd == IntPtr.Zero) return;
        try
        {
            var dark = style.Dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DwmaUseImmersiveDarkMode, ref dark, sizeof(int));
            if (Environment.OSVersion.Version.Build < 22000) return; // Windows 10: no color attributes
            var caption = style.CaptionColorRef;
            var text = style.TextColorRef;
            DwmSetWindowAttribute(hwnd, DwmaCaptionColor, ref caption, sizeof(int));
            DwmSetWindowAttribute(hwnd, DwmaTextColor, ref text, sizeof(int));
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { /* no DWM: leave the default bar */ }
    }

    /// <summary>Paints one window with the current theme (no-op before the first <see cref="Use"/>).</summary>
    public static void Apply(Window w)
    {
        if (_style is { } s) Set(w.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero, s);
    }

    /// <summary>A theme was chosen: every open window changes at once.</summary>
    public static void Use(Application app, Palette p)
    {
        _style = TitleBarStyle.For(p);
        if (app.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            foreach (var w in desktop.Windows) Apply(w);
    }

    /// <summary>Every window (main, dialogs...) gets the title bar of the current theme when it opens.</summary>
    public static void Watch() => Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => Apply(w));
}
