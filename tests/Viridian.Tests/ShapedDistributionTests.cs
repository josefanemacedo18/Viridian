using Viridian.Core.Distribution;
using Viridian.Core.Geometry;

namespace Viridian.Tests;

public class ShapedDistributionTests
{
    private static Polygon2 Square(double size, double x0 = 0, double y0 = 0) => new(new[]
    {
        new Point2(x0, y0), new Point2(x0 + size, y0), new Point2(x0 + size, y0 + size), new Point2(x0, y0 + size),
    });

    private static double DistanceToPath(Point2 p, IReadOnlyList<Point2> path)
    {
        double best = double.PositiveInfinity;
        for (int i = 1; i < path.Count; i++)
        {
            best = Math.Min(best, Geo.DistancePointSegment(p, path[i - 1], path[i]));
        }

        return best;
    }

    private static double MinDistance(IReadOnlyList<Point2> points)
    {
        double best = double.PositiveInfinity;
        for (int i = 0; i < points.Count; i++)
        {
            for (int j = i + 1; j < points.Count; j++)
            {
                best = Math.Min(best, points[i].DistanceTo(points[j]));
            }
        }

        return best;
    }

    [Fact]
    public void LinearPlantsStayOnTheAxisInRealWorldCoordinates()
    {
        // Coordenadas típicas de projeto (centenas de metros da origem) e eixo em várias direções.
        var path = new[] { new Point2(512.37, 1033.12), new Point2(540.10, 1047.55), new Point2(561.00, 1031.20), new Point2(590.40, 1060.00) };
        var request = new DistributionRequest { Pattern = DistributionPattern.Linear, SpacingM = 3.5, Path = path };
        DistributionResult result = PlantDistributor.Distribute(null, request);
        Assert.True(result.Points.Count >= 25);
        Assert.All(result.Points, p => Assert.True(DistanceToPath(p, path) < 1e-6, "planta fora do eixo"));
        for (int i = 1; i < result.Points.Count; i++)
        {
            Assert.True(result.Points[i].DistanceTo(result.Points[i - 1]) <= 3.5 + 1e-6);
        }
    }

    [Fact]
    public void StraightLineHasEqualSpacingAndOptionalOffset()
    {
        var path = new[] { new Point2(0, 0), new Point2(20.5, 0) };
        DistributionResult start = PlantDistributor.Distribute(null, new DistributionRequest { Pattern = DistributionPattern.Linear, SpacingM = 5, Path = path });
        Assert.Equal(new[] { 0.0, 5, 10, 15, 20 }, start.Points.Select(p => Math.Round(p.X, 6)));
        Assert.All(start.Points, p => Assert.Equal(0, p.Y, 9));

        DistributionResult centered = PlantDistributor.Distribute(null, new DistributionRequest
        {
            Pattern = DistributionPattern.Linear, SpacingM = 5, Path = path, LinearAlignment = LinearAlignment.Centralizado,
        });
        Assert.Equal(0.25, centered.Points[0].X, 9);
        Assert.Equal(20.25, centered.Points[^1].X, 9);

        DistributionResult fitted = PlantDistributor.Distribute(null, new DistributionRequest
        {
            Pattern = DistributionPattern.Linear, SpacingM = 5, Path = path, LinearAlignment = LinearAlignment.Ajustado, PathOffsetM = -1.5,
        });
        Assert.Equal(0, fitted.Points[0].X, 9);
        Assert.Equal(20.5, fitted.Points[^1].X, 9);
        Assert.All(fitted.Points, p => Assert.Equal(-1.5, p.Y, 9));
        Assert.Equal(5.125, fitted.EffectiveSpacingM, 9);
    }

    [Fact]
    public void FixedQuantityIsDividedEqually()
    {
        var path = new[] { new Point2(0, 0), new Point2(12, 0) };
        DistributionResult result = PlantDistributor.Distribute(null, new DistributionRequest
        {
            Pattern = DistributionPattern.Linear, LinearMode = LinearMode.Quantidade, Count = 4, Path = path,
        });
        Assert.Equal(new[] { 0.0, 4, 8, 12 }, result.Points.Select(p => Math.Round(p.X, 6)));

        DistributionResult single = PlantDistributor.Distribute(null, new DistributionRequest
        {
            Pattern = DistributionPattern.Linear, LinearMode = LinearMode.Quantidade, Count = 1, Path = path,
        });
        Assert.Equal(6, Assert.Single(single.Points).X, 9);
        Assert.NotEmpty(PlantDistributor.Distribute(null, new DistributionRequest { Pattern = DistributionPattern.Linear, LinearMode = LinearMode.Quantidade, Path = path }).Message);
    }

