using Viridian.Core.Geometry;
using Viridian.Core.Identification;

namespace Viridian.Tests;

public class LabelLayoutTests
{
    private static bool Overlap(Bounds2 a, Bounds2 b) => a.MinX < b.MaxX && a.MaxX > b.MinX && a.MinY < b.MaxY && a.MaxY > b.MinY;

    [Fact]
    public void FirstLabel_GoesNortheast()
    {
        var layout = new LabelLayout();
        LabelPlacement p = layout.Place(new Point2(0, 0), 1, 2, 0.5, 0.1);
        Assert.Equal(1, p.Box.MinX, 9);
        Assert.Equal(1, p.Box.MinY, 9);
        Assert.False(p.NeedsLeader);
    }

    [Fact]
    public void ClosePlants_GetNonOverlappingLabels()
    {
        var layout = new LabelLayout();
        var boxes = new List<Bounds2>();
        for (int i = 0; i < 12; i++)
        {
            // Plantas a 0,8 m umas das outras com etiquetas de 2 m de largura.
            boxes.Add(layout.Place(new Point2(i * 0.8, 0), 0.5, 2, 0.4, 0.05).Box);
        }

        for (int i = 0; i < boxes.Count; i++)
        {
            for (int j = i + 1; j < boxes.Count; j++)
            {
                Assert.False(Overlap(boxes[i], boxes[j]), $"{i} x {j}");
            }
        }
    }

    [Fact]
    public void ExistingText_IsAvoided()
    {
        var layout = new LabelLayout();
        layout.Occupy(new Bounds2(0.5, 0.5, 5, 3)); // texto existente a nordeste
        LabelPlacement p = layout.Place(new Point2(0, 0), 1, 2, 0.5, 0.1);
        Assert.False(Overlap(p.Box, new Bounds2(0.5, 0.5, 5, 3)));
        Assert.True(p.Box.MaxY <= 0 || p.Box.MaxX <= 0.5); // foi para baixo ou para a esquerda
    }

    [Fact]
    public void EstimateSize_ScalesWithViewScaleAndLines()
    {
        (double w1, double h1) = LabelLayout.EstimateSize("AR-001", 2, 100);
        (double w2, double h2) = LabelLayout.EstimateSize("AR-001\nIpê-amarelo", 2, 200);
        Assert.Equal(6 * 2 * 0.62 * 0.1, w1, 9);
        Assert.True(w2 > w1 * 2 - 1e-9);
        Assert.Equal(h1 * 4, h2, 9);
    }
}
