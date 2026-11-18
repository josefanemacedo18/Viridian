using Viridian.Core.Distribution;

namespace Viridian.Core.Massing;

/// <summary>Modo de representação gráfica de um maciço vegetal.</summary>
public enum MassingMode
{
    /// <summary>Somente hachura; a quantidade é calculada pela área e densidade.</summary>
    Hachura = 0,

    /// <summary>Somente símbolos; cada símbolo é uma unidade de plantio.</summary>
    Simbolos = 1,

    /// <summary>Hachura de fundo com símbolos distribuídos.</summary>
    Combinado = 2,
}

public enum DensityClass
{
    Baixa = 0,
    Media = 1,
    Alta = 2,
    MuitoAlta = 3,
}

/// <summary>Tipo de elemento usado para os símbolos de um maciço.</summary>
public enum MassingSymbolKind
{
    /// <summary>Família Planting: cada símbolo é um exemplar BIM.</summary>
    Planting = 0,

    /// <summary>Componente de detalhe personalizado: apenas gráfico, a quantidade fica no registro do maciço.</summary>
    DetailItem = 1,
}

/// <summary>Limites de densidade (un/m²) que determinam a representação sugerida de um maciço.</summary>
public sealed class DensityRules
{
    public double LowMax { get; set; } = 1.0;

    public double MediumMax { get; set; } = 4.0;

    public double HighMax { get; set; } = 16.0;

    public DensityClass Classify(double densityPerM2)
    {
        if (densityPerM2 <= LowMax)
        {
            return DensityClass.Baixa;
        }

        if (densityPerM2 <= MediumMax)
        {
            return DensityClass.Media;
        }

        return densityPerM2 <= HighMax ? DensityClass.Alta : DensityClass.MuitoAlta;
    }

    public MassingMode SuggestMode(double densityPerM2) => Classify(densityPerM2) switch
    {
        DensityClass.Baixa => MassingMode.Simbolos,
        DensityClass.Media => MassingMode.Combinado,
        _ => MassingMode.Hachura,
    };

    /// <summary>
    /// Modo inicial de um maciço: árvores e palmeiras viram exemplares (bosque); as demais espécies, hachura colorida
    /// com o traçado próprio da espécie, sem símbolos de planta no meio (o usuário pode escolher outro modo).
    /// </summary>
    public MassingMode DefaultMode(Model.Species species, double densityPerM2) =>
        species.Category is Model.PlantCategory.Arvore or Model.PlantCategory.Palmeira ? SuggestMode(densityPerM2) : MassingMode.Hachura;

    /// <summary>Densidade muito alta pede hachura simplificada, sem tentar sugerir cada muda.</summary>
    public bool UseSimplifiedHatch(double densityPerM2) => Classify(densityPerM2) == DensityClass.MuitoAlta;

    public void Normalize()
    {
        if (!(LowMax > 0))
        {
            LowMax = 1.0;
        }

        if (!(MediumMax > LowMax))
        {
            MediumMax = LowMax * 4;
        }

        if (!(HighMax > MediumMax))
        {
            HighMax = MediumMax * 4;
        }
    }

    public static string DisplayName(DensityClass densityClass) => densityClass switch
    {
        DensityClass.Baixa => "Baixa",
        DensityClass.Media => "Média",
        DensityClass.Alta => "Alta",
        DensityClass.MuitoAlta => "Muito alta",
        _ => densityClass.ToString(),
    };

    public static string DisplayName(MassingMode mode) => mode switch
    {
        MassingMode.Hachura => "Hachura",
        MassingMode.Simbolos => "Símbolos",
        MassingMode.Combinado => "Combinado (hachura + símbolos)",
        _ => mode.ToString(),
    };
}

