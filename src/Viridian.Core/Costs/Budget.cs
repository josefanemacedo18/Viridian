using Viridian.Core.Model;
using Viridian.Core.Reports;

namespace Viridian.Core.Costs;

/// <summary>Origem do preço de um item do orçamento.</summary>
public enum PriceSource
{
    /// <summary>Sem preço: precisa ser vinculado ou cadastrado.</summary>
    SemPreco = 0,

    /// <summary>Tabela de preços importada (pelo código vinculado à espécie).</summary>
    Tabela = 1,

    /// <summary>Preço unitário do cadastro da espécie.</summary>
    Cadastro = 2,
}

/// <summary>Linha do orçamento de paisagismo.</summary>
public sealed class BudgetRow
{
    /// <summary>Numeração do item (1.1, 1.2, 2.1...).</summary>
    public string Item { get; set; } = string.Empty;

    /// <summary>Grupo: fornecimento de mudas ou serviços de plantio.</summary>
    public string Group { get; set; } = string.Empty;

    public string SpeciesId { get; set; } = string.Empty;

    public string SpeciesCode { get; set; } = string.Empty;

    /// <summary>Código do item na tabela de preços (vazio quando o preço vem do cadastro).</summary>
    public string PriceCode { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Unit { get; set; } = string.Empty;

    public decimal Quantity { get; set; }

    /// <summary>Custo unitário sem BDI.</summary>
    public decimal UnitCost { get; set; }

    /// <summary>Preço unitário com BDI.</summary>
    public decimal UnitPrice { get; set; }

    public decimal Total => Math.Round(Quantity * UnitPrice, 2, MidpointRounding.AwayFromZero);

    public PriceSource Source { get; set; }

    public string SourceName => Source switch
    {
        PriceSource.Tabela => "Tabela",
        PriceSource.Cadastro => "Cadastro",
        _ => "Sem preço",
    };

    /// <summary>Observação: unidade divergente, código não encontrado etc.</summary>
    public string Note { get; set; } = string.Empty;

    /// <summary>Código exibido: o da tabela de preços ou, sem ele, o da espécie.</summary>
    public string DisplayCode => PriceCode.Length > 0 ? PriceCode : SpeciesCode;

    /// <summary>Indica se o item é um serviço (e não o fornecimento da muda).</summary>
    public bool IsService { get; set; }
}

/// <summary>Orçamento: linhas, BDI e totais.</summary>
public sealed class Budget
{
    public IReadOnlyList<BudgetRow> Rows { get; init; } = Array.Empty<BudgetRow>();

    public double BdiPercent { get; init; }

    public string TableName { get; init; } = string.Empty;

    public decimal SupplyTotal => Rows.Where(r => !r.IsService).Sum(r => r.Total);

    public decimal ServiceTotal => Rows.Where(r => r.IsService).Sum(r => r.Total);

    public decimal Total => SupplyTotal + ServiceTotal;

    public int MissingPrices => Rows.Count(r => r.Source == PriceSource.SemPreco);
}

/// <summary>
/// Monta o orçamento a partir da lista de aquisição: o fornecimento de cada espécie usa o código de insumo
/// vinculado (ou o preço do cadastro) e o plantio usa o código de serviço vinculado. O BDI é aplicado
/// sobre o custo unitário.
/// </summary>
public static class BudgetBuilder
{
    public const string SupplyGroup = "Fornecimento de mudas e insumos vegetais";
    public const string ServiceGroup = "Serviços de plantio";

