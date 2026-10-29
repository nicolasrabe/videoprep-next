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

    public MainWindow()
    {
        InitializeComponent();
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

    public void ShowMini()
    {
        if (_mini is not null || DataContext is not MainViewModel vm) return;
        _mini = new MiniWindow { DataContext = vm };
        var scale = Screens.ScreenFromWindow(this)?.Scaling ?? 1.0;
        var areas = Screens.All.OrderByDescending(sc => sc.IsPrimary)
            .Select(sc => new PixelBox(sc.WorkingArea.X, sc.WorkingArea.Y, sc.WorkingArea.Width, sc.WorkingArea.Height)).ToList();
        var (x, y) = MiniPlacement.Place(vm.MiniX, vm.MiniY, (int)(_mini.Width * scale), (int)(_mini.Height * scale), areas);
        _mini.WindowStartupLocation = WindowStartupLocation.Manual;
        _mini.Position = new PixelPoint(x, y);
        _mini.Show();   // ShowActivated="false": the program where the user is typing keeps the keyboard
        WindowState = WindowState.Minimized;   // one taskbar button (this one) and a way back
    }

    public void CloseMini()
    {
        var mini = _mini;
        _mini = null;
        mini?.CloseForReal();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

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
}
