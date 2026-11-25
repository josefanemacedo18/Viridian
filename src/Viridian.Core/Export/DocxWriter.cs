using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Viridian.Core.Export;

/// <summary>Trecho de texto com formatação.</summary>
public sealed record DocRun(string Text, bool Bold = false, bool Italic = false, string? Color = null, double? SizePt = null);

/// <summary>Bloco de um documento Word.</summary>
public abstract record DocBlock;

public enum DocStyle
{
    Normal,
    Title,
    Subtitle,
    Heading1,
    Heading2,
    Caption,
    Small,
}

public sealed record DocParagraph(IReadOnlyList<DocRun> Runs, DocStyle Style = DocStyle.Normal, bool Center = false) : DocBlock
{
    public DocParagraph(string text, DocStyle style = DocStyle.Normal, bool center = false)
        : this(new[] { new DocRun(text) }, style, center)
    {
    }
}

/// <summary>Imagem (JPEG ou PNG) com largura em centímetros; a altura segue a proporção do arquivo.</summary>
public sealed record DocImage(string File, double WidthCm, bool Center = false) : DocBlock;

public sealed record DocPageBreak : DocBlock;

/// <summary>Célula de tabela: blocos, preenchimento opcional (#RRGGBB).</summary>
public sealed record DocCell(IReadOnlyList<DocBlock> Content, string? Fill = null)
{
    public DocCell(string text, string? fill = null, bool bold = false)
        : this(new DocBlock[] { new DocParagraph(new[] { new DocRun(text, bold) }) }, fill)
    {
    }
}

/// <summary>Tabela com larguras relativas das colunas; a primeira linha pode ser cabeçalho.</summary>
/// <param name="FontPt">Tamanho do texto das células (nulo = tamanho normal).</param>
public sealed record DocTable(IReadOnlyList<double> ColumnWeights, IReadOnlyList<IReadOnlyList<DocCell>> Rows, bool HeaderRow = true, bool Borders = true, double? FontPt = null) : DocBlock;

/// <summary>
/// Gravador mínimo de documentos .docx (Office Open XML) editáveis no Word, sem bibliotecas externas:
/// estilos de título, parágrafos, tabelas com cores de fundo, imagens e quebras de página.
/// </summary>
public static class DocxWriter
{
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private const double PageWidthCm = 21.0 - 2 * 2.0; // A4 com margens de 2 cm

    public static void Write(string file, IReadOnlyList<DocBlock> blocks, string title = "")
    {
        string? folder = Path.GetDirectoryName(file);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        var images = new List<(string Name, byte[] Data, string Ext)>();
        var body = new StringBuilder();
        int docPr = 1;
        foreach (DocBlock block in blocks)
        {
            WriteBlock(body, block, images, ref docPr, PageWidthCm);
        }

        string temp = file + ".tmp";
        using (FileStream stream = File.Create(temp))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            Add(zip, "[Content_Types].xml", ContentTypes());
            Add(zip, "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>"
                + "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/>"
                + "</Relationships>");
            Add(zip, "docProps/core.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">"
                + $"<dc:title>{X(title)}</dc:title><dc:creator>Viridian</dc:creator>"
                + $"<dcterms:created xsi:type=\"dcterms:W3CDTF\">{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</dcterms:created></cp:coreProperties>");
            Add(zip, "word/styles.xml", Styles());
            var rels = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
            rels.Append("<Relationship Id=\"rIdStyles\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
            rels.Append("<Relationship Id=\"rIdFooter1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer\" Target=\"footer1.xml\"/>");
            Add(zip, "word/footer1.xml", Footer(title));
            for (int i = 0; i < images.Count; i++)
            {
                rels.Append(Inv, $"<Relationship Id=\"rIdImg{i + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/{images[i].Name}\"/>");
                ZipArchiveEntry entry = zip.CreateEntry("word/media/" + images[i].Name, CompressionLevel.NoCompression);
                using Stream s = entry.Open();
                s.Write(images[i].Data);
            }

            rels.Append("</Relationships>");
            Add(zip, "word/_rels/document.xml.rels", rels.ToString());
            Add(zip, "word/document.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + $"<w:document xmlns:w=\"{W}\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" "
                + "xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" "
                + "xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><w:body>"
                + body
                + "<w:sectPr><w:footerReference w:type=\"default\" r:id=\"rIdFooter1\"/><w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"1134\" w:right=\"1134\" w:bottom=\"1134\" w:left=\"1134\" w:header=\"567\" w:footer=\"567\" w:gutter=\"0\"/></w:sectPr>"
                + "</w:body></w:document>");
        }

        File.Move(temp, file, overwrite: true);
    }

