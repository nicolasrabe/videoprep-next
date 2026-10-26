namespace Videoprep.App.Services;

/// <summary>
/// Everything the view model asks the window for: messages, pickers, clipboard, opening folders, sounds.
/// The window implements it; tests use a fake that answers like a user would.
/// </summary>
public interface IUiServices
{
    Task ShowMessage(string text, bool warning = false);
    Task<bool> AskYesNo(string title, string text);
    Task<string?> PickFolder(string? start);
    Task<string?> PickTextFile();
    Task<string?> PickSaveFile(string suggestedName);
    Task<string?> PickColor(string current);
    Task<bool> ShowOptions(ViewModels.OptionsViewModel options);
    /// <summary>Agregar/Editar suscripción; true = Aceptar with valid data.</summary>
    Task<bool> EditSubscription(ViewModels.SubscriptionEditViewModel subscription);
    Task SetClipboardText(string text);
    void OpenFolder(string path);
    void PlayDoneSound();
}
