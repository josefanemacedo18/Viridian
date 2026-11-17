using Viridian.Core.Detailing;
using Viridian.Core.Geometry;
using Viridian.Core.Model;
using Viridian.Core.Model3D;

namespace Viridian.Tests;

public class MeshPrimitiveTests
{
    /// <summary>Malha fechada, orientada para fora e sem arestas minúsculas.</summary>
    internal static void AssertSolid(MeshPiece mesh, double minEdge = 0.001)
    {
        Assert.True(mesh.TriangleCount > 0);
        Assert.True(mesh.IsWatertight(), "malha aberta ou com arestas duplicadas");
        Assert.True(mesh.SignedVolume() > 0, "faces voltadas para dentro");
        Assert.True(mesh.ShortestEdge() >= minEdge, $"aresta curta: {mesh.ShortestEdge()}");
        foreach (Tri t in mesh.Triangles)
        {
            (Vec3 a, Vec3 b, Vec3 c) = mesh.Corners(t);
            Assert.True((b - a).Cross(c - a).Length > 1e-12, "triângulo degenerado");
        }
    }

    /// <summary>Em sólidos convexos, a normal de cada face aponta para longe do centro.</summary>
    private static void AssertOutward(MeshPiece mesh, Vec3 center)
    {
        foreach (Tri t in mesh.Triangles)
        {
            (Vec3 a, Vec3 b, Vec3 c) = mesh.Corners(t);
            Vec3 normal = (b - a).Cross(c - a);
            Vec3 mid = (a + b + c) * (1.0 / 3);
            Assert.True(normal.Dot(mid - center) > 0);
        }
    }

    [Fact]
    public void Ellipsoid_IsClosedAndOutward()
    {
        var center = new Vec3(1, 2, 3);
        MeshPiece e = Primitives.Ellipsoid(MeshPart.Folhagem, center, 2, 1.5, 1, 12, 7);
        AssertSolid(e);
        AssertOutward(e, center);
        double expected = 4.0 / 3 * Math.PI * 2 * 1.5 * 1;
        Assert.InRange(e.SignedVolume(), expected * 0.8, expected);
    }

    [Fact]
    public void Ellipsoid_WithNoise_StaysClosed()
    {
        MeshPiece e = Primitives.Ellipsoid(MeshPart.Folhagem, Vec3.Zero, 3, 3, 2, 14, 9, 0.1, 42);
        AssertSolid(e);
    }

    [Fact]
    public void Dome_SitsOnGround()
    {
        MeshPiece d = Primitives.Dome(MeshPart.Folhagem, new Vec3(0, 0, 5), 1, 1, 0.6, 10, 4, 0.08, 7);
        AssertSolid(d);
        AssertOutward(d, new Vec3(0, 0, 5.2));
        (Vec3 min, Vec3 max) = d.Bounds();
        Assert.Equal(5, min.Z, 9);
        Assert.InRange(max.Z, 5.5, 5.7);
    }

    [Theory]
    [InlineData(0.3, 0.2)]
    [InlineData(0.3, 0.3)]
    [InlineData(1.0, 0.0)]
    public void Frustum_CylinderAndCone(double baseRadius, double topRadius)
    {
        MeshPiece f = Primitives.Frustum(MeshPart.Tronco, Vec3.Zero, 2, baseRadius, topRadius, 8);
        AssertSolid(f);
        AssertOutward(f, new Vec3(0, 0, topRadius == 0 ? 0.5 : 1));
    }

    [Fact]
    public void Blob_IsClosedAndOutward()
    {
        MeshPiece b = Primitives.Blob(MeshPart.Flor, new Vec3(1, 1, 1), 0.1, 0.4);
        AssertSolid(b);
        AssertOutward(b, new Vec3(1, 1, 1));
    }

