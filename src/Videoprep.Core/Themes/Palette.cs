using System.Globalization;

namespace Videoprep.Core.Themes;

/// <summary>
/// The colors of one theme, as "#rrggbb" strings (no UI types in Core). Keys are the legacy ones:
/// bg (window), card (panels), inp (inputs), tx (text), mu (muted text), bd (borders), and the green/red/orange
/// button pairs gb/gt, rb/rt, ob/ot (background/text).
/// </summary>
public sealed record Palette(string Bg, string Card, string Inp, string Tx, string Mu, string Bd,
                             string Gb, string Gt, string Rb, string Rt, string Ob, string Ot)
{
    /// <summary>Dark themes also switch the standard controls (scroll bars, menus) to their dark look.</summary>
    public bool IsDark => Luminance(Bg) < 0.5;

    public static IReadOnlyList<string> Names { get; } = ["light", "dark", "beige", "gray", "slate", "custom"];

    private static readonly Dictionary<string, Palette> Fixed = new()
    {
        ["light"] = new("#f1f1ee", "#ffffff", "#ffffff", "#1f1f1d", "#6b6b66", "#d6d6cf", "#cdeccf", "#1f5a26", "#f6cccc", "#7a1f1f", "#fbddb8", "#7a4510"),
        ["dark"] = new("#1e1f22", "#2a2b2f", "#1e1f22", "#e8e8e6", "#a0a09b", "#3b3c41", "#2f5a33", "#cdeccf", "#5e2f2f", "#f6cccc", "#5e4426", "#fbddb8"),
        ["beige"] = new("#eadfca", "#f6efe0", "#fbf7ee", "#3b3226", "#7a6c58", "#d3c5aa", "#cfe6c4", "#2f5a26", "#f2cfc4", "#7a2b1f", "#f7d9a8", "#7a4510"),
        ["gray"] = new("#dcdcdc", "#eeeeee", "#fafafa", "#222222", "#666666", "#c2c2c2", "#cdeccf", "#1f5a26", "#f6cccc", "#7a1f1f", "#fbddb8", "#7a4510"),
        ["slate"] = new("#d9e1ea", "#e9eef4", "#f7f9fb", "#1f2a37", "#5b6b7d", "#bcc8d6", "#cdeccf", "#1f5a26", "#f6cccc", "#7a1f1f", "#fbddb8", "#7a4510"),
    };

    /// <summary>Port of Get-Palette: unknown names fall back to light; "custom" is built from the chosen color.</summary>
    public static Palette For(string name, string customColor)
    {
        if (name == "custom")
        {
            return TryParse(customColor, out _) ? Custom(customColor) : Fixed["light"];
        }
        return Fixed.TryGetValue(name, out var p) ? p : Fixed["light"];
    }

    /// <summary>
    /// Port of New-CustomPalette: start from light or dark (by the color's brightness), use the color as background and
    /// derive panel, input and border colors by mixing with white or black.
    /// </summary>
    public static Palette Custom(string hex)
    {
        var bg = Normalize(hex);
        var dark = Luminance(bg) < 0.5;
        var b = dark ? Fixed["dark"] : Fixed["light"];
        return dark
            ? b with { Bg = bg, Card = Mix(bg, "#ffffff", 0.08), Inp = Mix(bg, "#000000", 0.15), Bd = Mix(bg, "#ffffff", 0.25) }
            : b with { Bg = bg, Card = Mix(bg, "#ffffff", 0.6), Inp = Mix(bg, "#ffffff", 0.8), Bd = Mix(bg, "#000000", 0.15) };
    }

    public static bool TryParse(string? hex, out (int R, int G, int B) rgb)
    {
        rgb = default;
        var h = (hex ?? "").Trim().TrimStart('#');
        if (h.Length != 6 || !int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return false;
        rgb = ((v >> 16) & 0xff, (v >> 8) & 0xff, v & 0xff);
        return true;
    }

    public static string ToHex(int r, int g, int b) => $"#{r:x2}{g:x2}{b:x2}";

    private static string Normalize(string hex) => TryParse(hex, out var c) ? ToHex(c.R, c.G, c.B) : "#f1f1ee";

    /// <summary>Same weights as the legacy script (0.299, 0.587, 0.114), 0-1.</summary>
    public static double Luminance(string hex)
    {
        TryParse(hex, out var c);
        return (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;
    }

    /// <summary>Port of Mix-Color: a + (b - a) * t per channel.</summary>
    public static string Mix(string a, string b, double t)
    {
        TryParse(a, out var x);
        TryParse(b, out var y);
        // PowerShell's [int] cast rounds to even; Math.Round does the same.
        int M(int p, int q) => (int)Math.Round(p + (q - p) * t);
        return ToHex(M(x.R, y.R), M(x.G, y.G), M(x.B, y.B));
    }
}
