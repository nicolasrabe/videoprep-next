using System.ComponentModel;
using System.Globalization;
using Videoprep.Core.I18n;

namespace Videoprep.App.Services;

/// <summary>
/// UI texts for XAML: <c>{Binding [btn_send], Source={x:Static s:Loc.Instance}}</c>. Changing <see cref="Language"/>
/// refreshes every bound text at once (the legacy Apply-Language).
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    private Localizer _localizer = new(Localizer.DefaultLanguage());

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] => _localizer[key];

    public string Language
    {
        get => _localizer.Language;
        set
        {
            if (value == _localizer.Language) return;
            _localizer = new Localizer(value);
            // Indexer bindings listen for different names depending on the binding engine ("Item", "Item[]");
            // an empty name means "everything changed". Send all three so every bound text refreshes.
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }
    }

    public string Format(string key, params object[] args) => string.Format(Culture, this[key], args);

    /// <summary>Numbers in UI texts follow the language (45,2 % in Spanish).</summary>
    public CultureInfo Culture => CultureInfo.GetCultureInfo(Language == "es" ? "es-AR" : "en-US");
}
