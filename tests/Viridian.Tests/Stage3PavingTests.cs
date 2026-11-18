using Viridian.Core.Geometry;
using Viridian.Core.Hatch;
using Viridian.Core.Paving;
using Viridian.Library;

namespace Viridian.Tests;

public class PavingPatternTests
{
    public static IEnumerable<object[]> AllPatterns() =>
        Enum.GetValues<LayingPattern>().Select(p => new object[] { p });

    private static PavingType Sample(LayingPattern pattern, double length = 200, double width = 100, double joint = 3, double angle = 0) => new()
    {
        Name = "Teste",
        Pattern = pattern,
        LengthMm = length,
        WidthMm = width,
        JointMm = joint,
        AngleDeg = angle,
    };

    /// <summary>Quantas peças (com as translações da malha) contêm o ponto.</summary>
    private static int Cover(PavingMotif m, Point2 p)
    {
        int count = 0;
        for (int i = -16; i <= 16; i++)
        {
            for (int j = -16; j <= 16; j++)
            {
                Point2 t = m.T1 * i + m.T2 * j;
                foreach (IReadOnlyList<Point2> tile in m.Tiles)
                {
                    if (Geo.PointInRing(tile.Select(q => q + t).ToList(), p))
                    {
                        count++;
                    }
                }
            }
        }

        return count;
    }

    [Theory]
    [MemberData(nameof(AllPatterns))]
    public void Tilings_CoverThePlaneExactlyOnce(LayingPattern pattern)
    {
        foreach ((double l, double w) in new[] { (200.0, 100.0), (300.0, 100.0), (500.0, 500.0) })
        {
            PavingMotif m = PavingPatterns.Motif(Sample(pattern, l, w, 3, 17));
            if (!m.IsTiling)
            {
                continue;
            }

            Assert.Equal(m.CellArea, m.Tiles.Sum(t => Math.Abs(Geo.SignedArea(t.ToList()))), 3);
            var random = new DeterministicRandom(5);
            for (int k = 0; k < 150; k++)
            {
                var p = new Point2(random.Range(-500, 500), random.Range(-500, 500));
                Assert.Equal(1, Cover(m, p));
            }
        }
    }

    [Theory]
    [MemberData(nameof(AllPatterns))]
    public void Hatch_IsValid_AndDrawsOnlyJoints(LayingPattern pattern)
    {
        PavingType type = Sample(pattern, angle: pattern == LayingPattern.Grade ? 30 : 0);
        HatchPattern hatch = PavingPatterns.Build(type);
        Assert.True(hatch.IsValid(out string problem), problem);
        Assert.Equal(HatchTarget.Model, hatch.Target);
        Assert.InRange(hatch.Grids.Count, 1, 40);
        Assert.All(hatch.Grids, g => Assert.True(g.Offset > 0));

        PavingMotif m = PavingPatterns.Motif(type);
        List<(Point2 A, Point2 B)> edges = PavingPatterns.Edges(m);
        Assert.Equal(edges.Count, hatch.Grids.Count); // nenhum lado ficou sem família

        // Cada traço da hachura fica sobre um lado de peça (ou traço avulso) de alguma translação da malha.
        var area = new Bounds2(-600, -600, 600, 600);
        IReadOnlyList<(Point2 A, Point2 B)> segments = HatchRasterizer.Segments(hatch, area);
        Assert.NotEmpty(segments);
        var all = new List<(Point2 A, Point2 B)>();
        for (int i = -8; i <= 8; i++)
        {
            for (int j = -8; j <= 8; j++)
            {
                Point2 t = m.T1 * i + m.T2 * j;
                all.AddRange(edges.Select(e => (e.A + t, e.B + t)));
            }
        }

        foreach ((Point2 a, Point2 b) in segments.Where(s => s.A.DistanceTo(s.B) > 1).Take(300))
        {
            for (int k = 0; k <= 4; k++)
            {
                Point2 p = Point2.Lerp(a, b, k / 4.0);
                if (Math.Abs(p.X) > 450 || Math.Abs(p.Y) > 450)
                {
                    continue;
                }

                Assert.Contains(all, e => Geo.DistancePointSegment(p, e.A, e.B) < 0.05);
            }
        }

        // E todo lado de peça perto do centro aparece desenhado.
        foreach ((Point2 a, Point2 b) in all.Where(e => Math.Abs(e.A.X) < 300 && Math.Abs(e.A.Y) < 300 && Math.Abs(e.B.X) < 300 && Math.Abs(e.B.Y) < 300).Take(200))
        {
            Point2 mid = (a + b) / 2;
            Assert.Contains(segments, s => Geo.DistancePointSegment(mid, s.A, s.B) < 0.05);
        }
    }

    [Fact]
    public void Herringbone45_IsRotated()
    {
        HatchPattern h = PavingPatterns.Build(Sample(LayingPattern.EspinhaDePeixe45));
        Assert.Contains(h.Grids, g => Math.Abs(Math.Abs(g.AngleDeg % 90) - 45) < 1e-6);
    }

