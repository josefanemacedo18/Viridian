using Viridian.Core.Geometry;
using Viridian.Core.Hatch;
using Viridian.Core.Identification;
using Viridian.Core.Massing;
using Viridian.Core.Model;
using Viridian.Library;

namespace Viridian.Tests;

public class HatchVariantTests
{
    [Fact]
    public void EverySpecies_HasValidOwnHatch()
    {
        foreach (Species s in SeedCatalog.CreateSpecies())
        {
            string key = HatchLibrary.KeyFor(s);
            HatchPattern? h = HatchLibrary.Find(key);
            Assert.NotNull(h);
            Assert.True(h!.IsValid(out string problem), s.PopularName + ": " + problem);
            Assert.Equal(key, h.Key);
        }
    }

    [Fact]
    public void SameCategory_GetsDifferentTracings()
    {
        List<Species> shrubs = SeedCatalog.CreateSpecies().Where(s => s.Category == PlantCategory.Arbusto).ToList();
        int distinct = shrubs.Select(HatchLibrary.KeyFor).Distinct().Count();
        Assert.True(distinct >= Math.Min(6, shrubs.Count), distinct.ToString());
        Assert.NotEqual(HatchLibrary.Find(HatchLibrary.VariantKey(HatchLibrary.Irregular, 1))!.Name, HatchLibrary.Find(HatchLibrary.VariantKey(HatchLibrary.Irregular, 2))!.Name);
    }

    [Fact]
    public void ChosenHatch_IsKept_AndVariantIsDeterministic()
    {
        var s = new Species { Category = PlantCategory.Forracao, ScientificName = "Teste teste", HatchPatternKey = HatchLibrary.Water };
        Assert.Equal(HatchLibrary.Water, HatchLibrary.KeyFor(s));
        s.HatchPatternKey = HatchLibrary.DefaultKeyFor(s.Category);
        Assert.StartsWith(HatchLibrary.VariantPrefix, HatchLibrary.KeyFor(s), StringComparison.Ordinal);
        Assert.Equal(HatchLibrary.KeyFor(s), HatchLibrary.KeyFor(s.Clone()));
        Assert.Null(HatchLibrary.Find("var:inexistente:2"));
        Assert.Null(HatchLibrary.Find("var:sem-numero"));
        HatchPattern v0 = HatchLibrary.Variant(HatchLibrary.OrganicDots, 0)!;
        Assert.Equal(HatchLibrary.Find(HatchLibrary.OrganicDots)!.Grids.Count, v0.Grids.Count);
    }

    [Fact]
    public void ProjectKey_AvoidsTracingsOfOtherSpecies_AndReusesOwn()
    {
        var a = new Species { Id = "a", Category = PlantCategory.Arbusto, ScientificName = "Alfa alfa" };
        var b = new Species { Id = "b", Category = PlantCategory.Arbusto, ScientificName = "Beta beta", SymbolSeed = a.EffectiveSymbolSeed };
        string own = HatchLibrary.KeyFor(a);
        Assert.Equal(own, HatchLibrary.ProjectKey(a, []));

        // b teria o mesmo traçado de a: recebe a próxima variação livre
        string forB = HatchLibrary.ProjectKey(b, [("a", HatchLibrary.KeyFor(b))]);
        Assert.NotEqual(HatchLibrary.KeyFor(b), forB);
        Assert.NotNull(HatchLibrary.Find(forB));

        // outro maciço da mesma espécie repete o traçado dela
        Assert.Equal(forB, HatchLibrary.ProjectKey(b, [("a", own), ("b", forB)]));

        // hachura escolhida no cadastro é respeitada
        var c = new Species { Id = "c", Category = PlantCategory.Arbusto, HatchPatternKey = HatchLibrary.Water };
        Assert.Equal(HatchLibrary.Water, HatchLibrary.ProjectKey(c, [("a", HatchLibrary.Water)]));
    }

    [Fact]
    public void MassingDefault_HatchForBeds_SymbolsForTrees()
    {
        var rules = new DensityRules();
        Assert.Equal(MassingMode.Hachura, rules.DefaultMode(new Species { Category = PlantCategory.Arbusto }, 0.5));
        Assert.Equal(MassingMode.Hachura, rules.DefaultMode(new Species { Category = PlantCategory.Forracao }, 16));
        Assert.Equal(rules.SuggestMode(0.05), rules.DefaultMode(new Species { Category = PlantCategory.Arvore }, 0.05));
    }
}

public class PhotoCardAnchorTests
{
    private static readonly Bounds2 Card = new(0, 0, 4, 3);

    [Theory]
    [InlineData(10, 1.5, 4, 1.5)]
    [InlineData(-6, 1.5, 0, 1.5)]
    [InlineData(2, 10, 2, 3)]
    [InlineData(2, -9, 2, 0)]
    public void Anchor_IsOnTheSideFacingThePlant(double tx, double ty, double ax, double ay)
    {
        Point2 a = PhotoCardGeometry.Anchor(Card, new Point2(tx, ty));
        Assert.Equal(ax, a.X, 6);
        Assert.Equal(ay, a.Y, 6);
    }

    [Fact]
    public void Anchor_StaysAwayFromCorners_AndHandlesInside()
    {
        Point2 a = PhotoCardGeometry.Anchor(Card, new Point2(20, 50));
        Assert.Equal(3, a.Y, 6);
        Assert.InRange(a.X, 0.8, 3.2);
        Assert.Equal(new Point2(2, 0), PhotoCardGeometry.Anchor(Card, new Point2(1, 1)));
    }
}

public class DetailTemplateTests
{
    [Theory]
    [InlineData("Metric Detail Item", 100)]
    [InlineData("Item de detalhe métrico", 100)]
    [InlineData("Componente de detalhe", 100)]
    [InlineData("Metric Detail Item line based", 0)]
    [InlineData("Item de detalhe métrico baseado em linha", 0)]
    [InlineData("Metric Generic Model", 0)]
    [InlineData("Metric Generic Tag", 0)]
    public void DetailScore(string name, int expected)
    {
        int score = TemplateNames.DetailScore(name);
        if (expected == 0)
        {
            Assert.Equal(0, score);
        }
        else
        {
            Assert.True(score >= expected, score.ToString());
        }
    }
}

public class SettingsMigrationTests
{
    [Fact]
    public void OldSettingsFile_GetsStrongerMassingColors_ButNewChoiceIsKept()
    {
        var old = System.Text.Json.JsonSerializer.Deserialize<Viridian.Core.Config.ViridianSettings>("{\"Colors\":{\"MassingBackgroundTint\":0.55}}")!;
        old.Normalize();
        Assert.Equal(0.3, old.Colors.MassingBackgroundTint);
        Assert.Equal(Viridian.Core.Config.ViridianSettings.CurrentSchema, old.SchemaVersion);

        old.Colors.MassingBackgroundTint = 0.55; // escolha do usuário depois da migração
        string json = System.Text.Json.JsonSerializer.Serialize(old);
        var again = System.Text.Json.JsonSerializer.Deserialize<Viridian.Core.Config.ViridianSettings>(json)!;
        again.Normalize();
        Assert.Equal(0.55, again.Colors.MassingBackgroundTint);
    }
}
