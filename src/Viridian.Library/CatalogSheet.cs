using System.Globalization;
using Viridian.Core.Colors;
using Viridian.Core.Config;
using Viridian.Core.Export;
using Viridian.Core.Model;

namespace Viridian.Library;

/// <summary>Resultado da leitura de uma planilha do catálogo, antes de gravar.</summary>
public sealed class CatalogImport
{
    /// <summary>Espécies existentes com algum valor alterado (cópias já atualizadas).</summary>
    public List<Species> Updated { get; } = new();

    /// <summary>Espécies novas (linhas sem identificador conhecido nem nome científico do catálogo).</summary>
    public List<Species> Added { get; } = new();

    public int Unchanged { get; set; }

    /// <summary>Problemas por linha (valor inválido, linha sem nome). As células com problema são ignoradas.</summary>
    public List<string> Problems { get; } = new();

    public bool HasChanges => Updated.Count + Added.Count > 0;
}

/// <summary>
/// Catálogo em planilha: exporta as espécies para .xlsx e importa as alterações feitas no Excel.
/// Linhas são casadas pelo identificador (ou pelo nome científico); célula vazia mantém o valor atual.
/// </summary>
public static class CatalogSheet
{
    public const string IdHeader = "Identificador";
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private sealed record Field(string Header, double Width, XlsxFormat Format, Func<Species, object?> Get, Func<Species, string, string?> Set);

    private static readonly Field[] Fields =
    {
        new("Código", 9, XlsxFormat.Text, s => s.Code, (s, v) => { s.Code = v; return null; }),
        new("Nome popular", 26, XlsxFormat.Text, s => s.PopularName, (s, v) => { s.PopularName = v; return null; }),
        new("Nome científico", 30, XlsxFormat.Text, s => s.ScientificName, (s, v) => { s.ScientificName = v; return null; }),
        new("Família botânica", 18, XlsxFormat.Text, s => s.BotanicalFamily, (s, v) => { s.BotanicalFamily = v; return null; }),
        new("Categoria", 13, XlsxFormat.Text, s => Labels.Of(s.Category), (s, v) => Enum<PlantCategory>(v, Labels.Of, x => s.Category = x)),
        new("Origem", 15, XlsxFormat.Text, s => Labels.Of(s.Origin), (s, v) => Enum<PlantOrigin>(v, Labels.Of, x => s.Origin = x)),
        new("Biomas", 22, XlsxFormat.Text, s => s.Biomes == Biome.Nenhum ? string.Empty : Labels.Of(s.Biomes), SetBiomes),
        new("Forma da copa", 14, XlsxFormat.Text, s => Labels.Of(s.CrownForm), (s, v) => Enum<CrownForm>(v, Labels.Of, x => s.CrownForm = x)),
        new("Altura adulta (m)", 10, XlsxFormat.Decimal, s => s.AdultHeightM, (s, v) => Number(v, x => s.AdultHeightM = x)),
        new("Copa (m)", 9, XlsxFormat.Decimal, s => s.CrownDiameterM, (s, v) => Number(v, x => s.CrownDiameterM = x)),
        new("Altura de plantio (m)", 10, XlsxFormat.Decimal, s => s.PlantingHeightM, (s, v) => Number(v, x => s.PlantingHeightM = x)),
        new("Espaçamento (m)", 10, XlsxFormat.Decimal, s => s.RecommendedSpacingM, (s, v) => Number(v, x => s.RecommendedSpacingM = x)),
        new("Densidade (un/m²)", 10, XlsxFormat.Decimal, s => s.DensityPerM2, (s, v) => Number(v, x => s.DensityPerM2 = x)),
        new("Unidade de fornecimento", 12, XlsxFormat.Text, s => s.SupplyUnit, (s, v) => { s.SupplyUnit = v; return null; }),
        new("Preço unitário (R$)", 12, XlsxFormat.Currency, s => s.UnitPrice, (s, v) => Number(v, x => s.UnitPrice = (decimal)x)),
        new("Crescimento", 12, XlsxFormat.Text, s => Labels.Of(s.Growth), (s, v) => Enum<GrowthRate>(v, Labels.Of, x => s.Growth = x)),
        new("Água", 12, XlsxFormat.Text, s => Labels.Of(s.Water), (s, v) => Enum<WaterNeed>(v, Labels.Of, x => s.Water = x)),
        new("Luz", 14, XlsxFormat.Text, s => Labels.Of(s.Light), (s, v) => Enum<LightNeed>(v, Labels.Of, x => s.Light = x)),
        new("Tolera encharcamento", 12, XlsxFormat.Text, s => Labels.Of(s.Waterlogging), (s, v) => Enum<WaterloggingTolerance>(v, Labels.Of, x => s.Waterlogging = x)),
        new("Tolera seca", 12, XlsxFormat.Text, s => Labels.Of(s.Drought), (s, v) => Enum<DroughtTolerance>(v, Labels.Of, x => s.Drought = x)),
        new("Floração", 14, XlsxFormat.Text, s => s.Flowering, (s, v) => Months(v, x => s.Flowering = x)),
        new("Cor da flor", 10, XlsxFormat.Text, s => s.FlowerColor, (s, v) => Color(v, x => s.FlowerColor = x)),
        new("Cor da folhagem", 10, XlsxFormat.Text, s => s.FoliageColor, (s, v) => Color(v, x => s.FoliageColor = x)),
        new("Frutificação", 14, XlsxFormat.Text, s => s.Fruiting, (s, v) => Months(v, x => s.Fruiting = x)),
        new("Cor do fruto", 10, XlsxFormat.Text, s => s.FruitColor, (s, v) => Color(v, x => s.FruitColor = x)),
        new("Textura", 12, XlsxFormat.Text, s => Labels.Of(s.Texture), (s, v) => Enum<Texture>(v, Labels.Of, x => s.Texture = x)),
        new("Folhagem", 14, XlsxFormat.Text, s => Labels.Of(s.LeafHabit), (s, v) => Enum<LeafHabit>(v, Labels.Of, x => s.LeafHabit = x)),
        new("Meses sem folhas", 14, XlsxFormat.Text, s => s.LeaflessMonths, (s, v) => Months(v, x => s.LeaflessMonths = x)),
        new("Código de preço", 14, XlsxFormat.Text, s => s.PriceItemCode, (s, v) => { s.PriceItemCode = v; return null; }),
        new("Código do serviço", 14, XlsxFormat.Text, s => s.ServiceItemCode, (s, v) => { s.ServiceItemCode = v; return null; }),
        new("Observações", 40, XlsxFormat.Text, s => s.Notes, (s, v) => { s.Notes = v; return null; }),
    };

