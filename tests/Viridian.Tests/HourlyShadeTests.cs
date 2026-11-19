using Viridian.Core.Detailing;
using Viridian.Core.Geometry;
using Viridian.Core.Model;
using Viridian.Core.Model3D;
using Viridian.Core.Solar;

namespace Viridian.Tests;

public class HourlyShadeTests
{
    private static readonly SolarSite Brasilia = new(-15.78, -47.88, -3, "Brasília", SiteSource.Projeto);

    private static Species Tree(string scientific = "Ficus benjamina", double height = 8, double crown = 8) =>
        new() { Id = "t", Category = PlantCategory.Arvore, ScientificName = scientific, AdultHeightM = height, CrownDiameterM = crown };

    [Fact]
    public void SolarAndClockHours_AreInverse()
    {
        int day = new SolarDate("", 7, 17).DayOfYear;
        double solar = Sun.SolarHour(9, Brasilia.LongitudeDeg, Brasilia.TimeZoneHours, day);
        Assert.Equal(9, Sun.ClockHour(solar, Brasilia.LongitudeDeg, Brasilia.TimeZoneHours, day), 9);
        // Brasília fica a oeste do meridiano do fuso: às 12h do relógio o sol ainda não chegou ao ponto mais alto.
        Assert.True(Sun.SolarHour(12, Brasilia.LongitudeDeg, Brasilia.TimeZoneHours, day) < 12);
    }

    [Fact]
    public void Hours_OnlyWithSunUp()
    {
        List<int> hours = HourlyShade.Hours(Brasilia, new SolarDate("Junho", 6, 11), 5, 20);
        Assert.Equal(7, hours.First());
        Assert.Equal(17, hours.Last());
        Assert.DoesNotContain(5, hours);
        Assert.DoesNotContain(19, hours);
    }

    [Fact]
    public void Shadow_PointsAwayFromSun_AndLengthFollowsHeight()
    {
        CrownObstacle crown = CrownObstacle.For("a", Tree(), Vec3.Zero, GrowthStage.Maturidade)!;
        SolarDate july = new("Julho", 7, 17);
        SunPosition noon = HourlyShade.SunAt(Brasilia, july, 12.2);
        IReadOnlyList<Point2> shadow = crown.ShadowOnGround(noon.Direction);
        Point2 centroid = new(shadow.Average(p => p.X), shadow.Average(p => p.Y));
        // Inverno no hemisfério sul: o sol fica ao norte, a sombra cai ao sul da árvore.
        Assert.True(centroid.Y < -1, centroid.ToString());
        Assert.True(Math.Abs(centroid.X) < 1.5, centroid.ToString());

        SunPosition afternoon = HourlyShade.SunAt(Brasilia, july, 16);
        IReadOnlyList<Point2> late = crown.ShadowOnGround(afternoon.Direction);
        Point2 lateCentroid = new(late.Average(p => p.X), late.Average(p => p.Y));
        Assert.True(lateCentroid.X > 3, lateCentroid.ToString()); // à tarde o sol está a oeste: a sombra vai para leste (+X)
    }

    [Fact]
    public void DeciduousTree_IsMoreTransparentInLeaflessMonths()
    {
        Species ipe = Tree("Handroanthus chrysotrichus");
        Assert.Equal(LeafHabit.Decidua, ipe.EffectiveLeafHabit);
        Assert.True(ipe.LeafHabitEstimated);
        Assert.True(ipe.EffectiveLeaflessMonths.Contains(8));
        CrownObstacle c = CrownObstacle.For("i", ipe, Vec3.Zero, GrowthStage.Maturidade)!;
        Assert.True(c.OpacityIn(8) < c.OpacityIn(1));
        CrownObstacle north = CrownObstacle.For("i", ipe, Vec3.Zero, GrowthStage.Maturidade, southernHemisphere: false)!;
        Assert.True(north.OpacityIn(2) < north.OpacityIn(8));

        Species ficus = Tree();
        Assert.Equal(LeafHabit.Perenifolia, ficus.EffectiveLeafHabit);
        Assert.True(ficus.EffectiveLeaflessMonths.IsEmpty);
        ficus.LeafHabit = LeafHabit.Semidecidua;
        ficus.LeaflessMonths = "set–out";
        Assert.False(ficus.LeafHabitEstimated);
        Assert.True(ficus.EffectiveLeaflessMonths.Contains(10));
        Assert.False(ficus.EffectiveLeaflessMonths.Contains(7));
        Assert.Equal(LeafHabit.Semidecidua, LeafRules.Estimate(Tree("Tipuana tipu")));
        Assert.Equal(LeafHabit.Perenifolia, LeafRules.Estimate(new Species { Category = PlantCategory.Palmeira, ScientificName = "Handroanthus x" }));
    }

