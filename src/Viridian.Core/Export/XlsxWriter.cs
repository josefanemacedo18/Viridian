using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Viridian.Core.Export;

public enum XlsxFormat
{
    Text = 0,
    Integer = 1,
    Decimal = 2,
    Currency = 3,
}

public sealed class XlsxColumn
{
    public XlsxColumn(string header, double width = 14, XlsxFormat format = XlsxFormat.Text)
    {
        Header = header;
        Width = width;
        Format = format;
    }

    public string Header { get; }

    public double Width { get; }

    public XlsxFormat Format { get; }
}

public sealed class XlsxRow
{
    public XlsxRow(params object?[] cells)
    {
        Cells = cells;
    }

    public object?[] Cells { get; }

    /// <summary>Linha em negrito (totais).</summary>
    public bool Bold { get; init; }
}

public sealed class XlsxSheet
{
    public XlsxSheet(string name)
    {
        Name = name;
    }

    public string Name { get; }

    /// <summary>Linhas de título acima do cabeçalho (nome do relatório, projeto, data).</summary>
    public List<string> TitleLines { get; } = new();

    public List<XlsxColumn> Columns { get; } = new();

    public List<XlsxRow> Rows { get; } = new();
}

/// <summary>
/// Gravador mínimo de planilhas .xlsx (Office Open XML), sem bibliotecas externas.
/// Um plugin do Revit divide o processo com outros plugins; não carregar bibliotecas de terceiros
/// elimina conflitos de versão entre eles.
/// </summary>
public static class XlsxWriter
{
    private const string MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    // Índices de estilo definidos em styles.xml.
    private const int StyleDefault = 0;
    private const int StyleHeader = 1;
    private const int StyleInteger = 2;
    private const int StyleDecimal = 3;
    private const int StyleCurrency = 4;
    private const int StyleTitle = 5;
    private const int StyleBoldText = 6;
    private const int StyleBoldInteger = 7;
    private const int StyleBoldDecimal = 8;
    private const int StyleBoldCurrency = 9;
    private const int StyleSubtitle = 10;

