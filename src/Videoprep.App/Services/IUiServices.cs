namespace Videoprep.App.Services;

/// <summary>
/// Everything the view model asks the window for: messages, pickers, clipboard, opening folders, sounds.
/// The window implements it; tests use a fake that answers like a user would.
/// </summary>
public interface IUiServices : IClipboardSource
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
    /// <summary>Save dialog with its own file type (".csv", ".json"...).</summary>
    Task<string?> PickSaveFileAs(string suggestedName, string label, IReadOnlyList<string> patterns);
    Task<string?> PickOpenFileAs(string label, IReadOnlyList<string> patterns);
    /// <summary>A line of text asked from the user (null = Cancelar).</summary>
    Task<string?> AskText(string title, string prompt, string initial);
    /// <summary>Question with the buttons named by the caller ("Continuar" / "Cancelar").</summary>
    Task<bool> AskChoice(string title, string text, string yes, string no);
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
    /// <summary>F4: show the mini window (without taking the keyboard focus) and minimize the big one.</summary>
    void ShowMini();
    /// <summary>F4: close the mini window and bring the big one back.</summary>
    void CloseMini();
    /// <summary>"Cerrar videoprep" from the mini window: closes the big window, which asks first when something runs.</summary>
    void CloseApp();
    /// <summary>The big window is open in front (not minimized, not hidden behind): questions can be asked without stealing focus.</summary>
    bool IsMainActive { get; }
}
