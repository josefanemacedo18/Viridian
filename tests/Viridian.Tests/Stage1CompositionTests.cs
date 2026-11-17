using Viridian.Core.Colors;
using Viridian.Core.Composition;
using Viridian.Core.Model;
using Viridian.Library;

namespace Viridian.Tests;

public class TextureAndScaleTests
{
    [Fact]
    public void Suggestions_UseGenusThenCategory()
    {
        Assert.Equal(Texture.Grossa, TextureRules.Suggest(new Species { ScientificName = "Philodendron bipinnatifidum", Category = PlantCategory.Herbacea }));
        Assert.Equal(Texture.Fina, TextureRules.Suggest(new Species { ScientificName = "Jacaranda mimosifolia", Category = PlantCategory.Arvore }));
        Assert.Equal(Texture.Grossa, TextureRules.Suggest(new Species { ScientificName = "Syagrus romanzoffiana", Category = PlantCategory.Palmeira }));
        Assert.Equal(Texture.Fina, TextureRules.Suggest(new Species { ScientificName = "Xxx yyy", Category = PlantCategory.Gramado }));
    }

    [Fact]
    public void RegisteredTextureWinsOverSuggestion()
    {
        var s = new Species { ScientificName = "Ficus benjamina", Category = PlantCategory.Arvore, Texture = Texture.Fina };
        Assert.Equal(Texture.Fina, s.EffectiveTexture);
    }

    [Theory]
    [InlineData(20, 12, VisualScale.Monumental)]
    [InlineData(6, 5, VisualScale.Media)]
    [InlineData(0.3, 0.4, VisualScale.Miuda)]
    [InlineData(4, 11, VisualScale.Monumental)]
    public void Scale_FromAdultSize(double height, double crown, VisualScale expected) =>
        Assert.Equal(expected, new Species { AdultHeightM = height, CrownDiameterM = crown }.Scale);

    [Fact]
    public void WholeCatalog_HasTextureProposal()
    {
        List<Species> all = SeedCatalog.CreateSpecies();
        Assert.All(all, s => Assert.NotEqual(Texture.NaoInformada, s.Texture));
        Assert.Contains(all, s => s.Texture == Texture.Fina);
        Assert.Contains(all, s => s.Texture == Texture.Grossa);
    }
}

public class PaletteStudioTests
{
    private static readonly ColorSettings Colors = new();

    [Fact]
    public void PurplePalette_RanksPurpleFlowersFirst()
    {
        var purple = new Species { Id = "a", Code = "A", ScientificName = "A a", Category = PlantCategory.Arbusto, FlowerColor = "#7B3FB0", Flowering = "set–dez" };
        var yellow = new Species { Id = "b", Code = "B", ScientificName = "B b", Category = PlantCategory.Arbusto, FlowerColor = "#F2C230", Flowering = "set–dez" };
        var palette = Palette.Presets().First(p => p.Name.StartsWith("Roxos"));

        IReadOnlyList<PaletteMatch> ranked = PaletteStudio.Rank(new[] { yellow, purple }, palette, Colors, 0);

        Assert.Equal("a", ranked[0].Species.Id);
        Assert.True(ranked[0].Score > ranked[1].Score);
        Assert.Contains("roxa", ranked[0].Reason);
    }

    [Fact]
    public void Months_PenalizeSpeciesFloweringOutOfSeason()
    {
        var palette = Palette.FromHarmony(Harmony.Monocromatica, 50);
        palette.Months = MonthSet.Parse("jun–jul");
        var winter = new Species { Id = "w", FlowerColor = "#F2C230", Flowering = "jun–ago" };
        var summer = new Species { Id = "s", FlowerColor = "#F2C230", Flowering = "dez–fev" };

        PaletteMatch w = PaletteStudio.Evaluate(winter, palette, Colors);
        PaletteMatch s = PaletteStudio.Evaluate(summer, palette, Colors);

        Assert.True(w.FlowersInMonths);
        Assert.False(s.FlowersInMonths);
        Assert.True(w.Score > s.Score * 2);
    }

    [Fact]
    public void Harmonies_ProduceExpectedHues()
    {
        Assert.Equal(new[] { 20.0, 200.0 }, Palette.FromHarmony(Harmony.Complementar, 20).Hues);
        Assert.Equal(new[] { 330.0, 0.0, 30.0 }, Palette.FromHarmony(Harmony.Analoga, 0).Hues);
        Assert.Equal(3, Palette.FromHarmony(Harmony.Triade, 10).Hues.Count);
    }

    [Fact]
    public void WhiteGardenPreset_AcceptsWhiteFlowers()
    {
        var palette = Palette.Presets().First(p => p.Hues.Count == 0);
        var white = new Species { FlowerColor = "#FFFFFF" };
        var red = new Species { FlowerColor = "#D0202A" };
        Assert.True(PaletteStudio.Evaluate(white, palette, Colors).Score > PaletteStudio.Evaluate(red, palette, Colors).Score);
    }
}

