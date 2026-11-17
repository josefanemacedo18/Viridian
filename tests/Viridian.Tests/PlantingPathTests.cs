using Viridian.Core.Distribution;
using Viridian.Core.Geometry;
using Xunit;

namespace Viridian.Tests;

public class PlantingPathTests
{
    private static Point2 P(double x, double y) => new(x, y);

    [Fact]
    public void Chain_JoinsTouchingPiecesInAnyDirection()
    {
        // Três linhas desenhadas em ordem e sentidos misturados formam um único "L" com prolongamento.
        var pieces = new List<IReadOnlyList<Point2>>
        {
            new[] { P(10, 0), P(10, 5) },
            new[] { P(0, 0), P(10, 0) },
            new[] { P(10, 8), P(10, 5) },
        };

        IReadOnlyList<PlantingPath> paths = PathTools.Chain(pieces);

        PlantingPath path = Assert.Single(paths);
        Assert.False(path.Closed);
        Assert.Equal(4, path.Points.Count);
        Assert.Equal(18, path.Length, 6);
        Point2[] ends = { path.Points[0], path.Points[^1] };
        Assert.Contains(ends, e => e.DistanceTo(P(0, 0)) < 1e-9);
        Assert.Contains(ends, e => e.DistanceTo(P(10, 8)) < 1e-9);
    }

    [Fact]
    public void Chain_DetectsClosedLoop()
    {
        var pieces = new List<IReadOnlyList<Point2>>
        {
            new[] { P(0, 0), P(4, 0) },
            new[] { P(4, 0), P(4, 3) },
            new[] { P(0, 3), P(4, 3) },
            new[] { P(0, 3), P(0, 0.005) }, // emenda dentro da tolerância de 1 cm
        };

        PlantingPath path = Assert.Single(PathTools.Chain(pieces));

        Assert.True(path.Closed);
        Assert.Equal(4, path.Points.Count);
        Assert.Equal(14, path.Length, 1);
    }

    [Fact]
    public void Chain_KeepsSeparatePiecesApart()
    {
        var pieces = new List<IReadOnlyList<Point2>>
        {
            new[] { P(0, 0), P(5, 0) },
            new[] { P(0, 10), P(5, 10) },
        };

        Assert.Equal(2, PathTools.Chain(pieces).Count);
    }

    [Fact]
    public void Circle_IsClosedAndHasCorrectRadius()
    {
        PlantingPath circle = PathTools.Circle(P(3, -2), 5);

        Assert.True(circle.Closed);
        Assert.True(circle.Points.Count >= 24);
        Assert.All(circle.Points, p => Assert.Equal(5, p.DistanceTo(P(3, -2)), 9));
        Assert.Equal(2 * Math.PI * 5, circle.Length, 1);
    }

    [Fact]
    public void LinearOnCircle_PutsPlantsOnTheCircumference()
    {
        PlantingPath circle = PathTools.Circle(P(0, 0), 4);
        var request = new DistributionRequest { Pattern = DistributionPattern.Linear, LinearMode = LinearMode.Quantidade, Count = 12 };

        DistributionResult result = PlantDistributor.DistributeAlongPaths(null, new[] { circle }, request);

        Assert.Equal(12, result.Points.Count);
        Assert.All(result.Points, p => Assert.InRange(p.DistanceTo(P(0, 0)), 3.99, 4.0001));
    }

    [Fact]
    public void MultiplePaths_DoNotDuplicatePlantsAtJunctions()
    {
        // Duas linhas que se cruzam no meio: a planta do cruzamento não pode ser duplicada.
        var paths = new[]
        {
            new PlantingPath(new[] { P(-5, 0), P(5, 0) }, false),
            new PlantingPath(new[] { P(0, -5), P(0, 5) }, false),
        };
        var request = new DistributionRequest { Pattern = DistributionPattern.Linear, SpacingM = 1 };

        DistributionResult result = PlantDistributor.DistributeAlongPaths(null, paths, request);

        Assert.Equal(21, result.Points.Count); // 11 + 11 - 1 no cruzamento
        for (int i = 0; i < result.Points.Count; i++)
        {
            for (int j = i + 1; j < result.Points.Count; j++)
            {
                Assert.True(result.Points[i].DistanceTo(result.Points[j]) > 0.5);
            }
        }

        // Todas sobre um dos eixos.
        Assert.All(result.Points, p => Assert.True(Math.Abs(p.X) < 1e-9 || Math.Abs(p.Y) < 1e-9));
    }

    [Fact]
    public void MultiplePaths_RespectMaximum()
    {
        var paths = new[]
        {
            new PlantingPath(new[] { P(0, 0), P(10, 0) }, false),
            new PlantingPath(new[] { P(0, 5), P(10, 5) }, false),
        };
        var request = new DistributionRequest { Pattern = DistributionPattern.Linear, SpacingM = 1, MaxCount = 15 };

        DistributionResult result = PlantDistributor.DistributeAlongPaths(null, paths, request);

        Assert.True(result.Exceeded);
        Assert.Equal(22, result.ComputedCount);
        Assert.Empty(result.Points);
    }
}
