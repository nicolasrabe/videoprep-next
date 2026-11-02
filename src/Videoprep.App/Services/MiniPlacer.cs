using Videoprep.Core.Models;

namespace Videoprep.App.Services;

/// <summary>The three sizes of videoprep: the full window (maxi), the card (mini) and the one-line bar (micro).</summary>
public enum MiniLevel { Maxi, Mini, Micro }

/// <summary>The monitors, as the placement needs them. The first one is the main monitor. Real: Avalonia's screens; tests: a fake.</summary>
public interface IScreenSource
{
    IReadOnlyList<ScreenArea> All { get; }
}

/// <summary>What the placement does to the real windows. Real: the main window and the mini window; tests: a fake that writes it down.</summary>
public interface IMiniSurfaces
{
    /// <summary>Where the full window is now, in screen pixels.</summary>
    PixelBox FullBounds { get; }

    bool FullMaximized { get; }

    /// <summary>Where the mini window is now (null when it is not open).</summary>
    PixelBox? MiniBounds { get; }

    /// <summary>Opens the mini window (or changes its level) with its top left corner at (<paramref name="x"/>, <paramref name="y"/>) in pixels.</summary>
    void ShowMini(MiniLevel level, int x, int y);

    void CloseMini();

    /// <summary>Gets the full window out of the way (one taskbar button stays).</summary>
    void HideFull();

    /// <summary>Brings the full window back to its size and place from before, maximized again if it was.</summary>
    void RestoreFull(PixelBox bounds, bool maximized);
}

/// <summary>
/// RONDA 2 (spec 2.3): maxi, mini and micro. Going from maxi to a small level puts the new window on the same monitor as the full one,
/// at its top right corner (inside the work area, counting that monitor's scale); between mini and micro the top right corner stays;
/// going back to maxi opens the full window on the monitor where the small one is now, with its old size and its top right corner where the small one's was (RONDA 4). The last place of the mini window is NOT remembered.
/// </summary>
public sealed class MiniPlacer(IScreenSource screens, IMiniSurfaces surfaces)
{
    /// <summary>Size in device-independent units of each small level.</summary>
    public static (double Width, double Height) SizeOf(MiniLevel level) => level == MiniLevel.Mini ? (340, 296) : (380, 40);

    private PixelBox _full;
    private bool _fullMaximized;

    public MiniLevel Level { get; private set; } = MiniLevel.Maxi;

    public void Show(MiniLevel level)
    {
        if (level == MiniLevel.Maxi) { Close(); return; }
        var (w, h) = SizeOf(level);
        var all = screens.All;
        if (Level == MiniLevel.Maxi)
        {
            _full = surfaces.FullBounds;
            _fullMaximized = surfaces.FullMaximized;
            var (x, y) = MiniPlacement.TopRightOfWindowScreen(all, _full, w, h) is { } p ? (p.X, p.Y) : (100, 100);
            surfaces.ShowMini(level, x, y);
            surfaces.HideFull();
        }
        else
        {
            // Mini <-> micro: the top right corner does not move (the window may have been dragged to another monitor: that one counts).
            var pos = surfaces.MiniBounds is { } cur ? MiniPlacement.KeepTopRight(all, cur, w, h) : null;
            pos ??= MiniPlacement.TopRightOfWindowScreen(all, _full, w, h) is { } p ? (p.X, p.Y) : (100, 100);
            surfaces.ShowMini(level, pos.Value.X, pos.Value.Y);
        }
        Level = level;
    }

    public void Close()
    {
        if (Level == MiniLevel.Maxi) return;
        Level = MiniLevel.Maxi;
        var where = surfaces.MiniBounds is { } m ? MiniPlacement.FullFromMini(screens.All, _full, m) : _full;
        surfaces.CloseMini();
        surfaces.RestoreFull(where, _fullMaximized);
    }
}
