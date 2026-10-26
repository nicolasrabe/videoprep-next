using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Videoprep.App.ViewModels;
using Videoprep.App.Views;
using Videoprep.Core;

namespace Videoprep.App.Tests;

public class MainWindowTests
{
    [AvaloniaFact]
    public void Window_shows_name_and_version()
    {
        var window = new MainWindow { DataContext = new MainWindowViewModel() };
        window.Show();

        Assert.Equal($"videoprep {AppInfo.Version}", window.Title);
        Assert.Equal(window.Title, window.FindControl<TextBlock>("TitleText")!.Text);
        Assert.True(window.CanResize);
        Assert.Equal(900, window.MinWidth);
        Assert.Equal(640, window.MinHeight);

        // Saved so a person (or the CI artifact) can look at the window without opening the app.
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(dir);
        frame!.Save(Path.Combine(dir, "main-window.png"), PngBitmapEncoderOptions.Default);
    }
}
