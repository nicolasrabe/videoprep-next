using System.Globalization;

namespace Videoprep.Core.Update;

/// <summary>A version like 0.3.0 or 0.3.0-rc.1 (semver.org order: a pre-release is older than the same number without it).</summary>
public readonly record struct SemVer(int Major, int Minor, int Patch, string Pre = "") : IComparable<SemVer>
{
    public static SemVer Zero => default;

    public static bool TryParse(string? text, out SemVer version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim();
        if (s.Length > 64) return false;
        if (s[0] is 'v' or 'V') s = s[1..];
        var plus = s.IndexOf('+');
        if (plus >= 0) s = s[..plus];
        var pre = "";
        var dash = s.IndexOf('-');
        if (dash >= 0)
        {
            pre = s[(dash + 1)..];
            s = s[..dash];
            if (pre.Length == 0 || pre.Split('.').Any(p => p.Length == 0 || !p.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))) return false;
        }
        var parts = s.Split('.');
        if (parts.Length != 3) return false;
        var n = new int[3];
        for (var i = 0; i < 3; i++)
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out n[i])) return false;
        version = new SemVer(n[0], n[1], n[2], pre);
        return true;
    }

    public static SemVer Parse(string text) => TryParse(text, out var v) ? v : throw new FormatException($"not a version: {text}");

    public static SemVer ParseOrZero(string? text) => TryParse(text, out var v) ? v : Zero;

    public int CompareTo(SemVer other)
    {
        var c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        if (c != 0) return c;
        c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;
        if (Pre.Length == 0 || other.Pre.Length == 0) return Pre.Length == other.Pre.Length ? 0 : Pre.Length == 0 ? 1 : -1;   // the release is newer
        var a = Pre.Split('.');
        var b = other.Pre.Split('.');
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            var an = long.TryParse(a[i], NumberStyles.None, CultureInfo.InvariantCulture, out var x);
            var bn = long.TryParse(b[i], NumberStyles.None, CultureInfo.InvariantCulture, out var y);
            c = an && bn ? x.CompareTo(y) : an ? -1 : bn ? 1 : string.CompareOrdinal(a[i], b[i]);
            if (c != 0) return c;
        }
        return a.Length.CompareTo(b.Length);
    }

    public bool IsNewerThan(SemVer other) => CompareTo(other) > 0;

    public override string ToString() => $"{Major}.{Minor}.{Patch}" + (Pre.Length > 0 ? "-" + Pre : "");
}
