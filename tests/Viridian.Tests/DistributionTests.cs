using Viridian.Core.Distribution;
using Viridian.Core.Geometry;
using Viridian.Core.Massing;

namespace Viridian.Tests;

public class DistributionTests
{
    private static Polygon2 Square(double size) => new(new[]
    {
        new Point2(0, 0), new Point2(size, 0), new Point2(size, size), new Point2(0, size),
    });

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
    public void RectangularGridFillsSquareWithMargin()
    {
        var request = new DistributionRequest { Pattern = DistributionPattern.Retangular, SpacingM = 1, EdgeMarginM = 0.25 };
        DistributionResult result = PlantDistributor.Distribute(Square(10), request);
        Assert.False(result.Exceeded);
        Assert.Equal(81, result.Points.Count);
        Assert.All(result.Points, p => Assert.True(Square(10).ContainsWithMargin(p, 0.25)));
        Assert.Equal(1.0, MinDistance(result.Points), 9);
    }

    [Fact]
    public void TriangularGridKeepsSpacing()
    {
        var request = new DistributionRequest { Pattern = DistributionPattern.Triangular, SpacingM = 0.8, EdgeMarginM = 0.3 };
        DistributionResult result = PlantDistributor.Distribute(Square(12), request);
        Assert.True(result.Points.Count > 150);
        Assert.True(MinDistance(result.Points) >= 0.8 - 1e-9);
        Assert.All(result.Points, p => Assert.True(Square(12).ContainsWithMargin(p, 0.3 - 1e-9)));
    }

    [Fact]
    public void JitterKeepsPointsInsideAndIsReproducible()
    {
        var request = new DistributionRequest { Pattern = DistributionPattern.Triangular, SpacingM = 1, EdgeMarginM = 0.3, JitterPercent = 20, Seed = 7 };
        DistributionResult a = PlantDistributor.Distribute(Square(10), request);
        DistributionResult b = PlantDistributor.Distribute(Square(10), request);
        Assert.Equal(a.Points, b.Points);
        Assert.All(a.Points, p => Assert.True(Square(10).ContainsWithMargin(p, 0.3 - 1e-9)));

        request.Seed = 8;
        Assert.NotEqual(a.Points, PlantDistributor.Distribute(Square(10), request).Points);
    }

    [Fact]
    public void RotatedGridStillRespectsRegion()
    {
        var request = new DistributionRequest { Pattern = DistributionPattern.Retangular, SpacingM = 1, AngleDeg = 30, EdgeMarginM = 0.2 };
        DistributionResult result = PlantDistributor.Distribute(Square(10), request);
        Assert.True(result.Points.Count > 60);
        Assert.All(result.Points, p => Assert.True(Square(10).ContainsWithMargin(p, 0.2 - 1e-9)));
        Assert.True(MinDistance(result.Points) >= 1 - 1e-9);
    }

    [Fact]
    public void NaturalDistributionRespectsMinimumDistanceAndSeed()
    {
        var request = new DistributionRequest { Pattern = DistributionPattern.Natural, SpacingM = 1, EdgeMarginM = 0.2, Seed = 11 };
        DistributionResult a = PlantDistributor.Distribute(Square(20), request);
        Assert.False(a.Exceeded);
        Assert.True(a.Points.Count > 200, $"apenas {a.Points.Count} pontos");
        Assert.True(MinDistance(a.Points) >= 1 - 1e-9);
        Assert.All(a.Points, p => Assert.True(Square(20).ContainsWithMargin(p, 0.2 - 1e-9)));
        Assert.Equal(a.Points, PlantDistributor.Distribute(Square(20), request).Points);

        // A estimativa de densidade usada nos quantitativos é coerente com o resultado real.
        double realDensity = a.Points.Count / (19.6 * 19.6);
        double estimated = MassingCalculator.DensityFromSpacing(1, DistributionPattern.Natural);
        Assert.InRange(realDensity / estimated, 0.8, 1.25);
    }

