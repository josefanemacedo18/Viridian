using System.Text.Json;
using Viridian.Core.Colors;
using Viridian.Core.Config;
using Viridian.Core.Detailing;
using Viridian.Core.Model;
using Viridian.Core.Model3D;
using Viridian.Core.Model3D.Generation;
using Viridian.Core.Model3D.Profiles;
using Viridian.Core.Model3D.Seasonality;
using Viridian.Library;

namespace Viridian.Tests;

public class MaqueteBotanicalTests
{
    private static readonly List<Species> Catalog = SeedCatalog.CreateSpecies();
    private static readonly List<Plant3DProfile> Profiles = Plant3DProfileDefaults.Create();

    private static Species Find(string name) => Catalog.First(s => s.ScientificName == name);

    [Fact]
    public void OldMaqueteState_StaysOnTheLegacyGenerator()
    {
        // Estado gravado pela 1.8 (sem os campos novos).
        const string old = "{\"SchemaVersion\":1,\"Stage\":\"SeisAnos\",\"Region\":[],\"LevelName\":\"\"}";
        MaqueteState state = JsonSerializer.Deserialize<MaqueteState>(old, ViridianJson.Options)!.Upgrade(legacyFlowers: false);
        Assert.Equal(MaqueteEngine.Legado, state.Engine);
        Assert.Equal(GrowthStage.SeisAnos, state.Stage);
        Assert.False(state.Flowers);
        Assert.Equal(0, state.Month);
        Assert.Equal(MaqueteState.CurrentSchema, state.SchemaVersion);
        Assert.Contains("simples", state.Describe());
    }

    [Fact]
    public void NewMaqueteState_UsesBotanicalEngine_AndRoundTrips()
    {
        var state = new MaqueteState { Engine = MaqueteEngine.Botanico, Quality = QualityLevel.Detalhada, Month = 8, Fruits = true, Variation = false };
        string json = JsonSerializer.Serialize(state, ViridianJson.Options);
        MaqueteState back = JsonSerializer.Deserialize<MaqueteState>(json, ViridianJson.Options)!.Upgrade(true);
        Assert.Equal(MaqueteEngine.Botanico, back.Engine);
        Assert.Equal(QualityLevel.Detalhada, back.Quality);
        Assert.Equal(8, back.Month);
        Assert.True(back.Fruits);
        Assert.False(back.Variation);
        Assert.Contains("ago", back.Describe());

        back.Month = 15;
        Assert.Equal(0, back.Upgrade(true).Month);
    }

    [Fact]
    public void ModelSettings_NewFields_HaveSafeDefaults_AndNormalize()
    {
        var s = new ModelSettings { ReferenceMonth = 0, WarnTriangles = -1, Quality = (QualityLevel)99, Engine = (MaqueteEngine)7 };
        s.Normalize();
        Assert.Equal(1, s.ReferenceMonth);
        Assert.Equal(3_000_000, s.WarnTriangles);
        Assert.Equal(QualityLevel.Intermediaria, s.Quality);
        Assert.Equal(MaqueteEngine.Botanico, s.Engine);
        Assert.False(new ModelSettings().UseSeasonality);
        Assert.False(new ModelSettings().ShowFruits);
    }

    [Fact]
    public void Season_WithoutMonth_KeepsFullFoliage_AndShowyFlowers()
    {
        Species ipe = Find("Handroanthus chrysotrichus");
        (Plant3DProfile p, _) = ProfileResolver.Resolve(ipe, Profiles);
        PlantSeason season = PlantPlanner.Season(ipe, p, new PlantOptions { Month = 0, Flowers = true });
        Assert.Equal(FoliageState.Cheia, season.Foliage);
        Assert.True(season.Flowers);
        Assert.Null(season.Fruits);

        PlantSeason off = PlantPlanner.Season(ipe, p, new PlantOptions { Month = 0, Flowers = false });
        Assert.False(off.Flowers);
    }

    [Fact]
    public void Season_InAugust_IpeIsLeaflessAndFlowering_InJanuaryLeafyWithoutFlowers()
    {
        Species ipe = Find("Handroanthus chrysotrichus");
        (Plant3DProfile p, _) = ProfileResolver.Resolve(ipe, Profiles);
        PlantSeason august = PlantPlanner.Season(ipe, p, new PlantOptions { Month = 8, Flowers = true });
        Assert.Equal(FoliageState.SemFolhas, august.Foliage);
        Assert.True(august.Flowers);
        PlantSeason january = PlantPlanner.Season(ipe, p, new PlantOptions { Month = 1, Flowers = true });
        Assert.Equal(FoliageState.Cheia, january.Foliage);
        Assert.False(january.Flowers);

        // Hemisfério norte: os meses se deslocam seis meses.
        PlantSeason north = PlantPlanner.Season(ipe, p, new PlantOptions { Month = 2, Flowers = true, SouthernHemisphere = false });
        Assert.Equal(FoliageState.SemFolhas, north.Foliage);
    }

