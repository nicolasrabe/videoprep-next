using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Videoprep.Core.Themes;

namespace Videoprep.App.Services;

/// <summary>
/// Puts a theme's colors into the application resources. Every view uses them through {DynamicResource ...}, so a
/// theme change repaints the whole window (the legacy Set-ThemeOn walked every control by hand).
/// Dark themes also switch the standard Fluent controls to dark.
/// </summary>
public static class ThemeService
{
    public static readonly string[] Keys = ["Bg", "Card", "Inp", "Tx", "Mu", "Bd", "Gb", "Gt", "Rb", "Rt", "Ob", "Ot"];

    public static void Apply(Application app, Palette p)
    {
        string[] values = [p.Bg, p.Card, p.Inp, p.Tx, p.Mu, p.Bd, p.Gb, p.Gt, p.Rb, p.Rt, p.Ob, p.Ot];
        for (var i = 0; i < Keys.Length; i++)
            app.Resources[Keys[i]] = new SolidColorBrush(Color.Parse(values[i]));
        app.RequestedThemeVariant = p.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        // Check boxes, radio buttons and progress bars use the theme's orange instead of Fluent's blue.
        // Dark themes take the darker orange so the white check mark stays readable.
        var accent = Color.Parse(p.IsDark ? p.Ob : p.Ot);
        foreach (var key in new[] { "SystemAccentColor", "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3",
                                    "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3" })
            app.Resources[key] = accent;
    }
}
