using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Videoprep.App.ViewModels;

namespace Videoprep.App.Views;

public partial class DownloadView : UserControl
{
    public DownloadView()
    {
        InitializeComponent();
        // Paste: links are taken by videoprep (port of the legacy WM_PASTE hook); other text is pasted normally.
        LinkBox.PastingFromClipboard += OnPasting;
        LinkBox.KeyDown += OnLinkKeyDown;
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    private async void OnPasting(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;   // the clipboard is read asynchronously: decide after reading it
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            var text = clipboard is null ? null : await clipboard.TryGetTextAsync();
            if (Vm is null || string.IsNullOrEmpty(text) || Vm.SmartPaste(text)) return;
            // Not a link: paste the text like a normal text box would.
            var t = LinkBox.Text ?? "";
            var start = Math.Min(LinkBox.SelectionStart, LinkBox.SelectionEnd);
            var len = Math.Abs(LinkBox.SelectionEnd - LinkBox.SelectionStart);
            var clean = text.Trim();
            LinkBox.Text = t.Remove(start, len).Insert(start, clean);
            LinkBox.CaretIndex = start + clean.Length;
        }
        catch (Exception ex)
        {
            Program.Log.Crash(ex, "paste");
        }
    }

    private void OnLinkKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Vm?.LinkEnter();
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e) => e.DragEffects = DragDropEffects.Copy;

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        try
        {
            if (Vm is null) return;
            var files = (e.DataTransfer.TryGetFiles() ?? [])
                .Select(f => f.TryGetLocalPath()).OfType<string>().Where(File.Exists).ToList();
            await Vm.DropAsync(e.DataTransfer.TryGetText(), files);
        }
        catch (Exception ex)
        {
            Program.Log.Crash(ex, "drag and drop");
        }
    }
}
