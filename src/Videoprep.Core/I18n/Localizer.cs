using System.Globalization;
using System.Text.Json;

namespace Videoprep.Core.I18n;

/// <summary>
/// UI texts in Spanish and English (es.json / en.json, embedded; keys copied from the legacy $I18N).
/// Lookup falls back to English, then to the key itself (port of Tx). Log lines stay English on purpose.
/// </summary>
public sealed class Localizer
{
    public static IReadOnlyList<string> Languages { get; } = ["es", "en"];

    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Tables = Languages.ToDictionary(
        l => l, l => Load(l));

    public Localizer(string language) => Language = Languages.Contains(language) ? language : "en";

    public string Language { get; }

    public string this[string key] => Get(Language, key);

    public string Format(string key, params object[] args) => string.Format(CultureInfo.InvariantCulture, this[key], args);

    public static string Get(string language, string key)
    {
        if (Tables.TryGetValue(language, out var t) && t.TryGetValue(key, out var v)) return v;
        return Tables["en"].TryGetValue(key, out var en) ? en : key;
    }

    public static IReadOnlyDictionary<string, string> Table(string language) => Tables[language];

    /// <summary>Default language from the system, as the legacy script: Spanish if the UI culture is Spanish, else English.</summary>
    public static string DefaultLanguage(CultureInfo? culture = null) =>
        (culture ?? CultureInfo.CurrentUICulture).TwoLetterISOLanguageName == "es" ? "es" : "en";

    private static IReadOnlyDictionary<string, string> Load(string language)
    {
        var asm = typeof(Localizer).Assembly;
        using var stream = asm.GetManifestResourceStream($"Videoprep.Core.I18n.{language}.json")
                           ?? throw new InvalidOperationException($"missing embedded {language}.json");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
               ?? throw new InvalidOperationException($"empty {language}.json");
    }
}
