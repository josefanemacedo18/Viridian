using Viridian.Core.Colors;
using Viridian.Core.Detailing;
using Viridian.Core.Geometry;
using Viridian.Core.Model;
using Viridian.Core.Model3D.Generation;
using Viridian.Core.Model3D.Profiles;

namespace Viridian.Core.Model3D.Illustration;

/// <summary>Uma fase da planta nos cartões: código (valor do parâmetro "VIR Fase 3D"), nome e os dois planos.</summary>
public sealed record CardSet(int Code, string Label, CardRegions PlaneA, CardRegions PlaneB, CardArt ArtA, CardArt ArtB);

/// <summary>Cartões ilustrados de uma espécie e variante: uma fase por conjunto, com assinatura para o cache da família.</summary>
public sealed record PlantCards(IReadOnlyList<CardSet> Sets, string Hash)
{
    public int Edges => Sets.Sum(s => s.PlaneA.Edges + s.PlaneB.Edges);

    public int Regions => Sets.Sum(s => s.PlaneA.Regions.Count + s.PlaneB.Regions.Count);
}

/// <summary>
/// Vegetação ilustrada em cartões (dois planos verticais cruzados, quatro faces), no estilo dos símbolos: uma arte
/// por fase (plantio, 3 anos, 6 anos, adulta e, nas decíduas, adulta sem folhas), escolhida em cada exemplar pelo
/// parâmetro "VIR Fase 3D". Piloto: oito espécies de formas bem diferentes.
/// </summary>
public static class PlantCardsPlanner
{
    /// <summary>Versão dos cartões: aumente quando o desenho mudar (regera as famílias).</summary>
    public const int Version = 1;

    /// <summary>Parâmetros do traço (entram na assinatura: mudar o desenho regera as famílias).</summary>
    public const string Style = "dente12-massa2.2-grade420-dp0.95-escala-geom0.4-folhalonga";

    /// <summary>Fase mostrada quando o exemplar não escolhe outra.</summary>
    public const int DefaultStage = 4;

    public static readonly IReadOnlyList<string> Pilot = new[]
    {
        "Handroanthus chrysotrichus", "Delonix regia", "Syagrus romanzoffiana", "Bismarckia nobilis",
        "Araucaria angustifolia", "Agave attenuata", "Strelitzia reginae", "Dracaena marginata",
    };

    public static bool IsPilot(Species species) =>
        species is not null && Pilot.Any(n => string.Equals(n, species.ScientificName?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static PlantCards Build(Species species, int variant, IReadOnlyList<Plant3DProfile> profiles, ColorSettings colors, string trunkColor, bool southernHemisphere = true)
    {
        ArgumentNullException.ThrowIfNull(species);
        (Plant3DProfile profile, _) = ProfileResolver.Resolve(species, profiles);
        bool showy = !string.IsNullOrWhiteSpace(species.FlowerColor) && profile.Flowers is not null;
        var stages = new List<(int Code, string Label, GrowthStage Stage, int Month)>
        {
            (1, "Plantio", GrowthStage.Plantio, 0),
            (2, "3 anos", GrowthStage.TresAnos, 0),
            (3, "6 anos", GrowthStage.SeisAnos, 0),
            (4, "Adulta", GrowthStage.Maturidade, 0),
        };
        if (species.EffectiveLeafHabit == LeafHabit.Decidua)
        {
            int month = Enumerable.Range(1, 12).FirstOrDefault(m => species.EffectiveLeaflessMonths.Contains(m));
            if (month > 0)
            {
                stages.Add((5, "Adulta sem folhas", GrowthStage.Maturidade, month));
            }
        }

        long seed = DeterministicRandom.StableHash(species.Id + "#" + variant);
        (double adultCrown, double adultHeight) = GrowthModel.Size(species, GrowthStage.Maturidade);
        double reference = Math.Max(adultHeight, adultCrown);
        var sets = new List<CardSet>();
        var signature = new List<string> { "cards v" + Version, Style };
        foreach ((int code, string label, GrowthStage stage, int month) in stages)
        {
            (double crown, double height) = GrowthModel.Size(species, stage);
            var options = new PlantOptions
            {
                Quality = QualityLevel.Intermediaria,
                Month = month,
                SouthernHemisphere = southernHemisphere,
                Flowers = showy,
                Variation = true,
            };
            PlantRequest request = PlantPlanner.Request(species, profile, crown, height, seed, options);
            if (month == 0 && showy)
            {
                request = new PlantRequest
                {
                    Profile = request.Profile, HeightM = request.HeightM, CrownDiameterM = request.CrownDiameterM, Quality = request.Quality, Seed = request.Seed,
                    Foliage = request.Foliage, Flowers = true, Fruits = request.Fruits, SolidOnly = false, Variation = true,
                };
            }

            GeneratedPlant plant = PlantGeometryGenerator.Generate(request);
            PlantColors partColors = PlantPlanner.Colors(species, profile, colors, trunkColor, request.Fruits);
            var parts = plant.Model.Pieces.Select(p => new IllustratedPart(p, partColors.For(p.Part))).ToList();
            int artSeed = (int)(seed % int.MaxValue) + code * 31;
            CardArt a = CardArtist.Draw(parts, 0, 1, artSeed, reference);
            CardArt b = CardArtist.Draw(parts, Math.PI / 2, -1, artSeed + 7, reference);
            sets.Add(new CardSet(code, label, CardRaster.Flatten(a, referenceSize: reference), CardRaster.Flatten(b, referenceSize: reference), a, b));
            signature.Add(code + ":" + GeometryHash.Of(request) + ":" + string.Join(",", PlantColors.Parts.Select(p => partColors.For(p).Hex)));
        }

        return new PlantCards(sets, GeometryHash.Text(string.Join("|", signature))[..12]);
    }
}
