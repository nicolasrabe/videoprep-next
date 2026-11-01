using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Videoprep.App.Services;
using Videoprep.App.ViewModels;
using Videoprep.Core.Models;

namespace Videoprep.App.Views;

/// <summary>The main window. Implements the dialogs the view model asks for (<see cref="IUiServices"/>).</summary>
public partial class MainWindow : Window, IUiServices
{
    private bool _closeConfirmed;
    private MiniWindow? _mini;
    private readonly MiniPlacer _placer;

    public MainWindow()
    {
        InitializeComponent();
        _placer = new MiniPlacer(new WindowScreens(this), new WindowSurfaces(this));
        Closing += OnClosing;
        Closed += (_, _) => _mini?.CloseForReal();
        // F4: the big window comes back from the taskbar -> the mini closes; F5: a question that waited can be asked now.
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty && WindowState != WindowState.Minimized && DataContext is MainViewModel { IsMiniActive: true } vm) vm.CloseMini();
        };
        Activated += (_, _) => (DataContext as MainViewModel)?.OnMainActivated();
        Opened += async (_, _) =>
        {
            try
            {
                if (DataContext is MainViewModel vm) await vm.OnShownAsync();
            }
            catch (Exception ex)
            {
                await ShowError(ex, "startup");
            }
        };
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed || DataContext is not MainViewModel vm) return;
        e.Cancel = true;   // ask asynchronously, then close for real
        try
        {
            if (await vm.CanCloseAsync())
            {
                _closeConfirmed = true;
                Close();
            }
        }
        catch (Exception ex)
        {
            Program.Log.Crash(ex, "closing");
            _closeConfirmed = true;
            Close();
        }
    }

    /// <summary>AUDIT H4: a friendly message instead of a raw error dialog; the details are in the log and a crash file.</summary>
    public async Task ShowError(Exception ex, string where)
    {
        var file = Program.Log.Crash(ex, where);
        var nl = Environment.NewLine + Environment.NewLine;
        await Dialogs.Message(this, string.Format(Loc.Instance.Culture, Loc.Instance["msg_error"], nl, file ?? ""), warning: true);
    }

    /// <summary>The window a question belongs to: the mini one while the big window is minimized, else the big one.</summary>
    private Window DialogOwner => _mini is { IsVisible: true } m && WindowState == WindowState.Minimized ? m : this;

    public Task ShowMessage(string text, bool warning = false) => Dialogs.Message(DialogOwner, text, warning);

    public Task<bool> AskYesNo(string title, string text) => Dialogs.YesNo(DialogOwner, title, text);

    public Task<bool> AskChoice(string title, string text, string yes, string no) => Dialogs.Choice(DialogOwner, title, text, yes, no);

    public Task<int> AskThree(string title, string text, string first, string second, string third) =>
        Dialogs.Three(DialogOwner, title, text, first, second, third);

    public Task<string?> AskText(string title, string prompt, string initial) => Dialogs.Text(DialogOwner, title, prompt, initial);

    public async Task<string?> PickFolder(string? start)
    {
        var options = new FolderPickerOpenOptions { AllowMultiple = false };
        if (start is not null) options.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(start);
        var picked = await StorageProvider.OpenFolderPickerAsync(options);
        return picked.FirstOrDefault()?.TryGetLocalPath();
    }

    public async Task<string?> PickTextFile()
    {
        var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Text") { Patterns = ["*.txt"] }, FilePickerFileTypes.All],
        });
        return picked.FirstOrDefault()?.TryGetLocalPath();
    }

    public async Task<string?> PickLinkFile()
    {
        var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = Loc.Instance["btn_loadtxt"].TrimEnd('.'),
            FileTypeFilter =
            [
                new FilePickerFileType(Loc.Instance["imp_formats"]) { Patterns = ["*.txt", "*.docx", "*.md", "*.html", "*.htm", "*.csv"] },
                FilePickerFileTypes.All,
            ],
        });
        return picked.FirstOrDefault()?.TryGetLocalPath();
    }

    public Task<bool> ReviewImport(ImportReviewViewModel review) => Dialogs.ImportReview(this, review);

    public Task<bool> EditBatch(BatchEditViewModel edit) => Dialogs.BatchEdit(this, edit);

    public void ShowInFolder(string file) => Platform.ShowInFolder(file);

    public void OpenUrl(string url) => Platform.OpenUrl(url);

    public async Task<string?> PickSaveFile(string suggestedName)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = suggestedName,
            FileTypeChoices = [new FilePickerFileType("Text") { Patterns = ["*.txt"] }],
        });
        return file?.TryGetLocalPath();
    }

    public Task<string?> PickColor(string current) => Dialogs.Color(this, current);

    public Task<bool> ShowOptions(OptionsViewModel options) => Dialogs.Options(this, options);

    public Task<bool> EditSubscription(SubscriptionEditViewModel subscription) => Dialogs.Subscription(this, subscription);

    public async Task SetClipboardText(string text)
    {
        if (Clipboard is { } c) await c.SetTextAsync(text);
    }

    public void OpenFolder(string path) => Platform.OpenFolder(path);

    // ---------------- F4: mini window ----------------

    public void SetMiniLevel(MiniLevel level) => _placer.Show(level);

    public void CloseMini() => _placer.Close();

    public void CloseApp() => Close();

    public bool IsMainActive => _mini is null && IsVisible && WindowState != WindowState.Minimized && IsActive;

    // ---------------- F5: clipboard ----------------

    public long ClipboardSequence => Platform.ClipboardSequence();

    public async Task<string?> ReadClipboardTextAsync()
    {
        try { return Clipboard is { } c ? await c.TryGetTextAsync() : null; }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or IOException)
        {
            return null;   // busy clipboard: the next check tries again
        }
    }

    // ---------------- file pickers of this round ----------------

    public async Task<string?> PickSaveFileAs(string suggestedName, string label, IReadOnlyList<string> patterns)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = suggestedName,
            FileTypeChoices = [new FilePickerFileType(label) { Patterns = patterns }],
            DefaultExtension = patterns.Count > 0 ? patterns[0].TrimStart('*', '.') : null,
        });
        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickOpenFileAs(string label, IReadOnlyList<string> patterns)
    {
        var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(label) { Patterns = patterns }, FilePickerFileTypes.All],
        });
        return picked.FirstOrDefault()?.TryGetLocalPath();
    }

    public void PlayDoneSound() => Platform.PlayDoneSound();

    // ---------------- the monitors and the windows, for MiniPlacer ----------------

    private sealed class WindowScreens(MainWindow w) : IScreenSource
    {
        public IReadOnlyList<ScreenArea> All => w.Screens.All.OrderByDescending(s => s.IsPrimary)
            .Select(s => new ScreenArea(Box(s.Bounds), Box(s.WorkingArea), s.Scaling)).ToList();

        private static PixelBox Box(PixelRect r) => new(r.X, r.Y, r.Width, r.Height);
    }

    private sealed class WindowSurfaces(MainWindow w) : IMiniSurfaces
    {
        public PixelBox FullBounds => BoxOf(w);
        public bool FullMaximized => w.WindowState == WindowState.Maximized;
        public PixelBox? MiniBounds => w._mini is { } m ? BoxOf(m) : null;

        private static PixelBox BoxOf(Window win)
        {
            var scale = win.RenderScaling;
            var size = win.FrameSize ?? win.ClientSize;
            return new PixelBox(win.Position.X, win.Position.Y, (int)Math.Round(size.Width * scale), (int)Math.Round(size.Height * scale));
        }

        public void ShowMini(MiniLevel level, int x, int y)
        {
            if (w.DataContext is not MainViewModel vm) return;
            var first = w._mini is null;
            w._mini ??= new MiniWindow { DataContext = vm, WindowStartupLocation = WindowStartupLocation.Manual };
            w._mini.ApplyLevel(level);
            w._mini.Position = new PixelPoint(x, y);
            // The bar never takes the keyboard from the program where the user is typing; the card has a box to type in.
            if (first) w._mini.Show();
            w._mini.Position = new PixelPoint(x, y);
        }

        public void CloseMini()
        {
            var mini = w._mini;
            w._mini = null;
            mini?.CloseForReal();
        }

        public void HideFull() => w.WindowState = WindowState.Minimized;   // one taskbar button (this one) and a way back

        public void RestoreFull(PixelBox bounds, bool maximized)
        {
            w.WindowState = WindowState.Normal;
            if (bounds.Width > 0 && bounds.Height > 0)
            {
                w.Position = new PixelPoint(bounds.X, bounds.Y);
                var scale = w.RenderScaling;
                w.Width = bounds.Width / scale;
                w.Height = bounds.Height / scale;
            }
            if (maximized) w.WindowState = WindowState.Maximized;
            w.Activate();
        }
    }
}