    private static void WriteBlock(StringBuilder sb, DocBlock block, List<(string, byte[], string)> images, ref int docPr, double availableCm)
    {
        switch (block)
        {
            case DocParagraph p:
                sb.Append("<w:p><w:pPr>");
                if (p.Style != DocStyle.Normal)
                {
                    sb.Append("<w:pStyle w:val=\"").Append(p.Style).Append("\"/>");
                }

                if (p.Center)
                {
                    sb.Append("<w:jc w:val=\"center\"/>");
                }

                sb.Append("</w:pPr>");
                foreach (DocRun run in p.Runs)
                {
                    WriteRun(sb, run);
                }

                sb.Append("</w:p>");
                break;

            case DocImage image:
                WriteImage(sb, image, images, ref docPr, availableCm);
                break;

            case DocPageBreak:
                sb.Append("<w:p><w:r><w:br w:type=\"page\"/></w:r></w:p>");
                break;

            case DocTable table:
                WriteTable(sb, table, images, ref docPr, availableCm);
                break;
        }
    }

    private static void WriteRun(StringBuilder sb, DocRun run)
    {
        string[] lines = (run.Text ?? string.Empty).Replace("\r\n", "\n").Split('\n');
        sb.Append("<w:r>");
        if (run.Bold || run.Italic || run.Color is not null || run.SizePt is not null)
        {
            sb.Append("<w:rPr>");
            if (run.Bold)
            {
                sb.Append("<w:b/>");
            }

            if (run.Italic)
            {
                sb.Append("<w:i/>");
            }

            if (run.Color is not null)
            {
                sb.Append("<w:color w:val=\"").Append(run.Color.TrimStart('#')).Append("\"/>");
            }

            if (run.SizePt is double size)
            {
                sb.Append(Inv, $"<w:sz w:val=\"{Math.Round(size * 2)}\"/>");
            }

            sb.Append("</w:rPr>");
        }

        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                sb.Append("<w:br/>");
            }