    /// <summary>Cabeçalhos das colunas, na ordem da planilha.</summary>
    public static IReadOnlyList<string> Headers => new[] { IdHeader }.Concat(Fields.Select(f => f.Header)).ToList();

    /// <summary>Monta a planilha do catálogo.</summary>
    public static XlsxSheet Export(IEnumerable<Species> species)
    {
        ArgumentNullException.ThrowIfNull(species);
        var sheet = new XlsxSheet("Catálogo");
        sheet.TitleLines.Add("Viridian — catálogo de espécies");
        sheet.TitleLines.Add("Edite os valores e importe de volta (Buscar Espécie > Importar planilha). Não altere a coluna Identificador. "
                             + "Célula vazia mantém o valor atual. Linha sem identificador vira espécie nova.");
        sheet.Columns.Add(new XlsxColumn(IdHeader, 22));
        foreach (Field field in Fields)
        {
            sheet.Columns.Add(new XlsxColumn(field.Header, field.Width, field.Format));
        }

        foreach (Species s in species.OrderBy(x => x.Code, StringComparer.OrdinalIgnoreCase))
        {
            var cells = new object?[Fields.Length + 1];
            cells[0] = s.Id;
            for (int i = 0; i < Fields.Length; i++)
            {
                object? value = Fields[i].Get(s);
                cells[i + 1] = value switch
                {
                    double d when !(d > 0) => null,
                    decimal m when m <= 0 => null,
                    _ => value,
                };
            }

            sheet.Rows.Add(new XlsxRow(cells));
        }

        return sheet;
    }

    /// <summary>
    /// Lê as linhas da planilha e compara com o catálogo atual. Nada é gravado: o resultado traz cópias atualizadas.
    /// </summary>
    /// <param name="newDraft">Cria o rascunho de uma espécie nova com os padrões da categoria.</param>
    public static CatalogImport Read(IReadOnlyList<string[]> rows, IReadOnlyList<Species> current, Func<PlantCategory, Species> newDraft)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(newDraft);
        var result = new CatalogImport();

        int headerRow = -1;
        for (int i = 0; i < rows.Count && headerRow < 0; i++)
        {
            if (rows[i].Any(c => Same(c, IdHeader)) && rows[i].Any(c => Same(c, "Nome popular")))
            {
                headerRow = i;
            }
        }

        if (headerRow < 0)
        {
            result.Problems.Add("Cabeçalho não encontrado: a planilha precisa das colunas Identificador e Nome popular (use Exportar planilha como modelo).");
            return result;
        }

        string[] header = rows[headerRow];
        int idColumn = Array.FindIndex(header, c => Same(c, IdHeader));
        var columns = new List<(int Column, Field Field)>();
        foreach (Field field in Fields)
        {
            int column = Array.FindIndex(header, c => Same(c, field.Header));
            if (column >= 0)
            {
                columns.Add((column, field));
            }
        }

