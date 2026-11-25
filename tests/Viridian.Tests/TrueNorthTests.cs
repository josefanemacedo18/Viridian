using Viridian.Core.Geometry;
using Viridian.Core.Model3D;
using Viridian.Core.Solar;

namespace Viridian.Tests;

public class TrueNorthTests
{
    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(-1, 1, 45)]
    [InlineData(1, 1, -45)]
    [InlineData(-1, 0, 90)]
    [InlineData(0, -1, 180)]
    public void FromPoints_MeasuresCounterClockwiseFromTheTopOfThePlan(double dx, double dy, double expected)
    {
        double a = TrueNorth.FromPoints(new Point2(10, 20), new Point2(10 + dx, 20 + dy));
        Assert.Equal(expected, a, 6);

        // A mesma convenção do cálculo solar: girar o norte (0, 1) por esse ângulo dá a direção clicada.
        Vec3 north = new Vec3(0, 1, 0).RotateZ(Geo.DegToRad(a));
        double len = Math.Sqrt(dx * dx + dy * dy);
        Assert.Equal(dx / len, north.X, 6);
        Assert.Equal(dy / len, north.Y, 6);
        Point2 d = TrueNorth.Direction(a);
        Assert.Equal(dx / len, d.X, 6);
    }

    [Fact]
    public void Normalize_AndDescribe()
    {
        Assert.Equal(-90, TrueNorth.Normalize(270), 6);
        Assert.Equal(180, TrueNorth.Normalize(-180), 6);
        Assert.Contains("topo da planta", TrueNorth.Describe(0));
        Assert.Contains("à esquerda", TrueNorth.Describe(12.5));
        Assert.Contains("à direita", TrueNorth.Describe(-30));
        Assert.Throws<ArgumentException>(() => TrueNorth.FromPoints(new Point2(1, 1), new Point2(1, 1)));
    }
}