            sb.Append("<w:t xml:space=\"preserve\">").Append(X(lines[i])).Append("</w:t>");
        }

        sb.Append("</w:r>");
    }

    private static void WriteImage(StringBuilder sb, DocImage image, List<(string Name, byte[] Data, string Ext)> images, ref int docPr, double availableCm)
    {
        if (!File.Exists(image.File) || !ImageInfo.TryRead(image.File, out int pxW, out int pxH, out string ext))
        {
            return;
        }

        byte[] data = File.ReadAllBytes(image.File);
        string name = $"imagem{images.Count + 1}.{ext}";
        images.Add((name, data, ext));
        double widthCm = Math.Min(image.WidthCm, availableCm);
        long cx = (long)(widthCm * 360000);
        long cy = (long)(cx * (double)pxH / Math.Max(1, pxW));
        int id = docPr++;
        sb.Append("<w:p><w:pPr>").Append(image.Center ? "<w:jc w:val=\"center\"/>" : string.Empty).Append("</w:pPr><w:r><w:drawing>");
        sb.Append(Inv, $"<wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\"><wp:extent cx=\"{cx}\" cy=\"{cy}\"/><wp:docPr id=\"{id}\" name=\"Imagem {id}\"/>");
        sb.Append("<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:pic>");
        sb.Append(Inv, $"<pic:nvPicPr><pic:cNvPr id=\"{id}\" name=\"{name}\"/><pic:cNvPicPr/></pic:nvPicPr>");
        sb.Append(Inv, $"<pic:blipFill><a:blip r:embed=\"rIdImg{images.Count}\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>");
        sb.Append(Inv, $"<pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"{cx}\" cy=\"{cy}\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr>");
        sb.Append("</pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>");
    }

    private static void WriteTable(StringBuilder sb, DocTable table, List<(string, byte[], string)> images, ref int docPr, double availableCm)
    {
        double total = table.ColumnWeights.Sum();
        int[] twips = table.ColumnWeights.Select(w => (int)(availableCm * 567 * w / total)).ToArray();
        sb.Append("<w:tbl><w:tblPr><w:tblW w:w=\"5000\" w:type=\"pct\"/>");
        if (table.Borders)
        {
            sb.Append("<w:tblBorders>");
            foreach (string side in new[] { "top", "left", "bottom", "right", "insideH", "insideV" })
            {
                sb.Append("<w:").Append(side).Append(" w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"C9D3C4\"/>");
            }

            sb.Append("</w:tblBorders>");
        }

        sb.Append("<w:tblCellMar><w:left w:w=\"70\" w:type=\"dxa\"/><w:right w:w=\"70\" w:type=\"dxa\"/></w:tblCellMar></w:tblPr><w:tblGrid>");
        foreach (int t in twips)
        {
            sb.Append(Inv, $"<w:gridCol w:w=\"{t}\"/>");
        }

        sb.Append("</w:tblGrid>");
        for (int r = 0; r < table.Rows.Count; r++)
        {
            bool header = table.HeaderRow && r == 0;
            sb.Append("<w:tr>");
            if (header)
            {
                sb.Append("<w:trPr><w:tblHeader/></w:trPr>");
            }

            IReadOnlyList<DocCell> row = table.Rows[r];
            for (int c = 0; c < twips.Length; c++)
            {
                DocCell cell = c < row.Count ? row[c] : new DocCell(string.Empty);
                // Cabeçalho na cor principal e linhas alternadas claras (só em tabelas com cabeçalho e bordas).
                string? fill = cell.Fill ?? (header ? "2E4A36" : table.HeaderRow && table.Borders && r % 2 == 0 ? "F6F8F2" : null);
                sb.Append(Inv, $"<w:tc><w:tcPr><w:tcW w:w=\"{twips[c]}\" w:type=\"dxa\"/>");
                if (fill is not null)
                {
                    sb.Append("<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"").Append(fill.TrimStart('#')).Append("\"/>");
                }

                sb.Append("<w:vAlign w:val=\"center\"/></w:tcPr>");
                if (cell.Content.Count == 0)
                {
                    sb.Append("<w:p/>");
                }

                foreach (DocBlock content in cell.Content)
                {
                    DocBlock b = content is DocParagraph p
                        ? p with
                        {
                            Runs = p.Runs.Select(x => x with
                            {
                                Bold = x.Bold || header,
                                Color = header ? "FFFFFF" : x.Color,
                                SizePt = x.SizePt ?? table.FontPt,
                            }).ToList(),
                        }
                        : content;
                    WriteBlock(sb, b, images, ref docPr, twips[c] / 567.0 - 0.3);
                }

                sb.Append("</w:tc>");
            }

            sb.Append("</w:tr>");
        }

        sb.Append("</w:tbl><w:p/>");
    }

    private static string ContentTypes() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
        + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>"
        + "<Default Extension=\"xml\" ContentType=\"application/xml\"/>"
        + "<Default Extension=\"jpeg\" ContentType=\"image/jpeg\"/><Default Extension=\"jpg\" ContentType=\"image/jpeg\"/><Default Extension=\"png\" ContentType=\"image/png\"/>"
        + "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>"
        + "<Override PartName=\"/word/footer1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml\"/>"
        + "<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>"
        + "<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>"
        + "</Types>";

    /// <summary>Rodapé: nome do documento e número da página, sobre um fio verde.</summary>
    private static string Footer(string title) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
        + $"<w:ftr xmlns:w=\"{W}\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">"
        + "<w:p><w:pPr><w:pBdr><w:top w:val=\"single\" w:sz=\"6\" w:space=\"4\" w:color=\"5E824E\"/></w:pBdr><w:tabs><w:tab w:val=\"right\" w:pos=\"9638\"/></w:tabs></w:pPr>"
        + $"<w:r><w:rPr><w:color w:val=\"6B746D\"/><w:sz w:val=\"16\"/></w:rPr><w:t xml:space=\"preserve\">{X(title.Length > 0 ? title : "Viridian")}</w:t></w:r>"
        + "<w:r><w:rPr><w:color w:val=\"6B746D\"/><w:sz w:val=\"16\"/></w:rPr><w:tab/><w:t xml:space=\"preserve\">página </w:t></w:r>"
        + "<w:fldSimple w:instr=\" PAGE \"><w:r><w:rPr><w:b/><w:color w:val=\"2E4A36\"/><w:sz w:val=\"16\"/></w:rPr><w:t>1</w:t></w:r></w:fldSimple>"
        + "</w:p></w:ftr>";

    private static string Styles()
    {
        static string Style(string id, string name, string rPr, string pPr = "", bool basedOnNormal = true) =>
            $"<w:style w:type=\"paragraph\"{(id == "Normal" ? " w:default=\"1\"" : string.Empty)} w:styleId=\"{id}\"><w:name w:val=\"{name}\"/>{(basedOnNormal && id != "Normal" ? "<w:basedOn w:val=\"Normal\"/><w:next w:val=\"Normal\"/>" : string.Empty)}<w:qFormat/><w:pPr>{pPr}</w:pPr><w:rPr>{rPr}</w:rPr></w:style>";
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
               + $"<w:styles xmlns:w=\"{W}\"><w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\" w:cs=\"Calibri\"/><w:sz w:val=\"21\"/><w:lang w:val=\"pt-BR\"/></w:rPr></w:rPrDefault>"
               + "<w:pPrDefault><w:pPr><w:spacing w:after=\"80\" w:line=\"264\" w:lineRule=\"auto\"/></w:pPr></w:pPrDefault></w:docDefaults>"
               + Style("Normal", "Normal", "<w:color w:val=\"1E2A24\"/>")
               + Style("Title", "Title", "<w:b/><w:color w:val=\"2E4A36\"/><w:sz w:val=\"56\"/>", "<w:spacing w:before=\"600\" w:after=\"200\"/>")
               + Style("Subtitle", "Subtitle", "<w:color w:val=\"5E824E\"/><w:sz w:val=\"30\"/>", "<w:spacing w:after=\"160\"/>")
               + Style("Heading1", "heading 1", "<w:b/><w:color w:val=\"2E4A36\"/><w:sz w:val=\"32\"/>", "<w:keepNext/><w:pBdr><w:bottom w:val=\"single\" w:sz=\"8\" w:space=\"4\" w:color=\"5E824E\"/></w:pBdr><w:spacing w:before=\"360\" w:after=\"200\"/><w:outlineLvl w:val=\"0\"/>")
               + Style("Heading2", "heading 2", "<w:b/><w:color w:val=\"5E824E\"/><w:sz w:val=\"25\"/>", "<w:keepNext/><w:spacing w:before=\"240\" w:after=\"100\"/><w:outlineLvl w:val=\"1\"/>")
               + Style("Caption", "caption", "<w:i/><w:color w:val=\"5F6B63\"/><w:sz w:val=\"17\"/>")
               + Style("Small", "Texto pequeno", "<w:color w:val=\"5F6B63\"/><w:sz w:val=\"17\"/>")
               + "</w:styles>";
    }

    private static void Add(ZipArchive zip, string path, string content)
    {
        ZipArchiveEntry entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string X(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                default:
                    if (c >= 0x20 || c == '\t')
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        return sb.ToString();
    }
}