public class FloweringCalendarTests
{
    [Fact]
    public void Calendar_CountsMonthsAndFindsGaps()
    {
        var a = new Species { Id = "a", Flowering = "ago–set", FlowerColor = "#F2C230" };
        var b = new Species { Id = "b", Flowering = "dez–fev", FlowerColor = "#E04040" };
        var c = new Species { Id = "c", Flowering = "", FlowerColor = "" };

        FloweringCalendar cal = FloweringCalendar.Build(new[] { (a, "A", 2.0), (b, "B", 1.0), (c, "C", 5.0), (a, "A", 3.0) });

        Assert.Equal(3, cal.Rows.Count);
        Assert.Equal(5.0, cal.Rows.Single(r => r.Species.Id == "a").Quantity);
        Assert.Equal(1, cal.ShowyPerMonth[7]); // agosto
        Assert.Equal(1, cal.ShowyPerMonth[0]); // janeiro
        Assert.True(cal.ShowyGaps.Contains(5));
        Assert.False(cal.ShowyGaps.Contains(9));
        Assert.Single(cal.Unknown);
    }

    [Fact]
    public void Catalog_FloweringTextsAreMostlyParseable()
    {
        List<Species> all = SeedCatalog.CreateSpecies().Where(s => !string.IsNullOrWhiteSpace(s.Flowering)).ToList();
        int parsed = all.Count(s => !MonthSet.Parse(s.Flowering).IsEmpty);
        Assert.Equal(all.Count, parsed);
        Assert.DoesNotContain(SeedCatalog.CreateSpecies(), s => s.Flowering.Contains("string", StringComparison.Ordinal));
    }
}

public class CompositionRulesTests
{
    private static Species S(string id, PlantCategory c, double crown = 1, string flowering = "", string flower = "", string name = "") =>
        new() { Id = id, Code = id, PopularName = name == "" ? id : name, ScientificName = "Xxx " + id, Category = c, CrownDiameterM = crown, AdultHeightM = crown, Flowering = flowering, FlowerColor = flower };

    [Fact]
    public void DominantSpecies_IsReported()
    {
        var bed = new Bed("C1", new[]
        {
            new BedPlant(S("a", PlantCategory.Arbusto), "a", 20, 0),
            new BedPlant(S("b", PlantCategory.Arbusto), "b", 3, 0),
            new BedPlant(S("c", PlantCategory.Herbacea), "c", 3, 0),
        }, 30, 6, 5);

        IReadOnlyList<CompositionNote> notes = CompositionRules.Evaluate(bed, new CompositionSettings());

        Assert.Contains(notes, n => n.Rule == "Equilíbrio" && n.Message.Contains("a "));
        Assert.DoesNotContain(notes, n => n.Rule == "Equilíbrio" && n.Message.StartsWith("b "));
    }

    [Fact]
    public void LowDiversity_SameTexture_PalmWithoutBase_AndBigCrown()
    {
        var palm = S("p", PlantCategory.Palmeira, crown: 6);
        var bed = new Bed("C2", new[] { new BedPlant(palm, "p", 3, 0) }, 40, 4, 10);

        IReadOnlyList<CompositionNote> notes = CompositionRules.Evaluate(bed, new CompositionSettings());

        Assert.Contains(notes, n => n.Rule == "Diversidade");
        Assert.Contains(notes, n => n.Rule == "Sub-bosque");
        Assert.Contains(notes, n => n.Rule == "Escala");
    }

    [Fact]
    public void FloweringGaps_AreSuggested()
    {
        var bed = new Bed("C3", new[]
        {
            new BedPlant(S("a", PlantCategory.Arbusto, flowering: "set–nov", flower: "#E04040"), "a", 5, 0),
            new BedPlant(S("b", PlantCategory.Herbacea, flowering: "out–dez", flower: "#F2C230"), "b", 5, 0),
            new BedPlant(S("c", PlantCategory.Forracao), "c", 5, 0),
        }, 10, 3, 3);

        CompositionNote note = Assert.Single(CompositionRules.Evaluate(bed, new CompositionSettings()), n => n.Rule == "Floração");
        Assert.Contains("jan", note.Message);
    }

    [Fact]
    public void BalancedBed_HasNoWarnings()
    {
        var bed = new Bed("C4", new[]
        {
            new BedPlant(S("arv", PlantCategory.Arvore, crown: 4, flowering: "ano todo", flower: "#F2C230"), "arv", 1, 0),
            new BedPlant(new Species { Id = "ar", Code = "ar", ScientificName = "Philodendron x", Category = PlantCategory.Arbusto, CrownDiameterM = 1.5 }, "ar", 3, 0),
            new BedPlant(new Species { Id = "fo", Code = "fo", ScientificName = "Ophiopogon japonicus", Category = PlantCategory.Forracao, CrownDiameterM = 0.3 }, "fo", 3, 0),
        }, 25, 6, 5);

        Assert.DoesNotContain(CompositionRules.Evaluate(bed, new CompositionSettings()), n => n.Severity == CompositionSeverity.Aviso);
    }
}