    [Fact]
    public void DoubleStaggeredRowsFormAZigzag()
    {
        var path = new[] { new Point2(0, 0), new Point2(10, 0) };
        DistributionResult result = PlantDistributor.Distribute(null, new DistributionRequest
        {
            Pattern = DistributionPattern.Linear, SpacingM = 2, Path = path, Rows = 2, RowArrangement = RowArrangement.Alternadas, RowSpacingM = 1,
        });
        var top = result.Points.Where(p => p.Y > 0).ToList();
        var bottom = result.Points.Where(p => p.Y < 0).ToList();
        Assert.Equal(6, bottom.Count);
        Assert.Equal(5, top.Count);
        Assert.All(top, p => Assert.Equal(0.5, p.Y, 9));
        Assert.All(bottom, p => Assert.Equal(-0.5, p.Y, 9));
        Assert.Equal(new[] { 1.0, 3, 5, 7, 9 }, top.Select(p => Math.Round(p.X, 6)).OrderBy(x => x));
    }

    [Fact]
    public void ClosedPathDistributesEvenlyAroundTheLoop()
    {
        var loop = new[] { new Point2(0, 0), new Point2(10, 0), new Point2(10, 10), new Point2(0, 10) };
        DistributionResult result = PlantDistributor.Distribute(null, new DistributionRequest
        {
            Pattern = DistributionPattern.Linear, SpacingM = 2.5, Path = loop, PathClosed = true,
        });
        Assert.Equal(16, result.Points.Count);
        Assert.True(MinDistance(result.Points) >= 2.5 - 1e-6);
    }

    [Fact]
    public void BorderFollowsTheOutlineInsideTheBed()
    {
        Polygon2 bed = Square(10);
        DistributionResult result = PlantDistributor.Distribute(bed, new DistributionRequest
        {
            Pattern = DistributionPattern.Bordadura, SpacingM = 1, EdgeMarginM = 0.4, Rows = 2, RowSpacingM = 0.8,
        });
        Assert.NotEmpty(result.Points);
        Assert.All(result.Points, p =>
        {
            Assert.True(bed.Contains(p));
            double d = bed.DistanceToBoundary(p);
            Assert.True(Math.Abs(d - 0.4) < 0.02 || Math.Abs(d - 1.2) < 0.02, $"distância à borda {d:0.000}");
        });
        Assert.InRange(result.Points.Count(p => Math.Abs(bed.DistanceToBoundary(p) - 0.4) < 0.02), 34, 41);
        // Entre fileiras vale o espaçamento entre fileiras (0,8 m); ao longo da fileira, o espaçamento (1 m).
        Assert.True(MinDistance(result.Points) >= 0.72 - 1e-6);
        Assert.DoesNotContain(result.Points, p => bed.DistanceToBoundary(p) > 2);
        Assert.InRange(result.Points.Count(p => Math.Abs(bed.DistanceToBoundary(p) - 1.2) < 0.02), 20, 31);
    }

    [Fact]
    public void BorderHandlesHolesAndIrregularShapes()
    {
        var region = new Polygon2(new[]
        {
            new[] { new Point2(0, 0), new Point2(20, 0), new Point2(20, 12), new Point2(8, 12), new Point2(0, 6) },
            new[] { new Point2(8, 3), new Point2(12, 3), new Point2(12, 7), new Point2(8, 7) },
        });
        DistributionResult result = PlantDistributor.Distribute(region, new DistributionRequest
        {
            Pattern = DistributionPattern.Bordadura, SpacingM = 0.8, EdgeMarginM = 0.3,
        });
        Assert.NotEmpty(result.Points);
        Assert.All(result.Points, p => Assert.True(region.ContainsWithMargin(p, 0.29)));
        Assert.Contains(result.Points, p => p.X > 7.5 && p.X < 12.5 && p.Y > 2.5 && p.Y < 7.5); // contorna o vazio
    }