/// <summary>Leitura das dimensões de imagens JPEG e PNG (sem bibliotecas gráficas).</summary>
public static class ImageInfo
{
    public static bool TryRead(string file, out int width, out int height, out string extension)
    {
        width = height = 0;
        extension = string.Empty;
        try
        {
            using FileStream s = File.OpenRead(file);
            var head = new byte[24];
            if (s.Read(head, 0, 24) < 24)
            {
                return false;
            }

            if (head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47)
            {
                width = (head[16] << 24) | (head[17] << 16) | (head[18] << 8) | head[19];
                height = (head[20] << 24) | (head[21] << 16) | (head[22] << 8) | head[23];
                extension = "png";
                return width > 0 && height > 0;
            }

            if (head[0] != 0xFF || head[1] != 0xD8)
            {
                return false;
            }

            s.Position = 2;
            while (s.Position < s.Length)
            {
                int marker;
                do
                {
                    marker = s.ReadByte();
                }
                while (marker == 0xFF);

                if (marker < 0)
                {
                    return false;
                }

                int hi = s.ReadByte();
                int lo = s.ReadByte();
                int length = (hi << 8) | lo;
                bool sof = marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC;
                if (sof)
                {
                    s.ReadByte(); // precisão
                    height = (s.ReadByte() << 8) | s.ReadByte();
                    width = (s.ReadByte() << 8) | s.ReadByte();
                    extension = "jpeg";
                    return width > 0 && height > 0;
                }

                s.Position += length - 2;
            }
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        return false;
    }
}