        var byId = current.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
        var byScientific = current
            .Where(s => !string.IsNullOrWhiteSpace(s.ScientificName))
            .GroupBy(s => TextSearch.Normalize(s.ScientificName.Trim()))
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.First());
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int sciColumn = Array.FindIndex(header, c => Same(c, "Nome científico"));
        int categoryColumn = Array.FindIndex(header, c => Same(c, "Categoria"));

        for (int r = headerRow + 1; r < rows.Count; r++)
        {
            string[] row = rows[r];
            string Cell(int c) => c >= 0 && c < row.Length ? (row[c] ?? string.Empty).Trim() : string.Empty;
            if (row.All(c => string.IsNullOrWhiteSpace(c)))
            {
                continue;
            }

            int line = r + 1;
            string id = Cell(idColumn);
            Species? original = id.Length > 0 && byId.TryGetValue(id, out Species? found) ? found
                : id.Length == 0 && byScientific.TryGetValue(TextSearch.Normalize(Cell(sciColumn)), out Species? bySci) ? bySci
                : null;
            if (id.Length > 0 && original is null)
            {
                result.Problems.Add($"Linha {line}: identificador {id} não existe no catálogo (para uma espécie nova, deixe o identificador vazio).");
                continue;
            }

            Species target;
            if (original is not null)
            {
                if (!seen.Add(original.Id))
                {
                    result.Problems.Add($"Linha {line}: {original.PopularName} aparece mais de uma vez; só a primeira linha foi considerada.");
                    continue;
                }

                target = original.Clone();
            }
            else
            {
                PlantCategory category = PlantCategory.Arbusto;
                if (Cell(categoryColumn).Length == 0 || Enum<PlantCategory>(Cell(categoryColumn), Labels.Of, c => category = c) is not null)
                {
                    result.Problems.Add($"Linha {line}: espécie nova precisa de uma categoria válida (por exemplo Árvore, Arbusto, Forração).");
                    continue;
                }

                target = newDraft(category);
                target.Code = string.Empty; // o código vem da planilha ou é atribuído ao gravar
            }

            foreach ((int column, Field field) in columns)
            {
                string value = Cell(column);
                if (value.Length == 0)
                {
                    continue;
                }

                if (field.Set(target, value) is string problem)
                {
                    result.Problems.Add($"Linha {line}, {field.Header}: {problem}");
                }
            }

            if (original is null)
            {
                if (string.IsNullOrWhiteSpace(target.PopularName))
                {
                    result.Problems.Add($"Linha {line}: espécie nova sem nome popular.");
                    continue;
                }

                result.Added.Add(target);
            }
            else if (ViridianJson.Serialize(target) != ViridianJson.Serialize(original))
            {
                result.Updated.Add(target);
            }
            else
            {
                result.Unchanged++;
            }
        }

        return result;
    }

    private static bool Same(string? a, string b) => TextSearch.Normalize((a ?? string.Empty).Trim()) == TextSearch.Normalize(b);

    private static string? Enum<T>(string text, Func<T, string> label, Action<T> set)
        where T : struct, System.Enum
    {
        string key = TextSearch.Normalize(text.Trim());
        foreach (T value in System.Enum.GetValues<T>())
        {
            if (TextSearch.Normalize(label(value)) == key || TextSearch.Normalize(value.ToString()) == key)
            {
                set(value);
                return null;
            }
        }

        return $"\"{text}\" não é um valor válido (use: {string.Join(", ", System.Enum.GetValues<T>().Select(label))}).";
    }

    private static string? Number(string text, Action<double> set)
    {
        string t = text.Replace("R$", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        if ((double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && !t.Contains(','))
            || double.TryParse(t, NumberStyles.Float | NumberStyles.AllowThousands, PtBr, out v))
        {
            if (double.IsFinite(v) && v >= 0)
            {
                set(v);
                return null;
            }
        }

        return $"\"{text}\" não é um número válido.";
    }

    private static string? Color(string text, Action<string> set)
    {
        if (TextSearch.Normalize(text) is "-" or "sem cor" or "nenhuma")
        {
            set(string.Empty);
            return null;
        }

        string t = text.Trim();
        if (!t.StartsWith('#'))
        {
            t = "#" + t;
        }

        if (Rgb.TryParse(t, out Rgb color))
        {
            set(color.Hex);
            return null;
        }

        return $"\"{text}\" não é uma cor (use o código, por exemplo #F2C14E).";
    }

    private static string? Months(string text, Action<string> set)
    {
        if (TextSearch.Normalize(text) is "-" or "nenhuma" or "sem")
        {
            set(string.Empty);
            return null;
        }

        if (MonthSet.Parse(text).IsEmpty)
        {
            return $"\"{text}\" não tem meses reconhecíveis (por exemplo: ago-out, nov a fev, ano todo).";
        }

        set(text.Trim());
        return null;
    }

    private static string? SetBiomes(Species s, string text)
    {
        if (TextSearch.Normalize(text) is "-" or "—" or "nenhum")
        {
            s.Biomes = Biome.Nenhum;
            return null;
        }

        var biomes = Biome.Nenhum;
        foreach (string part in text.Split(new[] { ',', ';', '/' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            Biome? match = Labels.Biomes.Cast<Biome?>().FirstOrDefault(b => TextSearch.Normalize(Labels.Of(b!.Value)) == TextSearch.Normalize(part));
            if (match is null)
            {
                return $"bioma \"{part}\" desconhecido (use: {string.Join(", ", Labels.Biomes.Select(Labels.Of))}).";
            }

            biomes |= match.Value;
        }

        s.Biomes = biomes;
        return null;
    }
}
