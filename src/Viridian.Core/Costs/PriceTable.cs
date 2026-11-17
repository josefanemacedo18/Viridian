using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace Viridian.Core.Costs;

/// <summary>Item de uma tabela de preços (insumo ou composição de serviço).</summary>
public sealed class PriceItem
{
    public string Code { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Unit { get; set; } = string.Empty;

    /// <summary>Preço unitário sem BDI.</summary>
    public decimal Price { get; set; }
}

/// <summary>
/// Tabela de preços importada pelo escritório (SINAPI, tabela de prefeitura, cotação própria...).
/// O Viridian não traz preços nem códigos: tudo vem do arquivo que o usuário importa.
/// </summary>
public sealed class PriceTable
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Referência livre: base, local e data (por exemplo, "SINAPI SP 03/2026 não desonerado").</summary>
    public string Reference { get; set; } = string.Empty;

    public string SourceFile { get; set; } = string.Empty;

    public DateTime ImportedAt { get; set; }

    public List<PriceItem> Items { get; set; } = new();

    private Dictionary<string, PriceItem>? _index;

    /// <summary>Busca exata pelo código (ignorando espaços, pontos e zeros à esquerda).</summary>
    public PriceItem? Find(string? code)
    {
        string key = CodeKey(code);
        if (key.Length == 0)
        {
            return null;
        }

        _index ??= Items
            .GroupBy(i => CodeKey(i.Code), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Key.Length > 0)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        return _index.TryGetValue(key, out PriceItem? item) ? item : null;
    }

    /// <summary>Busca por trecho do código ou da descrição (todas as palavras precisam aparecer).</summary>
    public IReadOnlyList<PriceItem> Search(string? text, int max = 200)
    {
        string[] words = Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return Items.Take(max).ToList();
        }

        return Items
            .Where(i =>
            {
                string haystack = Normalize(i.Code + " " + i.Description);
                return words.All(w => haystack.Contains(w, StringComparison.Ordinal));
            })
            .Take(max)
            .ToList();
    }

    /// <summary>Chave de comparação de códigos: "SINAPI 00012.345" e "12345" são o mesmo código.</summary>
    public static string CodeKey(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (char c in code.Trim())
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToUpperInvariant(c));
            }
        }

        string key = sb.ToString();
        return key.All(char.IsDigit) ? key.TrimStart('0') is { Length: > 0 } trimmed ? trimmed : "0" : key;
    }

    internal static string Normalize(string? text)
    {
        var sb = new StringBuilder();
        foreach (char c in (text ?? string.Empty).Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }

        return sb.ToString();
    }
}

/// <summary>Resultado da leitura de uma tabela de preços.</summary>
public sealed class PriceTableReadResult
{
    public PriceTable? Table { get; init; }

    public List<string> Warnings { get; } = new();

    public string Error { get; init; } = string.Empty;
}

/// <summary>
/// Leitura de tabelas de preços em CSV (separador ";" ou ",") ou XLSX. A linha de cabeçalho é localizada
/// automaticamente pelas colunas de código, descrição, unidade e preço, o que aceita as planilhas
/// publicadas pela Caixa (SINAPI) e tabelas próprias.
/// </summary>
public static class PriceTableReader
{
    private static readonly string[] CodeHeaders = { "codigo", "cod", "codigo da composicao", "codigo do insumo", "item", "referencia" };
    private static readonly string[] DescriptionHeaders = { "descricao", "descricao do insumo", "descricao da composicao", "denominacao", "servico", "insumo" };
    private static readonly string[] UnitHeaders = { "unidade", "un", "und", "unid", "unidade de medida" };
    private static readonly string[] PriceHeaders = { "preco", "preco unitario", "preco mediano", "custo", "custo total", "custo unitario", "valor", "valor unitario", "preco mediano r", "custo total r" };

    public static PriceTableReadResult Read(string file)
    {
        try
        {
            string extension = Path.GetExtension(file).ToLowerInvariant();
            List<string[]> rows = extension switch
            {
                ".xlsx" => ReadXlsx(file),
                ".csv" or ".txt" => ReadCsv(File.ReadAllText(file, DetectEncoding(file))),
                _ => throw new FormatException("Formato não suportado. Use .csv ou .xlsx (no Excel, \"Salvar como\" .xlsx)."),
            };
            PriceTableReadResult result = FromRows(rows, Path.GetFileNameWithoutExtension(file));
            if (result.Table is not null)
            {
                result.Table.SourceFile = file;
            }

            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or InvalidDataException or System.Xml.XmlException)
        {
            return new PriceTableReadResult { Error = ex.Message };
        }
    }

