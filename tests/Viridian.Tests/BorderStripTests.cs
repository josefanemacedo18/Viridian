using Viridian.Core.Geometry;
using Viridian.Core.Paving;

namespace Viridian.Tests;

public class BorderStripTests
{
    private static double Area(IReadOnlyList<Point2> ring)
    {
        double s = 0;
        for (int i = 0; i < ring.Count; i++)
        {
            s += ring[i].Cross(ring[(i + 1) % ring.Count]);
        }

        return Math.Abs(s / 2);
    }

    [Theory]
    [InlineData(StripSide.Centro, -0.15, 0.15)]
    [InlineData(StripSide.Fora, 0, 0.3)]
    [InlineData(StripSide.Dentro, -0.3, 0)]
    public void OpenLine_StripHasWidthAndSide(StripSide side, double minY, double maxY)
    {
        var axis = new[] { new Point2(0, 0), new Point2(5, 0), new Point2(10, 0) };
        List<IReadOnlyList<Point2>> rings = BorderStrip.Build(axis, false, 0.3, side);
        Assert.Single(rings);
        Assert.Equal(3.0, Area(rings[0]), 6); // 10 m × 0,30 m
        Assert.Equal(minY, rings[0].Min(p => p.Y), 6);
        Assert.Equal(maxY, rings[0].Max(p => p.Y), 6);
        Assert.False(new Polygon2(rings).HasSelfIntersections());
    }

    [Fact]
    public void LShape_KeepsConstantWidthAtCorner()
    {
        var axis = new[] { new Point2(0, 0), new Point2(4, 0), new Point2(4, 3) };
        IReadOnlyList<Point2> ring = BorderStrip.Build(axis, false, 0.4, StripSide.Centro)[0];
        // Área de uma faixa em L com cantos em esquadro: comprimento do eixo × largura.
        Assert.Equal(7 * 0.4, Area(ring), 6);
    }

    [Theory]
    [InlineData(StripSide.Fora, 2.4, 2.0)]
    [InlineData(StripSide.Dentro, 2.0, 1.6)]
    [InlineData(StripSide.Centro, 2.2, 1.8)]
    public void ClosedSquare_OutsideAndInside(StripSide side, double outerHalf, double innerHalf)
    {
        var square = new[] { new Point2(-2, -2), new Point2(2, -2), new Point2(2, 2), new Point2(-2, 2) };
        var region = new Polygon2(square);
        foreach (IReadOnlyList<Point2> axis in new[] { square, square.Reverse().ToArray() })
        {
            List<IReadOnlyList<Point2>> rings = BorderStrip.Build(axis, true, 0.4, side, region.Contains);
            Assert.Equal(2, rings.Count);
            double big = rings.Max(r => r.Max(p => p.X));
            double small = rings.Min(r => r.Max(p => p.X));
            Assert.Equal(outerHalf, big, 6);
            Assert.Equal(innerHalf, small, 6);
            var strip = new Polygon2(rings);
            Assert.Equal(outerHalf * outerHalf * 4 - innerHalf * innerHalf * 4, strip.Area, 6);
        }
    }

    [Fact]
    public void InvalidInput_ReturnsNothing()
    {
        Assert.Empty(BorderStrip.Build(new[] { new Point2(0, 0) }, false, 0.3, StripSide.Centro));
        Assert.Empty(BorderStrip.Build(new[] { new Point2(0, 0), new Point2(1, 0) }, false, 0, StripSide.Centro));
        Assert.Empty(BorderStrip.Build(new[] { new Point2(0, 0), new Point2(1, 0) }, false, double.NaN, StripSide.Centro));
    }

    [Fact]
    public void SharpTurn_IsBeveled_NotSpiked()
    {
        var axis = new[] { new Point2(0, 0), new Point2(5, 0), new Point2(0, 0.3) }; // retorno quase total
        IReadOnlyList<Point2> ring = BorderStrip.Build(axis, false, 0.2, StripSide.Centro)[0];
        Assert.True(ring.Max(p => p.X) < 5.3, ring.Max(p => p.X).ToString());
    }
}
