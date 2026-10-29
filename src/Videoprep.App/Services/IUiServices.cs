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
    /// <summary>"Cargar archivo...": .txt, .docx, .md, .html, .csv.</summary>
    Task<string?> PickLinkFile();
    /// <summary>"Enlaces encontrados en ..."; true = "Agregar N a la cola".</summary>
    Task<bool> ReviewImport(ViewModels.ImportReviewViewModel review);
    /// <summary>"Cambiar N elementos de la cola"; true = "Aplicar".</summary>
    Task<bool> EditBatch(ViewModels.BatchEditViewModel edit);
    Task<string?> PickSaveFile(string suggestedName);
    Task<string?> PickColor(string current);
    Task<bool> ShowOptions(ViewModels.OptionsViewModel options);
    /// <summary>Agregar/Editar suscripción; true = Aceptar with valid data.</summary>
    Task<bool> EditSubscription(ViewModels.SubscriptionEditViewModel subscription);
    Task SetClipboardText(string text);
    void OpenFolder(string path);
    /// <summary>The file explorer with this file selected ("Mostrar en carpeta").</summary>
    void ShowInFolder(string file);
    /// <summary>A web link in the default browser (the caller only passes http/https).</summary>
    void OpenUrl(string url);
    void PlayDoneSound();
}
