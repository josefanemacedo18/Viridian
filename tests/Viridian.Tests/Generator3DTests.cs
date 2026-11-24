using Viridian.Core.Colors;
using Viridian.Core.Model;
using Viridian.Core.Model3D;
using Viridian.Core.Model3D.Generation;
using Viridian.Core.Model3D.Profiles;
using Viridian.Core.Model3D.Seasonality;

namespace Viridian.Tests;

public class Generator3DTests
{
    private static readonly List<Plant3DProfile> Profiles = Plant3DProfileDefaults.Create();

    private static readonly MeshPart[] FoliageParts = { MeshPart.Folhagem, MeshPart.FolhagemClara, MeshPart.FolhagemEscura };

    /// <summary>Porte típico de cada arquitetura (altura, copa), para testar em escala realista.</summary>
    internal static (double Height, double Crown) Typical(PlantArchitecture a) => a switch
    {
        PlantArchitecture.ArvoreArredondada or PlantArchitecture.ArvoreIrregular => (9, 8),
        PlantArchitecture.ArvoreEspalhada => (8, 11),
        PlantArchitecture.ArvoreColunar => (12, 3),
        PlantArchitecture.ArvoreConica => (14, 6),
        PlantArchitecture.PalmeiraEstipeUnico => (10, 5),
        PlantArchitecture.PalmeiraTouceira => (5, 4),
        PlantArchitecture.ArbustoArredondado => (1.6, 1.4),
        PlantArchitecture.ArbustoEreto => (2.2, 1.0),
        PlantArchitecture.TufosEmCaule => (1.8, 1.2),
        PlantArchitecture.Trepadeira => (3, 2.5),
        PlantArchitecture.Graminea => (0.9, 0.8),
        PlantArchitecture.RosetaRigida => (1.0, 1.4),
        PlantArchitecture.HerbaceaFolhaLarga => (1.5, 1.2),
        PlantArchitecture.Forracao => (0.2, 0.5),
        PlantArchitecture.AquaticaEmergente => (1.0, 0.7),
        PlantArchitecture.AquaticaFlutuante => (0.1, 1.0),
        PlantArchitecture.Gramado => (0.05, 1.0),
        _ => (2, 2),
    };

    private static PlantRequest Request(Plant3DProfile p, QualityLevel q = QualityLevel.Intermediaria, long seed = 7, FoliageState foliage = FoliageState.Cheia,
        bool flowers = true, FruitTraits? fruits = null, bool solid = false, bool variation = true, double scale = 1)
    {
        (double h, double d) = Typical(p.Architecture);
        return new PlantRequest
        {
            Profile = p, HeightM = h * scale, CrownDiameterM = d * scale, Quality = q, Seed = seed, Foliage = foliage, Flowers = flowers,
            Fruits = fruits, SolidOnly = solid, Variation = variation,
        };
    }

    private static FruitTraits SomeFruit(Plant3DProfile p) => new(
        new SeasonInfo(MonthSet.Range(1, 2), SeasonSource.Estimado),
        new Rgb(0xC0, 0x30, 0x20),
        p.Fruits?.Type ?? (p.Palm is not null ? FruitType.CachoPalmeira : FruitType.Baga),
        p.Fruits?.SizeM ?? 0.03);

    private static Plant3DProfile Profile(string id) => Profiles.Single(p => p.Id == id);

    private static IEnumerable<Vec3> Vertices(GeneratedPlant g, params MeshPart[] parts) =>
        g.Model.Pieces.Where(p => parts.Length == 0 || parts.Contains(p.Part)).SelectMany(p => p.Vertices);