/// <summary>Registro de um maciço vegetal. É gravado junto à região preenchida no projeto.</summary>
public sealed class MassingRecord
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Código do maciço, por exemplo MC-001.</summary>
    public string Id { get; set; } = string.Empty;

    public string SpeciesId { get; set; } = string.Empty;

    public string SpeciesCode { get; set; } = string.Empty;

    public string PopularName { get; set; } = string.Empty;

    public string ScientificName { get; set; } = string.Empty;

    /// <summary>Área líquida da região em m² (atualizada a cada recálculo).</summary>
    public double AreaM2 { get; set; }

    public double SpacingM { get; set; }

    public double DensityPerM2 { get; set; }

    /// <summary>Quantidade de unidades de plantio do maciço.</summary>
    public int Quantity { get; set; }

    public string SupplyUnit { get; set; } = "muda";

    public DistributionPattern Pattern { get; set; } = DistributionPattern.Triangular;

    /// <summary>Reserva de reposição, em porcentagem.</summary>
    public double ReservePercent { get; set; }

    public MassingMode Mode { get; set; } = MassingMode.Hachura;

    public MassingSymbolKind SymbolKind { get; set; } = MassingSymbolKind.Planting;

    public string HatchPatternKey { get; set; } = string.Empty;

    public double EdgeMarginM { get; set; }

    public double JitterPercent { get; set; }

    public double AngleDeg { get; set; }

    public int MaxCount { get; set; }

    /// <summary>Semente persistente da distribuição natural.</summary>
    public long Seed { get; set; } = 1;

    public bool RandomRotation { get; set; } = true;

    public string Notes { get; set; } = string.Empty;

    public MassingRecord Clone() => (MassingRecord)MemberwiseClone();
}

/// <summary>Cálculos de densidade, espaçamento e quantidade de maciços.</summary>
public static class MassingCalculator
{
    /// <summary>Fração de preenchimento de uma distribuição natural em relação à malha quadrada de mesmo espaçamento.</summary>
    private const double NaturalFactor = 0.70;

    public static double DensityFromSpacing(double spacingM, DistributionPattern pattern)
    {
        if (!(spacingM > 0))
        {
            return 0;
        }

        double square = 1.0 / (spacingM * spacingM);
        return pattern switch
        {
            DistributionPattern.Retangular => square,
            DistributionPattern.Natural => square * NaturalFactor,
            _ => square * 2.0 / Math.Sqrt(3.0),
        };
    }

    public static double SpacingFromDensity(double densityPerM2, DistributionPattern pattern)
    {
        if (!(densityPerM2 > 0))
        {
            return 0;
        }

        return pattern switch
        {
            DistributionPattern.Retangular => Math.Sqrt(1.0 / densityPerM2),
            DistributionPattern.Natural => Math.Sqrt(NaturalFactor / densityPerM2),
            _ => Math.Sqrt(2.0 / (Math.Sqrt(3.0) * densityPerM2)),
        };
    }

    /// <summary>Quantidade de unidades para uma área e densidade (arredondada para cima).</summary>
    public static int Quantity(double areaM2, double densityPerM2)
    {
        if (!(areaM2 > 0) || !(densityPerM2 > 0))
        {
            return 0;
        }

        double value = Math.Ceiling(areaM2 * densityPerM2 - 1e-9);
        return value >= int.MaxValue ? int.MaxValue : (int)value;
    }

    /// <summary>Quantidade de compra, acrescida da reserva de reposição.</summary>
    public static int PurchaseQuantity(int quantity, double reservePercent)
    {
        if (quantity <= 0)
        {
            return 0;
        }

        double factor = 1.0 + Math.Max(0, reservePercent) / 100.0;
        double value = Math.Ceiling(quantity * factor - 1e-9);
        return value >= int.MaxValue ? int.MaxValue : (int)value;
    }

    /// <summary>Densidade efetiva de um registro: a informada ou a derivada do espaçamento.</summary>
    public static double EffectiveDensity(MassingRecord record) =>
        record.DensityPerM2 > 0 ? record.DensityPerM2 : DensityFromSpacing(record.SpacingM, record.Pattern);

    /// <summary>Próximo código de maciço livre (MC-001, MC-002...).</summary>
    public static string NextId(IEnumerable<string> existingIds, string prefix = "MC")
    {
        int max = 0;
        foreach (string id in existingIds)
        {
            if (Coding.CodeFormat.TryParseSequential(id, prefix, out int number) && number > max)
            {
                max = number;
            }
        }

        return Coding.CodeFormat.Sequential(prefix, max + 1, 3);
    }
}
