using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Videoprep.Core.Update;

/// <summary>SHA256SUMS.txt (the output of sha256sum) and hashing files.</summary>
public static partial class Sha256Sums
{
    [GeneratedRegex(@"^([0-9a-fA-F]{64})[ \t]+\*?([^\r\n/\\]+)$")]
    private static partial Regex Line();

    /// <summary>File name -> lowercase hex. Lines that do not look like "hash  name" (or name a path) are ignored.</summary>
    public static IReadOnlyDictionary<string, string> Parse(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
            if (Line().Match(raw.TrimEnd('\r')) is { Success: true } m) result[m.Groups[2].Value] = m.Groups[1].Value.ToLowerInvariant();
        return result;
    }

    public static string HashFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}

/// <summary>Takes the one file we expect out of a downloaded zip without trusting anything in it.</summary>
public static class SafeZip
{
    public const int MaxEntries = 20;

    /// <summary>
    /// The zip must be flat (no folders, no "..", no drive letters, no backslashes), hold <paramref name="exeName"/> exactly once and
    /// that file must not grow past <paramref name="maxBytes"/> when unpacked (counted while writing, not taken from the header).
    /// Nothing else is ever written to disk.
    /// </summary>
    public static void ExtractOnly(string zipPath, string exeName, string destination, long maxBytes)
    {
        ZipArchive zip;
        try { zip = ZipFile.OpenRead(zipPath); }
        catch (Exception ex) when (ex is InvalidDataException or IOException) { throw new UpdateException(UpdateFailure.BadArchive, "the zip cannot be read: " + ex.Message); }
        using (zip)
        {
            if (zip.Entries.Count is 0 or > MaxEntries) throw new UpdateException(UpdateFailure.BadArchive, $"unexpected number of files in the zip ({zip.Entries.Count})");
            ZipArchiveEntry? found = null;
            foreach (var e in zip.Entries)
            {
                var name = e.FullName;
                if (name.Length == 0 || name.Contains('/') || name.Contains('\\') || name.Contains(':') || name.Contains("..") || name.Contains('\0') || Path.IsPathRooted(name))
                    throw new UpdateException(UpdateFailure.BadArchive, "the zip has a file with an unsafe name");
                if (!string.Equals(name, exeName, StringComparison.OrdinalIgnoreCase)) continue;
                if (found is not null) throw new UpdateException(UpdateFailure.BadArchive, $"{exeName} appears twice in the zip");
                found = e;
            }
            if (found is null) throw new UpdateException(UpdateFailure.BadArchive, $"{exeName} is not in the zip");
            if (found.Length > maxBytes) throw new UpdateException(UpdateFailure.TooBig, $"{exeName} would be {found.Length} bytes unpacked");

            try
            {
                using var input = found.Open();
                using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
                var buffer = new byte[81920];
                long total = 0;
                int n;
                while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    total += n;
                    if (total > maxBytes) throw new UpdateException(UpdateFailure.TooBig, "the unpacked file is larger than the limit");
                    output.Write(buffer, 0, n);
                }
            }
            catch (InvalidDataException ex) { throw new UpdateException(UpdateFailure.BadArchive, "the zip is damaged: " + ex.Message); }
        }
    }
}

/// <summary>Is this file a Windows x64 program and does it carry the version we were told to expect?</summary>
public static class ExeInspector
{
    public const ushort MachineAmd64 = 0x8664;

    /// <param name="versionOf">Reads the product version of the file (Windows: its version resource). Null result = no version.</param>
    public static void Check(string path, SemVer expected, Func<string, string?>? versionOf = null)
    {
        using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (f.Length < 100_000) throw new UpdateException(UpdateFailure.BadExecutable, "the file is too small to be videoprep");
            var head = new byte[64];
            f.ReadExactly(head);
            if (head[0] != (byte)'M' || head[1] != (byte)'Z') throw new UpdateException(UpdateFailure.BadExecutable, "not a Windows program (no MZ header)");
            var pe = BitConverter.ToInt32(head, 0x3C);
            if (pe < 64 || pe > f.Length - 6) throw new UpdateException(UpdateFailure.BadExecutable, "not a Windows program (bad PE offset)");
            f.Position = pe;
            var sig = new byte[6];
            f.ReadExactly(sig);
            if (sig[0] != 'P' || sig[1] != 'E' || sig[2] != 0 || sig[3] != 0) throw new UpdateException(UpdateFailure.BadExecutable, "not a Windows program (no PE signature)");
            if (BitConverter.ToUInt16(sig, 4) != MachineAmd64) throw new UpdateException(UpdateFailure.BadExecutable, "not a Windows x64 program");
        }
        var text = (versionOf ?? DefaultVersionOf)(path);
        if (!SemVer.TryParse(text, out var actual) || actual != expected)
            throw new UpdateException(UpdateFailure.WrongVersion, $"the program says it is version {text ?? "(none)"}, the release is {expected}");
    }

    private static string? DefaultVersionOf(string path) => FileVersionInfo.GetVersionInfo(path).ProductVersion;
}
