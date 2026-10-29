using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace Videoprep.Core.Import;

/// <summary>
/// "Cargar archivo...": reads the links of a .txt, .docx, .md, .html/.htm or .csv file (F1).
/// The file is opened read-only (also while Word has it open), only parsed, never handed to another program, and
/// nothing is fetched from the network. Time ranges written in the document are never interpreted.
/// </summary>
public static class LinkFileReader
{
    public static readonly IReadOnlyList<string> Extensions = [".txt", ".docx", ".md", ".html", ".htm", ".csv"];

    /// <summary>Formats people will try that cannot be read yet (the UI suggests saving as .docx or .txt).</summary>
    public static readonly IReadOnlyList<string> NotYet = [".doc", ".pdf", ".rtf", ".odt", ".pages"];

    static LinkFileReader() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static LinkFileResult Read(string path, ImportLimits? limits = null)
    {
        limits ??= ImportLimits.Default;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (NotYet.Contains(ext)) return LinkFileResult.Fail(ReadStatus.NotYet, ext);
        if (!Extensions.Contains(ext)) return LinkFileResult.Fail(ReadStatus.UnknownType, ext);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var max = ext == ".docx" ? limits.MaxFileBytes : limits.MaxTextBytes;
            if (stream.Length > max) return LinkFileResult.Fail(ReadStatus.TooBig, $"{stream.Length} bytes");
            IReadOnlyList<FoundLink> links;
            if (ext == ".docx")
            {
                if (stream.ReadByte() != 'P' || stream.ReadByte() != 'K') return LinkFileResult.Fail(ReadStatus.Damaged, "not a zip file");
                stream.Position = 0;
                links = DocxLinks.Read(stream, limits);
            }
            else
            {
                var bytes = new byte[stream.Length];
                stream.ReadExactly(bytes);
                links = FromText(ext, Decode(bytes));
            }
            return new LinkFileResult(ReadStatus.Ok, links.Take(limits.MaxLinks).ToList());
        }
        catch (ImportLimitException ex) { return LinkFileResult.Fail(ReadStatus.TooBig, ex.Message); }
        catch (Exception ex) when (ex is InvalidDataException or XmlException or RegexMatchTimeoutException or EndOfStreamException)
        {
            return LinkFileResult.Fail(ReadStatus.Damaged, $"{ex.GetType().Name}: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return LinkFileResult.Fail(ReadStatus.CannotOpen, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    public static IReadOnlyList<FoundLink> FromText(string ext, string text) => ext switch
    {
        ".md" => MarkupLinks.Markdown(text),
        ".html" or ".htm" => MarkupLinks.Html(text),
        ".csv" => MarkupLinks.Csv(text),
        _ => TextLinks.Links(text, allowBareIdLines: true).ToList(),
    };

    /// <summary>
    /// Text files: BOM (UTF-8, UTF-16) first; else strict UTF-8; else Windows-1252 (what Notepad and Excel in Spanish
    /// save as "ANSI").
    /// </summary>
    public static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1252).GetString(bytes);
        }
    }
}
