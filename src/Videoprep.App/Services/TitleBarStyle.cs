using Videoprep.Core.Themes;

namespace Videoprep.App.Services;

/// <summary>
/// How the window title bar looks for a theme: dark or light mode, and the caption and text colors (the theme's
/// background and text). Pure calculation, no window needed.
/// </summary>
internal readonly record struct TitleBarStyle(bool Dark, int CaptionColorRef, int TextColorRef)
{
    public static TitleBarStyle For(Palette p) => new(p.IsDark, ColorRef(p.Bg), ColorRef(p.Tx));

    /// <summary>Windows COLORREF: 0x00BBGGRR. A bad color gives black.</summary>
    public static int ColorRef(string hex) =>
        Palette.TryParse(hex, out var c) ? c.R | (c.G << 8) | (c.B << 16) : 0;
}