    [Fact]
    public void HolesAndDisconnectedRegionsAreHandled()
    {
        var region = new Polygon2(new[]
        {
            new[] { new Point2(0, 0), new Point2(10, 0), new Point2(10, 10), new Point2(0, 10) },
            new[] { new Point2(3, 3), new Point2(7, 3), new Point2(7, 7), new Point2(3, 7) },
            new[] { new Point2(20, 0), new Point2(25, 0), new Point2(25, 5), new Point2(20, 5) },
        });

        foreach (DistributionPattern pattern in new[] { DistributionPattern.Retangular, DistributionPattern.Triangular, DistributionPattern.Natural })
        {
            var request = new DistributionRequest { Pattern = pattern, SpacingM = 0.7, EdgeMarginM = 0.1 };
            DistributionResult result = PlantDistributor.Distribute(region, request);
            Assert.NotEmpty(result.Points);
            Assert.DoesNotContain(result.Points, p => p.X > 3.05 && p.X < 6.95 && p.Y > 3.05 && p.Y < 6.95);
            Assert.Contains(result.Points, p => p.X > 20);
            Assert.Contains(result.Points, p => p.X < 10);
        }
    }

    [Fact]
    public void ObstaclesAreAvoided()
    {
        var request = new DistributionRequest
        {
            Pattern = DistributionPattern.Retangular,
            SpacingM = 1,
            Obstacles = new[] { new Obstacle(new Point2(5, 5), 2.0) },
        };
        DistributionResult result = PlantDistributor.Distribute(Square(10), request);
        Assert.NotEmpty(result.Points);
        Assert.All(result.Points, p => Assert.True(p.DistanceTo(new Point2(5, 5)) >= 2.0));
    }

    [Fact]
    public void MaximumCountBlocksTheDistribution()
    {
        var request = new DistributionRequest { Pattern = DistributionPattern.Triangular, SpacingM = 0.5, MaxCount = 100 };
        DistributionResult result = PlantDistributor.Distribute(Square(20), request);
        Assert.True(result.Exceeded);
        Assert.Empty(result.Points);
        Assert.True(result.ComputedCount > 100);
        Assert.NotEmpty(result.Message);

        request.Pattern = DistributionPattern.Natural;
        result = PlantDistributor.Distribute(Square(20), request);
        Assert.True(result.Exceeded);
        Assert.Empty(result.Points);
    }

    [Fact]
    public void HugeRequestsAreRefusedInsteadOfFreezing()
    {
        var request = new DistributionRequest { Pattern = DistributionPattern.Triangular, SpacingM = 0.05 };
        DistributionResult result = PlantDistributor.Distribute(Square(500), request);
        Assert.True(result.Exceeded);
        Assert.Empty(result.Points);

        request.Pattern = DistributionPattern.Natural;
        result = PlantDistributor.Distribute(Square(500), request);
        Assert.True(result.Exceeded);
    }

    [Fact]
    public void DensityPatternDerivesSpacing()
    {
        var request = new DistributionRequest { Pattern = DistributionPattern.Densidade, DensityPerM2 = 4, EdgeMarginM = 0 };
        DistributionResult result = PlantDistributor.Distribute(Square(20), request);
        Assert.Equal(Math.Sqrt(2.0 / (Math.Sqrt(3.0) * 4)), result.EffectiveSpacingM, 9);
        Assert.InRange(result.Points.Count / 400.0, 3.5, 4.5);
    }

    [Fact]
    public void LinearDistributionFollowsThePath()
    {
        var request = new DistributionRequest
        {
            Pattern = DistributionPattern.Linear,
            SpacingM = 2,
            Path = new[] { new Point2(0, 0), new Point2(10, 0), new Point2(10, 10) },
        };
        DistributionResult result = PlantDistributor.Distribute(null, request);
        Assert.Equal(11, result.Points.Count);
        Assert.Equal(new Point2(0, 0), result.Points[0]);
        Assert.Equal(10, result.Points[5].X, 9);
        Assert.Equal(0, result.Points[5].Y, 9);
        Assert.Equal(10, result.Points[10].Y, 9);

        request.PathOffsetM = 1;
        result = PlantDistributor.Distribute(null, request);
        Assert.Equal(1, result.Points[1].Y, 9);
    }

