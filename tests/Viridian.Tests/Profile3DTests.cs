using Viridian.Core.Config;
using Viridian.Core.Model;
using Viridian.Core.Model3D.Profiles;
using Viridian.Core.Model3D.Seasonality;
using Viridian.Library;

namespace Viridian.Tests;

public class Profile3DTests
{
    private static readonly List<Species> Catalog = SeedCatalog.CreateSpecies();

    [Fact]
    public void Defaults_AreValid_Unique_AndCoverTheRequiredBases()
    {
        List<Plant3DProfile> all = Plant3DProfileDefaults.Create();
        Assert.Equal(all.Count, all.Select(p => p.Id).Distinct().Count());
        Assert.All(all, p => Assert.Empty(p.Validate()));
        string[] required =
        {
            "arvore-arredondada", "arvore-espalhada", "arvore-colunar", "arvore-conica", "arvore-irregular", "palmeira-estipe", "palmeira-touceira",
            "arbusto-arredondado", "arbusto-ereto", "trepadeira", "graminea", "roseta-rigida", "herbacea-folha-larga", "forracao", "aquatica-emergente", "gramado",
            "ipe", "flamboyant", "jeriva", "agave", "estrelitzia",
        };
        foreach (string id in required)
        {
            Assert.Contains(all, p => p.Id == id);
        }
    }

    [Fact]
    public void EveryCatalogSpecies_ResolvesAValidProfile_OfACompatibleKind()
    {
        List<Plant3DProfile> library = Plant3DProfileDefaults.Create();
        foreach (Species s in Catalog)
        {
            (Plant3DProfile p, bool fromSpecies) = ProfileResolver.Resolve(s, library);
            Assert.False(fromSpecies);
            Assert.Empty(p.Validate());
            bool tree = p.Architecture is >= PlantArchitecture.ArvoreArredondada and <= PlantArchitecture.ArvoreIrregular;
            if (s.Category == PlantCategory.Arvore)
            {
                Assert.True(tree, $"{s.ScientificName} → {p.Id}");
            }
            else
            {
                Assert.False(tree && s.Category != PlantCategory.Arvore, $"{s.ScientificName} → {p.Id}");
            }
        }
    }

    [Theory]
    [InlineData("Handroanthus chrysotrichus", "ipe")]
    [InlineData("Delonix regia", "flamboyant")]
    [InlineData("Syagrus romanzoffiana", "jeriva")]
    [InlineData("Agave attenuata", "agave")]
    [InlineData("Strelitzia reginae", "estrelitzia")]
    [InlineData("Cycas revoluta", "cica")]
    [InlineData("Livistona chinensis", "palmeira-leque")]
    [InlineData("Dypsis lutescens", "palmeira-touceira")]
    [InlineData("Alcantarea imperialis", "bromelia")]
    [InlineData("Agapanthus africanus", "touceira-fita")]
    [InlineData("Cortaderia selloana", "graminea")]
    [InlineData("Nymphaea caerulea", "aquatica-flutuante")]
    [InlineData("Typha domingensis", "aquatica-emergente")]
    [InlineData("Araucaria angustifolia", "araucaria")]
    [InlineData("Dracaena marginata", "tufos-em-caule")]
    [InlineData("Arachis repens", "forracao")]
    [InlineData("Zoysia japonica", "gramado")]
    [InlineData("Bougainvillea spectabilis", "trepadeira")]
    public void ReferenceSpecies_GetTheirProfiles(string scientific, string expected)
    {
        Species s = Catalog.First(x => x.ScientificName.StartsWith(scientific, StringComparison.Ordinal));
        Assert.Equal(expected, ProfileResolver.Resolve(s, Plant3DProfileDefaults.Create()).Profile.Id);
    }

    [Fact]
    public void ChosenProfile_Wins_AndMissingFallsBackSafely()
    {
        List<Plant3DProfile> library = Plant3DProfileDefaults.Create();
        Species s = Catalog.First(x => x.ScientificName.StartsWith("Handroanthus", StringComparison.Ordinal)).Clone();
        s.Plant3DProfileId = "arvore-colunar";
        Assert.Equal(("arvore-colunar", true), (ProfileResolver.Resolve(s, library).Profile.Id, ProfileResolver.Resolve(s, library).FromSpecies));
        s.Plant3DProfileId = "nao-existe";
        Assert.Equal("ipe", ProfileResolver.Resolve(s, library).Profile.Id);
        Assert.Equal("ipe", ProfileResolver.Resolve(s, library.Where(p => p.Id != "ipe").ToList()).Profile.Id); // padrão seguro
    }

    [Fact]
    public void Profile_Normalize_RepairsInvalidValues()
    {
        var p = new Plant3DProfile
        {
            Id = " x ",
            Name = "X",
            Architecture = (PlantArchitecture)99,
            Crown = new CrownProfile { BaseRatio = double.NaN, Density = -3, Envelope = (CrownEnvelope)77 },
            Trunk = new TrunkProfile { RadiusRatio = 9, Stems = -2 },
            Palm = new PalmProfile { FrondsMin = 50, FrondsMax = 2, Frond = LeafShape.Suculenta },
            Budget = new GeometryBudget { Simplificada = 5, Intermediaria = 1, Detalhada = 0 },
        };
        p.Normalize();
        Assert.Equal("x", p.Id);
        Assert.Equal(PlantArchitecture.ArvoreArredondada, p.Architecture);
        Assert.Equal(CrownEnvelope.Elipsoide, p.Crown.Envelope);
        Assert.Equal(0.35, p.Crown.BaseRatio);
        Assert.Equal(0.05, p.Crown.Density);
        Assert.Equal(0.08, p.Trunk!.RadiusRatio);
        Assert.Equal(1, p.Trunk.Stems);
        Assert.Equal(40, p.Palm!.FrondsMin);
        Assert.True(p.Palm.FrondsMax >= p.Palm.FrondsMin);
        Assert.Equal(LeafShape.Pinada, p.Palm.Frond);
        Assert.True(p.Budget.Simplificada <= p.Budget.Intermediaria && p.Budget.Intermediaria <= p.Budget.Detalhada);
        Assert.NotEmpty(new Plant3DProfile { Id = "a", Name = "b", Architecture = PlantArchitecture.PalmeiraEstipeUnico }.Validate());
        Assert.NotEmpty(new Plant3DProfile { Id = "a", Name = "b", SchemaVersion = 99 }.Validate());
    }

