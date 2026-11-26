using Viridian.Core.Geometry;
using Viridian.Core.Solar;
using Xunit;

namespace Viridian.Tests;

/// <summary>
/// O Revit recusa uma região preenchida cujos contornos se tocam ("curve loops intersect with each other"): as faixas do
/// mapa solar e das sombras precisam sair separadas por uma fresta.
/// </summary>
public class RegionStripsTests
{
    private static (List<Point2> Points, Func<int, int> Class) Lattice(int nx, int ny, double cell, int seed)
    {
        var random = new Random(seed);
        var points = new List<Point2>();
        var classes = new List<int>();
        for (int j = 0; j < ny; j++)
        {
            for (int i = 0; i < nx; i++)
            {
                points.Add(new Point2(10 + i * cell, -5 + j * cell));
                // manchas grandes (como sombras reais) com algum ruído e pontos "sem desenho" (-1)
                int cls = ((i / 4) + (j / 3)) % 3;
                classes.Add(random.NextDouble() < 0.15 ? random.Next(-1, 3) : cls);
            }
        }

        return (points, i => classes[i]);
    }

    private static bool Separated(Bounds2 a, Bounds2 b) => a.MaxX < b.MinX || b.MaxX < a.MinX || a.MaxY < b.MinY || b.MaxY < a.MinY;

    [Theory]
    [InlineData(0.5, 1)]
    [InlineData(1.0, 2)]
    [InlineData(0.25, 3)]
    [InlineData(2.0, 4)]
    public void Strips_OfTheSameClassNeverTouch(double cell, int seed)
    {
        (List<Point2> points, Func<int, int> classOf) = Lattice(40, 30, cell, seed);
        List<(int Class, Bounds2 Box)> runs = HourlyShade.Runs(points, cell, classOf);
        double gap = Math.Max(cell * 0.01, 0.003);
        List<(int Class, Bounds2 Box)> strips = HourlyShade.RegionStrips(runs, gap);

        // Sem a fresta, as faixas de linhas vizinhas se tocam (é o que o Revit recusava).
        Assert.Contains(runs, r => runs.Any(o => o.Class == r.Class && o.Box != r.Box && !Separated(o.Box, r.Box)));

        foreach (IGrouping<int, (int Class, Bounds2 Box)> group in strips.GroupBy(s => s.Class))
        {
            List<Bounds2> boxes = group.Select(g => g.Box).ToList();
            for (int i = 0; i < boxes.Count; i++)
            {
                Assert.True(boxes[i].MaxX > boxes[i].MinX && boxes[i].MaxY > boxes[i].MinY);
                for (int k = i + 1; k < boxes.Count; k++)
                {
                    Assert.True(Separated(boxes[i], boxes[k]), $"classe {group.Key}: {boxes[i]} toca {boxes[k]}");
                }
            }
        }

        // A área coberta continua praticamente a mesma (só a fresta sai), e nenhuma classe some.
        foreach (int cls in runs.Select(r => r.Class).Distinct())
        {
            double before = runs.Where(r => r.Class == cls).Sum(r => r.Box.Width * r.Box.Height);
            double after = strips.Where(r => r.Class == cls).Sum(r => r.Box.Width * r.Box.Height);
            Assert.InRange(after, before * 0.9, before);
        }

        // Faixas empilhadas com a mesma largura viram um retângulo só: menos contornos que faixas.
        Assert.True(strips.Count <= runs.Count);
    }

    [Fact]
    public void Strips_StackedEqualRunsMergeIntoOneRectangle()
    {
        var runs = new List<(int, Bounds2)>
        {
            (0, new Bounds2(0, 0, 3, 1)),
            (0, new Bounds2(0, 1, 3, 2)),
            (0, new Bounds2(0, 2, 3, 3)),
            (1, new Bounds2(3, 0, 5, 1)),
        };
        List<(int Class, Bounds2 Box)> strips = HourlyShade.RegionStrips(runs, 0.01);
        Assert.Equal(2, strips.Count);
        Bounds2 merged = strips.Single(s => s.Class == 0).Box;
        Assert.Equal(0.01, merged.MinX, 9);
        Assert.Equal(2.99, merged.MaxY, 9);
    }

    [Fact]
    public void Strips_SmallerThanTheGapAreDropped()
    {
        var runs = new List<(int, Bounds2)> { (0, new Bounds2(0, 0, 0.004, 1)) };
        Assert.Empty(HourlyShade.RegionStrips(runs, 0.003));
    }
}