    [Fact]
    public void Frames_ShadeUnderAndBesideTheTree()
    {
        CrownObstacle crown = CrownObstacle.For("a", Tree(), Vec3.Zero, GrowthStage.Maturidade)!;
        var grid = new List<Vec3> { new(0, -6, 0.3), new(0, 12, 0.3), new(20, 0, 0.3) };
        var plants = new List<(Vec3, string)> { (new Vec3(0, -5, 0.5), "p") };
        List<ShadeFrame> frames = HourlyShade.Compute(grid, plants, new[] { crown }, Brasilia, new SolarDate("Julho", 7, 17), 7, 17, v => v, null);
        Assert.Equal(11, frames.Count);
        ShadeFrame noon = frames.First(f => f.ClockHour == 12);
        Assert.True(noon.Grid[0] < 0.35);  // ao sul da árvore, à sombra ao meio-dia de inverno
        Assert.Equal(1, noon.Grid[1]);     // ao norte, sol
        Assert.Equal(1, noon.Grid[2]);
        Assert.True(noon.Plants[0] < 0.35);
        Assert.Single(noon.Shadows);
        Assert.InRange(noon.ShadedFraction, 0.3, 0.34);

        // Edificação bloqueando tudo: tudo à sombra.
        List<ShadeFrame> blocked = HourlyShade.Compute(grid, plants, Array.Empty<CrownObstacle>(), Brasilia, new SolarDate("Julho", 7, 17), 7, 17, v => v, (_, _) => true);
        Assert.All(blocked, f => Assert.All(f.Grid, l => Assert.Equal(0, l)));
    }

    [Fact]
    public void Runs_MergeNeighbours()
    {
        var points = new List<Point2>();
        for (int x = 0; x < 5; x++)
        {
            points.Add(new Point2(x, 0));
        }

        int[] cls = { 0, 0, -1, 1, 1 };
        List<(int Class, Bounds2 Box)> runs = HourlyShade.Runs(points, 1, i => cls[i]);
        Assert.Equal(2, runs.Count);
        Assert.Equal(-0.5, runs.First(r => r.Class == 0).Box.MinX, 9);
        Assert.Equal(1.5, runs.First(r => r.Class == 0).Box.MaxX, 9);
        Assert.Equal(2.5, runs.First(r => r.Class == 1).Box.MinX, 9);
    }

    [Fact]
    public void Geometry_HullAndClip()
    {
        List<Point2> hull = Geo.ConvexHull(new[] { new Point2(0, 0), new Point2(2, 0), new Point2(1, 1), new Point2(2, 2), new Point2(0, 2), new Point2(1, 0.5) });
        Assert.Equal(4, hull.Count);
        List<Point2> clipped = Geo.ClipToRect(new[] { new Point2(-1, -1), new Point2(3, -1), new Point2(3, 3), new Point2(-1, 3) }, new Bounds2(0, 0, 2, 2));
        Assert.Equal(4, Math.Abs(Geo.SignedArea(clipped)), 9);
        Assert.Empty(Geo.ClipToRect(new[] { new Point2(5, 5), new Point2(6, 5), new Point2(6, 6) }, new Bounds2(0, 0, 2, 2)));
    }