    [Fact]
    public void InvalidInputsProduceMessagesNotExceptions()
    {
        Assert.NotEmpty(PlantDistributor.Distribute(null, new DistributionRequest()).Message);
        Assert.NotEmpty(PlantDistributor.Distribute(Square(5), new DistributionRequest { SpacingM = 0 }).Message);
        Assert.NotEmpty(PlantDistributor.Distribute(Square(5), new DistributionRequest { SpacingM = double.NaN }).Message);
        Assert.NotEmpty(PlantDistributor.Distribute(null, new DistributionRequest { Pattern = DistributionPattern.Linear }).Message);
        Assert.NotEmpty(PlantDistributor.Distribute(Square(5), new DistributionRequest { Pattern = DistributionPattern.Densidade }).Message);
        Assert.Empty(PlantDistributor.Distribute(Square(5), new DistributionRequest { Pattern = DistributionPattern.Manual }).Points);
        Assert.NotEmpty(PlantDistributor.Distribute(Square(1), new DistributionRequest { SpacingM = 1, EdgeMarginM = 5 }).Message);
    }

    [Fact]
    public void RandomRotationsAreStable()
    {
        Assert.Equal(PlantDistributor.RandomRotations(20, 5), PlantDistributor.RandomRotations(20, 5));
        Assert.All(PlantDistributor.RandomRotations(50, 9), a => Assert.InRange(a, 0, Geo.TwoPi));
    }
}

public class MassingTests
{
    [Fact]
    public void DensityAndSpacingAreInverse()
    {
        foreach (DistributionPattern pattern in new[] { DistributionPattern.Retangular, DistributionPattern.Triangular, DistributionPattern.Natural, DistributionPattern.Densidade })
        {
            double density = MassingCalculator.DensityFromSpacing(0.4, pattern);
            Assert.Equal(0.4, MassingCalculator.SpacingFromDensity(density, pattern), 9);
        }

        Assert.Equal(1, MassingCalculator.DensityFromSpacing(1, DistributionPattern.Retangular), 9);
        Assert.Equal(0, MassingCalculator.DensityFromSpacing(0, DistributionPattern.Triangular));
        Assert.Equal(0, MassingCalculator.SpacingFromDensity(-1, DistributionPattern.Triangular));
    }

    [Fact]
    public void QuantitiesRoundUp()
    {
        Assert.Equal(160, MassingCalculator.Quantity(10, 16));
        Assert.Equal(161, MassingCalculator.Quantity(10.01, 16));
        Assert.Equal(0, MassingCalculator.Quantity(0, 16));
        Assert.Equal(0, MassingCalculator.Quantity(double.NaN, 16));
        Assert.Equal(110, MassingCalculator.PurchaseQuantity(100, 10));
        Assert.Equal(107, MassingCalculator.PurchaseQuantity(101, 5));
        Assert.Equal(105, MassingCalculator.PurchaseQuantity(100, 5));
        Assert.Equal(100, MassingCalculator.PurchaseQuantity(100, -5));
        Assert.Equal(0, MassingCalculator.PurchaseQuantity(0, 10));
    }

    [Fact]
    public void DensityRulesChooseRepresentation()
    {
        var rules = new DensityRules();
        Assert.Equal(MassingMode.Simbolos, rules.SuggestMode(0.5));
        Assert.Equal(MassingMode.Combinado, rules.SuggestMode(3));
        Assert.Equal(MassingMode.Hachura, rules.SuggestMode(12));
        Assert.Equal(MassingMode.Hachura, rules.SuggestMode(40));
        Assert.False(rules.UseSimplifiedHatch(12));
        Assert.True(rules.UseSimplifiedHatch(40));

        var broken = new DensityRules { LowMax = 5, MediumMax = 2, HighMax = 1 };
        broken.Normalize();
        Assert.True(broken.LowMax < broken.MediumMax && broken.MediumMax < broken.HighMax);
    }

    [Fact]
    public void NextMassingIdContinuesTheSequence()
    {
        Assert.Equal("MC-001", MassingCalculator.NextId(Array.Empty<string>()));
        Assert.Equal("MC-008", MassingCalculator.NextId(new[] { "MC-001", "mc-007", "XX-099", "MC-abc", string.Empty }));
    }
}
