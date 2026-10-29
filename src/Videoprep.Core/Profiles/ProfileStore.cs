using System.Text.Json;
using System.Text.Json.Serialization;
using Videoprep.Core.Formats;
using Videoprep.Core.Storage;

namespace Videoprep.Core.Profiles;

/// <summary>Result of importing profiles from a file.</summary>
public sealed record ProfileImport(IReadOnlyList<FormatProfile> Added, int Skipped, string? Problem);

/// <summary>
/// The user's profiles in profiles.json (F6), plus the read-only examples. Written atomically with a version number.
/// A file that cannot be read is kept as profiles.json.bad and the store starts empty (nothing is lost silently).
/// Imported files come from anywhere: limited in size and count, every value checked, names never replace existing ones.
/// </summary>
public sealed class ProfileStore
{
    public const int CurrentVersion = 1;
    public const int MaxProfiles = 200;
    public const long MaxImportBytes = 1024 * 1024;

    private sealed record ProfilesFile(
        [property: JsonPropertyName("version")] int Version,
        [property: JsonPropertyName("profiles")] IReadOnlyList<FormatProfile> Profiles);

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>"Premiere 1080p", "ProRes 4K", "Solo audio (M4A)". Their names are translated by the screens.</summary>
    public static IReadOnlyList<FormatProfile> BuiltIns { get; } =
    [
        new() { Id = "builtin:premiere-1080", Name = "Premiere 1080p", Format = "mp4", Cap = 1080 },
        new() { Id = "builtin:prores-4k", Name = "ProRes 4K", Format = "movp", Cap = 2160 },
        new() { Id = "builtin:audio-m4a", Name = "Audio (M4A)", Format = "m4a", Cap = 1080 },
    ];

    private readonly string? _path;
    private readonly List<FormatProfile> _user;

    private ProfileStore(string? path, List<FormatProfile> user)
    {
        _path = path;
        _user = user;
    }

    /// <summary>A store that is never saved (tests, and "no file yet").</summary>
    public static ProfileStore InMemory(IEnumerable<FormatProfile>? user = null) => new(null, user?.ToList() ?? []);

    public IReadOnlyList<FormatProfile> User => _user;
    public IEnumerable<FormatProfile> All => BuiltIns.Concat(_user);

    public FormatProfile? Find(string? id) => id is null || id.Length == 0 ? null : All.FirstOrDefault(p => p.Id == id);

    public static string NewId() => "p_" + Guid.NewGuid().ToString("N")[..8];

    /// <summary>Reads the file. Missing = empty store. Unreadable = empty store, the old file kept as .bad, <paramref name="problem"/> says why.</summary>
    public static ProfileStore Load(string path, out string? problem)
    {
        problem = null;
        if (!File.Exists(path)) return new ProfileStore(path, []);
        try
        {
            var file = JsonSerializer.Deserialize<ProfilesFile>(File.ReadAllText(path), Options);
            var user = (file?.Profiles ?? [])
                .Where(p => p.Id.Length > 0 && !p.Id.StartsWith(FormatProfile.BuiltInPrefix, StringComparison.Ordinal))
                .Select(p => p.Normalized())
                .Where(p => p.Name.Length > 0)
                .DistinctBy(p => p.Id)
                .Take(MaxProfiles)
                .ToList();
            return new ProfileStore(path, user);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            problem = $"profiles file could not be read ({ex.GetType().Name}: {ex.Message}); kept as profiles.json.bad, starting empty";
            try { File.Move(path, path + ".bad", overwrite: true); }
            catch (Exception ex2) when (ex2 is IOException or UnauthorizedAccessException) { /* the original stays where it is */ }
            return new ProfileStore(path, []);
        }
    }

