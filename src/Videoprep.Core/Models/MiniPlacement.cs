namespace Videoprep.Core.Models;

/// <summary>A rectangle in screen pixels.</summary>
public readonly record struct PixelBox(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool Intersects(PixelBox o) => X < o.Right && o.X < Right && Y < o.Bottom && o.Y < Bottom;
}

/// <summary>
/// Where the mini window opens (F4): where the user left it, unless that is no longer on any screen (a monitor was
/// unplugged, the resolution changed), then the top right corner of the main screen.
/// </summary>
public static class MiniPlacement
{
    /// <summary>At least this part (pixels) of the window must be on a screen to count as reachable.</summary>
    private const int MinVisible = 60;

    public const int Margin = 24;

    public static (int X, int Y) Place(int? savedX, int? savedY, int width, int height, IReadOnlyList<PixelBox> workAreas)
    {
        if (workAreas.Count == 0) return (savedX ?? 100, savedY ?? 100);
        if (savedX is { } x && savedY is { } y)
        {
            foreach (var a in workAreas)
            {
                var overlapW = Math.Min(x + width, a.Right) - Math.Max(x, a.X);
                var overlapH = Math.Min(y + height, a.Bottom) - Math.Max(y, a.Y);
                if (overlapW >= Math.Min(MinVisible, width) && overlapH >= Math.Min(height, 12)) return (x, y);
            }
        }
        var main = workAreas[0];
        return (main.Right - width - Margin, main.Y + Margin);
    }
}
