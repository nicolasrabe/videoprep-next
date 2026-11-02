namespace Videoprep.Core.Models;

/// <summary>A rectangle in screen pixels.</summary>
public readonly record struct PixelBox(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool Intersects(PixelBox o) => X < o.Right && o.X < Right && Y < o.Bottom && o.Y < Bottom;

    /// <summary>Area of the overlap with <paramref name="o"/> (0 when they do not touch).</summary>
    public long OverlapArea(PixelBox o)
    {
        var w = Math.Min(Right, o.Right) - Math.Max(X, o.X);
        var h = Math.Min(Bottom, o.Bottom) - Math.Max(Y, o.Y);
        return w > 0 && h > 0 ? (long)w * h : 0;
    }
}

/// <summary>One monitor: its full area, the part not covered by the taskbar, and its scale (1.0 = 100 %, 1.5 = 150 %...).</summary>
public readonly record struct ScreenArea(PixelBox Bounds, PixelBox WorkArea, double Scale);

/// <summary>
/// Where the mini and micro windows go (RONDA 2, spec 2.3): on the SAME monitor as the full window and stuck to its top right
/// corner, inside the work area (never over the taskbar), counting each monitor's own scale. Changes between mini and micro keep
/// that corner. Pure arithmetic: the windows and the monitors are reached through <c>IScreens</c> in the app.
/// </summary>
public static class MiniPlacement
{
    /// <summary>The monitor where most of <paramref name="window"/> is; the first one (the main one) when it is on none.</summary>
    public static ScreenArea? ScreenOf(IReadOnlyList<ScreenArea> screens, PixelBox window)
    {
        if (screens.Count == 0) return null;
        var best = screens[0];
        var bestArea = 0L;
        foreach (var s in screens)
        {
            var a = window.OverlapArea(s.Bounds);
            if (a > bestArea) (best, bestArea) = (s, a);
        }
        return best;
    }

    /// <summary>Size in pixels of a window of <paramref name="dipWidth"/> x <paramref name="dipHeight"/> (device independent) on this monitor.</summary>
    public static (int Width, int Height) PixelSize(ScreenArea screen, double dipWidth, double dipHeight) =>
        ((int)Math.Round(dipWidth * screen.Scale), (int)Math.Round(dipHeight * screen.Scale));

    /// <summary>
    /// Top right corner of the work area of the monitor of <paramref name="fullWindow"/> (maximized or not: its monitor decides).
    /// Returns the position of the new window's top left corner and the monitor used.
    /// </summary>
    public static (int X, int Y, ScreenArea Screen)? TopRightOfWindowScreen(IReadOnlyList<ScreenArea> screens, PixelBox fullWindow, double dipWidth, double dipHeight)
    {
        if (ScreenOf(screens, fullWindow) is not { } screen) return null;
        var (w, h) = PixelSize(screen, dipWidth, dipHeight);
        return Clamp(screen, screen.WorkArea.Right - w, screen.WorkArea.Y, w, h) is var (x, y) ? (x, y, screen) : null;
    }

    /// <summary>
    /// Changing level (mini to micro or back): the top right corner of <paramref name="current"/> stays where it is, in the same monitor;
    /// only the width and height change. Kept inside the work area.
    /// </summary>
    public static (int X, int Y)? KeepTopRight(IReadOnlyList<ScreenArea> screens, PixelBox current, double dipWidth, double dipHeight)
    {
        if (ScreenOf(screens, current) is not { } screen) return null;
        var (w, h) = PixelSize(screen, dipWidth, dipHeight);
        return Clamp(screen, current.Right - w, current.Y, w, h);
    }

    /// <summary>
    /// Coming back to the full window (RONDA 4, spec 2.3): it opens on the monitor where the small window is NOW, with the size it had
    /// (same device independent size, so the new monitor's scale is counted), its top right corner where the small window's was,
    /// all inside that monitor's work area.
    /// </summary>
    public static PixelBox FullFromMini(IReadOnlyList<ScreenArea> screens, PixelBox previousFull, PixelBox mini)
    {
        if (ScreenOf(screens, mini) is not { } target) return previousFull;
        var fromScale = ScreenOf(screens, previousFull)?.Scale ?? target.Scale;
        var (w, h) = PixelSize(target, previousFull.Width / fromScale, previousFull.Height / fromScale);
        var a = target.WorkArea;
        w = Math.Min(w, a.Width);
        h = Math.Min(h, a.Height);
        var (x, y) = Clamp(target, mini.Right - w, mini.Y, w, h);
        return new PixelBox(x, y, w, h);
    }

    private static (int X, int Y) Clamp(ScreenArea s, int x, int y, int w, int h)
    {
        var a = s.WorkArea;
        x = Math.Max(a.X, Math.Min(x, a.Right - w));
        y = Math.Max(a.Y, Math.Min(y, a.Bottom - h));
        return (x, y);
    }
}
