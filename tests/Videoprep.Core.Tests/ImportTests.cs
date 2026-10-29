using System.IO.Compression;
using System.Text;
using Videoprep.Core.Import;

namespace Videoprep.Core.Tests;

/// <summary>"Cargar archivo..." (F1): reading links from .docx, .txt, .md, .html and .csv files that come from anywhere.</summary>
public sealed class ImportTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("videoprep-import-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); }
        catch (IOException) { }
    }

    private const string Bunny = "https://www.youtube.com/watch?v=aqz-KE-bpKQ";
    private const string Zoo = "https://www.youtube.com/watch?v=jNQXAC9IVRw";

    private string Write(string name, string text, Encoding? enc = null)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, text, enc ?? new UTF8Encoding(false));
        return path;
    }

    private static LinkFileResult Ok(string path)
    {
        var r = LinkFileReader.Read(path);
        Assert.True(r.Status == ReadStatus.Ok, $"{r.Status}: {r.Detail}");
        return r;
    }

    // ---------------- .docx ----------------

    [Fact]
    public void Docx_finds_typed_links_split_across_runs_and_hidden_hyperlinks()
    {
        var d = new DocxBuilder();
        // Word splits typed text into runs (spell-check, edits): the link must be found whole.
        d.Para("Escena 1: ver ", "https://www.you", "tube.com/watch?v=aqz-KE-", "bpKQ", " para el arranque.");
        d.Body($"<w:p>{DocxBuilder.Run("Para el final ")}{d.Hyperlink("https://youtu.be/jNQXAC9IVRw?si=abc", "mirar este clip")}{DocxBuilder.Run(".")}</w:p>");
        var r = Ok(d.Save(_dir));

        Assert.Equal(2, r.Links.Count);
        Assert.Equal(LinkHow.Text, r.Links[0].How);
        Assert.Equal(Bunny, r.Links[0].Url);
        Assert.Equal("Escena 1: ver … para el arranque.", r.Links[0].Context);
        Assert.Equal(LinkHow.Hyperlink, r.Links[1].How);
        Assert.Equal("mirar este clip", r.Links[1].Context);
        Assert.Equal(Zoo, LinkNormalizer.ForQueue(r.Links[1].Url));
    }

    [Fact]
    public void Docx_reads_hyperlink_field_codes_even_when_the_instruction_is_split()
    {
        var d = new DocxBuilder();
        d.Body("<w:p><w:r><w:t xml:space=\"preserve\">Mirá </w:t></w:r>" +
               "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>" +
               "<w:r><w:instrText xml:space=\"preserve\"> HYPERLINK \"https://www.twitch.tv/</w:instrText></w:r>" +
               "<w:r><w:instrText xml:space=\"preserve\">videos/123456\" \\o \"tip\" </w:instrText></w:r>" +
               "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>" +
               "<w:r><w:t>la transmisión</w:t></w:r>" +
               "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>");
        d.Body("<w:p><w:fldSimple w:instr=\" HYPERLINK &quot;https://vimeo.com/76979871&quot; \"><w:r><w:t>corto de Vimeo</w:t></w:r></w:fldSimple></w:p>");
        var r = Ok(d.Save(_dir));

        Assert.Equal(2, r.Links.Count);
        Assert.Equal(("https://www.twitch.tv/videos/123456", "la transmisión", LinkHow.Hyperlink), (r.Links[0].Url, r.Links[0].Context, r.Links[0].How));
        Assert.Equal(("https://vimeo.com/76979871", "corto de Vimeo"), (r.Links[1].Url, r.Links[1].Context));
    }

    [Fact]
    public void Docx_counts_an_autocorrected_link_once_and_ignores_mail_and_internal_links()
    {
        var d = new DocxBuilder();
        // Word turns a typed URL into a hyperlink whose text is the same URL: one link, shown as hyperlink.
        d.Body($"<w:p>{d.Hyperlink(Bunny, Bunny)}</w:p>");
        d.Body($"<w:p>{d.Hyperlink("mailto:productor@example.com", "escribile al productor")}</w:p>");
        d.Body("<w:p><w:hyperlink w:anchor=\"_Toc1\"><w:r><w:t>Índice</w:t></w:r></w:hyperlink></w:p>");
        d.Body("<w:p><w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText> HYPERLINK \\l \"escena2\" </w:instrText></w:r>" +
               "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>ir a la escena 2</w:t></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>");
        var r = Ok(d.Save(_dir));

        var link = Assert.Single(r.Links, l => LinkNormalizer.ForQueue(l.Url) is not null);
        Assert.Equal(LinkHow.Hyperlink, link.How);
        Assert.Equal(Bunny, link.Url);
    }

    [Fact]
    public void Docx_reads_tables_headers_footers_footnotes_and_text_boxes_once()
    {
        var d = new DocxBuilder();
        d.Body("<w:tbl><w:tr><w:tc><w:p>" + DocxBuilder.Run("Plano 3") + "</w:p></w:tc><w:tc><w:p>" + DocxBuilder.Run("x.com/nasa/status/1234567890") + "</w:p></w:tc></w:tr></w:tbl>");
        // A text box: Word writes it twice (new shape + old VML fallback). It must be counted once.
        d.Body("<w:p><w:r><mc:AlternateContent><mc:Choice Requires=\"wps\"><w:drawing><wps:txbx><w:txbxContent><w:p>" +
               DocxBuilder.Run("caja: https://kick.com/canal/videos/abc") +
               "</w:p></w:txbxContent></wps:txbx></w:drawing></mc:Choice><mc:Fallback><w:pict><v:textbox><w:txbxContent><w:p>" +
               DocxBuilder.Run("caja: https://kick.com/canal/videos/abc") +
               "</w:p></w:txbxContent></v:textbox></w:pict></mc:Fallback></mc:AlternateContent></w:r></w:p>");
        // Deleted text (tracked changes) is not there any more.
        d.Body("<w:p><w:del><w:r><w:delText>https://vimeo.com/1</w:delText></w:r></w:del></w:p>");
        d.Part("header1.xml", $"<w:hdr {DocxBuilder.Ns}><w:p>{DocxBuilder.Run("Cabecera https://youtu.be/jNQXAC9IVRw")}</w:p></w:hdr>");
        d.Part("footer1.xml", $"<w:ftr {DocxBuilder.Ns}><w:p>{DocxBuilder.Run("Pie: www.dailymotion.com/video/x7tgad0")}</w:p></w:ftr>");
        d.Part("footnotes.xml", $"<w:footnotes {DocxBuilder.Ns}><w:footnote w:id=\"1\"><w:p>{DocxBuilder.Run("Fuente: https://vimeo.com/76979871")}</w:p></w:footnote></w:footnotes>");
        var r = Ok(d.Save(_dir));

        // Body first (table, then the text box once), then header, footer and footnotes; the deleted link is not there.
        Assert.Equal(
            ["https://x.com/nasa/status/1234567890", "https://kick.com/canal/videos/abc", Zoo,
             "https://www.dailymotion.com/video/x7tgad0", "https://vimeo.com/76979871"],
            r.Links.Select(l => LinkNormalizer.ForQueue(l.Url)));
        Assert.Equal("caja:", r.Links[1].Context);
    }

    [Fact]
    public void Docx_with_a_DTD_is_refused()
    {
        var d = new DocxBuilder();
        var evil = $"<?xml version=\"1.0\"?><!DOCTYPE w:document [<!ENTITY x SYSTEM \"file:///C:/Windows/win.ini\">]><w:document {DocxBuilder.Ns}><w:body><w:p><w:r><w:t>&x;</w:t></w:r></w:p></w:body></w:document>";
        var path = Path.Combine(_dir, "dtd.docx");
        File.WriteAllBytes(path, d.Build(evil));
        Assert.Equal(ReadStatus.Damaged, LinkFileReader.Read(path).Status);
    }

    [Fact]
    public void Docx_zip_bomb_is_stopped_by_the_text_limit()
    {
        // 30 MB of spaces compress to a few KB.
        var huge = new string(' ', 30 * 1024 * 1024);
        var doc = $"<?xml version=\"1.0\"?><w:document {DocxBuilder.Ns}><w:body><w:p><w:r><w:t>{huge}</w:t></w:r></w:p></w:body></w:document>";
        var path = Path.Combine(_dir, "bomb.docx");
        File.WriteAllBytes(path, new DocxBuilder().Build(doc));
        Assert.True(new FileInfo(path).Length < 1024 * 1024);
        var r = LinkFileReader.Read(path);
        Assert.Equal(ReadStatus.TooBig, r.Status);
    }

    [Fact]
    public void Docx_with_too_many_parts_or_not_a_zip_is_refused()
    {
        var path = Path.Combine(_dir, "many.docx");
        using (var fs = File.Create(path))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            for (var i = 0; i < 30; i++) zip.CreateEntry($"x/{i}.bin");
        Assert.Equal(ReadStatus.TooBig, LinkFileReader.Read(path, new ImportLimits { MaxZipEntries = 20 }).Status);

        var fake = Write("falso.docx", "esto no es un zip https://vimeo.com/1");
        Assert.Equal(ReadStatus.Damaged, LinkFileReader.Read(fake).Status);

        var big = new DocxBuilder().Para("https://vimeo.com/1").Save(_dir, "grande.docx");
        Assert.Equal(ReadStatus.TooBig, LinkFileReader.Read(big, new ImportLimits { MaxFileBytes = 100 }).Status);
    }

    [Fact]
    public void Docx_can_be_read_while_another_program_has_it_open()
    {
        var path = new DocxBuilder().Para(Bunny).Save(_dir);
        using var word = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        Assert.Single(Ok(path).Links);
    }

    // ---------------- other formats ----------------

    [Fact]
    public void Doc_and_pdf_are_not_read_yet_and_unknown_types_are_refused()
    {
        Assert.Equal(ReadStatus.NotYet, LinkFileReader.Read(Write("viejo.doc", "x")).Status);
        Assert.Equal(ReadStatus.NotYet, LinkFileReader.Read(Write("guion.pdf", "x")).Status);
        Assert.Equal(ReadStatus.UnknownType, LinkFileReader.Read(Write("programa.exe", "x")).Status);
        Assert.Equal(ReadStatus.CannotOpen, LinkFileReader.Read(Path.Combine(_dir, "no-existe.txt")).Status);
    }

    [Fact]
    public void Txt_keeps_the_0_2_rules_and_trims_punctuation_around_links()
    {
        var path = Write("enlaces.txt",
            "aqz-KE-bpKQ\n" +                                         // a bare ID alone on its line (0.2 rule)
            "mirá Hello_World y (https://vimeo.com/76979871).\n" +    // an 11-letter word in a sentence is not an ID
            "«www.dailymotion.com/video/x7tgad0», y además:https://youtu.be/jNQXAC9IVRw!\n" +
            "https://es.wikipedia.org/wiki/Big_Buck_Bunny_(película)\n" +
            "https:// no es nada\n");
        var r = Ok(path);
        Assert.Equal(
            [Bunny, "https://vimeo.com/76979871", "https://www.dailymotion.com/video/x7tgad0", Zoo, "https://es.wikipedia.org/wiki/Big_Buck_Bunny_(película)"],
            r.Links.Select(l => l.Url));
        Assert.Equal("mirá Hello_World y", r.Links[1].Context);
        Assert.Equal("", r.Links[0].Context);
    }

    [Fact]
    public void Txt_in_windows_1252_and_utf16_is_decoded()
    {
        var ansi = Write("ansi.txt", "canción: https://vimeo.com/2", Encoding.GetEncoding(1252));
        Assert.Equal("canción:", Ok(ansi).Links[0].Context);
        var utf16 = Write("u16.txt", "canción: https://vimeo.com/3", Encoding.Unicode);
        Assert.Equal("https://vimeo.com/3", Ok(utf16).Links[0].Url);
    }

    [Fact]
    public void Text_over_the_size_limit_is_refused()
    {
        var path = Write("grande.txt", new string('a', 2000) + " https://vimeo.com/1");
        Assert.Equal(ReadStatus.TooBig, LinkFileReader.Read(path, new ImportLimits { MaxTextBytes = 1000 }).Status);
    }

    [Fact]
    public void Markdown_links_references_autolinks_and_bare_urls()
    {
        var r = Ok(Write("notas.md",
            "# Guion\n- [Escena del conejo](https://youtu.be/aqz-KE-bpKQ \"título\")\n" +
            "- Ver <https://vimeo.com/76979871>\n- Referencia: [el zoo][zoo]\n- suelto https://kick.com/a/videos/b\n\n" +
            "[zoo]: https://www.youtube.com/watch?v=jNQXAC9IVRw\n"));
        Assert.Equal(
            [("https://youtu.be/aqz-KE-bpKQ", "Escena del conejo", LinkHow.Hyperlink),
             ("https://vimeo.com/76979871", "- Ver", LinkHow.Text),
             ("https://kick.com/a/videos/b", "- suelto", LinkHow.Text),
             (Zoo, "zoo", LinkHow.Hyperlink)],
            r.Links.Select(l => (LinkNormalizer.ForQueue(l.Url) == Bunny ? "https://youtu.be/aqz-KE-bpKQ" : l.Url, l.Context, l.How)));
    }

    [Fact]
    public void Html_anchors_embeds_and_typed_links_but_not_scripts()
    {
        var r = Ok(Write("pagina.html",
            "<html><head><script>var u='https://evil.example/x.mp4';</script><style>a{}</style></head><body>" +
            "<p>Intro <a class=\"x\" href=\"https://www.google.com/url?q=https://youtu.be/aqz-KE-bpKQ&amp;sa=D\">el <b>conejo</b> &amp; amigos</a></p>" +
            "<iframe width=\"560\" src=\"https://www.youtube.com/embed/jNQXAC9IVRw\"></iframe>" +
            "<!-- https://vimeo.com/oculto -->" +
            "<div>Escrito: https://vimeo.com/76979871?utm_source=x&amp;h=1</div></body></html>"));
        Assert.Equal(3, r.Links.Count);
        Assert.Equal((LinkHow.Hyperlink, "el conejo & amigos"), (r.Links[0].How, r.Links[0].Context));
        Assert.Equal(Bunny, LinkNormalizer.ForQueue(r.Links[0].Url));   // Google Docs redirect unwrapped
        Assert.Equal(Zoo, LinkNormalizer.ForQueue(r.Links[1].Url));
        Assert.Equal(("https://vimeo.com/76979871?h=1", "Escrito:"), (LinkNormalizer.ForQueue(r.Links[2].Url), r.Links[2].Context));
    }

    [Fact]
    public void Csv_with_semicolons_quotes_and_line_breaks()
    {
        var r = Ok(Write("planilla.csv",
            "Escena;Enlace;Nota\r\n" +
            "1;https://youtu.be/aqz-KE-bpKQ;\"abre, con \"\"risa\"\"\"\r\n" +
            "2;\"varios:\nhttps://vimeo.com/76979871\";cierre\r\n"));
        Assert.Equal(2, r.Links.Count);
        Assert.Equal("1 · abre, con \"risa\"", r.Links[0].Context);
        Assert.Equal("2 · cierre", r.Links[1].Context);
    }

    // ---------------- clean-up, classes, review rows ----------------

    [Theory]
    [InlineData("https://youtu.be/aqz-KE-bpKQ?si=XyZ", Bunny)]
    [InlineData("https://www.youtube.com/shorts/aqz-KE-bpKQ", Bunny)]
    [InlineData("https://m.youtube.com/watch?v=aqz-KE-bpKQ&t=95s&list=PL1&index=2", Bunny)]   // never a time range, never a playlist
    [InlineData("https://www.google.com/url?q=https%3A%2F%2Fyoutu.be%2Faqz-KE-bpKQ&sa=D&ust=1", Bunny)]
    [InlineData("https://nam12.safelinks.protection.outlook.com/?url=https%3A%2F%2Fvimeo.com%2F76979871%3Futm_campaign%3Dz&data=05", "https://vimeo.com/76979871")]
    [InlineData("https://www.youtube.com/redirect?q=https%3A%2F%2Fvimeo.com%2F1&v=x", "https://vimeo.com/1")]
    [InlineData("https://x.com/nasa/status/1?s=20&t=abcd", "https://x.com/nasa/status/1")]
    [InlineData("https://www.twitch.tv/videos/1?feature=share&fbclid=1#chat", "https://www.twitch.tv/videos/1")]
    public void Links_are_cleaned_for_the_queue(string found, string expected) =>
        Assert.Equal(expected, LinkNormalizer.ForQueue(found));

    [Theory]
    [InlineData("mailto:a@b.com")]
    [InlineData("file:///C:/Users/x.mp4")]
    [InlineData("ftp://example.com/x.mp4")]
    [InlineData("https://user:secret@example.com/x")]
    public void Only_web_links_without_credentials_are_used(string url) => Assert.Null(LinkNormalizer.ForQueue(url));

    [Fact]
    public void The_same_video_written_differently_has_one_key()
    {
        var keys = new[] { "youtu.be/aqz-KE-bpKQ", Bunny, "https://youtube.com/shorts/aqz-KE-bpKQ?feature=share" }
            .Select(u => LinkNormalizer.CompareKey(TextLinks.ToUrl(u)!)).Distinct();
        Assert.Single(keys);
        Assert.Equal(LinkNormalizer.CompareKey("https://twitter.com/a/status/1/"), LinkNormalizer.CompareKey("https://www.x.com/a/status/1?s=46"));
        Assert.NotEqual(LinkNormalizer.CompareKey("https://vimeo.com/1"), LinkNormalizer.CompareKey("https://vimeo.com/2"));
    }

    [Theory]
    [InlineData(Bunny, LinkKind.Video, "YouTube")]
    [InlineData("https://www.youtube.com/@BlenderStudio", LinkKind.Channel, "YouTube")]
    [InlineData("https://www.youtube.com/playlist?list=PL123", LinkKind.Playlist, "YouTube")]
    [InlineData("https://www.youtube.com/", LinkKind.NotVideo, "YouTube")]
    [InlineData("https://www.twitch.tv/ejemplo/clip/AbCdEf", LinkKind.Video, "Twitch")]
    [InlineData("https://clips.twitch.tv/AbCdEf", LinkKind.Video, "Twitch")]
    [InlineData("https://www.twitch.tv/nasa", LinkKind.Channel, "Twitch")]
    [InlineData("https://x.com/usuario/status/1234567890", LinkKind.Video, "X")]
    [InlineData("https://x.com/usuario", LinkKind.Channel, "X")]
    [InlineData("https://www.instagram.com/reel/Cx1/", LinkKind.Video, "Instagram")]
    [InlineData("https://www.tiktok.com/@a/video/123", LinkKind.Video, "TikTok")]
    [InlineData("https://vimeo.com/76979871", LinkKind.Video, "Vimeo")]
    [InlineData("https://www.reddit.com/r/videos/comments/abc/x/", LinkKind.Video, "Reddit")]
    [InlineData("https://miempresa.com/informe.pdf", LinkKind.NotVideo, "miempresa.com")]
    [InlineData("https://cdn.example.org/clip.mp4", LinkKind.Video, "cdn.example.org")]
    [InlineData("https://miempresa.com/novedades", LinkKind.Unknown, "miempresa.com")]
    public void Links_are_classified_from_their_text(string url, LinkKind kind, string site)
    {
        var c = LinkClassifier.Classify(url);
        Assert.Equal((kind, site), (c.Kind, c.Site));
    }

    [Fact]
    public void Review_rows_mark_repeats_queued_links_and_non_videos()
    {
        FoundLink F(string u) => new(u, "", LinkHow.Text);
        var rows = ImportPlanner.Plan(
            [F("youtu.be/aqz-KE-bpKQ".Insert(0, "https://")), F("https://youtu.be/jNQXAC9IVRw"), F(Bunny + "&t=10"),
             F("https://www.youtube.com/@BlenderStudio"), F("https://miempresa.com/informe.pdf"), F("https://miempresa.com/x"),
             F("mailto:x@y.z")],
            queuedUrls: [Zoo]);
        Assert.Equal(
            [CandidateState.New, CandidateState.InQueue, CandidateState.Repeated, CandidateState.Channel, CandidateState.NotVideo, CandidateState.Unknown],
            rows.Select(r => r.State));
        Assert.Equal([true, false, false, false, false, false], rows.Select(r => r.CheckedByDefault));
        Assert.Equal(Bunny, rows[0].Url);
    }
}