    /// <summary>Monta a tabela a partir de linhas já lidas (a primeira linha que parece cabeçalho define as colunas).</summary>
    public static PriceTableReadResult FromRows(IReadOnlyList<string[]> rows, string name)
    {
        int headerRow = -1;
        int code = -1, description = -1, unit = -1, price = -1;
        for (int r = 0; r < Math.Min(rows.Count, 60) && headerRow < 0; r++)
        {
            int c0 = -1, d0 = -1, u0 = -1, p0 = -1;
            for (int c = 0; c < rows[r].Length; c++)
            {
                string h = PriceTable.Normalize(rows[r][c]).Trim();
                h = string.Join(' ', h.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                if (h.Length == 0)
                {
                    continue;
                }

                if (c0 < 0 && Matches(h, CodeHeaders))
                {
                    c0 = c;
                }
                else if (d0 < 0 && Matches(h, DescriptionHeaders))
                {
                    d0 = c;
                }
                else if (u0 < 0 && Matches(h, UnitHeaders))
                {
                    u0 = c;
                }
                else if (Matches(h, PriceHeaders))
                {
                    // Em tabelas com várias colunas de valor, a última costuma ser o total (custo unitário final).
                    p0 = c;
                }
            }

            if (c0 >= 0 && d0 >= 0 && p0 >= 0)
            {
                headerRow = r;
                (code, description, unit, price) = (c0, d0, u0, p0);
            }
        }

        if (headerRow < 0)
        {
            return new PriceTableReadResult
            {
                Error = "Não encontrei a linha de cabeçalho. A tabela precisa ter colunas de código, descrição e preço (unidade é opcional).",
            };
        }

        var table = new PriceTable { Name = name, ImportedAt = DateTime.Now };
        var result = new PriceTableReadResult { Table = table };
        int skipped = 0;
        for (int r = headerRow + 1; r < rows.Count; r++)
        {
            string[] row = rows[r];
            string codeText = Cell(row, code).Trim();
            string descriptionText = Cell(row, description).Trim();
            if (codeText.Length == 0 || descriptionText.Length == 0)
            {
                continue;
            }

            if (!TryPrice(Cell(row, price), out decimal value))
            {
                skipped++;
                continue;
            }

            table.Items.Add(new PriceItem
            {
                Code = codeText,
                Description = descriptionText,
                Unit = unit >= 0 ? Cell(row, unit).Trim() : string.Empty,
                Price = value,
            });
        }

        if (skipped > 0)
        {
            result.Warnings.Add($"{skipped} linha(s) sem preço numérico foram ignoradas.");
        }

        if (table.Items.Count == 0)
        {
            return new PriceTableReadResult { Error = "Nenhum item com código, descrição e preço foi encontrado abaixo do cabeçalho." };
        }

        return result;
    }

    /// <summary>Interpreta preços como "1.234,56", "1234.56", "R$ 12,50".</summary>
    public static bool TryPrice(string? text, out decimal value)
    {
        value = 0;
        string t = (text ?? string.Empty).Replace("R$", string.Empty, StringComparison.OrdinalIgnoreCase).Replace(" ", string.Empty).Replace(" ", string.Empty).Trim();
        if (t.Length == 0)
        {
            return false;
        }

        int comma = t.LastIndexOf(',');
        int dot = t.LastIndexOf('.');
        if (comma >= 0 && dot >= 0)
        {
            // O último separador é o decimal.
            t = comma > dot ? t.Replace(".", string.Empty).Replace(',', '.') : t.Replace(",", string.Empty);
        }
        else if (comma >= 0)
        {
            t = t.Replace(',', '.');
        }

        bool ok = decimal.TryParse(t, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out value);
        return ok && value >= 0;
    }

    private static bool Matches(string header, string[] candidates) =>
        candidates.Any(c => header == c || header.StartsWith(c + " ", StringComparison.Ordinal));

    private static string Cell(string[] row, int index) => index >= 0 && index < row.Length ? row[index] ?? string.Empty : string.Empty;

    private static Encoding DetectEncoding(string file)
    {
        byte[] bytes = File.ReadAllBytes(file);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8;
        }

        try
        {
            _ = new UTF8Encoding(false, true).GetString(bytes);
            return Encoding.UTF8;
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1; // planilhas exportadas pelo Excel em português costumam usar Windows-1252
        }
    }