    [Fact]
    public void Blade_IsClosed()
    {
        var path = Enumerable.Range(0, 7).Select(i => new Vec3(i * 0.3, 0, 3 + 0.2 * i - 0.06 * i * i)).ToList();
        var widths = Enumerable.Range(0, 7).Select(i => 0.3 * Math.Sin(Math.PI * (0.1 + 0.9 * i / 6.0))).ToList();
        MeshPiece blade = Primitives.Blade(MeshPart.Folhagem, path, widths, 0.03);
        AssertSolid(blade);
    }

    [Fact]
    public void Transform_RotatesAndMoves()
    {
        MeshPiece f = Primitives.Frustum(MeshPart.Tronco, Vec3.Zero, 1, 0.2, 0.2, 8);
        MeshPiece moved = f.Transform(Math.PI / 2, new Vec3(10, 20, 30));
        AssertSolid(moved);
        Assert.Equal(f.SignedVolume(), moved.SignedVolume(), 6);
        Assert.Equal(30, moved.Bounds().Min.Z, 9);
        Assert.Equal(10, (moved.Bounds().Min.X + moved.Bounds().Max.X) / 2, 6);
    }
}

public class TriangulatorTests
{
    private static Polygon2 Square(double size, double x = 0, double y = 0) =>
        new(new[] { new Point2(x, y), new Point2(x + size, y), new Point2(x + size, y + size), new Point2(x, y + size) });

    [Fact]
    public void Square_TwoTriangles()
    {
        Triangulation t = Triangulator.Triangulate(Square(4));
        Assert.Equal(2, t.Triangles.Count);
        Assert.Equal(16, t.Area, 6);
    }

    [Fact]
    public void ClockwiseRing_IsAccepted()
    {
        var cw = new Polygon2(new[] { new Point2(0, 0), new Point2(0, 3), new Point2(3, 3), new Point2(3, 0) });
        Assert.Equal(9, Triangulator.Triangulate(cw).Area, 6);
    }

    [Fact]
    public void ConcaveLShape_CoversArea()
    {
        var l = new Polygon2(new[] { new Point2(0, 0), new Point2(6, 0), new Point2(6, 2), new Point2(2, 2), new Point2(2, 6), new Point2(0, 6) });
        Triangulation t = Triangulator.Triangulate(l);
        Assert.Equal(l.Area, t.Area, 6);
        Assert.Equal(4, t.Triangles.Count);
    }

    [Fact]
    public void Star_CoversArea()
    {
        var points = Enumerable.Range(0, 14).Select(i => Point2.Polar(i % 2 == 0 ? 5 : 2, Geo.TwoPi * i / 14)).ToList();
        var star = new Polygon2(points);
        Assert.Equal(star.Area, Triangulator.Triangulate(star).Area, 6);
    }

    [Fact]
    public void Holes_AreExcluded()
    {
        var outer = new[] { new Point2(0, 0), new Point2(10, 0), new Point2(10, 10), new Point2(0, 10) };
        var hole1 = new[] { new Point2(2, 2), new Point2(4, 2), new Point2(4, 4), new Point2(2, 4) };
        var hole2 = new[] { new Point2(6, 6), new Point2(8, 6), new Point2(8, 8), new Point2(6, 8) };
        var polygon = new Polygon2(new[] { outer, hole1, hole2 });
        Triangulation t = Triangulator.Triangulate(polygon);
        Assert.Equal(100 - 4 - 4, t.Area, 6);
        Assert.Equal(3, t.Rings.Count);
    }

    [Fact]
    public void Circle_ManyVertices()
    {
        var circle = new Polygon2(Enumerable.Range(0, 120).Select(i => Point2.Polar(8, Geo.TwoPi * i / 120)));
        Assert.Equal(circle.Area, Triangulator.Triangulate(circle).Area, 6);
    }

    [Fact]
    public void CleanRing_RemovesDuplicatesAndCollinear()
    {
        var ring = new[] { new Point2(0, 0), new Point2(0, 0), new Point2(2, 0), new Point2(4, 0), new Point2(4, 4), new Point2(0, 4), new Point2(0, 0.001) };
        List<Point2> clean = Triangulator.CleanRing(ring);
        Assert.Equal(4, clean.Count);
    }

