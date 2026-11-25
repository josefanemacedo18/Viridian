using Viridian.Core.Colors;
using Viridian.Core.Geometry;
using Viridian.Core.Model;
using Viridian.Core.Model3D;
using Viridian.Core.Model3D.Illustration;
using Viridian.Core.Model3D.Profiles;
using Viridian.Library;

namespace Viridian.Tests;

public class SpriteTests
{
    private static readonly List<Species> Catalog = SeedCatalog.CreateSpecies();
    private static readonly List<Plant3DProfile> Profiles = Plant3DProfileDefaults.Create();
    private static readonly Dictionary<string, PlantSprites> Cache = new();

    private static PlantSprites Sprites(string name)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(name, out PlantSprites? sprites))
            {
                Species s = Catalog.First(x => x.ScientificName == name);
                sprites = PlantSpritesPlanner.Build(s, 0, Profiles, new ColorSettings(), "#6B4F3A");
                Cache[name] = sprites;
            }

            return sprites;
        }
    }

    public static IEnumerable<object[]> PilotNames() => PlantSpritesPlanner.Showcase.Select(n => new object[] { n });

    [Fact]
    public void EverySpecies_IsIllustrated_ExceptCustomFamilies()
    {
        foreach (string name in PlantSpritesPlanner.Showcase)
        {
            Assert.Contains(Catalog, s => s.ScientificName == name);
        }

        Assert.All(Catalog, s => Assert.True(PlantSpritesPlanner.IsIllustrated(s), s.DisplayName));
        Species custom = Viridian.Core.Config.ViridianJson.Deserialize<Species>(Viridian.Core.Config.ViridianJson.Serialize(Catalog[0]))!;
        custom.CustomPlantingFamilyFile = "escritorio.rfa";
        Assert.False(PlantSpritesPlanner.IsIllustrated(custom));
    }

    [Fact]
    public void OneSpeciesOfEachCategory_DrawsCleanly_FromTheSideAndFromAbove()
    {
        foreach (IGrouping<PlantCategory, Species> group in Catalog.GroupBy(s => s.Category))
        {
            Species s = group.OrderBy(x => x.Code).First();
            PlantSprites sprites = PlantSpritesPlanner.Build(s, 0, Profiles, new ColorSettings(), "#6B4F3A");
            foreach (int view in new[] { 3, PlantSprites.TopView })
            {
                SpriteMesh mesh = sprites.Get(PlantSpritesPlanner.DefaultStage, view);
                Assert.True(mesh.TriangleCount > 0 && mesh.TriangleCount < 30000, $"{s.DisplayName} v{view}: {mesh.TriangleCount} triângulos");
                double regions = mesh.Regions.Regions.Sum(r => r.Loops.Sum(l => Geo.SignedArea(l)));
                Assert.True(Math.Abs(mesh.CoveredArea() - regions) <= regions * 0.03, $"{s.DisplayName} v{view}: área");
            }
        }
    }

    [Fact]
    public void Earcut_TriangulatesASquareWithAHole_WithTheRightArea()
    {
        var outer = new List<Point2> { new(0, 0), new(10, 0), new(10, 10), new(0, 10) };
        var hole = new List<Point2> { new(3, 3), new(3, 7), new(7, 7), new(7, 3) };
        List<int> tris = Earcut.Triangulate(outer, new[] { hole });
        List<Point2> all = outer.Concat(hole).ToList();
        Assert.Equal(0, tris.Count % 3);
        double area = 0;
        for (int i = 0; i < tris.Count; i += 3)
        {
            Point2 a = all[tris[i]], b = all[tris[i + 1]], c = all[tris[i + 2]];
            double t = ((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X)) / 2;
            Assert.True(t > 0, "triângulo fora do sentido anti-horário");
            area += t;
        }

        Assert.Equal(84, area, 6);
    }

    [Fact]
    public void Earcut_ToleratesDegenerateInput()
    {
        Assert.Empty(Earcut.Triangulate(new List<Point2> { new(0, 0), new(1, 1) }));
        Assert.Empty(Earcut.Triangulate(new List<Point2> { new(0, 0), new(1, 1), new(2, 2) }));
        var bowtie = new List<Point2> { new(0, 0), new(2, 2), new(2, 0), new(0, 2) };
        Assert.Equal(0, Earcut.Triangulate(bowtie).Count % 3);
        var repeated = new List<Point2> { new(0, 0), new(0, 0), new(4, 0), new(4, 4), new(4, 4), new(0, 4) };
        Assert.Equal(6, Earcut.Triangulate(repeated).Count);
    }

    [Theory]
    [MemberData(nameof(PilotNames))]
    public void AdultImages_CoverTheRegions_WithAnOutline_AndAreLight(string name)
    {
        PlantSprites sprites = Sprites(name);
        foreach (int view in new[] { 0, 5, PlantSprites.TopView })
        {
            SpriteMesh mesh = sprites.Get(PlantSpritesPlanner.DefaultStage, view);
            Assert.True(mesh.TriangleCount > 20, $"{name} v{view}: {mesh.TriangleCount} triângulos");

            // Plantas baixas (rosetas, forrações) têm contorno no tom da folhagem, não preto.
            if (sprites.Get(PlantSpritesPlanner.DefaultStage, 2).MaxY >= 0.6)
            {
                Assert.NotEmpty(mesh.OutlineTriangles);
            }

            // Orçamento do nível de detalhe: até 30 mil triângulos por imagem.
            Assert.True(mesh.TriangleCount < 30000, $"{name} v{view}: {mesh.TriangleCount} triângulos");

            // A malha cobre a mesma área das regiões de cor (contornos externos menos os furos).
            double regions = mesh.Regions.Regions.Sum(r => r.Loops.Sum(l => Geo.SignedArea(l)));
            double covered = mesh.CoveredArea();
            Assert.True(Math.Abs(covered - regions) <= regions * 0.02, $"{name} v{view}: malha {covered:0.###} m² × regiões {regions:0.###} m²");

            // O contorno preto fica na frente de todas as cores.
            if (mesh.OutlineTriangles.Count > 0)
            {
                double blackZ = mesh.Vertices.Where(v => v.Outline).Max(v => v.Z);
                Assert.All(mesh.Vertices.Where(v => !v.Outline), v => Assert.True(v.Z < blackZ));
            }
        }
    }

    [Fact]
    public void SideImages_StandOnTheGround_AndTheTopImageIsCentered()
    {
        PlantSprites ipe = Sprites("Handroanthus chrysotrichus");
        SpriteMesh side = ipe.Get(PlantSpritesPlanner.DefaultStage, 0);
        SpriteStage adult = ipe.StageFor(PlantSpritesPlanner.DefaultStage);
        Assert.InRange(side.MinY, -0.2, 0.05);
        Assert.InRange(side.MaxY, adult.HeightM * 0.85, adult.HeightM * 1.15);
        SpriteMesh top = ipe.Get(PlantSpritesPlanner.DefaultStage, PlantSprites.TopView);
        Assert.InRange((top.MinX + top.MaxX) / 2, -adult.CrownM * 0.2, adult.CrownM * 0.2);
        Assert.InRange((top.MinY + top.MaxY) / 2, -adult.CrownM * 0.2, adult.CrownM * 0.2);
    }

    [Fact]
    public void DeciduousSpecies_GetALeaflessStage_OthersDoNot()
    {
        Assert.Contains(Sprites("Handroanthus chrysotrichus").Stages, s => s.Code == 5);
        Assert.DoesNotContain(Sprites("Syagrus romanzoffiana").Stages, s => s.Code == 5);
        Assert.DoesNotContain(Sprites("Agave attenuata").Stages, s => s.Code == 5);

        // Uma perene que receba "sem folhas" mostra a adulta.
        Assert.Equal(PlantSpritesPlanner.DefaultStage, Sprites("Syagrus romanzoffiana").StageFor(5).Code);
    }

    [Fact]
    public void SmallerStages_AreShorter()
    {
        PlantSprites ipe = Sprites("Handroanthus chrysotrichus");
        Assert.True(ipe.Get(1, 0).MaxY < ipe.Get(4, 0).MaxY * 0.7);
        Assert.True(ipe.StageFor(1).HeightM < ipe.StageFor(2).HeightM);
    }

    [Fact]
    public void ImagesAreCached_AndSafeFromManyThreads()
    {
        PlantSprites agave = Sprites("Agave attenuata");
        var meshes = new SpriteMesh[8];
        Parallel.For(0, meshes.Length, i => meshes[i] = agave.Get(3, 7));
        Assert.All(meshes, m => Assert.Same(meshes[0], m));
        Assert.Same(meshes[0], agave.TryGet(3, 7));
        Assert.NotNull(agave.TryGetNearest(3, 8));
    }

    [Fact]
    public void ViewIndex_WrapsAround()
    {
        Assert.Equal(0, PlantSprites.ViewIndex(0));
        Assert.Equal(0, PlantSprites.ViewIndex(2 * Math.PI));
        Assert.Equal(PlantSprites.SideViews / 4, PlantSprites.ViewIndex(Math.PI / 2));
        Assert.Equal(PlantSprites.SideViews - 1, PlantSprites.ViewIndex(-Geo.TwoPi / PlantSprites.SideViews));
        Assert.Equal(PlantSprites.SideViews / 2, PlantSprites.ViewIndex(-Math.PI));
    }

    [Fact]
    public void Billboard_AlwaysFacesTheCamera_AndStaysUpright()
    {
        var basePoint = new Vec3(10, 5, 0);
        foreach (double azimuth in new[] { 0.0, 0.7, 2.1, 3.9, 5.5 })
        {
            foreach (double pitch in new[] { 0.0, 0.3, 0.6 })
            {
                var toEye = new Vec3(Math.Cos(azimuth) * Math.Cos(pitch), Math.Sin(azimuth) * Math.Cos(pitch), Math.Sin(pitch));
                Vec3 direction = -toEye;
                Vec3 up = (new Vec3(0, 0, 1) - toEye * toEye.Z).Normalized();
                SpriteFrame frame = SpriteBillboard.Place(basePoint, 0.4, 8, basePoint + toEye * 50, direction, up, false, 0.1);
                Assert.NotEqual(PlantSprites.TopView, frame.View);
                Assert.Equal(1, frame.Normal.Dot(toEye), 6);
                Assert.Equal(0, frame.Right.Dot(frame.Up), 6);
                Assert.Equal(0, frame.Right.Dot(frame.Normal), 6);
                Assert.True(frame.Up.Z > 0.7, "a imagem deve ficar em pé na tela");
                Assert.True(frame.Right.Z == 0 || Math.Abs(frame.Right.Z) < 1e-9, "sem inclinação lateral");
                Vec3 cross = frame.Right.Cross(frame.Up);
                Assert.Equal(1, cross.Dot(frame.Normal), 6);

                // A imagem escolhida é a da direção de onde se olha, no sistema da família (rotação 0,4).
                Assert.Equal(PlantSprites.ViewIndex(azimuth - 0.4), frame.View);
            }
        }
    }

    [Fact]
    public void Billboard_FromAbove_UsesTheTopImage_AndPerspectiveFacesTheEye()
    {
        var basePoint = new Vec3(0, 0, 0);
        SpriteFrame top = SpriteBillboard.Place(basePoint, 0.5, 10, new Vec3(0, 0, 100), new Vec3(0.1, 0, -1).Normalized(), new Vec3(1, 0, 0.1).Normalized(), false, 0.1);
        Assert.Equal(PlantSprites.TopView, top.View);
        Assert.Equal(1, top.Normal.Z, 6);
        Assert.Equal(10 * SpriteBillboard.TopViewHeightFraction, top.Origin.Z, 6);
        Assert.Equal(Math.Cos(0.5), top.Right.X, 6);

        // Em perspectiva, duas plantas lado a lado encaram o olho (normais diferentes).
        var eye = new Vec3(0, -20, 1.7);
        var direction = new Vec3(0, 1, 0);
        var up = new Vec3(0, 0, 1);
        SpriteFrame left = SpriteBillboard.Place(new Vec3(-8, 0, 0), 0, 3.4, eye, direction, up, true, 0);
        SpriteFrame right = SpriteBillboard.Place(new Vec3(8, 0, 0), 0, 3.4, eye, direction, up, true, 0);
        Assert.True(left.Normal.X > 0.2 && right.Normal.X < -0.2);
        Assert.Equal(1, left.Normal.Dot((eye - new Vec3(-8, 0, 1.7)).Normalized()), 6);
    }

    [Fact]
    public void Drawing_FollowsTheSpeciesData_HeightAndStyle()
    {
        // Altura do desenho adulto próxima da ficha (as rosetas rígidas incluem a haste floral na ficha).
        foreach (string name in new[] { "Handroanthus chrysotrichus", "Syagrus romanzoffiana", "Strelitzia reginae", "Dracaena marginata" })
        {
            Species s = Catalog.First(x => x.ScientificName == name);
            SpriteMesh side = Sprites(name).Get(PlantSpritesPlanner.DefaultStage, 2);
            Assert.InRange(side.MaxY, s.AdultHeightM * 0.7, s.AdultHeightM * 1.35);
        }

        // Estilo pela espécie: folhas em leque no ipê, pinadas no flamboyant, anéis no estipe das palmeiras, sem chão.
        (Plant3DProfile ipeProfile, _) = ProfileResolver.Resolve(Catalog.First(x => x.ScientificName == "Handroanthus chrysotrichus"), Profiles);
        CardStyle ipe = PlantSpritesPlanner.StyleFor(Catalog.First(x => x.ScientificName == "Handroanthus chrysotrichus"), ipeProfile, 4);
        Assert.Equal(LeafShape.Palmada, ipe.Leaf);
        Assert.False(ipe.Ground);
        Species jeriva = Catalog.First(x => x.ScientificName == "Syagrus romanzoffiana");
        Assert.Equal(BarkKind.Aneis, PlantSpritesPlanner.StyleFor(jeriva, ProfileResolver.Resolve(jeriva, Profiles).Profile, 4).Bark);
        Assert.True(PlantSpritesPlanner.StyleFor(jeriva, ProfileResolver.Resolve(jeriva, Profiles).Profile, 1).FlowerDensity < 1);
    }

    [Fact]
    public void Hash_IsStable_AndFollowsTheSpeciesData()
    {
        Species ipe = Catalog.First(x => x.ScientificName == "Handroanthus chrysotrichus");
        string a = PlantSpritesPlanner.Build(ipe, 0, Profiles, new ColorSettings(), "#6B4F3A").Hash;
        Assert.Equal(Sprites("Handroanthus chrysotrichus").Hash, a);
        Assert.NotEqual(a, PlantSpritesPlanner.Build(ipe, 1, Profiles, new ColorSettings(), "#6B4F3A").Hash);
        Species changed = Viridian.Core.Config.ViridianJson.Deserialize<Species>(Viridian.Core.Config.ViridianJson.Serialize(ipe))!;
        changed.FlowerColor = "#FF00FF";
        Assert.NotEqual(a, PlantSpritesPlanner.Build(changed, 0, Profiles, new ColorSettings(), "#6B4F3A").Hash);
    }

    [Fact]
    public void Svg_Preview_DrawsEveryTriangle_InEachStyle()
    {
        SpriteMesh mesh = Sprites("Agave attenuata").Get(PlantSpritesPlanner.DefaultStage, 0);
        Assert.Equal(mesh.TriangleCount, CardSvg.Mesh(mesh, 2).Split("<path").Length - 1);
        Assert.Equal(mesh.OutlineTriangles.Count / 3, CardSvg.Mesh(mesh, 2, style: "arame").Split("<path").Length - 1);
        Assert.Contains("#FFFFFF", CardSvg.Mesh(mesh, 2, style: "linha"));
    }
}
