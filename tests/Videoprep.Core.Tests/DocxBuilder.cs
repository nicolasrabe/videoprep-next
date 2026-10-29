using System.IO.Compression;
using System.Text;

namespace Videoprep.Core.Tests;

/// <summary>
/// Builds small .docx files in code, shaped like the ones Word writes (no user documents in the repo).
/// Body XML goes inside &lt;w:body&gt;; hyperlinks are added as relationships with <see cref="Link"/>.
/// </summary>
internal sealed class DocxBuilder
{
    public const string Ns =
        "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" " +
        "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" " +
        "xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" " +
        "xmlns:wps=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\" " +
        "xmlns:v=\"urn:schemas-microsoft-com:vml\"";

    private readonly StringBuilder _body = new();
    private readonly Dictionary<string, string> _parts = [];
    private readonly List<(string Id, string Target)> _rels = [];

    /// <summary>Raw body XML (paragraphs, tables...).</summary>
    public DocxBuilder Body(string xml) { _body.Append(xml); return this; }

    /// <summary>A plain paragraph; each argument is one run (Word splits text into runs freely).</summary>
    public DocxBuilder Para(params string[] runs)
    {
        _body.Append("<w:p>");
        foreach (var r in runs) _body.Append(Run(r));
        _body.Append("</w:p>");
        return this;
    }

    public static string Run(string text) => $"<w:r><w:rPr><w:lang w:val=\"es-AR\"/></w:rPr><w:t xml:space=\"preserve\">{Esc(text)}</w:t></w:r>";

    /// <summary>Adds a hyperlink relationship and returns its id.</summary>
    public string Link(string target)
    {
        var id = "rId" + (100 + _rels.Count);
        _rels.Add((id, target));
        return id;
    }

    /// <summary>A w:hyperlink with the given visible text.</summary>
    public string Hyperlink(string target, string shown) =>
        $"<w:hyperlink r:id=\"{Link(target)}\" w:history=\"1\"><w:r><w:rPr><w:rStyle w:val=\"Hyperlink\"/></w:rPr><w:t>{Esc(shown)}</w:t></w:r></w:hyperlink>";

    /// <summary>Another part (header1.xml, footnotes.xml...) with its own content.</summary>
    public DocxBuilder Part(string name, string xml) { _parts[name] = xml; return this; }

    public static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    public byte[] Build(string? documentXmlOverride = null)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/></Types>");
            Write(zip, "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
            Write(zip, "word/document.xml", documentXmlOverride ??
                $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:document {Ns}><w:body>{_body}<w:sectPr/></w:body></w:document>");
            var rels = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
            rels.Append("<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
            foreach (var (id, target) in _rels)
                rels.Append($"<Relationship Id=\"{id}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink\" Target=\"{Esc(target)}\" TargetMode=\"External\"/>");
            rels.Append("</Relationships>");
            Write(zip, "word/_rels/document.xml.rels", rels.ToString());
            foreach (var (name, xml) in _parts) Write(zip, "word/" + name, xml);
        }
        return ms.ToArray();
    }

    public string Save(string dir, string name = "guion.docx")
    {
        var path = Path.Combine(dir, name);
        File.WriteAllBytes(path, Build());
        return path;
    }

    private static void Write(ZipArchive zip, string name, string content)
    {
        using var s = zip.CreateEntry(name).Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        s.Write(bytes);
    }
}
