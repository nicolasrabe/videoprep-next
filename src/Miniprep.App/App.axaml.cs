using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Miniprep.App.Services;
using Miniprep.App.ViewModels;
using Miniprep.App.Views;
using Videoprep.Core.Themes;

namespace Miniprep.App;

public partial class App : Application
{
    /// <summary>The one theme of miniprep: the dark palette of videoprep, with the colors of the mockup for the title bar.</summary>
    public static readonly Palette Dark = Palette.For("dark", "") with { Bg = "#1e1f22", Tx = "#e8e8e6" };

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            WindowTitleBar.Watch();
            WindowTitleBar.Use(this, Dark);
            var window = new MainWindow();
            var paths = MiniPaths.ForCurrentUser();
            var svc = MiniServices.CreateDefault(Program.Log, paths);
            window.DataContext = new MiniMainViewModel(svc, window);
            desktop.MainWindow = window;
            desktop.Exit += (_, _) =>
            {
                svc.Info.Stop();
                try { if (File.Exists(paths.SessionCookieFile)) File.Delete(paths.SessionCookieFile); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* a leftover copy is harmless */ }
            };
            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                e.Handled = true;
                Program.Log.Crash(e.Exception, "user interface");
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
