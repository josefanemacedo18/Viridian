using Viridian.Core.Colors;
using Viridian.Core.Detailing;
using Viridian.Core.Geometry;
using Viridian.Core.Sheets;
using Xunit;

namespace Viridian.Tests;

/// <summary>
/// Cada área colorida de um desenho vira uma região preenchida no Revit, que recusa contornos que cruzam a si mesmos.
/// </summary>
public class DrawingFillValidityTests
{
    private const double RevitShortCurveM = 0.0007986 * 1.5;

    private static void AssertFillsAreSimple(Drawing d)
    {
        foreach (DrawFill fill in d.Fills)
        {
            var pts = new List<Point2>();
            foreach (Point2 p in fill.Outline)
            {
                if (pts.Count == 0 || pts[^1].DistanceTo(p) >= RevitShortCurveM)
                {
                    pts.Add(p);
                }
            }

            while (pts.Count > 2 && pts[^1].DistanceTo(pts[0]) < RevitShortCurveM)
            {
                pts.RemoveAt(pts.Count - 1);
            }

            if (pts.Count >= 3)
            {
                Assert.False(new Polygon2(pts).HasSelfIntersections(), $"área {fill.Color} em {new Polygon2(fill.Outline).Bounds} cruza a si mesma");
            }
        }
    }

    [Theory]
    [InlineData(4.0, 3.0)]
    [InlineData(8.0, 5.0)]
    [InlineData(12.0, 7.0)]
    [InlineData(2.5, 2.0)]
    public void PalmFronds_NeverCrossThemselves(double height, double crown)
    {
        for (int seed = 1; seed <= 40; seed++)
        {
            var d = new Drawing();
            Sketch.Palm(d, Point2.Zero, height, crown, new Rgb(0x6B, 0x91, 0x38), seed);
            AssertFillsAreSimple(d);
        }
    }

    [Fact]
    public void SheetFrame_FillsAreSimple()
    {
        AssertFillsAreSimple(SheetFrame.Build(new SheetFrameInfo { Project = "P", SheetNumber = "1", Scale = 100, NorthDeg = 37, Notes = SheetFrame.DefaultNotes }));
    }
}
