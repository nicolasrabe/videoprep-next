using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Videoprep.Core;

namespace Videoprep.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    public string Title => $"{AppInfo.Name} {AppInfo.Version}";

    // Temporary text for the skeleton build; real i18n (es/en from the legacy $I18N) comes in phase 2.
    public string Subtitle => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es"
        ? "Esqueleto de la versión 0.2: todavía no descarga nada."
        : "Skeleton of version 0.2: it does not download anything yet.";
}
