using System.Text.Json.Serialization;
using Videoprep.Core.Formats;
using Videoprep.Core.Text;

namespace Videoprep.Core.Profiles;

/// <summary>
/// A named set of Download choices (F6): format, maximum resolution and how the files are named.
/// The kind (video or audio) comes from the format. <see cref="ConstantRate"/> is the F8 option "normalizar a
/// velocidad constante": saved already so the file does not need a new version later, but not shown or used yet.
/// </summary>
public sealed record FormatProfile
{
    public const string BuiltInPrefix = "builtin:";
    public const int MaxNameLength = 60;

    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("fmt")] public string Format { get; init; } = "mp4";
    [JsonPropertyName("cap")] public int Cap { get; init; } = FormatCatalog.DefaultCap;
    [JsonPropertyName("constantRate")] public bool ConstantRate { get; init; }
    /// <summary>"orig" (title of the video) or "series" (prefix + number).</summary>
    [JsonPropertyName("naming")] public string Naming { get; init; } = "orig";
    [JsonPropertyName("prefix")] public string Prefix { get; init; } = "";

    [JsonIgnore] public bool BuiltIn => Id.StartsWith(BuiltInPrefix, StringComparison.Ordinal);
    [JsonIgnore] public bool IsSeries => Naming == "series";
    [JsonIgnore] public MediaKind Kind => FormatCatalog.Get(Format).Kind;

    /// <summary>The same choices (name and id aside).</summary>
    public bool SameChoices(FormatProfile o) =>
        Format == o.Format && Cap == o.Cap && Naming == o.Naming && (Naming != "series" || Prefix == o.Prefix);

    /// <summary>Fixes anything odd (a file from anywhere): known format, known resolution, clean name and prefix.</summary>
    public FormatProfile Normalized()
    {
        var format = FormatCatalog.IsKnown(Format) ? Format : "mp4";
        var cap = ClampCap(format, FormatCatalog.Resolutions.Values.Contains(Cap) ? Cap : FormatCatalog.DefaultCap);
        var name = CleanName(Name);
        var naming = Naming == "series" ? "series" : "orig";
        return this with
        {
            Format = format, Cap = cap, Name = name, Naming = naming,
            Prefix = naming == "series" ? SafeName.Make(Prefix.Trim(), "Video") : "",
        };
    }

    /// <summary>
    /// A resolution the format really offers (H.264 formats and audio stop at 1080p, as in the Download tab): the biggest
    /// one that is not above <paramref name="cap"/>, or the smallest available.
    /// </summary>
    public static int ClampCap(string format, int cap)
    {
        var allowed = FormatCatalog.ResolutionChoices(format).Select(r => FormatCatalog.Resolutions[r]).OrderBy(c => c).ToList();
        if (allowed.Contains(cap)) return cap;
        return allowed.LastOrDefault(c => c <= cap, allowed[0]);
    }

    /// <summary>Name as shown in lists: no control characters, one line, at most 60 characters.</summary>
    public static string CleanName(string? name)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var c in name ?? "") sb.Append(char.IsControl(c) ? ' ' : c);
        var n = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        return n.Length > MaxNameLength ? n[..MaxNameLength].TrimEnd() : n;
    }
}