    public static Budget Build(IReadOnlyList<AcquisitionRow> acquisition, Func<string, Species?> lookup, PriceTable? table, double bdiPercent)
    {
        bdiPercent = double.IsFinite(bdiPercent) ? Math.Clamp(bdiPercent, 0, 200) : 0;
        decimal factor = 1m + (decimal)bdiPercent / 100m;
        var supply = new List<BudgetRow>();
        var services = new List<BudgetRow>();

        foreach (AcquisitionRow a in acquisition)
        {
            Species? species = string.IsNullOrWhiteSpace(a.SpeciesId) ? null : lookup(a.SpeciesId);
            string name = string.IsNullOrWhiteSpace(a.ScientificName) ? a.PopularName : $"{a.PopularName} ({a.ScientificName})";

            var row = new BudgetRow
            {
                Group = SupplyGroup,
                SpeciesId = a.SpeciesId,
                SpeciesCode = a.Code,
                Description = "Muda de " + name,
                Unit = a.Unit,
                Quantity = a.PurchaseQuantity,
            };
            PriceItem? item = table?.Find(species?.PriceItemCode);
            if (item is not null)
            {
                Apply(row, item, factor, a.Unit);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(species?.PriceItemCode))
                {
                    row.Note = table is null
                        ? $"Código {species!.PriceItemCode} vinculado, mas nenhuma tabela de preços foi importada."
                        : $"Código {species!.PriceItemCode} não encontrado na tabela {table.Name}.";
                }

                if (a.UnitPrice > 0)
                {
                    row.UnitCost = a.UnitPrice;
                    row.UnitPrice = Math.Round(a.UnitPrice * factor, 2, MidpointRounding.AwayFromZero);
                    row.Source = PriceSource.Cadastro;
                }
            }

            supply.Add(row);

            // O serviço de plantio vale para a quantidade de projeto (a reserva é só para reposição).
            if (!string.IsNullOrWhiteSpace(species?.ServiceItemCode) && a.Quantity > 0)
            {
                var service = new BudgetRow
                {
                    Group = ServiceGroup,
                    SpeciesId = a.SpeciesId,
                    SpeciesCode = a.Code,
                    Description = "Plantio de " + name,
                    Unit = a.Unit,
                    Quantity = a.Quantity,
                    IsService = true,
                };
                PriceItem? serviceItem = table?.Find(species!.ServiceItemCode);
                if (serviceItem is not null)
                {
                    Apply(service, serviceItem, factor, a.Unit);
                }
                else
                {
                    service.PriceCode = species!.ServiceItemCode;
                    service.Note = table is null
                        ? "Importe a tabela de preços para valorizar o serviço."
                        : $"Código {species.ServiceItemCode} não encontrado na tabela {table.Name}.";
                }

                services.Add(service);
            }
        }

        Number(supply, 1);
        Number(services, 2);
        return new Budget
        {
            Rows = supply.Concat(services).ToList(),
            BdiPercent = bdiPercent,
            TableName = table?.Name ?? string.Empty,
        };
    }

    private static void Apply(BudgetRow row, PriceItem item, decimal factor, string projectUnit)
    {
        row.PriceCode = item.Code;
        row.Description = item.Description;
        row.UnitCost = item.Price;
        row.UnitPrice = Math.Round(item.Price * factor, 2, MidpointRounding.AwayFromZero);
        row.Source = PriceSource.Tabela;
        if (!string.IsNullOrWhiteSpace(item.Unit))
        {
            if (!SameUnit(item.Unit, projectUnit))
            {
                row.Note = $"Unidade da tabela ({item.Unit}) diferente da unidade do projeto ({projectUnit}): confira a quantidade.";
            }

            row.Unit = item.Unit;
        }
    }

    private static void Number(List<BudgetRow> rows, int group)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            rows[i].Item = $"{group}.{i + 1}";
        }
    }

    /// <summary>Compara unidades com grafias diferentes ("un", "und", "muda", "m2", "m²"...).</summary>
    public static bool SameUnit(string? a, string? b) => UnitKey(a) == UnitKey(b);

    private static string UnitKey(string? unit)
    {
        string u = PriceTable.Normalize(unit).Replace(" ", string.Empty);
        return u switch
        {
            "un" or "und" or "unid" or "unidade" or "muda" or "mudas" or "pc" or "peca" or "unidades" => "un",
            "m2" or "m" when unit?.Contains('²') == true => "m2",
            "m2" or "metroquadrado" or "mq" => "m2",
            _ => u,
        };
    }
}

/// <summary>Configurações do orçamento.</summary>
public sealed class CostSettings
{
    /// <summary>BDI (benefícios e despesas indiretas), em porcentagem sobre o custo.</summary>
    public double BdiPercent { get; set; } = 25;

    /// <summary>Referência da tabela de preços (base, local, data), exibida no orçamento.</summary>
    public string PriceReference { get; set; } = string.Empty;

    public void Normalize()
    {
        BdiPercent = double.IsFinite(BdiPercent) ? Math.Clamp(BdiPercent, 0, 200) : 25;
        PriceReference ??= string.Empty;
    }
}