    [Fact]
    public void Degenerate_ReturnsEmpty()
    {
        var line = new Polygon2(new[] { new Point2(0, 0), new Point2(1, 0), new Point2(2, 0) });
        Assert.Empty(Triangulator.Triangulate(line).Triangles);
    }
}

public class BedModelTests
{
    [Fact]
    public void FlatBed_IsClosedPrism()
    {
        var l = new Polygon2(new[] { new Point2(0, 0), new Point2(6, 0), new Point2(6, 2), new Point2(2, 2), new Point2(2, 6), new Point2(0, 6) });
        MeshPiece bed = BedModels.Build(l, _ => 10, 0.4);
        MeshPrimitiveTests.AssertSolid(bed);
        Assert.Equal(l.Area * (0.4 + BedModels.Embed), bed.SignedVolume(), 6);
        Assert.Equal(10.4, bed.Bounds().Max.Z, 9);
    }

    [Fact]
    public void BedWithHole_IsClosed()
    {
        var outer = new[] { new Point2(0, 0), new Point2(10, 0), new Point2(10, 10), new Point2(0, 10) };
        var hole = new[] { new Point2(3, 3), new Point2(6, 3), new Point2(6, 6), new Point2(3, 6) };
        var polygon = new Polygon2(new[] { outer, hole });
        MeshPiece bed = BedModels.Build(polygon, _ => 0, 0.2);
        MeshPrimitiveTests.AssertSolid(bed);
        Assert.Equal(91 * 0.25, bed.SignedVolume(), 6);
    }

    [Fact]
    public void SlopedTerrain_FollowsGround()
    {
        var square = new Polygon2(new[] { new Point2(0, 0), new Point2(10, 0), new Point2(10, 10), new Point2(0, 10) });
        MeshPiece bed = BedModels.Build(square, p => p.X * 0.1, 0.3);
        MeshPrimitiveTests.AssertSolid(bed);
        Assert.Equal(1.3, bed.Bounds().Max.Z, 9);
        Assert.Equal(-BedModels.Embed, bed.Bounds().Min.Z, 9);
    }

    [Fact]
    public void InvalidGround_FallsBackToZero()
    {
        var square = new Polygon2(new[] { new Point2(0, 0), new Point2(2, 0), new Point2(2, 2), new Point2(0, 2) });
        MeshPiece bed = BedModels.Build(square, _ => double.NaN, 0.3);
        MeshPrimitiveTests.AssertSolid(bed);
    }

    [Fact]
    public void FlowerPoints_LieOnTop()
    {
        var square = new Polygon2(new[] { new Point2(0, 0), new Point2(10, 0), new Point2(10, 10), new Point2(0, 10) });
        MeshPiece bed = BedModels.Build(square, p => p.Y * 0.05, 0.5);
        IReadOnlyList<Vec3> points = BedModels.FlowerPoints(bed, square, 0.8, 30, 3);
        Assert.InRange(points.Count, 10, 30);
        foreach (Vec3 p in points)
        {
            Assert.True(square.Contains(new Point2(p.X, p.Y)));
            Assert.Equal(p.Y * 0.05 + 0.5, p.Z, 6);
        }
    }
}

