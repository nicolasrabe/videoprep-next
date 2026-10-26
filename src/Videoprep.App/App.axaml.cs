using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Videoprep.App.Services;
using Videoprep.App.ViewModels;
using Videoprep.App.Views;

namespace Videoprep.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            var vm = new MainViewModel(AppServices.CreateDefault(Program.Log), window);
            ThemeService.Apply(this, MainViewModel.PaletteFor(vm.Settings));
            vm.PaletteChanged += p => ThemeService.Apply(this, p);
            window.DataContext = vm;
            desktop.MainWindow = window;

            // AUDIT H4: errors in buttons and other handlers are logged and explained; the window stays open.
            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                e.Handled = true;
                _ = window.ShowError(e.Exception, "user interface");
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
