using Viridian.Core.Composition;
using Viridian.Core.Massing;
using Viridian.Core.Model;
using Viridian.Core.Reports;
using Xunit;

namespace Viridian.Tests;

public class ProjectFlowTests
{
    private static Species Sp(string id, string flowering, string color = "#F2C14E") =>
        new() { Id = id, Code = id.ToUpperInvariant(), PopularName = "Planta " + id, Flowering = flowering, FlowerColor = color };

    [Fact]
    public void ProjectSpecies_CountsIsolatedPlantsAndMassingsSeparately()
    {
        var plants = new List<PlantRecord>
        {
            new() { SpeciesId = "ipe" },
            new() { SpeciesId = "ipe" },
            new() { SpeciesId = "lav", MassingId = "MC-001" },   // pertence ao maciço: não é isolado
            new() { SpeciesId = "lav", MassingId = "MC-999" },   // maciço apagado: conta como isolado
            new() { SpeciesId = string.Empty },                  // sem cadastro: ignorado
        };
        var massings = new List<MassingRecord>
        {
            new() { Id = "MC-001", SpeciesId = "lav", AreaM2 = 10.5 },
            new() { Id = "MC-002", SpeciesId = "LAV", AreaM2 = 4.25 },
        };
        IReadOnlyDictionary<string, SpeciesUsage> usage = ProjectSpecies.Build(plants, massings);
        Assert.Equal(2, usage.Count);
        Assert.Equal(2, usage["ipe"].Plants);
        Assert.Equal(0, usage["ipe"].Massings);
        Assert.Equal(1, usage["lav"].Plants);
        Assert.Equal(2, usage["lav"].Massings);
        Assert.Equal(14.75, usage["lav"].MassingAreaM2, 6);
        Assert.Equal("2 un.", usage["ipe"].Describe());
        Assert.Equal("1 un. · 2 maciços (14,8 m²)", usage["lav"].Describe());
    }

    [Fact]
    public void FloweringMap_GroupsMassingsAndIsolatedSpeciesWithSummary()
    {
        Species ipe = Sp("ipe", "ago-set"), lav = Sp("lav", "out-dez", "#8E6CC0"), grass = Sp("gra", "jan-dez", string.Empty);
        IReadOnlyList<FloweringGroup> groups = FloweringMap.Build(
            new[] { ("MC-001", lav, (IReadOnlyList<long>)new long[] { 10, 11, 12 }) },
            new[] { (ipe, 1L), (ipe, 2L), (grass, 3L) });

        Assert.Equal(4, groups.Count);
        FloweringGroup summary = groups[0];
        Assert.True(summary.IsSummary);
        Assert.Equal("3 espécies", summary.Detail);
        Assert.Contains("MC-001", groups[1].Title);
        Assert.Equal("2 exemplares", groups.Single(g => g.Title.StartsWith("IPE", StringComparison.Ordinal)).Detail);

        // Continuidade: flor vistosa de ago a dez; a grama floresce, mas é discreta.
        Assert.True(summary.Showy.Contains(8) && summary.Showy.Contains(12));
        Assert.False(summary.Showy.Contains(3));
        Assert.True(summary.Any.Contains(3));
        Assert.Equal("sem flor em jan–jul", summary.GapText);
        Assert.Equal(new[] { "#8E6CC0" }, summary.ColorsIn(11));
        Assert.Equal(new long[] { 10, 11, 12, 3 }.OrderBy(x => x), summary.ElementsFloweringIn(11).OrderBy(x => x));
        Assert.Equal(new long[] { 1, 2, 3 }.OrderBy(x => x), summary.ElementsFloweringIn(8).OrderBy(x => x));

        FloweringGroup grassRow = groups.Single(g => g.Title.StartsWith("GRA", StringComparison.Ordinal));
        Assert.Equal("só floração discreta", grassRow.GapText);
    }

    [Fact]
    public void FloweringMap_UnknownFloweringAndEmptyInput()
    {
        Assert.Empty(FloweringMap.Build(Array.Empty<(string, Species, IReadOnlyList<long>)>(), Array.Empty<(Species, long)>()));
        IReadOnlyList<FloweringGroup> groups = FloweringMap.Build(Array.Empty<(string, Species, IReadOnlyList<long>)>(), new[] { (Sp("x", string.Empty), 5L) });
        Assert.Equal("floração não cadastrada", groups[1].GapText);
        Assert.True(groups[1].Gaps.IsEmpty);
        Assert.Empty(groups[1].ElementsFloweringIn(6));
        Species always = Sp("y", "jan-dez");
        Assert.Equal("flor o ano todo", FloweringMap.Build(Array.Empty<(string, Species, IReadOnlyList<long>)>(), new[] { (always, 1L) })[1].GapText);
    }
}