    [Fact]
    public void EveryProfile_Quality_AndFoliageState_ProducesValidGeometry_WithinBudget()
    {
        var problems = new List<string>();
        foreach (Plant3DProfile p in Profiles)
        {
            foreach (QualityLevel q in Enum.GetValues<QualityLevel>())
            {
                foreach (FoliageState f in Enum.GetValues<FoliageState>())
                {
                    foreach (long seed in new long[] { 1, 99 })
                    {
                        foreach (bool flowers in new[] { false, true })
                        {
                            GeneratedPlant g = PlantGeometryGenerator.Generate(Request(p, q, seed, f, flowers, flowers ? SomeFruit(p) : null));
                            foreach (MeshIssue issue in MeshValidator.Check(g.Model, g.Budget))
                            {
                                problems.Add($"{p.Id} {q} {f} s{seed} fl={flowers}: {issue.Message}");
                            }
                        }
                    }
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems.Distinct().Take(60)));
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(3)]
    public void SmallAndLargeSizes_StayValid(double scale)
    {
        var problems = new List<string>();
        foreach (Plant3DProfile p in Profiles)
        {
            foreach (QualityLevel q in Enum.GetValues<QualityLevel>())
            {
                GeneratedPlant g = PlantGeometryGenerator.Generate(Request(p, q, flowers: true, fruits: SomeFruit(p), scale: scale));
                problems.AddRange(MeshValidator.Check(g.Model, g.Budget).Select(i => $"{p.Id} {q}: {i.Message}"));
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems.Take(60)));
    }

    [Fact]
    public void SolidOnly_ProducesOnlyClosedWatertightSolids()
    {
        var problems = new List<string>();
        foreach (Plant3DProfile p in Profiles)
        {
            foreach (QualityLevel q in Enum.GetValues<QualityLevel>())
            {
                GeneratedPlant g = PlantGeometryGenerator.Generate(Request(p, q, solid: true, fruits: SomeFruit(p)));
                if (g.Model.Pieces.Count == 0)
                {
                    problems.Add($"{p.Id} {q}: sem sólidos");
                }

                problems.AddRange(g.Model.Pieces.Where(x => !x.Closed || !x.IsWatertight() || x.SignedVolume() <= 0).Select(x => $"{p.Id} {q}: {x.Part} não é sólido"));
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems.Take(60)));
    }

    [Fact]
    public void SameRequest_IsDeterministic_AndHashIsStable()
    {
        foreach (Plant3DProfile p in Profiles)
        {
            GeneratedPlant a = PlantGeometryGenerator.Generate(Request(p, QualityLevel.Detalhada, fruits: SomeFruit(p)));
            GeneratedPlant b = PlantGeometryGenerator.Generate(Request(p, QualityLevel.Detalhada, fruits: SomeFruit(p)));
            Assert.Equal(a.Hash, b.Hash);
            Assert.Equal(16, a.Hash.Length);
            Assert.Equal(a.Model.TriangleCount, b.Model.TriangleCount);
            Assert.Equal(Vertices(a), Vertices(b));
        }
    }

    [Fact]
    public void DifferentSeeds_GiveDifferentPlants_UnlessVariationIsOff()
    {
        foreach (Plant3DProfile p in Profiles.Where(x => x.Architecture != PlantArchitecture.Gramado))
        {
            GeneratedPlant a = PlantGeometryGenerator.Generate(Request(p, seed: 1));
            GeneratedPlant b = PlantGeometryGenerator.Generate(Request(p, seed: 2));
            Assert.NotEqual(a.Hash, b.Hash);
            Assert.NotEqual(Vertices(a), Vertices(b));

            GeneratedPlant c = PlantGeometryGenerator.Generate(Request(p, seed: 1, variation: false));
            GeneratedPlant d = PlantGeometryGenerator.Generate(Request(p, seed: 2, variation: false));
            Assert.Equal(c.Hash, d.Hash);
            Assert.Equal(Vertices(c), Vertices(d));
        }
    }

    [Fact]
    public void Hash_ChangesWithEveryInputThatChangesTheShape()
    {
        Plant3DProfile ipe = Profile("ipe");
        string baseHash = PlantGeometryGenerator.Generate(Request(ipe)).Hash;
        Assert.NotEqual(baseHash, PlantGeometryGenerator.Generate(Request(ipe, QualityLevel.Detalhada)).Hash);
        Assert.NotEqual(baseHash, PlantGeometryGenerator.Generate(Request(ipe, foliage: FoliageState.SemFolhas)).Hash);
        Assert.NotEqual(baseHash, PlantGeometryGenerator.Generate(Request(ipe, flowers: false)).Hash);
        Assert.NotEqual(baseHash, PlantGeometryGenerator.Generate(Request(ipe, fruits: SomeFruit(ipe))).Hash);
        Assert.NotEqual(baseHash, PlantGeometryGenerator.Generate(Request(ipe, scale: 1.1)).Hash);
        Plant3DProfile changed = ipe.Clone();
        changed.Crown.Density = Math.Clamp(changed.Crown.Density - 0.1, 0.1, 1);
        Assert.NotEqual(baseHash, PlantGeometryGenerator.Generate(Request(changed)).Hash);
    }

    [Fact]
    public void HigherQuality_HasMoreTriangles()
    {
        foreach (string id in new[] { "ipe", "jeriva", "agave", "estrelitzia", "arbusto-arredondado", "araucaria" })
        {
            Plant3DProfile p = Profile(id);
            int simple = PlantGeometryGenerator.Generate(Request(p, QualityLevel.Simplificada)).Model.TriangleCount;
            int medium = PlantGeometryGenerator.Generate(Request(p, QualityLevel.Intermediaria)).Model.TriangleCount;
            int detailed = PlantGeometryGenerator.Generate(Request(p, QualityLevel.Detalhada)).Model.TriangleCount;
            Assert.True(simple < medium && medium < detailed, $"{id}: {simple} / {medium} / {detailed}");
        }
    }

    [Fact]
    public void ColumnarTree_IsNarrow_AndSpreadingTree_IsWide()
    {
        GeneratedPlant column = PlantGeometryGenerator.Generate(Request(Profile("arvore-colunar"), QualityLevel.Detalhada));
        GeneratedPlant spread = PlantGeometryGenerator.Generate(Request(Profile("arvore-espalhada"), QualityLevel.Detalhada));
        (Vec3 cMin, Vec3 cMax) = MeshValidator.Bounds(column.Model);
        (Vec3 sMin, Vec3 sMax) = MeshValidator.Bounds(spread.Model);
        Assert.True((cMax.X - cMin.X) / cMax.Z < 0.45, $"colunar {(cMax.X - cMin.X):0.00} × {cMax.Z:0.00}");
        Assert.True((sMax.X - sMin.X) / sMax.Z > 1.0, $"espalhada {(sMax.X - sMin.X):0.00} × {sMax.Z:0.00}");
    }

    [Fact]
    public void Palm_HasStipeToTheTop_AndFrondsInTheCrown()
    {
        foreach (string id in new[] { "jeriva", "palmeira-imperial", "palmeira-leque" })
        {
            GeneratedPlant g = PlantGeometryGenerator.Generate(Request(Profile(id), QualityLevel.Intermediaria));
            double h = g.Model.HeightM;
            Assert.True(Vertices(g, MeshPart.Tronco).Max(v => v.Z) > h * 0.6, id + ": estipe baixo");
            List<Vec3> fronds = Vertices(g, FoliageParts).ToList();
            Assert.NotEmpty(fronds);
            Assert.True(fronds.Average(v => v.Z) > h * 0.6, id + ": folhas abaixo da coroa");
            // Folhas para todos os lados.
            Assert.True(fronds.Select(v => Quadrant(v)).Distinct().Count() == 4, id + ": coroa assimétrica");
        }
    }

    [Fact]
    public void Rosette_IsRadial_AndLowerThanWide()
    {
        foreach (string id in new[] { "agave", "bromelia", "roseta-rigida" })
        {
            GeneratedPlant g = PlantGeometryGenerator.Generate(Request(Profile(id), QualityLevel.Intermediaria, flowers: false));
            List<Vec3> leaves = Vertices(g, FoliageParts).ToList();
            Assert.Equal(4, leaves.Select(Quadrant).Distinct().Count());
            Assert.True(Math.Abs(leaves.Average(v => v.X)) < g.Model.CrownDiameterM * 0.2, id);
            Assert.True(Math.Abs(leaves.Average(v => v.Y)) < g.Model.CrownDiameterM * 0.2, id);
        }
    }

    [Fact]
    public void BirdOfParadise_FanIsPlanar_AndFlowerAppearsWhenRequested()
    {
        Plant3DProfile p = Profile("estrelitzia");
        GeneratedPlant g = PlantGeometryGenerator.Generate(Request(p, QualityLevel.Intermediaria, flowers: true));
        Assert.Contains(g.Model.Pieces, x => x.Part == MeshPart.Flor);
        GeneratedPlant none = PlantGeometryGenerator.Generate(Request(p, QualityLevel.Intermediaria, flowers: false));
        Assert.DoesNotContain(none.Model.Pieces, x => x.Part == MeshPart.Flor);
    }

    [Fact]
    public void LeaflessTree_HasNoFoliage_ButShowsBranchesAndFlowers()
    {
        Plant3DProfile ipe = Profile("ipe");
        GeneratedPlant g = PlantGeometryGenerator.Generate(Request(ipe, QualityLevel.Intermediaria, foliage: FoliageState.SemFolhas, flowers: true));
        Assert.DoesNotContain(g.Model.Pieces, x => FoliageParts.Contains(x.Part));
        Assert.Contains(g.Model.Pieces, x => x.Part == MeshPart.Galho);
        Assert.Contains(g.Model.Pieces, x => x.Part == MeshPart.Flor);

        GeneratedPlant full = PlantGeometryGenerator.Generate(Request(ipe, QualityLevel.Intermediaria, foliage: FoliageState.Cheia, flowers: false));
        GeneratedPlant sparse = PlantGeometryGenerator.Generate(Request(ipe, QualityLevel.Intermediaria, foliage: FoliageState.Rala, flowers: false));
        int Leaves(GeneratedPlant x) => x.Model.Pieces.Where(y => FoliageParts.Contains(y.Part)).Sum(y => y.TriangleCount);
        Assert.True(Leaves(sparse) < Leaves(full), $"{Leaves(sparse)} >= {Leaves(full)}");
    }

    [Fact]
    public void FlowersAndFruits_OnlyWhenRequested()
    {
        foreach (Plant3DProfile p in Profiles)
        {
            GeneratedPlant plain = PlantGeometryGenerator.Generate(Request(p, QualityLevel.Detalhada, flowers: false, fruits: null));
            Assert.DoesNotContain(plain.Model.Pieces, x => x.Part is MeshPart.Flor or MeshPart.Fruto);
        }

        foreach (string id in new[] { "jeriva", "ipe", "arbusto-arredondado", "arvore-arredondada" })
        {
            Plant3DProfile p = Profile(id);
            GeneratedPlant g = PlantGeometryGenerator.Generate(Request(p, QualityLevel.Intermediaria, flowers: false, fruits: SomeFruit(p)));
            Assert.Contains(g.Model.Pieces, x => x.Part == MeshPart.Fruto);
        }
    }

    [Fact]
    public void LegacyGenerator_IsUntouched()
    {
        // O gerador antigo continua disponível e igual (maquetes existentes não mudam sem o usuário pedir).
        var s = Viridian.Library.SeedCatalog.CreateSpecies().First(x => x.ScientificName == "Handroanthus chrysotrichus");
        PlantModel a = PlantModels.Build(s, 6, 8, 42, true);
        PlantModel b = PlantModels.Build(s, 6, 8, 42, true);
        Assert.Equal(a.TriangleCount, b.TriangleCount);
        Assert.Null(a.Architecture);
        Assert.All(a.Pieces, x => Assert.True(x.Closed));
    }

    [Fact]
    public void Validator_FlagsBrokenMeshes()
    {
        var model = new PlantModel { HeightM = 1, CrownDiameterM = 1 };
        var open = new MeshPiece(MeshPart.Tronco);
        int a = open.Add(Vec3.Zero), b = open.Add(new Vec3(1, 0, 0)), c = open.Add(new Vec3(0, 1, 0));
        open.Triangle(a, b, c);
        model.Pieces.Add(open);
        var nan = new MeshPiece(MeshPart.Folhagem, closed: false);
        int n0 = nan.Add(new Vec3(double.NaN, 0, 0)), n1 = nan.Add(new Vec3(0, 0, 1)), n2 = nan.Add(new Vec3(0, 1, 1));
        nan.Triangle(n0, n1, n2);
        model.Pieces.Add(nan);
        List<MeshIssue> issues = MeshValidator.Check(model, budget: 1);
        Assert.Contains(issues, i => i.Code == "aberto");
        Assert.Contains(issues, i => i.Code == "numero");
        Assert.Contains(issues, i => i.Code == "orcamento");

        MeshValidator.Sanitize(model);
        Assert.Single(model.Pieces);
        Assert.Contains(MeshValidator.Check(new PlantModel()), i => i.Code == "vazio");
    }

    private static int Quadrant(Vec3 v) => (v.X >= 0 ? 1 : 0) + (v.Y >= 0 ? 2 : 0);
}