    [Fact]
    public void Sheet_DrawsPanelsAndTable()
    {
        CrownObstacle crown = CrownObstacle.For("a", Tree(), new Vec3(5, 5, 0), GrowthStage.Maturidade)!;
        var region = new Polygon2(new[] { new Point2(0, 0), new Point2(20, 0), new Point2(20, 15), new Point2(0, 15) });
        (List<Point2> grid, double cell) = SolarStudy.Grid(region, 1, 400);
        var date = new SolarDate("Julho", 7, 17);
        List<ShadeFrame> frames = HourlyShade.Compute(grid.Select(p => new Vec3(p.X, p.Y, 0.3)).ToList(), new List<(Vec3, string)> { (new Vec3(5, 1, 0.5), "x") },
            new[] { crown }, Brasilia, date, 7, 17, v => v, null);
        Drawing d = ShadeSheet.Build("Sombras por horário", Brasilia, date, frames, grid, cell, region, new[] { crown }, new[] { new ShadeRow("AR-1 Fícus", new[] { 0 }) }, 0, false, "adultas");
        Assert.Contains(d.Texts, t => t.Text == "07:00");
        Assert.Contains(d.Texts, t => t.Text == "17:00");
        Assert.Contains(d.Texts, t => t.Text.StartsWith("sol a", StringComparison.Ordinal));
        Assert.Contains(d.Texts, t => t.Text == "AR-1 Fícus");
        Assert.True(d.Fills.Count < 400, d.Fills.Count.ToString()); // faixas agrupadas, não uma célula por ponto
        Assert.Contains(d.Lines, l => l.Dashed);
    }

    [Fact]
    public void Cardinal_Directions()
    {
        Assert.Equal("N", HourlyShade.Cardinal(2));
        Assert.Equal("NE", HourlyShade.Cardinal(50));
        Assert.Equal("L", HourlyShade.Cardinal(90));
        Assert.Equal("O", HourlyShade.Cardinal(275));
        Assert.Equal("N", HourlyShade.Cardinal(359));
    }
}

public class LeafHabitCatalogTests
{
    [Fact]
    public void CatalogEstimates_AreSensible()
    {
        List<Species> trees = Viridian.Library.SeedCatalog.CreateSpecies().Where(s => s.Category == PlantCategory.Arvore).ToList();
        Assert.All(trees.Where(t => t.ScientificName.StartsWith("Handroanthus", StringComparison.Ordinal)), t =>
        {
            Assert.Equal(LeafHabit.Decidua, t.EffectiveLeafHabit);
            Assert.True(t.EffectiveLeaflessMonths.Contains(8));
        });
        Assert.Equal(LeafHabit.Perenifolia, trees.First(t => t.ScientificName.StartsWith("Ficus benjamina", StringComparison.Ordinal)).EffectiveLeafHabit);
        int deciduous = trees.Count(t => t.EffectiveLeafHabit == LeafHabit.Decidua);
        int semi = trees.Count(t => t.EffectiveLeafHabit == LeafHabit.Semidecidua);
        Assert.InRange(deciduous, 20, 60);
        Assert.InRange(semi, 15, 50);
    }

    [Fact]
    public void SolarSettings_HourlyOptions_Normalize()
    {
        var s = new SolarSettings { FromHour = 30, ToHour = 2, PlanHours = new() { 25, 15, 9, 9 }, Stage = (GrowthStage)99 };
        s.Normalize();
        Assert.Equal(23, s.FromHour);
        Assert.Equal(23, s.ToHour);
        Assert.Equal(new[] { 9, 15 }, s.PlanHours);
        Assert.Equal(GrowthStage.Maturidade, s.Stage);
        SolarSettings copy = s.Clone();
        copy.PlanHours.Add(12);
        Assert.Equal(2, s.PlanHours.Count);
    }
}
