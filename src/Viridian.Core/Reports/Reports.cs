using Viridian.Core.Geometry;
using Viridian.Core.Massing;
using Viridian.Core.Model;

namespace Viridian.Core.Reports;

/// <summary>Dados de um exemplar de vegetação lido do projeto.</summary>
public sealed class PlantRecord
{
    public long ElementId { get; set; }

    public string UniqueId { get; set; } = string.Empty;

    public string SpeciesId { get; set; } = string.Empty;

    public string SpeciesCode { get; set; } = string.Empty;

    public string PopularName { get; set; } = string.Empty;

    public string ScientificName { get; set; } = string.Empty;

    public string CategoryName { get; set; } = string.Empty;

    public string ProjectCode { get; set; } = string.Empty;

    public PlantStatus Status { get; set; }

    /// <summary>Código do maciço ao qual o exemplar pertence (vazio para exemplares isolados).</summary>
    public string MassingId { get; set; } = string.Empty;

    /// <summary>Posição em planta, em metros.</summary>
    public Point2 Location { get; set; }

    public double CrownDiameterM { get; set; }

    public double RecommendedSpacingM { get; set; }

    public string SupplyUnit { get; set; } = string.Empty;

    public string FamilyName { get; set; } = string.Empty;

    public string TypeName { get; set; } = string.Empty;

    public string LevelName { get; set; } = string.Empty;

    /// <summary>Verdadeiro quando o tipo está vinculado a uma espécie do catálogo Viridian.</summary>
    public bool IsRegistered => !string.IsNullOrWhiteSpace(SpeciesId);

    public bool BelongsToMassing => !string.IsNullOrWhiteSpace(MassingId);

    /// <summary>Chave de agrupamento por espécie (ou por família/tipo quando não cadastrada).</summary>
    public string GroupKey => IsRegistered ? "sp:" + SpeciesId : "fam:" + FamilyName + "|" + TypeName;
}

/// <summary>Dados de um maciço vegetal lido do projeto.</summary>
public sealed class MassingInfo
{
    public long ElementId { get; set; }

    public long ViewId { get; set; }

    public string ViewName { get; set; } = string.Empty;

    public MassingRecord Record { get; set; } = new();

    /// <summary>Limite atual da região, em metros (nulo quando não pôde ser lido).</summary>
    public Polygon2? Boundary { get; set; }

    /// <summary>Quantidade de símbolos vinculados encontrados no projeto.</summary>
    public int LinkedSymbolCount { get; set; }
}

public sealed class SpeciesReportRow
{
    /// <summary>Identificador da espécie no catálogo (vazio para famílias não cadastradas).</summary>
    public string SpeciesId { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string PopularName { get; set; } = string.Empty;

    public string ScientificName { get; set; } = string.Empty;

    public string CategoryName { get; set; } = string.Empty;

    public int New { get; set; }

    public int Existing { get; set; }

    public int Transplanted { get; set; }

    public int Removed { get; set; }

    public int InMassings { get; set; }

    /// <summary>Área dos maciços da espécie, em m².</summary>
    public double MassingAreaM2 { get; set; }

    /// <summary>Total no projeto: novos + existentes + transplantados + maciços (removidos não contam).</summary>
    public int Total => New + Existing + Transplanted + InMassings;

    public string Unit { get; set; } = string.Empty;
}

public sealed class MassingReportRow
{
    public string Id { get; set; } = string.Empty;

    public string SpeciesCode { get; set; } = string.Empty;

    public string PopularName { get; set; } = string.Empty;

    public string ScientificName { get; set; } = string.Empty;

    public double AreaM2 { get; set; }

    public double DensityPerM2 { get; set; }

    public double SpacingM { get; set; }

    public int Quantity { get; set; }

    public string Unit { get; set; } = string.Empty;

    public string Mode { get; set; } = string.Empty;

    public double ReservePercent { get; set; }

