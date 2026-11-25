using Viridian.Core.Colors;
using Viridian.Core.Config;
using Viridian.Core.Distribution;
using Viridian.Core.Geometry;
using Viridian.Core.Massing;
using Viridian.Core.Model;

namespace Viridian.Core.Model3D.Illustration;

/// <summary>Malha plana colorida (tapete de um maciço), em metros no sistema do projeto, com z a partir do chão.</summary>
public sealed class FlatMesh
{
    public FlatMesh(IReadOnlyList<SpriteVertex> vertices, IReadOnlyList<int> triangles)
    {
        Vertices = vertices;
        Triangles = triangles;
    }

    public IReadOnlyList<SpriteVertex> Vertices { get; }

    /// <summary>Triângulos (índices de três em três), anti-horários vistos de cima.</summary>
    public IReadOnlyList<int> Triangles { get; }

    public int TriangleCount => Triangles.Count / 3;
}

/// <summary>Um maciço pronto para a vista 3D: o tapete e os pontos onde ficam as plantas ilustradas.</summary>
public sealed record BedVisual(FlatMesh Carpet, IReadOnlyList<Point2> Points, IReadOnlyList<double> Rotations, int PlannedCount);

/// <summary>
/// Maciços na vista 3D, no estilo da vegetação ilustrada: um tapete na cor da espécie com textura de folhagem
/// (ou de grama, nos gramados) e, por cima, as plantas ilustradas nos pontos de plantio. Para não pesar a vista, as
/// plantas de um maciço são limitadas (os pontos são espaçados por igual quando o maciço passaria do limite).
/// </summary>
public static class BedIllustration
{
    /// <summary>Plantas ilustradas por maciço, no máximo.</summary>
    public const int MaxPlantsPerBed = 200;

    /// <summary>Plantas baixas (até meio metro) por maciço, no máximo: o tapete já dá a cor e a textura.</summary>
    public const int MaxLowPlantsPerBed = 70;

    /// <summary>Manchas de textura do tapete por maciço, no máximo.</summary>
    public const int MaxCarpetMarks = 3500;

    /// <summary>Altura do tapete sobre o chão (m): fica acima de pisos e do terreno na mesma cota.</summary>
    public const double CarpetLift = 0.012;

    /// <summary>Gramados viram só tapete (as folhas de grama seriam pequenas demais para aparecer).</summary>
    public static bool CarpetOnly(Species species) => species.Category == PlantCategory.Gramado;

    public static BedVisual Build(Polygon2 polygon, Species species, MassingRecord record, ColorSettings colors, bool withCarpet, bool withPlants)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        ArgumentNullException.ThrowIfNull(species);
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(colors);
        long seed = record.Seed != 0 ? record.Seed : DeterministicRandom.StableHash(record.Id);
        FlatMesh carpet = withCarpet ? Carpet(polygon, species, colors, seed) : new FlatMesh(Array.Empty<SpriteVertex>(), Array.Empty<int>());
        if (!withPlants || CarpetOnly(species) || polygon.IsEmpty)
        {
            return new BedVisual(carpet, Array.Empty<Point2>(), Array.Empty<double>(), 0);
        }

        double spacing = record.SpacingM > 0 ? record.SpacingM
            : record.DensityPerM2 > 0 ? MassingCalculator.SpacingFromDensity(record.DensityPerM2, DistributionPattern.Triangular)
            : species.EffectiveSpacingM;

        // Plantas miúdas (forrações) muito juntas: o tapete já dá a cor; as plantas espaçam o bastante para dar volume.
        int cap = species.AdultHeightM <= 0.5 ? MaxLowPlantsPerBed : MaxPlantsPerBed;
        double minimum = Math.Sqrt(Math.Max(polygon.Area, 1e-6) / cap);
        var request = new DistributionRequest
        {
            Pattern = DistributionPattern.Triangular,
            SpacingM = Math.Max(Math.Max(spacing, 0.12), minimum),
            JitterPercent = 38,
            Seed = seed,
            MaxCount = cap * 4,
        };
        DistributionResult result = PlantDistributor.Distribute(polygon, request);
        List<Point2> points = result.Points.ToList();
        if (points.Count > cap)
        {
            double step = (double)points.Count / cap;
            points = Enumerable.Range(0, cap).Select(i => points[(int)(i * step)]).ToList();
        }

