using Viridian.Core.Colors;
using Viridian.Core.Geometry;
using Viridian.Core.Massing;
using Viridian.Core.Model;
using Viridian.Core.Model3D.Illustration;
using Viridian.Library;

namespace Viridian.Tests;

public class BedIllustrationTests
{
    private static readonly List<Species> Catalog = SeedCatalog.CreateSpecies();

    private static Species Find(PlantCategory category, Func<Species, bool>? extra = null) =>
        Catalog.First(s => s.Category == category && (extra?.Invoke(s) ?? true));

    private static Polygon2 Rect(double w, double h) => new(new[] { new Point2(0, 0), new Point2(w, 0), new Point2(w, h), new Point2(0, h) });

    [Fact]
    public void Carpet_CoversTheBed_AndFollowsHoles()
    {
        var ring = new Polygon2(new[]
        {
            new[] { new Point2(0, 0), new Point2(10, 0), new Point2(10, 6), new Point2(0, 6) },
            new[] { new Point2(4, 2), new Point2(4, 4), new Point2(6, 4), new Point2(6, 2) },
        });
        Species shrub = Find(PlantCategory.Arbusto);
        FlatMesh carpet = BedIllustration.Carpet(ring, shrub, new ColorSettings(), 3);
        double baseArea = 0;
        for (int i = 0; i + 2 < carpet.Triangles.Count; i += 3)
        {
            SpriteVertex a = carpet.Vertices[carpet.Triangles[i]], b = carpet.Vertices[carpet.Triangles[i + 1]], c = carpet.Vertices[carpet.Triangles[i + 2]];
            double area = ((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X)) / 2;
            Assert.True(area >= 0, "triângulo do tapete virado para baixo");
            if (a.Z == BedIllustration.CarpetLift)
            {
                baseArea += area;
            }
        }

        Assert.Equal(56, baseArea, 3);

        // Manchas só dentro do maciço (fora do furo).
        Assert.All(carpet.Vertices.Where(v => v.Z > BedIllustration.CarpetLift), v => Assert.False(v.X > 4.4 && v.X < 5.6 && v.Y > 2.4 && v.Y < 3.6));
    }

    [Fact]
    public void Lawn_IsOnlyCarpet_ShrubBed_GetsPlants_UpToTheLimit()
    {
        Species lawn = Find(PlantCategory.Gramado);
        BedVisual grass = BedIllustration.Build(Rect(20, 10), lawn, new MassingRecord { Id = "M1", Seed = 5 }, new ColorSettings(), true, true);
        Assert.Empty(grass.Points);
        Assert.True(grass.Carpet.TriangleCount > 100);

        Species shrub = Find(PlantCategory.Arbusto, s => s.AdultHeightM > 0.6);
        BedVisual bed = BedIllustration.Build(Rect(4, 3), shrub, new MassingRecord { Id = "M2", Seed = 7, SpacingM = 0.6 }, new ColorSettings(), true, true);
        Assert.InRange(bed.Points.Count, 10, BedIllustration.MaxPlantsPerBed);
        Assert.Equal(bed.Points.Count, bed.Rotations.Count);
        Assert.All(bed.Points, p => Assert.True(p.X >= 0 && p.X <= 4 && p.Y >= 0 && p.Y <= 3));

        BedVisual huge = BedIllustration.Build(Rect(80, 60), shrub, new MassingRecord { Id = "M3", Seed = 7, SpacingM = 0.5 }, new ColorSettings(), false, true);
        Assert.True(huge.Points.Count <= BedIllustration.MaxPlantsPerBed);
        Assert.Empty(huge.Carpet.Triangles);

        Species low = Find(PlantCategory.Forracao, s => s.AdultHeightM <= 0.5);
        BedVisual groundcover = BedIllustration.Build(Rect(30, 20), low, new MassingRecord { Id = "M4", Seed = 9, SpacingM = 0.25 }, new ColorSettings(), true, true);
        Assert.True(groundcover.Points.Count <= BedIllustration.MaxLowPlantsPerBed);
    }

    [Fact]
    public void Bed_IsReproducible()
    {
        Species shrub = Find(PlantCategory.Arbusto);
        var record = new MassingRecord { Id = "M9", Seed = 42, SpacingM = 0.5 };
        BedVisual a = BedIllustration.Build(Rect(5, 5), shrub, record, new ColorSettings(), true, true);
        BedVisual b = BedIllustration.Build(Rect(5, 5), shrub, record, new ColorSettings(), true, true);
        Assert.Equal(a.Points, b.Points);
        Assert.Equal(a.Carpet.Vertices, b.Carpet.Vertices);
    }
}
