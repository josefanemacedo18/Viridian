using Viridian.Core.Colors;
using Viridian.Core.Model;
using Viridian.Core.Model3D.Profiles;
using Viridian.Core.Model3D.Seasonality;

namespace Viridian.Core.Model3D.Generation;

/// <summary>Escolhas da maquete que valem para todos os exemplares.</summary>
public sealed class PlantOptions
{
    public QualityLevel Quality { get; init; } = QualityLevel.Intermediaria;

    /// <summary>Mês de referência (1 a 12) para folhagem, flores e frutos; zero = sem sazonalidade.</summary>
    public int Month { get; init; }

    public bool SouthernHemisphere { get; init; } = true;

    public bool Flowers { get; init; } = true;

    public bool Fruits { get; init; }

    public bool Variation { get; init; } = true;

    public bool SolidOnly { get; init; }
}

/// <summary>O que um exemplar mostra no mês: estado da folhagem, flores e frutos, e de onde veio a informação.</summary>
public sealed record PlantSeason(FoliageState Foliage, bool Flowers, FruitTraits? Fruits, SeasonSource FloweringSource);

/// <summary>
/// Liga a ficha da espécie, o perfil 3D e as escolhas da maquete ao pedido do gerador, e define as cores das partes.
/// Sem sazonalidade, a folhagem é cheia e as flores aparecem nas espécies de floração vistosa (como antes).
/// </summary>
public static class PlantPlanner
{
    public static PlantSeason Season(Species species, Plant3DProfile profile, PlantOptions options)
    {
        ArgumentNullException.ThrowIfNull(species);
        ArgumentNullException.ThrowIfNull(options);
        bool seasonal = options.Month is >= 1 and <= 12;
        bool south = options.SouthernHemisphere;
        FoliageState foliage = seasonal ? SeasonalityResolver.Foliage(species, options.Month, south) : FoliageState.Cheia;
        SeasonInfo flowering = SeasonalityResolver.Flowering(species);
        bool showy = !string.IsNullOrWhiteSpace(species.FlowerColor);
        bool flowers = options.Flowers && showy && profile.Flowers is not null && (!seasonal || SeasonalityResolver.InMonth(flowering, options.Month, south));
        FruitTraits? fruits = null;
        if (options.Fruits)
        {
            FruitTraits? fruit = SeasonalityResolver.Fruit(species, profile);
            if (fruit is not null && (!seasonal || SeasonalityResolver.InMonth(fruit.Season, options.Month, south)))
            {
                fruits = fruit;
            }
        }

        return new PlantSeason(foliage, flowers, fruits, flowering.Source);
    }

    public static PlantRequest Request(Species species, Plant3DProfile profile, double crownM, double heightM, long seed, PlantOptions options)
    {
        PlantSeason season = Season(species, profile, options);
        return new PlantRequest
        {
            Profile = profile,
            HeightM = heightM,
            CrownDiameterM = crownM,
            Quality = options.Quality,
            Seed = seed,
            Foliage = season.Foliage,
            Flowers = season.Flowers,
            Fruits = season.Fruits,
            SolidOnly = options.SolidOnly,
            Variation = options.Variation,
        };
    }

    /// <summary>Cores das partes: folhagem da espécie, tronco do perfil (ou o padrão da maquete), flor e fruto.</summary>
    public static PlantColors Colors(Species species, Plant3DProfile profile, ColorSettings colors, string defaultTrunk, FruitTraits? fruits)
    {
        ArgumentNullException.ThrowIfNull(species);
        Rgb foliage = VegetationPalette.Foliage(species, colors);
        Rgb trunk = Rgb.TryParse(profile.Trunk?.Color, out Rgb own) ? own : Rgb.Parse(defaultTrunk, PlantColors.DefaultTrunk);
        Rgb flower = Rgb.Parse(species.FlowerColor, Rgb.White);
        Rgb fruit = fruits?.Color ?? new Rgb(0xB0, 0x30, 0x20);
        return new PlantColors(foliage, trunk, flower, fruit);
    }
}