        IReadOnlyList<double> rotations = PlantDistributor.RandomRotations(points.Count, seed);
        return new BedVisual(carpet, points, rotations, result.Points.Count);
    }

    /// <summary>Tapete: o contorno do maciço preenchido e manchas de folhas (ou folhas de grama) em três tons.</summary>
    public static FlatMesh Carpet(Polygon2 polygon, Species species, ColorSettings colors, long seed)
    {
        var vertices = new List<SpriteVertex>();
        var triangles = new List<int>();
        if (polygon.IsEmpty)
        {
            return new FlatMesh(vertices, triangles);
        }

        bool lawn = species.Category == PlantCategory.Gramado;
        Rgb foliage = VegetationPalette.Foliage(species, colors);
        Rgb ground = lawn ? foliage.Shade(0.08) : foliage.Shade(0.38);

        // Base: cada anel externo com os furos que estão dentro dele.
        var outers = polygon.Rings.Where(r => Geo.SignedArea(r) > 0).ToList();
        var holes = polygon.Rings.Where(r => Geo.SignedArea(r) < 0).ToList();
        if (outers.Count == 0)
        {
            outers = polygon.Rings.Select(r => (IReadOnlyList<Point2>)r.Reverse().ToList()).ToList();
            holes.Clear();
        }

        foreach (IReadOnlyList<Point2> outer in outers)
        {
            var mine = holes.Where(h => h.Count > 0 && Geo.PointInRing(outer, h[0])).ToList();
            List<int> local = Earcut.Triangulate(outer, mine);
            int start = vertices.Count;
            foreach (Point2 p in outer.Concat(mine.SelectMany(h => h)))
            {
                vertices.Add(new SpriteVertex(p.X, p.Y, CarpetLift, ground, false));
            }

            triangles.AddRange(local.Select(i => start + i));
        }

        // Textura: manchas pequenas espalhadas por igual (folhas nos canteiros, folhas de grama nos gramados).
        var random = new DeterministicRandom(DeterministicRandom.Combine(seed, 9001));
        Bounds2 b = polygon.Bounds;
        double perM2 = lawn ? 28 : 14;
        int marks = (int)Math.Min(MaxCarpetMarks, Math.Ceiling(polygon.Area * perM2));
        Rgb[] tones = lawn
            ? new[] { foliage.Shade(0.22), foliage.Tint(0.12), foliage.Mix(new Rgb(0xC9, 0xC2, 0x4A), 0.18) }
            : new[] { foliage.Shade(0.18), foliage, foliage.Tint(0.16) };
        double size = Math.Clamp(Math.Sqrt(polygon.Area / Math.Max(marks, 1)) * 0.9, 0.05, 0.35);
        for (int i = 0, tries = 0; i < marks && tries < marks * 6; tries++)
        {
            var at = new Point2(b.MinX + random.NextDouble() * (b.MaxX - b.MinX), b.MinY + random.NextDouble() * (b.MaxY - b.MinY));
            if (!polygon.Contains(at))
            {
                continue;
            }

            i++;
            Rgb tone = tones[random.NextInt(0, tones.Length)];
            double z = CarpetLift + 0.002;
            double angle = random.NextDouble() * Math.PI;
            int first = vertices.Count;
            if (lawn)
            {
                // Folha de grama: triângulo fino.
                double length = size * (0.8 + 0.6 * random.NextDouble()), width = length * 0.18;
                double c = Math.Cos(angle), s = Math.Sin(angle);
                vertices.Add(new SpriteVertex(at.X - s * width / 2, at.Y + c * width / 2, z, tone, false));
                vertices.Add(new SpriteVertex(at.X + s * width / 2, at.Y - c * width / 2, z, tone, false));
                vertices.Add(new SpriteVertex(at.X + c * length, at.Y + s * length, z, tone, false));
                triangles.AddRange(Ccw(vertices, first, first + 1, first + 2));
            }
            else
            {
                // Folha: losango com um tom por mancha.
                double length = size * (0.7 + 0.6 * random.NextDouble()), width = length * 0.55;
                double c = Math.Cos(angle), s = Math.Sin(angle);
                vertices.Add(new SpriteVertex(at.X - c * length / 2, at.Y - s * length / 2, z, tone, false));
                vertices.Add(new SpriteVertex(at.X - s * width / 2, at.Y + c * width / 2, z, tone, false));
                vertices.Add(new SpriteVertex(at.X + c * length / 2, at.Y + s * length / 2, z, tone, false));
                vertices.Add(new SpriteVertex(at.X + s * width / 2, at.Y - c * width / 2, z, tone, false));
                triangles.AddRange(Ccw(vertices, first, first + 1, first + 2));
                triangles.AddRange(Ccw(vertices, first, first + 2, first + 3));
            }
        }

        return new FlatMesh(vertices, triangles);
    }

    private static int[] Ccw(List<SpriteVertex> v, int a, int b, int c)
    {
        double cross = (v[b].X - v[a].X) * (v[c].Y - v[a].Y) - (v[b].Y - v[a].Y) * (v[c].X - v[a].X);
        return cross >= 0 ? new[] { a, b, c } : new[] { a, c, b };
    }
}