    /// <summary>CSV com separador ";" (padrão brasileiro) ou ",", aspas e quebras de linha dentro de campos.</summary>
    public static List<string[]> ReadCsv(string text)
    {
        string firstLine = text.Split('\n').FirstOrDefault(l => l.Trim().Length > 0) ?? string.Empty;
        char separator = firstLine.Count(c => c == ';') >= firstLine.Count(c => c == ',') ? ';' : ',';
        if (text.Split('\n').Take(40).Sum(l => l.Count(c => c == ';')) > 0)
        {
            separator = ';';
        }

        var rows = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            if (c == '"')
            {
                quoted = true;
            }
            else if (c == separator)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\n')
            {
                fields.Add(field.ToString().TrimEnd('\r'));
                field.Clear();
                rows.Add(fields.ToArray());
                fields.Clear();
            }
            else
            {
                field.Append(c);
            }
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString().TrimEnd('\r'));
            rows.Add(fields.ToArray());
        }

        return rows;
    }

    /// <summary>Leitor mínimo de .xlsx: primeira planilha, valores (texto compartilhado, texto embutido e números).</summary>
    public static List<string[]> ReadXlsx(string file)
    {
        using ZipArchive zip = ZipFile.OpenRead(file);
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace pkg = "http://schemas.openxmlformats.org/package/2006/relationships";

        XDocument Load(string path)
        {
            ZipArchiveEntry entry = zip.GetEntry(path) ?? throw new InvalidDataException("Arquivo .xlsx incompleto: falta " + path);
            using Stream stream = entry.Open();
            return XDocument.Load(stream);
        }

        var shared = new List<string>();
        if (zip.GetEntry("xl/sharedStrings.xml") is not null)
        {
            foreach (XElement si in Load("xl/sharedStrings.xml").Root!.Elements(main + "si"))
            {
                shared.Add(string.Concat(si.Descendants(main + "t").Select(t => t.Value)));
            }
        }

        // Primeira planilha do livro, pelo relacionamento (o nome do arquivo nem sempre é sheet1.xml).
        string sheetPath = "xl/worksheets/sheet1.xml";
        XElement? firstSheet = Load("xl/workbook.xml").Root?.Element(main + "sheets")?.Elements(main + "sheet").FirstOrDefault();
        string? relId = firstSheet?.Attribute(rel + "id")?.Value;
        if (relId is not null && zip.GetEntry("xl/_rels/workbook.xml.rels") is not null)
        {
            string? target = Load("xl/_rels/workbook.xml.rels").Root?.Elements(pkg + "Relationship")
                .FirstOrDefault(r => r.Attribute("Id")?.Value == relId)?.Attribute("Target")?.Value;
            if (!string.IsNullOrEmpty(target))
            {
                sheetPath = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
            }
        }

        var rows = new List<string[]>();
        foreach (XElement row in Load(sheetPath).Descendants(main + "row"))
        {
            var cells = new SortedDictionary<int, string>();
            int next = 0;
            foreach (XElement c in row.Elements(main + "c"))
            {
                int column = ColumnIndex(c.Attribute("r")?.Value) ?? next;
                next = column + 1;
                string type = c.Attribute("t")?.Value ?? string.Empty;
                string value = type switch
                {
                    "s" => int.TryParse(c.Element(main + "v")?.Value, out int index) && index >= 0 && index < shared.Count ? shared[index] : string.Empty,
                    "inlineStr" => string.Concat(c.Descendants(main + "t").Select(t => t.Value)),
                    _ => c.Element(main + "v")?.Value ?? string.Empty,
                };
                cells[column] = value;
            }

            int width = cells.Count == 0 ? 0 : cells.Keys.Max() + 1;
            var values = new string[width];
            for (int i = 0; i < width; i++)
            {
                values[i] = cells.TryGetValue(i, out string? v) ? v : string.Empty;
            }

            rows.Add(values);
        }

        return rows;
    }

    private static int? ColumnIndex(string? reference)
    {
        if (string.IsNullOrEmpty(reference))
        {
            return null;
        }

        int index = 0;
        int letters = 0;
        foreach (char ch in reference)
        {
            if (ch is >= 'A' and <= 'Z')
            {
                index = index * 26 + (ch - 'A' + 1);
                letters++;
            }
            else
            {
                break;
            }
        }

        return letters == 0 ? null : index - 1;
    }
}