    public string ViewName { get; set; } = string.Empty;
}

public sealed class AcquisitionRow
{
    /// <summary>Identificador da espécie no catálogo (vazio para famílias não cadastradas).</summary>
    public string SpeciesId { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string PopularName { get; set; } = string.Empty;

    public string ScientificName { get; set; } = string.Empty;

    /// <summary>Quantidade de projeto (exemplares novos e maciços).</summary>
    public int Quantity { get; set; }

    /// <summary>Unidades adicionais para reposição.</summary>
    public int Reserve { get; set; }

    public int PurchaseQuantity => Quantity + Reserve;

    public string Unit { get; set; } = string.Empty;

    public decimal UnitPrice { get; set; }

    public decimal Total => PurchaseQuantity * UnitPrice;
}

/// <summary>Unidades de fornecimento.</summary>
public static class SupplyUnits
{
    public static readonly IReadOnlyList<string> Common = new[] { "muda", "unidade", "m²", "placa", "bandeja", "vaso", "saco", "caixa" };

    /// <summary>Espécies vendidas por área (grama em m², por exemplo) são quantificadas pela área do maciço.</summary>
    public static bool IsAreaBased(string? unit)
    {
        string value = (unit ?? string.Empty).Trim().ToLowerInvariant();
        return value is "m²" or "m2";
    }
}

/// <summary>Monta os relatórios de quantitativos a partir dos dados do projeto.</summary>
public static class ReportBuilder
{
    /// <summary>Maciços distintos: o mesmo código em vistas diferentes representa um único maciço.</summary>
    public static IReadOnlyList<MassingInfo> DistinctMassings(IEnumerable<MassingInfo> massings) =>
        massings
            .Where(m => !string.IsNullOrWhiteSpace(m.Record.Id))
            .OrderBy(m => m.ElementId)
            .GroupBy(m => m.Record.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

    public static IReadOnlyList<SpeciesReportRow> BuildSpecies(IEnumerable<PlantRecord> plants, IEnumerable<MassingInfo> massings)
    {
        IReadOnlyList<MassingInfo> distinct = DistinctMassings(massings);
        var massingIds = new HashSet<string>(distinct.Select(m => m.Record.Id.Trim()), StringComparer.OrdinalIgnoreCase);
        var rows = new Dictionary<string, SpeciesReportRow>(StringComparer.OrdinalIgnoreCase);

        foreach (PlantRecord plant in plants)
        {
            // Símbolos de um maciço existente são contados pelo registro do maciço.
            if (plant.BelongsToMassing && massingIds.Contains(plant.MassingId.Trim()))
            {
                continue;
            }

            if (!rows.TryGetValue(plant.GroupKey, out SpeciesReportRow? row))
            {
                row = new SpeciesReportRow
                {
                    SpeciesId = plant.SpeciesId,
                    Code = plant.IsRegistered ? plant.SpeciesCode : "—",
                    PopularName = plant.IsRegistered ? plant.PopularName : $"{plant.FamilyName} : {plant.TypeName}",
                    ScientificName = plant.IsRegistered ? plant.ScientificName : "(família não cadastrada)",
                    CategoryName = plant.CategoryName,
                    Unit = string.IsNullOrWhiteSpace(plant.SupplyUnit) ? "muda" : plant.SupplyUnit,
                };
                rows[plant.GroupKey] = row;
            }

            switch (plant.Status)
            {
                case PlantStatus.Existente:
                    row.Existing++;
                    break;
                case PlantStatus.Transplantado:
                    row.Transplanted++;
                    break;
                case PlantStatus.Removido:
                    row.Removed++;
                    break;
                default:
                    row.New++;
                    break;
            }
        }

        foreach (MassingInfo massing in distinct)
        {
            MassingRecord record = massing.Record;
            string key = string.IsNullOrWhiteSpace(record.SpeciesId) ? "mc:" + record.SpeciesCode : "sp:" + record.SpeciesId;
            if (!rows.TryGetValue(key, out SpeciesReportRow? row))
            {
                row = new SpeciesReportRow
                {
                    SpeciesId = record.SpeciesId,
                    Code = record.SpeciesCode,
                    PopularName = record.PopularName,
                    ScientificName = record.ScientificName,
                    Unit = string.IsNullOrWhiteSpace(record.SupplyUnit) ? "muda" : record.SupplyUnit,
                };
                rows[key] = row;
            }

            row.InMassings += Math.Max(0, record.Quantity);
            row.MassingAreaM2 += Math.Max(0, record.AreaM2);
        }

        return rows.Values
            .OrderBy(r => r.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.PopularName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<MassingReportRow> BuildMassings(IEnumerable<MassingInfo> massings) =>
        DistinctMassings(massings)
            .Select(m => new MassingReportRow
            {
                Id = m.Record.Id,
                SpeciesCode = m.Record.SpeciesCode,
                PopularName = m.Record.PopularName,
                ScientificName = m.Record.ScientificName,
                AreaM2 = m.Record.AreaM2,
                DensityPerM2 = SupplyUnits.IsAreaBased(m.Record.SupplyUnit) ? 0 : MassingCalculator.EffectiveDensity(m.Record),
                SpacingM = m.Record.SpacingM,
                Quantity = m.Record.Quantity,
                Unit = m.Record.SupplyUnit,
                Mode = DensityRules.DisplayName(m.Record.Mode),
                ReservePercent = m.Record.ReservePercent,
                ViewName = m.ViewName,
            })
            .OrderBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Lista de aquisição: exemplares novos e maciços, com reserva de reposição e preço cadastrado.
    /// </summary>
    /// <param name="priceLookup">Devolve o preço unitário cadastrado para o identificador da espécie.</param>
    /// <param name="individualReservePercent">Reserva aplicada aos exemplares isolados.</param>
    public static IReadOnlyList<AcquisitionRow> BuildAcquisition(
        IEnumerable<PlantRecord> plants,
        IEnumerable<MassingInfo> massings,
        Func<string, decimal> priceLookup,
        double individualReservePercent)
    {
        IReadOnlyList<MassingInfo> distinct = DistinctMassings(massings);
        var massingIds = new HashSet<string>(distinct.Select(m => m.Record.Id.Trim()), StringComparer.OrdinalIgnoreCase);
        var rows = new Dictionary<string, AcquisitionRow>(StringComparer.OrdinalIgnoreCase);
        var individual = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var speciesIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        AcquisitionRow Row(string key, string speciesId, string code, string popular, string scientific, string unit)
        {
            if (!rows.TryGetValue(key, out AcquisitionRow? row))
            {
                row = new AcquisitionRow
                {
                    SpeciesId = speciesId,
                    Code = code,
                    PopularName = popular,
                    ScientificName = scientific,
                    Unit = string.IsNullOrWhiteSpace(unit) ? "muda" : unit,
                };
                rows[key] = row;
                speciesIds[key] = speciesId;
            }

            return row;
        }

        foreach (PlantRecord plant in plants)
        {
            if (plant.Status != PlantStatus.Novo)
            {
                continue;
            }

            if (plant.BelongsToMassing && massingIds.Contains(plant.MassingId.Trim()))
            {
                continue;
            }

            Row(
                plant.GroupKey,
                plant.SpeciesId,
                plant.IsRegistered ? plant.SpeciesCode : "—",
                plant.IsRegistered ? plant.PopularName : $"{plant.FamilyName} : {plant.TypeName}",
                plant.IsRegistered ? plant.ScientificName : "(família não cadastrada)",
                plant.SupplyUnit);
            individual[plant.GroupKey] = individual.GetValueOrDefault(plant.GroupKey) + 1;
        }

        foreach ((string key, int count) in individual)
        {
            AcquisitionRow row = rows[key];
            row.Quantity += count;
            row.Reserve += MassingCalculator.PurchaseQuantity(count, individualReservePercent) - count;
        }

        foreach (MassingInfo massing in distinct)
        {
            MassingRecord record = massing.Record;
            int quantity = Math.Max(0, record.Quantity);
            if (quantity == 0)
            {
                continue;
            }

            string key = string.IsNullOrWhiteSpace(record.SpeciesId) ? "mc:" + record.SpeciesCode : "sp:" + record.SpeciesId;
            AcquisitionRow row = Row(key, record.SpeciesId, record.SpeciesCode, record.PopularName, record.ScientificName, record.SupplyUnit);
            row.Quantity += quantity;
            row.Reserve += MassingCalculator.PurchaseQuantity(quantity, record.ReservePercent) - quantity;
        }

        foreach ((string key, AcquisitionRow row) in rows)
        {
            string speciesId = speciesIds[key];
            row.UnitPrice = string.IsNullOrWhiteSpace(speciesId) ? 0m : Math.Max(0m, priceLookup(speciesId));
        }

        return rows.Values
            .OrderBy(r => r.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.PopularName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