    public static void Write(string path, IReadOnlyList<XlsxSheet> sheets)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        Write(stream, sheets);
    }

    public static void Write(Stream stream, IReadOnlyList<XlsxSheet> sheets)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(sheets);
        if (sheets.Count == 0)
        {
            throw new ArgumentException("A planilha precisa de pelo menos uma aba.", nameof(sheets));
        }

        List<string> names = UniqueSheetNames(sheets);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);

        AddEntry(zip, "[Content_Types].xml", ContentTypes(sheets.Count));
        AddEntry(zip, "_rels/.rels",
            $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"{PackageRelNs}\">" +
            $"<Relationship Id=\"rId1\" Type=\"{RelNs}/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
        AddEntry(zip, "xl/workbook.xml", Workbook(names));
        AddEntry(zip, "xl/_rels/workbook.xml.rels", WorkbookRels(sheets.Count));
        AddEntry(zip, "xl/styles.xml", Styles());
        for (int i = 0; i < sheets.Count; i++)
        {
            AddEntry(zip, $"xl/worksheets/sheet{i + 1}.xml", Sheet(sheets[i]));
        }
    }

    private static void AddEntry(ZipArchive zip, string name, string content)
    {
        ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using Stream stream = entry.Open();
        byte[] bytes = new UTF8Encoding(false).GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static string ContentTypes(int sheetCount)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
        sb.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
        sb.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
        sb.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
        sb.Append("<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
        for (int i = 1; i <= sheetCount; i++)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<Override PartName=\"/xl/worksheets/sheet{i}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
        }

        sb.Append("</Types>");
        return sb.ToString();
    }

    private static string Workbook(List<string> names)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append(CultureInfo.InvariantCulture, $"<workbook xmlns=\"{MainNs}\" xmlns:r=\"{RelNs}\"><sheets>");
        for (int i = 0; i < names.Count; i++)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<sheet name=\"{Escape(names[i])}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>");
        }

        sb.Append("</sheets></workbook>");
        return sb.ToString();
    }

    private static string WorkbookRels(int sheetCount)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append(CultureInfo.InvariantCulture, $"<Relationships xmlns=\"{PackageRelNs}\">");
        for (int i = 1; i <= sheetCount; i++)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<Relationship Id=\"rId{i}\" Type=\"{RelNs}/worksheet\" Target=\"worksheets/sheet{i}.xml\"/>");
        }

        sb.Append(CultureInfo.InvariantCulture, $"<Relationship Id=\"rId{sheetCount + 1}\" Type=\"{RelNs}/styles\" Target=\"styles.xml\"/>");
        sb.Append("</Relationships>");
        return sb.ToString();
    }

    private static string Styles()
    {
        const string border = "<border><left style=\"thin\"><color rgb=\"FFBFC8BF\"/></left><right style=\"thin\"><color rgb=\"FFBFC8BF\"/></right>" +
                              "<top style=\"thin\"><color rgb=\"FFBFC8BF\"/></top><bottom style=\"thin\"><color rgb=\"FFBFC8BF\"/></bottom><diagonal/></border>";
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append(CultureInfo.InvariantCulture, $"<styleSheet xmlns=\"{MainNs}\">");
        sb.Append("<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"&quot;R$&quot;\\ #,##0.00\"/></numFmts>");
        sb.Append("<fonts count=\"5\">");
        sb.Append("<font><sz val=\"10\"/><name val=\"Calibri\"/></font>");
        sb.Append("<font><b/><sz val=\"10\"/><color rgb=\"FFFFFFFF\"/><name val=\"Calibri\"/></font>");
        sb.Append("<font><b/><sz val=\"14\"/><color rgb=\"FF1F4D3A\"/><name val=\"Calibri\"/></font>");
        sb.Append("<font><b/><sz val=\"10\"/><name val=\"Calibri\"/></font>");
        sb.Append("<font><sz val=\"10\"/><color rgb=\"FF5F6B63\"/><name val=\"Calibri\"/></font>");
        sb.Append("</fonts>");
        sb.Append("<fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill>");
        sb.Append("<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF2F6B4F\"/><bgColor indexed=\"64\"/></patternFill></fill></fills>");
        sb.Append("<borders count=\"2\"><border><left/><right/><top/><bottom/><diagonal/></border>").Append(border).Append("</borders>");
        sb.Append("<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>");
        sb.Append("<cellXfs count=\"11\">");
        sb.Append(Xf(0, 0, 0, 1, null));              // 0 texto
        sb.Append(Xf(0, 1, 2, 1, "center"));          // 1 cabeçalho
        sb.Append(Xf(3, 0, 0, 1, null));              // 2 inteiro
        sb.Append(Xf(4, 0, 0, 1, null));              // 3 decimal
        sb.Append(Xf(164, 0, 0, 1, null));            // 4 moeda
        sb.Append(Xf(0, 2, 0, 0, null));              // 5 título
        sb.Append(Xf(0, 3, 0, 1, null));              // 6 texto em negrito
        sb.Append(Xf(3, 3, 0, 1, null));              // 7 inteiro em negrito
        sb.Append(Xf(4, 3, 0, 1, null));              // 8 decimal em negrito
        sb.Append(Xf(164, 3, 0, 1, null));            // 9 moeda em negrito
        sb.Append(Xf(0, 4, 0, 0, null));              // 10 subtítulo
        sb.Append("</cellXfs>");
        sb.Append("<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>");
        sb.Append("</styleSheet>");
        return sb.ToString();
    }

    private static string Xf(int numFmt, int font, int fill, int border, string? horizontal)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"<xf numFmtId=\"{numFmt}\" fontId=\"{font}\" fillId=\"{fill}\" borderId=\"{border}\" xfId=\"0\"");
        if (numFmt != 0)
        {
            sb.Append(" applyNumberFormat=\"1\"");
        }

        if (font != 0)
        {
            sb.Append(" applyFont=\"1\"");
        }

        if (fill != 0)
        {
            sb.Append(" applyFill=\"1\"");
        }

        if (border != 0)
        {
            sb.Append(" applyBorder=\"1\"");
        }

        if (horizontal is null)
        {
            sb.Append("/>");
        }
        else
        {
            sb.Append(CultureInfo.InvariantCulture, $" applyAlignment=\"1\"><alignment horizontal=\"{horizontal}\" vertical=\"center\" wrapText=\"1\"/></xf>");
        }

        return sb.ToString();
    }

    private static string Sheet(XlsxSheet sheet)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append(CultureInfo.InvariantCulture, $"<worksheet xmlns=\"{MainNs}\">");

        int headerRow = sheet.TitleLines.Count > 0 ? sheet.TitleLines.Count + 2 : 1;
        sb.Append("<sheetViews><sheetView workbookViewId=\"0\">");
        if (sheet.Columns.Count > 0)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<pane ySplit=\"{headerRow}\" topLeftCell=\"A{headerRow + 1}\" activePane=\"bottomLeft\" state=\"frozen\"/>");
        }

        sb.Append("</sheetView></sheetViews>");

        if (sheet.Columns.Count > 0)
        {
            sb.Append("<cols>");
            for (int c = 0; c < sheet.Columns.Count; c++)
            {
                double width = Math.Clamp(sheet.Columns[c].Width, 2, 120);
                sb.Append(CultureInfo.InvariantCulture, $"<col min=\"{c + 1}\" max=\"{c + 1}\" width=\"{width:0.##}\" customWidth=\"1\"/>");
            }

            sb.Append("</cols>");
        }

        sb.Append("<sheetData>");
        int rowNumber = 1;
        for (int t = 0; t < sheet.TitleLines.Count; t++)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<row r=\"{rowNumber}\">");
            AppendText(sb, 0, rowNumber, sheet.TitleLines[t], t == 0 ? StyleTitle : StyleSubtitle);
            sb.Append("</row>");
            rowNumber++;
        }

        if (sheet.TitleLines.Count > 0)
        {
            rowNumber++; // linha em branco entre o título e a tabela
        }

        if (sheet.Columns.Count > 0)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<row r=\"{rowNumber}\" ht=\"28\" customHeight=\"1\">");
            for (int c = 0; c < sheet.Columns.Count; c++)
            {
                AppendText(sb, c, rowNumber, sheet.Columns[c].Header, StyleHeader);
            }

            sb.Append("</row>");
            rowNumber++;
        }

        foreach (XlsxRow row in sheet.Rows)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<row r=\"{rowNumber}\">");
            for (int c = 0; c < row.Cells.Length; c++)
            {
                XlsxFormat format = c < sheet.Columns.Count ? sheet.Columns[c].Format : XlsxFormat.Text;
                AppendCell(sb, c, rowNumber, row.Cells[c], format, row.Bold);
            }

            sb.Append("</row>");
            rowNumber++;
        }

        sb.Append("</sheetData></worksheet>");
        return sb.ToString();
    }

    private static void AppendCell(StringBuilder sb, int column, int row, object? value, XlsxFormat format, bool bold)
    {
        switch (value)
        {
            case null:
                AppendText(sb, column, row, string.Empty, bold ? StyleBoldText : StyleDefault);
                break;
            case string text:
                AppendText(sb, column, row, text, bold ? StyleBoldText : StyleDefault);
                break;
            case not null when IsNumeric(value):
                double numeric = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (!double.IsFinite(numeric))
                {
                    AppendText(sb, column, row, string.Empty, bold ? StyleBoldText : StyleDefault);
                    break;
                }

                int style = format switch
                {
                    XlsxFormat.Integer => bold ? StyleBoldInteger : StyleInteger,
                    XlsxFormat.Currency => bold ? StyleBoldCurrency : StyleCurrency,
                    XlsxFormat.Decimal => bold ? StyleBoldDecimal : StyleDecimal,
                    _ => value is int or long or short or byte ? (bold ? StyleBoldInteger : StyleInteger) : (bold ? StyleBoldDecimal : StyleDecimal),
                };
                sb.Append(CultureInfo.InvariantCulture, $"<c r=\"{CellRef(column, row)}\" s=\"{style}\"><v>{numeric.ToString("R", CultureInfo.InvariantCulture)}</v></c>");
                break;
            default:
                AppendText(sb, column, row, Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty, bold ? StyleBoldText : StyleDefault);
                break;
        }
    }

    private static bool IsNumeric(object value) =>
        value is int or long or short or byte or double or float or decimal;

    private static void AppendText(StringBuilder sb, int column, int row, string text, int style) =>
        sb.Append(CultureInfo.InvariantCulture, $"<c r=\"{CellRef(column, row)}\" s=\"{style}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{Escape(text)}</t></is></c>");

    private static string CellRef(int column, int row)
    {
        var letters = new StringBuilder();
        int n = column + 1;
        while (n > 0)
        {
            int rem = (n - 1) % 26;
            letters.Insert(0, (char)('A' + rem));
            n = (n - 1) / 26;
        }

        return letters.Append(row.ToString(CultureInfo.InvariantCulture)).ToString();
    }

    private static List<string> UniqueSheetNames(IReadOnlyList<XlsxSheet> sheets)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (XlsxSheet sheet in sheets)
        {
            var chars = (sheet.Name ?? string.Empty).Where(c => !"[]:*?/\\".Contains(c) && !char.IsControl(c)).ToArray();
            string baseName = new string(chars).Trim().Trim('\'');
            if (baseName.Length == 0)
            {
                baseName = "Planilha";
            }

            if (baseName.Length > 31)
            {
                baseName = baseName[..31];
            }

            string name = baseName;
            int suffix = 2;
            while (!used.Add(name))
            {
                string tail = " " + suffix.ToString(CultureInfo.InvariantCulture);
                name = (baseName.Length + tail.Length > 31 ? baseName[..(31 - tail.Length)] : baseName) + tail;
                suffix++;
            }

            result.Add(name);
        }

        return result;
    }

    private static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length + 8);
        foreach (char c in text)
        {
            switch (c)
            {
                case '&':
                    sb.Append("&amp;");
                    break;
                case '<':
                    sb.Append("&lt;");
                    break;
                case '>':
                    sb.Append("&gt;");
                    break;
                case '"':
                    sb.Append("&quot;");
                    break;
                default:
                    // Caracteres de controle e substitutos isolados não são válidos em XML 1.0.
                    if (c == '\t' || c == '\n' || c == '\r' || (c >= 0x20 && !char.IsSurrogate(c) && c != 0xFFFE && c != 0xFFFF))
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        return sb.ToString();
    }
}