public class PlantModelTests
{
    public static IEnumerable<object[]> Shapes() => new[]
    {
        new object[] { PlantCategory.Arvore, CrownForm.Arredondada, PlantShape.ArvoreArredondada },
        new object[] { PlantCategory.Arvore, CrownForm.Espalhada, PlantShape.ArvoreEspalhada },
        new object[] { PlantCategory.Arvore, CrownForm.Colunar, PlantShape.ArvoreColunar },
        new object[] { PlantCategory.Arvore, CrownForm.Piramidal, PlantShape.ArvoreConica },
        new object[] { PlantCategory.Arvore, CrownForm.Irregular, PlantShape.ArvoreIrregular },
        new object[] { PlantCategory.Palmeira, CrownForm.Arredondada, PlantShape.Palmeira },
        new object[] { PlantCategory.Arbusto, CrownForm.Arredondada, PlantShape.Arbusto },
        new object[] { PlantCategory.Trepadeira, CrownForm.Arredondada, PlantShape.Arbusto },
        new object[] { PlantCategory.Herbacea, CrownForm.Arredondada, PlantShape.Touceira },
        new object[] { PlantCategory.Forracao, CrownForm.Arredondada, PlantShape.Touceira },
        new object[] { PlantCategory.Aquatica, CrownForm.Arredondada, PlantShape.Touceira },
        new object[] { PlantCategory.Gramado, CrownForm.Arredondada, PlantShape.Gramado },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void EveryShape_IsMadeOfClosedPieces(PlantCategory category, CrownForm form, PlantShape shape)
    {
        var species = new Species { Category = category, CrownForm = form, AdultHeightM = 8, CrownDiameterM = 6, PlantingHeightM = 1.5 };
        foreach (GrowthStage stage in GrowthModel.Stages)
        {
            (double crown, double height) = GrowthModel.Size(species, stage);
            PlantModel model = PlantModels.Build(species, crown, height, 1234, flower: true);
            Assert.Equal(shape, model.Shape);
            Assert.NotEmpty(model.Pieces);
            foreach (MeshPiece piece in model.Pieces)
            {
                MeshPrimitiveTests.AssertSolid(piece);
            }

            (Vec3 min, Vec3 max) = Bounds(model);
            Assert.InRange(min.Z, -1e-9, 0.05);
            Assert.InRange(max.Z, model.HeightM * 0.75, model.HeightM * 1.25);
            Assert.InRange(max.X - min.X, model.CrownDiameterM * 0.5, model.CrownDiameterM * 1.5);
            Assert.InRange(model.TriangleCount, 8, 1500);
        }
    }

    [Fact]
    public void Trees_HaveTrunkAndFlowers()
    {
        var ipe = new Species { Category = PlantCategory.Arvore, AdultHeightM = 10, CrownDiameterM = 8 };
        PlantModel model = PlantModels.Build(ipe, 8, 10, 5, flower: true);
        Assert.Contains(model.Pieces, p => p.Part == MeshPart.Tronco);
        Assert.Contains(model.Pieces, p => p.Part == MeshPart.Flor);
        Assert.DoesNotContain(PlantModels.Build(ipe, 8, 10, 5, flower: false).Pieces, p => p.Part == MeshPart.Flor);
    }

    [Fact]
    public void SameSeed_SameModel_DifferentSeed_DifferentModel()
    {
        var species = new Species { Category = PlantCategory.Arvore, AdultHeightM = 10, CrownDiameterM = 8 };
        PlantModel a = PlantModels.Build(species, 8, 10, 99, true);
        PlantModel b = PlantModels.Build(species, 8, 10, 99, true);
        PlantModel c = PlantModels.Build(species, 8, 10, 100, true);
        Assert.Equal(a.Pieces.SelectMany(p => p.Vertices), b.Pieces.SelectMany(p => p.Vertices));
        Assert.NotEqual(a.Pieces.SelectMany(p => p.Vertices), c.Pieces.SelectMany(p => p.Vertices));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(double.NaN, double.PositiveInfinity)]
    [InlineData(0.01, 0.01)]
    [InlineData(200, 300)]
    public void ExtremeSizes_AreClamped(double crown, double height)
    {
        foreach (PlantCategory category in Labels.Categories)
        {
            var species = new Species { Category = category };
            PlantModel model = PlantModels.Build(species, crown, height, 1, true);
            foreach (MeshPiece piece in model.Pieces)
            {
                MeshPrimitiveTests.AssertSolid(piece);
            }
        }
    }

    [Fact]
    public void WholeCatalog_BuildsValidModels()
    {
        foreach (Species species in Viridian.Library.SeedCatalog.CreateSpecies())
        {
            (double crown, double height) = GrowthModel.Size(species, GrowthStage.Maturidade);
            PlantModel model = PlantModels.Build(species, crown, height, DeterministicRandom.StableHash(species.Id), true);
            Assert.All(model.Pieces, p => Assert.True(p.IsWatertight() && p.SignedVolume() > 0, species.PopularName));
            Assert.True(model.TriangleCount <= 1500, species.PopularName);
        }
    }

    private static (Vec3 Min, Vec3 Max) Bounds(PlantModel model)
    {
        var all = model.Pieces.SelectMany(p => p.Vertices).ToList();
        return (new Vec3(all.Min(v => v.X), all.Min(v => v.Y), all.Min(v => v.Z)), new Vec3(all.Max(v => v.X), all.Max(v => v.Y), all.Max(v => v.Z)));
    }
}

public class ModelSettingsTests
{
    [Fact]
    public void Normalize_FixesInvalidValues()
    {
        var s = new ModelSettings { MaxPlants = -5, TerrainSearchM = double.NaN, TrunkColor = "marrom" };
        s.Normalize();
        Assert.Equal(4000, s.MaxPlants);
        Assert.Equal(30, s.TerrainSearchM);
        Assert.Equal("#6B4F3A", s.TrunkColor);

        s = new ModelSettings { MaxPlants = 999999, TerrainSearchM = 0.01 };
        s.Normalize();
        Assert.Equal(ModelSettings.HardLimit, s.MaxPlants);
        Assert.Equal(0.5, s.TerrainSearchM);
    }

