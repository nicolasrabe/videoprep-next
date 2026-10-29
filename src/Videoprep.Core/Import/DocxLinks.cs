using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Videoprep.Core.Import;

/// <summary>
/// Links in a Word .docx (a zip of XML parts). Reads the body, headers, footers, footnotes and endnotes:
/// hyperlinks (w:hyperlink r:id resolved through the part's relationships), HYPERLINK field codes (also split across
/// several runs, as Word writes them), and URLs typed as text (also split across runs by spell-check marks).
/// Safety: the zip is only read; entries are counted, every part is read through a byte counter (the size written in
/// the zip is not trusted), and XML is parsed with DTDs prohibited and no resolver (no external entities).
/// </summary>
public static partial class DocxLinks
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string HyperlinkType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink";
    private const string OfficeDocumentType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";

    [GeneratedRegex(@"^\s*HYPERLINK\s+(?:""([^""]*)""|(\S+))", RegexOptions.IgnoreCase)]
    private static partial Regex HyperlinkField();

    [GeneratedRegex(@"^word/(header\d*|footer\d*|footnotes|endnotes)\.xml$", RegexOptions.IgnoreCase)]
    private static partial Regex ExtraPart();

    private static readonly string[] PartOrder = ["word/header", "word/footer", "word/footnotes", "word/endnotes"];

    public static IReadOnlyList<FoundLink> Read(Stream zip, ImportLimits limits)
    {
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count > limits.MaxZipEntries) throw new ImportLimitException($"{archive.Entries.Count} parts in the zip");
        var budget = new Budget(limits.MaxTextBytes);

        var main = MainPart(archive, budget) ?? "word/document.xml";
        var parts = new List<string> { main };
        // Then headers, footers, footnotes and endnotes, in that order.
        parts.AddRange(archive.Entries.Select(e => e.FullName).Where(n => ExtraPart().IsMatch(n))
            .OrderBy(n => Array.FindIndex(PartOrder, p => n.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            .ThenBy(n => n, StringComparer.Ordinal));
        if (archive.GetEntry(main) is null) throw new InvalidDataException("no document.xml: not a Word document");

        var found = new List<FoundLink>();
        foreach (var name in parts.Distinct())
        {
            var entry = archive.GetEntry(name);
            if (entry is null) continue;
            var rels = Relationships(archive, name, budget);
            var doc = Load(entry, budget);
            new Walker(rels, found, limits.MaxLinks).Block(doc.Root!);
            if (found.Count >= limits.MaxLinks) break;
        }
        return found;
    }

    /// <summary>The main document part named in _rels/.rels (Word: word/document.xml).</summary>
    private static string? MainPart(ZipArchive archive, Budget budget)
    {
        var entry = archive.GetEntry("_rels/.rels");
        if (entry is null) return null;
        var target = Load(entry, budget).Root?.Elements(Rel + "Relationship")
            .FirstOrDefault(r => (string?)r.Attribute("Type") == OfficeDocumentType)?.Attribute("Target")?.Value;
        return target?.TrimStart('/');
    }

    /// <summary>Hyperlink relationships of a part: id to external target.</summary>
    private static Dictionary<string, string> Relationships(ZipArchive archive, string part, Budget budget)
    {
        var dir = Path.GetDirectoryName(part)?.Replace('\\', '/') ?? "";
        var relsName = (dir.Length > 0 ? dir + "/" : "") + "_rels/" + Path.GetFileName(part) + ".rels";
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var entry = archive.GetEntry(relsName);
        if (entry is null) return map;
        foreach (var r in Load(entry, budget).Root?.Elements(Rel + "Relationship") ?? [])
        {
            if ((string?)r.Attribute("Type") != HyperlinkType) continue;
            if ((string?)r.Attribute("Id") is { } id && (string?)r.Attribute("Target") is { } target) map[id] = target;
        }
        return map;
    }

    private static XDocument Load(ZipArchiveEntry entry, Budget budget)
    {
        // The declared size can lie (zip bomb): refuse early when it is already too big, and count real bytes anyway.
        if (entry.Length > budget.Left) throw new ImportLimitException($"{entry.FullName} is too big");
        using var raw = entry.Open();
        using var counted = new CountingStream(raw, budget);
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersFromEntities = 1024,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
        };
        using var reader = XmlReader.Create(counted, settings);
        return XDocument.Load(reader);
    }

    /// <summary>Walks one XML part. Paragraphs collect their visible text so links split across runs are found whole.</summary>
    private sealed class Walker(Dictionary<string, string> rels, List<FoundLink> found, int maxLinks)
    {
        private sealed class Field
        {
            public readonly StringBuilder Instr = new();
            public readonly StringBuilder Result = new();
            public bool InResult;
        }

        private sealed class Para
        {
            public readonly StringBuilder Text = new();
            public readonly List<(string Url, int Start, int End, string Shown)> Links = [];
        }

        private readonly Stack<Field> _fields = new();
        private Para? _para;

        public void Block(XElement el)
        {
            foreach (var child in el.Elements())
            {
                if (found.Count >= maxLinks) return;
                if (child.Name == W + "p") Paragraph(child);
                else if (child.Name == Mc + "AlternateContent") Alternate(child, Block);
                else Block(child);
            }
        }

        private void Paragraph(XElement p)
        {
            var outer = _para;
            var para = _para = new Para();
            var nested = new List<XElement>();
            Inline(p, nested);
            _para = outer;

            var text = para.Text.ToString();
            foreach (var l in para.Links)
            {
                var shown = TextLinks.Squash(l.Shown).Trim();
                if (shown.Length == 0) shown = TextLinks.Squash(text).Trim();
                found.Add(new FoundLink(l.Url, shown.Length > 120 ? shown[..119] + "…" : shown, LinkHow.Hyperlink));
            }
            foreach (var m in TextLinks.Find(text))
            {
                // A typed URL that is the text of a hyperlink to the same place was already counted as a hyperlink
                // (Word turns typed links into hyperlinks automatically).
                var inside = para.Links.Any(l => m.Index >= l.Start && m.Index + m.Length <= l.End
                                                 && SameTarget(l.Url, m.Url));
                if (!inside) found.Add(new FoundLink(m.Url, TextLinks.Snippet(text, m.Index, m.Length), LinkHow.Text));
            }
            // Text boxes inside the paragraph are paragraphs of their own.
            foreach (var n in nested) Block(n);
        }

        private static bool SameTarget(string a, string b) =>
            LinkNormalizer.CompareKey(a) == LinkNormalizer.CompareKey(b);

        private void Inline(XElement el, List<XElement> nested)
        {
            foreach (var child in el.Elements())
            {
                var name = child.Name;
                if (name == W + "t") Append(child.Value);
                else if (name == W + "tab") Append(" ");
                else if (name == W + "br" || name == W + "cr") Append("\n");
                else if (name == W + "noBreakHyphen") Append("-");
                else if (name == W + "instrText")
                {
                    if (_fields.TryPeek(out var f) && !f.InResult) f.Instr.Append(child.Value);
                }
                else if (name == W + "fldChar") FieldChar((string?)child.Attribute(W + "fldCharType"));
                else if (name == W + "del" || name == W + "delText" || name == W + "rPr" || name == W + "pPr") { /* deleted text and formatting */ }
                else if (name == W + "txbxContent") nested.Add(child);
                else if (name == W + "p") nested.Add(new XElement(W + "body", child));
                else if (name == Mc + "AlternateContent") Alternate(child, c => Inline(c, nested));
                else if (name == W + "hyperlink") Hyperlink(child, nested);
                else if (name == W + "fldSimple") SimpleField(child, nested);
                else Inline(child, nested);   // runs, w:ins, w:smartTag, w:sdt, drawings...
            }
        }

        /// <summary>Word writes text boxes twice (new format + old VML fallback): read only the first choice.</summary>
        private static void Alternate(XElement ac, Action<XElement> walk)
        {
            var choice = ac.Element(Mc + "Choice") ?? ac.Element(Mc + "Fallback");
            if (choice is not null) walk(choice);
        }

        private void Append(string s)
        {
            _para?.Text.Append(s);
            foreach (var f in _fields) if (f.InResult) f.Result.Append(s);
        }

        private void Hyperlink(XElement h, List<XElement> nested)
        {
            var start = _para?.Text.Length ?? 0;
            Inline(h, nested);
            var id = (string?)h.Attribute(R + "id");
            if (id is null || !rels.TryGetValue(id, out var target) || _para is null) return;   // internal (bookmark) link
            var anchor = (string?)h.Attribute(W + "anchor");
            var url = anchor is { Length: > 0 } && !target.Contains('#') ? target + "#" + anchor : target;
            AddLink(url, start);
        }

        private void SimpleField(XElement f, List<XElement> nested)
        {
            var start = _para?.Text.Length ?? 0;
            Inline(f, nested);
            if (FieldUrl((string?)f.Attribute(W + "instr") ?? "") is { } url) AddLink(url, start);
        }

        private void FieldChar(string? type)
        {
            switch (type)
            {
                case "begin":
                    _fields.Push(new Field());
                    break;
                case "separate":
                    if (_fields.TryPeek(out var f)) f.InResult = true;
                    break;
                case "end":
                    if (!_fields.TryPop(out var done)) break;
                    if (FieldUrl(done.Instr.ToString()) is { } url && _para is not null)
                    {
                        var shown = done.Result.ToString();
                        var text = _para.Text.ToString();
                        // The result usually ends in this paragraph; find where it starts for the overlap check.
                        var start = text.EndsWith(shown, StringComparison.Ordinal) ? text.Length - shown.Length : text.Length;
                        _para.Links.Add((url, start, text.Length, shown));
                    }
                    break;
            }
        }

        private void AddLink(string url, int start)
        {
            if (_para is null) return;
            var text = _para.Text.ToString();
            _para.Links.Add((url, start, text.Length, text[start..]));
        }

        /// <summary>HYPERLINK "url" [\l "anchor"] [\o "tip"]; an anchor alone is a link inside the document (ignored).</summary>
        private static string? FieldUrl(string instr)
        {
            var m = HyperlinkField().Match(instr);
            if (!m.Success) return null;
            var url = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            return url.Trim().Length > 0 && !url.StartsWith('\\') ? url.Trim() : null;
        }
    }

    /// <summary>Shared byte budget for every part read from one file.</summary>
    private sealed class Budget(long max)
    {
        public long Left { get; private set; } = max;

        public void Use(int n)
        {
            Left -= n;
            if (Left < 0) throw new ImportLimitException("the text inside the document is too big (or the file is a zip bomb)");
        }
    }

    private sealed class CountingStream(Stream inner, Budget budget) : Stream
    {
        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = inner.Read(buffer, offset, count);
            budget.Use(n);
            return n;
        }

        public override int Read(Span<byte> buffer)
        {
            var n = inner.Read(buffer);
            budget.Use(n);
            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
