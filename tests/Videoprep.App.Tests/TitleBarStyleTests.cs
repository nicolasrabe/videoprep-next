using Videoprep.App.Services;
using Videoprep.Core.Themes;

namespace Videoprep.App.Tests;

public class TitleBarStyleTests
{
    [Fact]
    public void Dark_theme_gives_dark_mode_with_its_background_and_text()
    {
        var s = TitleBarStyle.For(Palette.For("dark", ""));
        Assert.True(s.Dark);
        Assert.Equal(0x221F1E, s.CaptionColorRef); // #1e1f22 as 0x00BBGGRR
        Assert.Equal(0xE6E8E8, s.TextColorRef);    // #e8e8e6
    }

    [Fact]
    public void Light_theme_gives_light_mode()
    {
        var s = TitleBarStyle.For(Palette.For("light", ""));
        Assert.False(s.Dark);
        Assert.Equal(0xEEF1F1, s.CaptionColorRef); // #f1f1ee
    }

    [Theory]
    [InlineData("custom", "#102030", true)]
    [InlineData("custom", "#f0e0d0", false)]
    [InlineData("custom", "nonsense", false)]
    public void Custom_theme_follows_its_color(string name, string color, bool dark) =>
        Assert.Equal(dark, TitleBarStyle.For(Palette.For(name, color)).Dark);

    [Fact]
    public void Bad_color_gives_black_and_never_throws() => Assert.Equal(0, TitleBarStyle.ColorRef("zz"));

    [Fact]
    public void Setting_the_title_bar_does_not_fail_without_a_window()
    {
        WindowTitleBar.Set(IntPtr.Zero, TitleBarStyle.For(Palette.For("dark", "")));
        WindowTitleBar.Set(new IntPtr(1), TitleBarStyle.For(Palette.For("dark", ""))); // not a window: the system refuses, we ignore it
    }
}