    [Fact]
    public void ConcentricRingsStartAtTheCenter()
    {
        Polygon2 bed = Square(20, -10, -10);
        DistributionResult result = PlantDistributor.Distribute(bed, new DistributionRequest
        {
            Pattern = DistributionPattern.Circular, SpacingM = 1.5, RowSpacingM = 2, CircleCenter = new Point2(0, 0),
        });
        Assert.Contains(result.Points, p => p.Length < 1e-9);
        Assert.All(result.Points, p =>
        {
            double ring = p.Length / 2;
            Assert.True(Math.Abs(ring - Math.Round(ring)) < 1e-6, "ponto fora de um anel");
            Assert.True(bed.Contains(p));
        });
    }

    [Fact]
    public void CircleByCenterAndRadius()
    {
        DistributionResult result = PlantDistributor.Distribute(null, new DistributionRequest
        {
            Pattern = DistributionPattern.Circular, SpacingM = 2, CircleCenter = new Point2(100, 50), CircleRadiusM = 6,
        });
        Assert.Equal(19, result.Points.Count);
        Assert.All(result.Points, p => Assert.Equal(6, p.DistanceTo(new Point2(100, 50)), 9));
        Assert.NotEmpty(PlantDistributor.Distribute(null, new DistributionRequest { Pattern = DistributionPattern.Circular, CircleCenter = new Point2(0, 0) }).Message);
    }

    [Fact]
    public void GroupsFormOddClustersWithGaps()
    {
        Polygon2 bed = Square(30);
        var request = new DistributionRequest
        {
            Pattern = DistributionPattern.Agrupamentos, SpacingM = 0.8, GroupSizeMin = 3, GroupSizeMax = 7, GroupGapM = 1.5, Seed = 5,
        };
        DistributionResult result = PlantDistributor.Distribute(bed, request);
        Assert.True(result.Points.Count > 60);
        Assert.True(MinDistance(result.Points) >= 0.8 * 0.92 - 1e-6);
        Assert.All(result.Points, p => Assert.True(bed.Contains(p)));
        Assert.Equal(result.Points, PlantDistributor.Distribute(bed, request).Points);

        // Agrupado: há muito mais vizinhos próximos do que distantes (manchas com respiros entre elas).
        int isolated = result.Points.Count(p => result.Points.Count(q => q.DistanceTo(p) < 1.3) <= 1);
        Assert.True(isolated < result.Points.Count / 4);
    }

    [Fact]
    public void GradientThinsTowardTheEnd()
    {
        Polygon2 bed = Square(30);
        DistributionResult result = PlantDistributor.Distribute(bed, new DistributionRequest
        {
            Pattern = DistributionPattern.Gradiente, SpacingM = 0.8, AngleDeg = 0, GradientEndPercent = 10, Seed = 3,
        });
        int left = result.Points.Count(p => p.X < 10);
        int right = result.Points.Count(p => p.X > 20);
        Assert.True(left > right * 3, $"esquerda {left}, direita {right}");
        Assert.True(MinDistance(result.Points) >= 0.8 - 1e-6);
    }

    [Fact]
    public void NewPatternsRespectObstaclesAndMaximum()
    {
        Polygon2 bed = Square(20);
        var obstacle = new Obstacle(new Point2(10, 10), 3);
        foreach (DistributionPattern pattern in new[] { DistributionPattern.Bordadura, DistributionPattern.Circular, DistributionPattern.Agrupamentos, DistributionPattern.Gradiente })
        {
            var request = new DistributionRequest { Pattern = pattern, SpacingM = 0.7, Obstacles = new[] { obstacle }, CircleCenter = pattern == DistributionPattern.Circular ? new Point2(10, 10) : null };
            DistributionResult result = PlantDistributor.Distribute(bed, request);
            Assert.All(result.Points, p => Assert.True(p.DistanceTo(obstacle.Center) >= 3 - 1e-9, pattern.ToString()));

            request.MaxCount = 5;
            DistributionResult limited = PlantDistributor.Distribute(bed, request);
            Assert.True(limited.Exceeded, pattern.ToString());
            Assert.Empty(limited.Points);
        }
    }
}
