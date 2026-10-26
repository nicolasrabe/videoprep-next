using System.Reflection;

namespace Videoprep.Core;

/// <summary>Product name and version, read from the assembly (set in Directory.Build.props).</summary>
public static class AppInfo
{
    public const string Name = "videoprep";

    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var attr = typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        var v = attr?.InformationalVersion ?? "0.0.0";
        // Drop build metadata ("+abc123") if a tool adds it anyway.
        var plus = v.IndexOf('+');
        return plus >= 0 ? v[..plus] : v;
    }
}
