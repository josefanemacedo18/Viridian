using Viridian.Core.Model;

namespace Viridian.Core.Detailing;

/// <summary>Momentos da simulação de crescimento.</summary>
public enum GrowthStage
{
    Plantio = 0,
    TresAnos = 1,
    SeisAnos = 2,
    Maturidade = 3,
}

/// <summary>
/// Estimativa de porte ao longo do tempo, pela taxa de crescimento da espécie. São curvas médias de referência
/// (não substituem a experiência local): a copa e a altura crescem de forma desacelerada até o porte adulto.
/// </summary>
public static class GrowthModel
{
    public static readonly IReadOnlyList<GrowthStage> Stages = Enum.GetValues<GrowthStage>();

    public static string Name(GrowthStage stage) => stage switch
    {
        GrowthStage.Plantio => "Plantio",
        GrowthStage.TresAnos => "3 anos",
        GrowthStage.SeisAnos => "6 anos",
        _ => "Maturidade",
    };

    /// <summary>Fração do porte adulto em cada momento.</summary>
    public static double Fraction(GrowthRate rate, GrowthStage stage) => (rate, stage) switch
    {
        (_, GrowthStage.Maturidade) => 1.0,
        (_, GrowthStage.Plantio) => 0.0,
        (GrowthRate.Lento, GrowthStage.TresAnos) => 0.30,
        (GrowthRate.Lento, GrowthStage.SeisAnos) => 0.55,
        (GrowthRate.Rapido, GrowthStage.TresAnos) => 0.65,
        (GrowthRate.Rapido, GrowthStage.SeisAnos) => 0.95,
        (_, GrowthStage.TresAnos) => 0.45,
        _ => 0.75,
    };

    /// <summary>Copa e altura (m) no momento indicado.</summary>
    public static (double CrownM, double HeightM) Size(Species species, GrowthStage stage)
    {
        double adultCrown = Math.Max(0.05, species.CrownDiameterM);
        double adultHeight = Math.Max(0.05, species.AdultHeightM > 0 ? species.AdultHeightM : adultCrown);
        double plantingHeight = species.PlantingHeightM > 0 ? Math.Min(species.PlantingHeightM, adultHeight) : Math.Min(adultHeight, Seedling(species));
        double plantingCrown = Math.Min(adultCrown, Math.Max(0.1, adultCrown * PlantingCrownRatio(species.Category)));
        if (stage == GrowthStage.Plantio)
        {
            return (plantingCrown, plantingHeight);
        }

        double f = Fraction(species.Growth, stage);
        return (plantingCrown + (adultCrown - plantingCrown) * f, plantingHeight + (adultHeight - plantingHeight) * f);
    }

    private static double Seedling(Species s) => s.Category switch
    {
        PlantCategory.Arvore => 2.0,
        PlantCategory.Palmeira => 1.5,
        PlantCategory.Arbusto or PlantCategory.Trepadeira => 0.6,
        _ => 0.2,
    };

    /// <summary>Copa da muda em relação à copa adulta: forrações e gramados já chegam perto do porte final.</summary>
    private static double PlantingCrownRatio(PlantCategory category) => category switch
    {
        PlantCategory.Arvore => 0.12,
        PlantCategory.Palmeira => 0.25,
        PlantCategory.Arbusto or PlantCategory.Trepadeira => 0.35,
        _ => 0.6,
    };
}