    [Fact]
    public void Profiles_RoundTripJson_WithoutLosingData()
    {
        foreach (Plant3DProfile p in Plant3DProfileDefaults.Create())
        {
            Plant3DProfile back = ViridianJson.Deserialize<Plant3DProfile>(ViridianJson.Serialize(p))!;
            Assert.Equal(p.Signature(), back.Signature());
        }
    }

    [Fact]
    public void Library_Merge_PreservesOfficeProfiles_UpdatesSeeds_DropsInvalid()
    {
        Plant3DProfile edited = Plant3DProfileDefaults.Create().First(p => p.Id == "ipe");
        edited.Name = "Ipê do escritório";
        edited.IsUserDefined = true;
        Plant3DProfile stale = Plant3DProfileDefaults.Create().First(p => p.Id == "agave");
        stale.Name = "antigo";
        var mine = new Plant3DProfile { Id = "meu-perfil", Name = "Meu", IsUserDefined = true, Architecture = PlantArchitecture.ArbustoArredondado };
        var invalid = new Plant3DProfile { Id = "ruim", Name = "Ruim", SchemaVersion = 42 };
        (List<Plant3DProfile> merged, bool changed) = Plant3DProfileLibrary.Merge(new Plant3DProfileFile { SeedVersion = 0, Profiles = new() { edited, stale, mine, invalid } });
        Assert.True(changed);
        Assert.Equal("Ipê do escritório", merged.First(p => p.Id == "ipe").Name);
        Assert.NotEqual("antigo", merged.First(p => p.Id == "agave").Name);
        Assert.Contains(merged, p => p.Id == "meu-perfil");
        Assert.DoesNotContain(merged, p => p.Id == "ruim");
        Assert.Equal(Plant3DProfileDefaults.Create().Count + 1, merged.Count);
    }
}

public class SeasonalityTests
{
    private static readonly List<Species> Catalog = SeedCatalog.CreateSpecies();

    private static Species Find(string scientific) => Catalog.First(s => s.ScientificName.StartsWith(scientific, StringComparison.Ordinal));

    [Fact]
    public void Flowering_FromFicha_Estimated_OrUnknown()
    {
        SeasonInfo ipe = SeasonalityResolver.Flowering(Find("Handroanthus chrysotrichus"));
        Assert.Equal(SeasonSource.Ficha, ipe.Source);
        Assert.True(ipe.Known);
        SeasonInfo bromelia = SeasonalityResolver.Flowering(Find("Neoregelia cruenta"));
        Assert.Equal(SeasonSource.Estimado, bromelia.Source);
        Assert.Contains("estimada", bromelia.Text, StringComparison.Ordinal);
        var unknown = new Species { ScientificName = "Genus inexistens", FlowerColor = "#FF0000" };
        Assert.Equal(SeasonSource.NaoInformado, SeasonalityResolver.Flowering(unknown).Source);
        Assert.False(SeasonalityResolver.InMonth(SeasonalityResolver.Flowering(unknown), 10));
    }

    [Fact]
    public void Fruit_EstimatedByGenus_OrFromFicha_NeverInvented()
    {
        Plant3DProfile tree = Plant3DProfileDefaults.Create().First(p => p.Id == "arvore-arredondada");
        FruitTraits? pitanga = SeasonalityResolver.Fruit(Find("Eugenia uniflora"), tree);
        Assert.NotNull(pitanga);
        Assert.Equal(SeasonSource.Estimado, pitanga!.Season.Source);
        Assert.True(pitanga.Season.Months.Contains(11));
        Assert.Null(SeasonalityResolver.Fruit(Find("Ficus benjamina"), tree));
        Species custom = Find("Ficus benjamina").Clone();
        custom.Fruiting = "mar–abr";
        custom.FruitColor = "#550033";
        FruitTraits? informed = SeasonalityResolver.Fruit(custom, tree);
        Assert.Equal(SeasonSource.Ficha, informed!.Season.Source);
        Assert.Equal("#550033", informed.Color.Hex);
        Assert.Equal(FruitType.Caulinar, SeasonalityResolver.Fruit(Find("Plinia cauliflora"), tree)!.Type);
    }

    [Fact]
    public void Foliage_ByMonth_AndHemisphere()
    {
        Species ipe = Find("Handroanthus chrysotrichus");
        Assert.Equal(FoliageState.SemFolhas, SeasonalityResolver.Foliage(ipe, 8));
        Assert.Equal(FoliageState.Cheia, SeasonalityResolver.Foliage(ipe, 1));
        Assert.Equal(FoliageState.Cheia, SeasonalityResolver.Foliage(ipe, 0));
        Assert.Equal(FoliageState.SemFolhas, SeasonalityResolver.Foliage(ipe, 2, southernHemisphere: false));
        Assert.Equal(FoliageState.Rala, SeasonalityResolver.Foliage(Find("Tipuana tipu"), 7));
        Assert.Equal(FoliageState.Cheia, SeasonalityResolver.Foliage(Find("Ficus benjamina"), 7));
    }
}
