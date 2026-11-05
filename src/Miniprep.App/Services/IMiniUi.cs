namespace Miniprep.App.Services;

/// <summary>Everything the view models ask the window for. The window implements it; tests use a fake that answers like a person.</summary>
public interface IMiniUi
{
    /// <summary>The Windows "Guardar como" with the types Video MP4 and Audio MP3; the full path chosen, or null.</summary>
    Task<string?> PickSaveFile(string suggestedName, string? startFolder);
    Task<string?> PickFolder(string? start);
    /// <summary>A line of text asked from the person (null = Cancelar).</summary>
    Task<string?> AskText(string title, string prompt, string initial);
    /// <summary>"Guardar como" for a picture (JPG or PNG); null = Cancelar.</summary>
    Task<string?> PickSaveThumbnail(string suggestedName);
    Task SetClipboardText(string text);
    Task<string?> GetClipboardText();
    void OpenUrl(string url);
    Task ShowMessage(string text);
}
