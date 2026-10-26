using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Videoprep.App.Services;
using Videoprep.App.ViewModels;

namespace Videoprep.App.Views;

/// <summary>The main window. Implements the dialogs the view model asks for (<see cref="IUiServices"/>).</summary>
public partial class MainWindow : Window, IUiServices
{
    private bool _closeConfirmed;

    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
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

    public Task ShowMessage(string text, bool warning = false) => Dialogs.Message(this, text, warning);

    public Task<bool> AskYesNo(string title, string text) => Dialogs.YesNo(this, title, text);

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

    public void PlayDoneSound() => Platform.PlayDoneSound();
}
