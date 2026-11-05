using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Miniprep.App.Services;
using Miniprep.App.ViewModels;

namespace Miniprep.App.Views;

/// <summary>The window of miniprep. It also answers what the view models ask of the system: pickers, clipboard, browser, small dialogs.</summary>
public partial class MainWindow : Window, IMiniUi
{
    public MainWindow()
    {
        InitializeComponent();
        PrepTab.Click += (_, _) => { if (DataContext is MiniMainViewModel vm) vm.SelectedTab = 0; };
        ColaTab.Click += (_, _) => { if (DataContext is MiniMainViewModel vm) vm.SelectedTab = 1; };
    }

    public async Task<string?> PickSaveFile(string suggestedName, string? startFolder)
    {
        var options = new FilePickerSaveOptions
        {
            Title = Loc.Instance["mp_save_title"],
            SuggestedFileName = suggestedName,
            FileTypeChoices =
            [
                new FilePickerFileType(Loc.Instance["mp_m_mp4"] + " (*.mp4)") { Patterns = ["*.mp4"] },
                new FilePickerFileType(Loc.Instance["mp_m_mp3"] + " (*.mp3)") { Patterns = ["*.mp3"] },
            ],
        };
        if (startFolder is { Length: > 0 }) options.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(startFolder);
        var file = await StorageProvider.SaveFilePickerAsync(options);
        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickFolder(string? start)
    {
        var options = new FolderPickerOpenOptions { AllowMultiple = false };
        if (start is { Length: > 0 }) options.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(start);
        var picked = await StorageProvider.OpenFolderPickerAsync(options);
        return picked.FirstOrDefault()?.TryGetLocalPath();
    }

    public Task<string?> AskText(string title, string prompt, string initial) => Dialogs.Text(this, title, prompt, initial);

    public async Task<string?> PickSaveThumbnail(string suggestedName)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = suggestedName,
            FileTypeChoices = [new FilePickerFileType("JPG") { Patterns = ["*.jpg", "*.jpeg"] }, new FilePickerFileType("PNG") { Patterns = ["*.png"] }],
            DefaultExtension = "jpg",
        });
        return file?.TryGetLocalPath();
    }

    public async Task SetClipboardText(string text)
    {
        if (Clipboard is { } c) await c.SetTextAsync(text);
    }

    public async Task<string?> GetClipboardText()
    {
        try { return Clipboard is { } c ? await c.TryGetTextAsync() : null; }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or IOException) { return null; }
    }

    public void OpenUrl(string url)
    {
        // Only web links; anything else is ignored.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeHttp)) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true })?.Dispose(); }
        catch (System.ComponentModel.Win32Exception) { /* no browser: nothing to do */ }
    }

    public Task ShowMessage(string text) => Dialogs.Message(this, text);
}
