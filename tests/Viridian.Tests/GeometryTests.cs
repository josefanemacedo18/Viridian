using Viridian.Core.Geometry;

namespace Viridian.Tests;

public class GeometryTests
{
    private static Polygon2 Square(double size) => new(new[]
    {
        new Point2(0, 0), new Point2(size, 0), new Point2(size, size), new Point2(0, size),
    });

    [Fact]
    public void SquareAreaAndPerimeter()
    {
        Polygon2 p = Square(10);
        Assert.Equal(100, p.Area, 9);
        Assert.Equal(40, p.Perimeter, 9);
        Assert.False(p.HasSelfIntersections());
    }

    [Fact]
    public void HoleIsSubtractedRegardlessOfOrientation()
    {
        var outer = new[] { new Point2(0, 0), new Point2(10, 0), new Point2(10, 10), new Point2(0, 10) };
        var holeCcw = new[] { new Point2(4, 4), new Point2(6, 4), new Point2(6, 6), new Point2(4, 6) };
        var holeCw = holeCcw.Reverse().ToArray();

        foreach (Point2[] hole in new[] { holeCcw, holeCw })
        {
            var p = new Polygon2(new[] { outer, hole });
            Assert.Equal(96, p.Area, 9);
            Assert.True(p.Contains(new Point2(1, 1)));
            Assert.False(p.Contains(new Point2(5, 5)));
            Assert.False(p.Contains(new Point2(11, 5)));
        }
    }

    [Fact]
    public void ClosingPointAndDuplicatesAreRemoved()
    {
        var p = new Polygon2(new[]
        {
            new Point2(0, 0), new Point2(0, 0), new Point2(2, 0), new Point2(2, 2), new Point2(0, 2), new Point2(0, 0),
        });
        Assert.Equal(4, p.VertexCount);
        Assert.Equal(4, p.Area, 9);
    }

    [Fact]
    public void DegenerateRingsAreIgnored()
    {
        var p = new Polygon2(new[] { new[] { new Point2(0, 0), new Point2(1, 1) } });
        Assert.True(p.IsEmpty);
        Assert.Equal(0, p.Area);
        Assert.False(p.Contains(new Point2(0.5, 0.5)));
    }

    [Fact]
    public void DistanceToBoundaryAndMargin()
    {
        Polygon2 p = Square(10);
        Assert.Equal(2, p.DistanceToBoundary(new Point2(2, 5)), 9);
        Assert.True(p.ContainsWithMargin(new Point2(2, 5), 1.9));
        Assert.False(p.ContainsWithMargin(new Point2(2, 5), 2.1));
    }

    [Fact]
    public void BowtieIsSelfIntersecting()
    {
        var p = new Polygon2(new[] { new Point2(0, 0), new Point2(6, 4), new Point2(6, 0), new Point2(0, 3) });
        Assert.True(p.HasSelfIntersections());

        // Um laço simétrico tem área líquida nula e é descartado como limite inválido.
        var symmetric = new Polygon2(new[] { new Point2(0, 0), new Point2(4, 4), new Point2(4, 0), new Point2(0, 4) });
        Assert.True(symmetric.IsEmpty);
    }

    [Fact]
    public void InteriorPointOfConcavePolygonIsInside()
    {
        // Polígono em "U": o centroide cai no vão.
        var u = new Polygon2(new[]
        {
            new Point2(0, 0), new Point2(10, 0), new Point2(10, 10), new Point2(8, 10),
            new Point2(8, 2), new Point2(2, 2), new Point2(2, 10), new Point2(0, 10),
        });
        Assert.True(u.Contains(u.InteriorPoint()));
    }

    [Fact]
    public void ArcThroughThreePoints()
    {
        Assert.True(ArcInfo.TryFromThreePoints(new Point2(1, 0), new Point2(0, 1), new Point2(-1, 0), out ArcInfo ccw));
        Assert.Equal(0, ccw.Center.X, 9);
        Assert.Equal(0, ccw.Center.Y, 9);
        Assert.Equal(1, ccw.Radius, 9);
        Assert.Equal(Math.PI, ccw.Sweep, 9);

        Assert.True(ArcInfo.TryFromThreePoints(new Point2(1, 0), new Point2(0, -1), new Point2(-1, 0), out ArcInfo cw));
        Assert.Equal(-Math.PI, cw.Sweep, 9);
        Point2 mid = cw.PointAt(0.5);
        Assert.Equal(0, mid.X, 9);
        Assert.Equal(-1, mid.Y, 9);

        Assert.False(ArcInfo.TryFromThreePoints(new Point2(0, 0), new Point2(1, 0), new Point2(2, 0), out _));
    }

    [Fact]
    public void DeterministicRandomIsReproducible()
    {
        var a = new DeterministicRandom(42);
        var b = new DeterministicRandom(42);
        for (int i = 0; i < 100; i++)
        {
            double value = a.NextDouble();
            Assert.Equal(value, b.NextDouble());
            Assert.InRange(value, 0, 0.9999999999999999);
        }

        Assert.NotEqual(new DeterministicRandom(1).NextUInt64(), new DeterministicRandom(2).NextUInt64());
        Assert.Equal(DeterministicRandom.StableHash("Handroanthus"), DeterministicRandom.StableHash("Handroanthus"));
    }
}