    public void Save()
    {
        if (_path is null) return;
        AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(new ProfilesFile(CurrentVersion, _user), Options));
    }

    // ---------------- names ----------------

    /// <summary>True when another profile (built-in or yours) already has this name, ignoring case.</summary>
    public bool NameTaken(string name, string? exceptId = null, IEnumerable<string>? extraReserved = null)
    {
        var n = FormatProfile.CleanName(name);
        return All.Any(p => p.Id != exceptId && string.Equals(p.Name, n, StringComparison.CurrentCultureIgnoreCase))
               || (extraReserved?.Any(r => string.Equals(r, n, StringComparison.CurrentCultureIgnoreCase)) ?? false);
    }

    /// <summary>"Name", else "Name (2)", "Name (3)"... never an existing name.</summary>
    public string UniqueName(string name, IEnumerable<string>? extraReserved = null)
    {
        var baseName = FormatProfile.CleanName(name);
        if (baseName.Length == 0) baseName = "Perfil";
        var candidate = baseName;
        var reserved = extraReserved?.ToList() ?? [];
        for (var n = 2; NameTaken(candidate, null, reserved); n++)
        {
            var suffix = $" ({n})";
            candidate = (baseName.Length + suffix.Length > FormatProfile.MaxNameLength ? baseName[..(FormatProfile.MaxNameLength - suffix.Length)] : baseName) + suffix;
        }
        return candidate;
    }

    // ---------------- changes ----------------

    /// <summary>Adds a new profile (a new id) and saves. The caller checked the name.</summary>
    public FormatProfile Add(FormatProfile p)
    {
        var added = p.Normalized() with { Id = NewId() };
        _user.Add(added);
        Save();
        return added;
    }

    /// <summary>Replaces the choices of an existing user profile (same id and name) and saves.</summary>
    public FormatProfile? ReplaceChoices(string id, FormatProfile from)
    {
        var i = _user.FindIndex(p => p.Id == id);
        if (i < 0) return null;
        var n = from.Normalized();
        _user[i] = _user[i] with { Format = n.Format, Cap = n.Cap, Naming = n.Naming, Prefix = n.Prefix, ConstantRate = n.ConstantRate };
        Save();
        return _user[i];
    }

    public FormatProfile? Rename(string id, string name)
    {
        var i = _user.FindIndex(p => p.Id == id);
        var clean = FormatProfile.CleanName(name);
        if (i < 0 || clean.Length == 0) return null;
        _user[i] = _user[i] with { Name = clean };
        Save();
        return _user[i];
    }

    public bool Remove(string id)
    {
        var removed = _user.RemoveAll(p => p.Id == id) > 0;
        if (removed) Save();
        return removed;
    }

    // ---------------- export / import ----------------

    /// <summary>The json to write for "Exportar": version and the given profiles (ids are not exported).</summary>
    public static string Export(IEnumerable<FormatProfile> profiles) =>
        JsonSerializer.Serialize(new ProfilesFile(CurrentVersion, profiles.Select(p => p with { Id = "" }).ToList()), Options);

    /// <summary>
    /// "Importar": adds the profiles of an exported file. The file is untrusted: size and count are limited, formats and
    /// resolutions must exist, names are cleaned and made unique (a taken name becomes "Name (2)"; nothing is replaced).
    /// </summary>
    public ProfileImport Import(string path, IEnumerable<string>? reservedNames = null)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return new ProfileImport([], 0, "missing");
            if (info.Length > MaxImportBytes) return new ProfileImport([], 0, "too big");
            return ImportJson(File.ReadAllText(path), reservedNames);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ProfileImport([], 0, ex.Message);
        }
    }

    public ProfileImport ImportJson(string json, IEnumerable<string>? reservedNames = null)
    {
        ProfilesFile? file;
        try { file = JsonSerializer.Deserialize<ProfilesFile>(json, Options); }
        catch (Exception ex) when (ex is JsonException or NotSupportedException) { return new ProfileImport([], 0, "not a profiles file"); }
        if (file is null || file.Profiles is null) return new ProfileImport([], 0, "not a profiles file");

        var reserved = reservedNames?.ToList() ?? [];
        var added = new List<FormatProfile>();
        var skipped = 0;
        foreach (var raw in file.Profiles)
        {
            if (_user.Count >= MaxProfiles) { skipped++; continue; }
            var p = raw.Normalized();
            if (p.Name.Length == 0) { skipped++; continue; }
            var named = p with { Name = UniqueName(p.Name, reserved), Id = NewId() };
            _user.Add(named);
            added.Add(named);
        }
        if (added.Count > 0) Save();
        return new ProfileImport(added, skipped, null);
    }
}