    [Fact]
    public void OldConfigFile_GetsDefaultModelSettings()
    {
        var settings = Viridian.Core.Config.ViridianJson.Deserialize<Viridian.Core.Config.ViridianSettings>("{\"LibraryRoot\":\"\"}")!;
        settings.Model3D = null!;
        settings.Normalize();
        Assert.NotNull(settings.Model3D);
        Assert.True(settings.Model3D.FollowTerrain);
        Assert.True(settings.Model3D.ShowFlowers);
    }

    [Fact]
    public void Settings_RoundTrip()
    {
        var settings = new Viridian.Core.Config.ViridianSettings();
        settings.Model3D.MaxPlants = 1234;
        settings.Model3D.FollowTerrain = false;
        string json = Viridian.Core.Config.ViridianJson.Serialize(settings);
        var back = Viridian.Core.Config.ViridianJson.Deserialize<Viridian.Core.Config.ViridianSettings>(json)!;
        back.Normalize();
        Assert.Equal(1234, back.Model3D.MaxPlants);
        Assert.False(back.Model3D.FollowTerrain);
    }

    [Fact]
    public void MaqueteState_RegionRoundTrip()
    {
        var outer = new[] { new Point2(0, 0), new Point2(10, 0), new Point2(10, 10), new Point2(0, 10) };
        var hole = new[] { new Point2(3, 3), new Point2(6, 3), new Point2(6, 6), new Point2(3, 6) };
        var state = new MaqueteState
        {
            Stage = GrowthStage.SeisAnos,
            Region = MaqueteState.FromPolygon(new Polygon2(new[] { outer, hole })),
            LevelName = "Térreo",
        };
        string json = System.Text.Json.JsonSerializer.Serialize(state);
        MaqueteState back = Viridian.Core.Config.ViridianJson.Deserialize<MaqueteState>(json)!;
        Assert.False(back.IsWholeProject);
        Assert.Equal(GrowthStage.SeisAnos, back.Stage);
        Assert.Equal("Térreo", back.LevelName);
        Polygon2 polygon = back.RegionPolygon()!;
        Assert.Equal(91, polygon.Area, 6);
        Assert.True(new MaqueteState().IsWholeProject);
        Assert.Null(new MaqueteState().RegionPolygon());
    }
}