    [Fact]
    public void Fruits_OnlyWhenAsked_AndInTheirSeason()
    {
        Species jeriva = Find("Syagrus romanzoffiana");
        (Plant3DProfile p, _) = ProfileResolver.Resolve(jeriva, Profiles);
        Assert.Null(PlantPlanner.Season(jeriva, p, new PlantOptions { Month = 4, Fruits = false }).Fruits);
        Assert.NotNull(PlantPlanner.Season(jeriva, p, new PlantOptions { Month = 4, Fruits = true }).Fruits);
        Assert.Null(PlantPlanner.Season(jeriva, p, new PlantOptions { Month = 11, Fruits = true }).Fruits);
        Assert.NotNull(PlantPlanner.Season(jeriva, p, new PlantOptions { Month = 0, Fruits = true }).Fruits);
    }

    [Fact]
    public void Request_FromPlanner_GeneratesValidGeometry_ForEveryCatalogSpecies()
    {
        var problems = new List<string>();
        foreach (Species s in Catalog)
        {
            (Plant3DProfile p, _) = ProfileResolver.Resolve(s, Profiles);
            foreach (GrowthStage stage in new[] { GrowthStage.Plantio, GrowthStage.Maturidade })
            {
                (double crown, double height) = GrowthModel.Size(s, stage);
                foreach (QualityLevel quality in new[] { QualityLevel.Simplificada, QualityLevel.Detalhada })
                {
                    foreach (int month in new[] { 0, 8 })
                    {
                        PlantRequest request = PlantPlanner.Request(s, p, crown, height, 42, new PlantOptions { Quality = quality, Month = month, Fruits = true });
                        GeneratedPlant g = PlantGeometryGenerator.Generate(request);
                        problems.AddRange(MeshValidator.Check(g.Model, g.Budget).Select(i => $"{s.ScientificName} ({p.Id}, {stage}, {quality}, mês {month}): {i.Message}"));
                    }
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems.Take(40)));
    }

    [Fact]
    public void Colors_UseSpeciesFoliage_ProfileTrunk_AndFruit()
    {
        Species jeriva = Find("Syagrus romanzoffiana");
        (Plant3DProfile p, _) = ProfileResolver.Resolve(jeriva, Profiles);
        FruitTraits? fruit = SeasonalityResolver.Fruit(jeriva, p);
        PlantColors colors = PlantPlanner.Colors(jeriva, p, new ColorSettings(), "#6B4F3A", fruit);
        Assert.Equal(VegetationPalette.Foliage(jeriva, new ColorSettings()), colors.Foliage);
        Assert.Equal(Rgb.Parse(p.Trunk!.Color, Rgb.Black), colors.Trunk); // estipe cinza do perfil de palmeira
        Assert.Equal(fruit!.Color, colors.For(MeshPart.Fruto));
        Assert.True(colors.For(MeshPart.FolhagemClara).Luminance > colors.Foliage.Luminance);
        Assert.True(colors.For(MeshPart.FolhagemEscura).Luminance < colors.Foliage.Luminance);
        Assert.Equal(7, PlantColors.Parts.Distinct().Count());
    }

    [Fact]
    public void Cost_GrowsWithQuality_AndSuggestsALighterLevel()
    {
        List<Species> plants = Catalog.Where(s => s.Category == PlantCategory.Arvore).Take(50).ToList();
        long simple = MaqueteCost.Triangles(plants, Profiles, QualityLevel.Simplificada);
        long detailed = MaqueteCost.Triangles(plants, Profiles, QualityLevel.Detalhada);
        Assert.True(simple > 0 && simple < detailed);
        Assert.Equal(QualityLevel.Detalhada, MaqueteCost.Suggest(plants, Profiles, long.MaxValue));
        Assert.Equal(QualityLevel.Simplificada, MaqueteCost.Suggest(plants, Profiles, simple));
        Assert.Null(MaqueteCost.Suggest(plants, Profiles, simple - 1));
    }

    [Fact]
    public void ProfileLibrary_RoundTrip_KeepsUserProfiles_AndUpdatesSeeds()
    {
        string folder = Path.Combine(Path.GetTempPath(), "vir-perfis-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new LibraryPaths(folder);
            var library = new Plant3DProfileLibrary(paths);
            Assert.True(File.Exists(paths.Profiles3DFile));
            Plant3DProfile mine = library.Find("ipe")!.Clone();
            mine.Id = "meu-ipe";
            mine.Name = "Meu ipê";
            mine.IsUserDefined = true;
            library.Save(mine);
            var again = new Plant3DProfileLibrary(paths);
            Assert.NotNull(again.Find("meu-ipe"));
            Assert.Equal(Profiles.Count + 1, again.Profiles.Count);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
