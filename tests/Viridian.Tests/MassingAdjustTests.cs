using Viridian.Core.Config;
using Viridian.Core.Distribution;
using Viridian.Core.Geometry;
using Viridian.Core.Massing;
using Xunit;

namespace Viridian.Tests;

public class MassingAdjustTests
{
    private static Polygon2 Rect(double x0, double y0, double x1, double y1) =>
        new(new[] { new Point2(x0, y0), new Point2(x1, y0), new Point2(x1, y1), new Point2(x0, y1) });

    private static List<Point2> Grid(Polygon2 area, double spacing) =>
        PlantDistributor.Distribute(area, new DistributionRequest { Pattern = DistributionPattern.Retangular, SpacingM = spacing, Seed = 7 }).Points.ToList();

    [Fact]
    public void Enlarged_KeepsExistingAndPlantsOnlyTheNewPart()
    {
        Polygon2 old = Rect(0, 0, 4, 4), now = Rect(0, 0, 8, 4);
        // Exemplares atuais, um deles movido à mão.
        var existing = Grid(old, 0.5);
        existing[0] = existing[0] + new Point2(0.1, 0.05);
        MassingAdjustPlan plan = MassingAdjust.Plan(now, old, existing, Grid(now, 0.5), 0.5);

        Assert.Equal(existing.Count, plan.Inside.Count);
        Assert.Empty(plan.Outside);
        Assert.NotEmpty(plan.Added);
        Assert.All(plan.Added, p => Assert.False(old.Contains(p)));
        Assert.All(plan.Added, p => Assert.True(now.Contains(p)));
        // Nenhum ponto novo colado a um existente.
        Assert.All(plan.Added, p => Assert.All(existing, e => Assert.True((e - p).Length >= 0.5 * MassingAdjust.MinDistanceFactor - 1e-9)));
        Assert.False(plan.WithoutPrevious);
    }

    [Fact]
    public void Reduced_ReportsOutsideAndAddsNothing()
    {
        Polygon2 old = Rect(0, 0, 8, 4), now = Rect(0, 0, 4, 4);
        var existing = Grid(old, 0.5);
        MassingAdjustPlan plan = MassingAdjust.Plan(now, old, existing, Grid(now, 0.5), 0.5);
        Assert.Empty(plan.Added);
        Assert.NotEmpty(plan.Outside);
        Assert.Equal(existing.Count, plan.Inside.Count + plan.Outside.Count);
        Assert.All(plan.Outside, i => Assert.False(now.Contains(existing[i])));
    }

    [Fact]
    public void DeliberateHoleInsideOldArea_IsNotRefilled()
    {
        Polygon2 old = Rect(0, 0, 4, 4), now = Rect(0, 0, 6, 4);
        // O projetista apagou as plantas do centro do maciço antigo.
        var existing = Grid(old, 0.5).Where(p => (p - new Point2(2, 2)).Length > 1.0).ToList();
        MassingAdjustPlan plan = MassingAdjust.Plan(now, old, existing, Grid(now, 0.5), 0.5);
        Assert.DoesNotContain(plan.Added, p => (p - new Point2(2, 2)).Length <= 1.0);
    }

    [Fact]
    public void KeptOutsidePlants_BlockNewPointsButRemovedOnesDoNot()
    {
        // Exemplar fora do contorno, a 0,3 m de um ponto novo possível.
        Polygon2 now = Rect(0, 0, 4, 4);
        var stray = new List<Point2> { new(4.2, 2) };
        var layout = new List<Point2> { new(3.9, 2) };
        Assert.Empty(MassingAdjust.Plan(now, null, stray, layout, 0.5, OutsidePlants.Avisar).Added);
        Assert.Single(MassingAdjust.Plan(now, null, stray, layout, 0.5, OutsidePlants.Remover).Added);
    }

    [Fact]
    public void WithoutPrevious_FillsGapsByDistance()
    {
        Polygon2 now = Rect(0, 0, 6, 4);
        var existing = Grid(Rect(0, 0, 4, 4), 0.5);
        MassingAdjustPlan plan = MassingAdjust.Plan(now, null, existing, Grid(now, 0.5), 0.5);
        Assert.True(plan.WithoutPrevious);
        Assert.NotEmpty(plan.Added);
        Assert.All(plan.Added, p => Assert.True(p.X > 3.6));
    }

    [Fact]
    public void SameBoundary_ToleratesMillimetres()
    {
        Assert.True(MassingAdjust.SameBoundary(Rect(0, 0, 4, 4), Rect(0, 0, 4.0004, 4)));
        Assert.False(MassingAdjust.SameBoundary(Rect(0, 0, 4, 4), Rect(0, 0, 4.01, 4)));
        Assert.False(MassingAdjust.SameBoundary(null, Rect(0, 0, 4, 4)));
    }

    [Fact]
    public void Record_StoresBoundary_ClonesDeeplyAndSurvivesJson()
    {
        var record = new MassingRecord { Id = "MC-001" };
        Assert.Null(record.StoredBoundary());
        record.StoreBoundary(Rect(0, 0, 3, 2));
        MassingRecord copy = record.Clone();
        copy.Boundary![0][0] = new Point2(9, 9);
        Assert.Equal(new Point2(0, 0), record.Boundary![0][0]);

        MassingRecord back = ViridianJson.Deserialize<MassingRecord>(ViridianJson.Serialize(record))!;
        Polygon2? polygon = back.StoredBoundary();
        Assert.NotNull(polygon);
        Assert.True(MassingAdjust.SameBoundary(polygon, Rect(0, 0, 3, 2)));
        Assert.Equal(6, polygon!.Area, 6);

        // Registros antigos, sem o campo, continuam legíveis.
        MassingRecord legacy = ViridianJson.Deserialize<MassingRecord>("{\"Id\":\"MC-002\",\"AreaM2\":4}")!;
        Assert.Null(legacy.StoredBoundary());
    }
}