    [Fact]
    public void Signature_ChangesWithDrawing_NotWithNotes()
    {
        PavingType a = Sample(LayingPattern.Grade);
        PavingType b = a.Clone();
        b.Notes = "outra observação";
        Assert.Equal(a.Signature, b.Signature);
        b.JointMm = 10;
        Assert.NotEqual(a.Signature, b.Signature);
        Assert.NotEqual(PavingPatterns.Build(a).Name, PavingPatterns.Build(b).Name);
    }
}

public class PavingLibraryTests
{
    [Fact]
    public void Defaults_AreValid_AndCoverAllCategories()
    {
        List<PavingType> all = PavingDefaults.Create();
        Assert.True(all.Count >= 30, all.Count.ToString());
        Assert.Equal(all.Count, all.Select(t => t.Id).Distinct().Count());
        foreach (PavingCategory c in Enum.GetValues<PavingCategory>())
        {
            Assert.Contains(all, t => t.Category == c);
        }

        foreach (PavingType t in all)
        {
            Assert.Empty(t.Validate());
            Assert.False(string.IsNullOrWhiteSpace(t.References), t.Name);
            Assert.True(PavingPatterns.Build(t).IsValid(out string p), t.Name + ": " + p);
            Assert.NotEmpty(PavingDetail.Draw(t).Lines);
        }

        Assert.Contains(all, t => t.Name.Contains("Fulget", StringComparison.Ordinal) && t.CastInPlace);
    }

    [Fact]
    public void Validate_CatchesBadInput()
    {
        var t = new PavingType { Name = "", LengthMm = 2, WidthMm = 100, ThicknessMm = 0, JointMm = -1, Color = "verde" };
        t.Layers.Add(new PavingLayer { Name = "Base", Kind = PavingLayerKind.Base, ThicknessMm = 0 });
        IReadOnlyList<string> problems = t.Validate();
        Assert.True(problems.Count >= 5);
        var herring = new PavingType { Name = "x", Pattern = LayingPattern.EspinhaDePeixe, LengthMm = 100, WidthMm = 200 };
        Assert.Contains(herring.Validate(), p => p.Contains("espinha", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Merge_KeepsOfficeTypes_AndUpdatesSeeds()
    {
        List<PavingType> seeds = PavingDefaults.Create();
        PavingType editedSeed = seeds[0].Clone();
        editedSeed.Name = "Editado pelo escritório";
        editedSeed.IsUserDefined = true;
        PavingType staleSeed = seeds[1].Clone();
        staleSeed.Name = "Versão antiga";
        var office = new PavingType { Id = "meu-piso", Name = "Piso do escritório", IsUserDefined = true };
        var file = new PavingLibraryFile { SeedVersion = 0, Types = new() { editedSeed, staleSeed, office } };
        (List<PavingType> merged, bool changed) = PavingLibrary.Merge(file);
        Assert.True(changed);
        Assert.Equal("Editado pelo escritório", merged.Single(t => t.Id == seeds[0].Id).Name);
        Assert.Equal(seeds[1].Name, merged.Single(t => t.Id == seeds[1].Id).Name);
        Assert.Contains(merged, t => t.Id == "meu-piso");
        Assert.Equal(seeds.Count + 1, merged.Count);

        (_, bool again) = PavingLibrary.Merge(new PavingLibraryFile { SeedVersion = PavingDefaults.Version, Types = merged });
        Assert.False(again);
        Assert.Equal(seeds.Count, PavingLibrary.Merge(null).Types.Count);
    }

    [Fact]
    public void Library_SavesAndReloads()
    {
        string root = Path.Combine(Path.GetTempPath(), "vir-pisos-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new LibraryPaths(root);
            var lib = new PavingLibrary(paths);
            PavingType copy = lib.Types[0].Clone();
            copy.Id = "copia";
            copy.Name = "Cópia";
            copy.IsUserDefined = true;
            lib.Save(copy);
            var again = new PavingLibrary(paths);
            Assert.NotNull(again.Find("copia"));
            Assert.True(again.Delete("copia"));
            Assert.Null(new PavingLibrary(paths).Find("copia"));
            Assert.Throws<InvalidOperationException>(() => again.Save(new PavingType { Name = "" }));

            File.WriteAllText(paths.PavingFile, "{ quebrado");
            Assert.NotEmpty(new PavingLibrary(paths).Types);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public void Quantities()
    {
        PavingType placa = PavingDefaults.Create().First(t => t.Id == "vir-cimenticio-50");
        var q = new PavingQuantity { Type = placa, AreaM2 = 100 };
        Assert.Equal(110, q.PurchaseAreaM2, 6);
        Assert.Equal(440, q.Pieces);
        Assert.Contains(q.Layers, l => l.Unit == "m³" && Math.Abs(l.Quantity - 3) < 1e-9);

        PavingType fulget = PavingDefaults.Create().First(t => t.Id == "vir-fulget-moldado");
        var f = new PavingQuantity { Type = fulget, AreaM2 = 50 };
        Assert.Null(f.Pieces);
        Assert.Contains("m³", f.PurchaseText, StringComparison.Ordinal);

        PavingType pedrisco = PavingDefaults.Create().First(t => t.Id == "vir-pedrisco");
        var p = new PavingQuantity { Type = pedrisco, AreaM2 = 20 };
        Assert.Equal(1.0, p.SurfaceVolumeM3, 6);
        Assert.Contains(p.Layers, l => l.Unit == "m²");
    }
}
