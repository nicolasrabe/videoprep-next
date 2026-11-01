using System.Text.Json;
using System.Text.RegularExpressions;

namespace Videoprep.Core.Tests;

/// <summary>
/// The text files and the screen files read directly, as plain files: no window, no Avalonia, nothing that can fail at random.
/// They stand in for the screen tests that check for untranslated texts. Every key exists in Spanish and English and is not empty,
/// every key the screens and the code ask for exists, and the screen files have no fixed user-visible text.
/// </summary>
public partial class TextFilesTests
{
    private static readonly string Root = FindRoot();
    private static string SrcDir => Path.Combine(Root, "src");
    private static string I18nDir => Path.Combine(SrcDir, "Videoprep.Core", "I18n");

    private static string FindRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "videoprep.slnx"))) return d.FullName;
        throw new InvalidOperationException("videoprep.slnx not found above " + AppContext.BaseDirectory);
    }

    private static List<(string Key, string Value)> ReadPairs(string lang)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(I18nDir, lang + ".json")));
        return doc.RootElement.EnumerateObject().Select(p => (p.Name, p.Value.GetString() ?? "")).ToList();
    }

    private static IEnumerable<string> Files(string pattern) =>
        Directory.EnumerateFiles(SrcDir, pattern, SearchOption.AllDirectories)
                 .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar));

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void No_key_is_repeated_in_a_text_file(string lang)
    {
        var repeated = ReadPairs(lang).GroupBy(p => p.Key).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(repeated.Count == 0, $"{lang}.json repeats: {string.Join(", ", repeated)}");
    }

    [Fact]
    public void Every_key_exists_in_Spanish_and_in_English()
    {
        var es = ReadPairs("es").Select(p => p.Key).ToHashSet();
        var en = ReadPairs("en").Select(p => p.Key).ToHashSet();
        Assert.True(es.SetEquals(en), $"only in es: {string.Join(", ", es.Except(en))}; only in en: {string.Join(", ", en.Except(es))}");
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void No_text_is_empty(string lang)
    {
        var empty = ReadPairs(lang).Where(p => string.IsNullOrWhiteSpace(p.Value)).Select(p => p.Key).ToList();
        Assert.True(empty.Count == 0, $"{lang}.json has empty texts: {string.Join(", ", empty)}");
    }

    [GeneratedRegex(@"\{s:T\s+(\w+)\s*\}")]
    private static partial Regex ScreenKey();

    [GeneratedRegex(@"\b(?:L|Loc\.Instance|loc|Localizer)\[""(\w+)""\]|\bL\.Format\(\s*""(\w+)""|\bL\.Text\(\s*""(\w+)""")]
    private static partial Regex CodeKey();

    [Fact]
    public void Every_key_the_screens_ask_for_exists()
    {
        var known = ReadPairs("es").Select(p => p.Key).ToHashSet();
        var missing = new List<string>();
        foreach (var f in Files("*.axaml"))
            foreach (Match m in ScreenKey().Matches(File.ReadAllText(f)))
                if (!known.Contains(m.Groups[1].Value)) missing.Add($"{Path.GetFileName(f)}: {m.Groups[1].Value}");
        Assert.True(missing.Count == 0, "keys used by the screens that are not in the text files: " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_key_the_code_asks_for_exists()
    {
        var known = ReadPairs("es").Select(p => p.Key).ToHashSet();
        var missing = new List<string>();
        foreach (var f in Files("*.cs"))
            foreach (Match m in CodeKey().Matches(File.ReadAllText(f)))
            {
                var key = m.Groups.Cast<Group>().Skip(1).First(g => g.Success).Value;
                if (!known.Contains(key)) missing.Add($"{Path.GetFileName(f)}: {key}");
            }
        Assert.True(missing.Count == 0, "keys used by the code that are not in the text files: " + string.Join(", ", missing));
    }

    /// <summary>The only fixed texts a screen may have: the program's name, signs, key combinations and timecode examples.</summary>
    private static readonly HashSet<string> AllowedFixed = ["videoprep", "i", "X", "Ctrl+Z", "M:SS", "H:MM:SS"];

    [GeneratedRegex(@"\s(Content|Text|Header|Title|PlaceholderText|Watermark|ToolTip\.Tip|AutomationProperties\.Name|Label)=""([^""]*)""")]
    private static partial Regex TextAttribute();

    [GeneratedRegex(@">([^<>{}]*[A-Za-zÁ-ú]{2,}[^<>{}]*)<")]
    private static partial Regex InlineText();

    [GeneratedRegex("^#[0-9a-fA-F]{3,8}$")]
    private static partial Regex ColorCode();

    [Fact]
    public void The_screen_files_have_no_fixed_text_that_is_not_translated()
    {
        var fixedText = new List<string>();
        foreach (var f in Files("*.axaml"))
        {
            var text = Regex.Replace(File.ReadAllText(f), "<!--.*?-->", "", RegexOptions.Singleline);
            foreach (Match m in TextAttribute().Matches(text))
            {
                var value = m.Groups[2].Value;
                if (value.StartsWith('{') || !Regex.IsMatch(value, "[A-Za-zÁ-ú]") || AllowedFixed.Contains(value)) continue;
                fixedText.Add($"{Path.GetFileName(f)}: {m.Groups[1].Value}=\"{value}\"");
            }
            foreach (Match m in InlineText().Matches(text))
            {
                var value = m.Groups[1].Value.Trim();
                if (value.Length == 0 || ColorCode().IsMatch(value) || AllowedFixed.Contains(value)) continue;
                fixedText.Add($"{Path.GetFileName(f)}: text between tags \"{value}\"");
            }
        }
        Assert.True(fixedText.Count == 0, "texts written in the screen files instead of the text files (add them to es.json and en.json and use {s:T key}): " + string.Join("; ", fixedText));
    }

    [Fact]
    public void The_text_files_are_found_from_the_test_folder()
    {
        // If this fails the other checks would silently look at nothing.
        Assert.True(Files("*.axaml").Count() >= 10, "screen files not found under " + SrcDir);
        Assert.True(ReadPairs("es").Count >= 200);
    }
}
