using Viridian.Core.Colors;
using Viridian.Core.Geometry;

namespace Viridian.Core.Model3D.Illustration;

/// <summary>
/// Desenha a planta num plano vertical do cartão, no estilo dos símbolos de vegetação: a copa vira muitas massas de
/// folhas sobrepostas de borda serrilhada (com entalhes), em dois tons (plano claro ou escuro, com inversões), com
/// contorno fino, hachuras e pontos de flor ou fruto; folhas longas viram formas inteiras; tronco claro.
/// Tudo em metros, a partir das malhas do motor (que trazem porte, copa, forma e cores da espécie).
/// </summary>
public static class CardArtist
{
    private static readonly Vec3 Up = new(0, 0, 1);

    /// <summary>Fração mínima do porte adulto usada como base de medida nas fases menores.</summary>
    public const double MinScaleFraction = 0.4;

    /// <summary>
    /// Base das medidas do estilo (traço, massas, célula): a média geométrica entre o porte da fase e o adulto, nunca
    /// abaixo de uma fração do adulto. Assim as mudas não ficam com traço grosso e as fases intermediárias não pesam mais
    /// que a adulta.
    /// </summary>
    public static double Scale(double size, double referenceSize) => referenceSize > size
        ? Math.Max(referenceSize * MinScaleFraction, Math.Sqrt(size * referenceSize))
        : size;

    /// <param name="azimuth">Direção de quem olha o plano (radianos): 0 = plano A, π/2 = plano B.</param>
    /// <param name="tone">+1: plano claro; -1: plano escuro.</param>
    /// <param name="referenceSize">Porte de referência (o da planta adulta): as fases menores usam uma fração dele como base do traço.</param>
    public static CardArt Draw(IReadOnlyList<IllustratedPart> parts, double azimuth, double tone, int seed, double referenceSize = 0)
    {
        var toEye = new Vec3(Math.Cos(azimuth), Math.Sin(azimuth), 0);
        return DrawView(parts, toEye, Up.Cross(toEye).Normalized(), Up, false, tone, seed, referenceSize);
    }

    /// <summary>
    /// A planta vista de cima (copa em planta): x para leste (+X da família) e y para norte (+Y). Usada quando a
    /// câmera olha de cima, no lugar da imagem em pé.
    /// </summary>
    public static CardArt DrawTop(IReadOnlyList<IllustratedPart> parts, double tone, int seed, double referenceSize = 0) =>
        DrawView(parts, Up, new Vec3(1, 0, 0), new Vec3(0, 1, 0), true, tone, seed, referenceSize);

    private static CardArt DrawView(IReadOnlyList<IllustratedPart> parts, Vec3 toEye, Vec3 right, Vec3 upAxis, bool top, double tone, int seed, double referenceSize)
    {
        ArgumentNullException.ThrowIfNull(parts);
        Point2 Proj(Vec3 v) => new(v.Dot(right), v.Dot(upAxis));

        double height = 0.01, reach = 0.01;
        foreach (IllustratedPart part in parts)
        {
            foreach (Vec3 v in part.Piece.Vertices)
            {
                height = Math.Max(height, v.Z);
                reach = Math.Max(reach, Math.Sqrt(v.X * v.X + v.Y * v.Y));
            }
        }

        double size = top ? 2 * reach : Math.Max(height, 2 * reach);
        // As fases menores usam uma fração do porte adulto como base: o traço continua coerente entre as fases sem
        // ficar grosso demais nas mudas.
        double unit = Scale(size, referenceSize);
        double px = unit / 720; // "pixel" de referência: medidas do estilo proporcionais ao porte adulto
        var art = new CardArt { Size = size, Outline = 1.3 * px };
        var random = new DeterministicRandom(DeterministicRandom.Combine(seed, 6151));

        var woods = new List<(double Depth, int Color, List<Point2> Tris)>();
        var leaves = new List<(double Depth, Rgb Color, double Bias, List<Point2> Tris)>();
        var clusters = new List<(Rgb Color, double Bias, List<Point2> Points)>();
        var decors = new List<(Rgb Color, List<Point2> Points)>();
        Rgb? flowerColor = null;
        Rgb WoodTone(Rgb c) => c.Mix(new Rgb(0xDD, 0xD6, 0xC8), 0.62).Shade(tone < 0 ? 0.1 : 0);
        foreach (IllustratedPart part in parts)
        {
            MeshPiece piece = part.Piece;
            if (piece.TriangleCount == 0)
            {
                continue;
            }

            bool wood = piece.Part is MeshPart.Tronco or MeshPart.Galho;
            bool decoration = piece.Part is MeshPart.Flor or MeshPart.Fruto;
            double bias = piece.Part == MeshPart.FolhagemClara ? 0.55 : piece.Part == MeshPart.FolhagemEscura ? -0.55 : 0;
            if (piece.Part == MeshPart.Flor)
            {
                flowerColor ??= part.Color;
            }

            List<Point2> TrisOf(IEnumerable<Tri> tris) => tris.SelectMany(t => new[] { Proj(piece.Vertices[t.A]), Proj(piece.Vertices[t.B]), Proj(piece.Vertices[t.C]) }).ToList();
            if (piece.Closed)
            {
                double depth = piece.Vertices.Average(v => v.Dot(toEye));
                if (wood)
                {
                    woods.Add((depth, art.ColorIndex(WoodTone(part.Color)), TrisOf(piece.Triangles)));
                }
                else if (!decoration && Elongated(piece))
                {
                    // Folha alongada e fechada (agave, rosetas): forma própria na cor da folhagem, não massa nem madeira.
                    leaves.Add((depth, part.Color, bias, TrisOf(piece.Triangles)));
                }
                else if (decoration)
                {
                    decors.Add((part.Color, piece.Vertices.Select(Proj).ToList()));
                }
                else
                {
                    clusters.Add((part.Color, bias, piece.Vertices.Select(Proj).ToList()));
                }

                continue;
            }

            // Superfícies abertas: folhas grandes ficam como formas próprias; as miúdas (folíolos) formam uma massa só
            // por tom, para o contorno não virar um rabisco preto.
            var small = new List<Tri>();
            foreach (List<Tri> comp in Components(piece))
            {
                List<Point2> pts = TrisOf(comp);
                double w = pts.Max(p => p.X) - pts.Min(p => p.X), h = pts.Max(p => p.Y) - pts.Min(p => p.Y);
                double depth = comp.Average(t => (piece.Vertices[t.A] + piece.Vertices[t.B] + piece.Vertices[t.C]).Dot(toEye) / 3);
                if (wood)
                {
                    woods.Add((depth, art.ColorIndex(WoodTone(part.Color)), pts));
                }
                else if (Math.Max(w, h) < 40 * px)
                {
                    small.AddRange(comp);
                }
                else
                {
                    leaves.Add((depth, part.Color, bias, pts));
                }
            }

            if (small.Count > 0)
            {
                leaves.Add((piece.Vertices.Average(v => v.Dot(toEye)), part.Color, bias, TrisOf(small)));
            }
        }

        double plantWidth = 2 * reach;
        double massRadius = Math.Clamp(plantWidth * 0.08, 0.026 * unit, 0.075 * unit);
        double tooth = 12 * px;

        // Tronco e galhos.
        foreach ((double _, int color, List<Point2> tris) in woods.OrderBy(w => w.Depth))
        {
            art.Ops.Add(new ArtOp(ArtOpKind.Union, color, tris, art.Outline, true));
        }

        // Fundo da copa (tom escuro, sem contorno): fecha os vãos grandes entre as massas, como na referência.
        foreach ((Rgb color, double _, List<Point2> points) in clusters)
        {
            List<Point2> hull = Hull(points);
            if (hull.Count >= 3)
            {
                double cx = hull.Average(p => p.X), cy = hull.Average(p => p.Y);
                art.Ops.Add(new ArtOp(ArtOpKind.Fill, art.ColorIndex(color.Shade(0.35)), hull.Select(p => new Point2(cx + (p.X - cx) * 0.82, cy + (p.Y - cy) * 0.82)).ToList(), 0, false));
            }
        }

        // Folhas inteiras (palmeiras, rosetas, herbáceas), das mais distantes às mais próximas.
        foreach ((double _, Rgb color, double bias, List<Point2> tris) in leaves.OrderBy(l => l.Depth))
        {
            art.Ops.Add(new ArtOp(ArtOpKind.Union, art.ColorIndex(Tone(color, bias, tone, random)), tris, art.Outline, true));
        }

        // Massas de folhas: cada grupo da copa vira várias massas pequenas e sobrepostas; as de baixo cobrem as de cima.
        var masses = new List<(Point2 Center, double Radius, Rgb Color, double Bias, bool Flower)>();
        foreach ((Rgb color, double bias, List<Point2> points) in clusters)
        {
            AddMasses(masses, points, color, bias, massRadius, random, false);
        }

        // Flores grandes (copa florida sem folhas, como o ipê no inverno) também viram massas, na cor da flor.
        var dots = new List<(Point2 Center, Rgb Color)>();
        foreach ((Rgb color, List<Point2> points) in decors)
        {
            List<Point2> hull = Hull(points);
            if (hull.Count < 3)
            {
                continue;
            }

            double radius = Math.Sqrt(Math.Max(1e-9, Area(hull)) / Math.PI);
            if (clusters.Count == 0 && radius > massRadius * 0.7)
            {
                // Sem folhas (ipê no inverno): a floração ocupa o lugar da copa.
                AddMasses(masses, points, color, 0, massRadius, random, true);
                continue;
            }

            double cx = hull.Average(p => p.X), cy = hull.Average(p => p.Y);
            int n = Math.Clamp((int)Math.Round(radius * radius / Math.Pow(6.5 * px, 2) / 3), 1, 9);
            for (int k = 0; k < n; k++)
            {
                double rho = n == 1 ? 0 : Math.Sqrt(random.NextDouble()) * radius * 0.8;
                double theta = random.NextDouble() * Geo.TwoPi;
                dots.Add((new Point2(cx + Math.Cos(theta) * rho, cy + Math.Sin(theta) * rho), color));
            }
        }

        foreach ((Point2 center, double radius, Rgb color, double bias, bool flower) in masses.OrderByDescending(m => m.Center.Y))
        {
            Rgb fill = flower ? (tone > 0 ? color.Tint(0.08) : color.Shade(0.12)) : Tone(color, bias, tone, random);
            int fillIndex = art.ColorIndex(fill);
            art.Ops.Add(new ArtOp(ArtOpKind.Fill, fillIndex, Serrate(Blob(center, radius, random), tooth, random), 0, true));
            if (!flower && random.NextDouble() < 0.22)
            {
                int hatch = art.ColorIndex(fill.Shade(0.4));
                int lines = 3 + random.NextInt(0, 3);
                for (int i = 0; i < lines; i++)
                {
                    double x = center.X - radius * 0.5 + i * radius * 0.17;
                    double y = center.Y + radius * 0.45;
                    double len = radius * (0.25 + 0.2 * random.NextDouble());
                    art.Ops.Add(new ArtOp(ArtOpKind.Line, hatch, new[] { new Point2(x, y), new Point2(x - len * 0.2, y - len) }, art.Outline * 0.8, false));
                }
            }

            if (!flower && flowerColor is Rgb petals && random.NextDouble() < 0.85)
            {
                int count = 1 + (random.NextDouble() < 0.35 ? 1 : 0);
                for (int d = 0; d < count; d++)
                {
                    double rho = Math.Sqrt(random.NextDouble()) * radius * 0.6;
                    double theta = random.NextDouble() * Geo.TwoPi;
                    Rgb dotColor = fill.Luminance < 0.4 ? petals.Shade(0.12) : petals;
                    art.Ops.Add(new ArtOp(ArtOpKind.Fill, art.ColorIndex(dotColor), Pentagon(new Point2(center.X + Math.Cos(theta) * rho, center.Y + Math.Sin(theta) * rho), 6.5 * px, random), 0, true));
                }
            }
        }

        foreach ((Point2 center, Rgb color) in dots)
        {
            art.Ops.Add(new ArtOp(ArtOpKind.Fill, art.ColorIndex(color), Pentagon(center, 6.5 * px, random), 0, true));
        }

        return art;
    }

    private static void AddMasses(List<(Point2 Center, double Radius, Rgb Color, double Bias, bool Flower)> masses, List<Point2> points, Rgb color, double bias,
        double massRadius, DeterministicRandom random, bool flower)
    {
        List<Point2> hull = Hull(points);
        if (hull.Count < 3)
        {
            return;
        }

        double cx = hull.Average(p => p.X), cy = hull.Average(p => p.Y);
        double radius = Math.Sqrt(Math.Max(1e-9, Area(hull)) / Math.PI);
        int count = radius < massRadius * 1.1 ? 1 : Math.Clamp((int)Math.Round(2.2 * radius * radius / (massRadius * massRadius)), 2, 18);
        for (int j = 0; j < count; j++)
        {
            double rm = count == 1 ? radius * 1.05 : Math.Min(radius, massRadius * (0.85 + 0.4 * random.NextDouble()));
            double reach = Math.Max(0, radius - rm * 0.5);
            double rho = j == 0 && count > 1 ? 0 : Math.Sqrt(random.NextDouble()) * reach;
            double theta = random.NextDouble() * Geo.TwoPi;
            masses.Add((new Point2(cx + Math.Cos(theta) * rho, cy + Math.Sin(theta) * rho * 0.9), rm, color, bias, flower));
        }
    }

    private static Rgb Tone(Rgb color, double bias, double tone, DeterministicRandom random)
    {
        double score = tone * 0.95 + bias + (random.NextDouble() - 0.5) * 1.5;
        return score > 0 ? color.Tint(0.24) : color.Shade(0.22);
    }

    private static bool Elongated(MeshPiece piece)
    {
        (Vec3 min, Vec3 max) = piece.Bounds();
        double[] e = { max.X - min.X, max.Y - min.Y, max.Z - min.Z };
        Array.Sort(e);
        return e[2] > 2.4 * Math.Max(e[1], 1e-6);
    }

    private static List<List<Tri>> Components(MeshPiece piece)
    {
        var parent = Enumerable.Range(0, piece.Vertices.Count).ToArray();
        int Find(int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }

            return x;
        }

        foreach (Tri t in piece.Triangles)
        {
            parent[Find(t.A)] = Find(t.B);
            parent[Find(t.B)] = Find(t.C);
        }

        return piece.Triangles.GroupBy(t => Find(t.A)).Select(g => g.ToList()).ToList();
    }

    private static List<Point2> Pentagon(Point2 center, double radius, DeterministicRandom random)
    {
        double rotation = random.NextDouble() * Geo.TwoPi;
        return Enumerable.Range(0, 5).Select(i =>
        {
            double a = rotation + i * Geo.TwoPi / 5;
            double r = radius * (0.85 + 0.3 * random.NextDouble());
            return new Point2(center.X + Math.Cos(a) * r, center.Y + Math.Sin(a) * r);
        }).ToList();
    }

    private static List<Point2> Blob(Point2 center, double radius, DeterministicRandom random)
    {
        const int n = 13;
        double phase = random.NextDouble() * Geo.TwoPi;
        return Enumerable.Range(0, n).Select(i =>
        {
            double a = phase + i * Geo.TwoPi / n;
            double r = radius * (0.8 + 0.34 * random.NextDouble());
            return new Point2(center.X + Math.Cos(a) * r, center.Y + Math.Sin(a) * r * 0.86);
        }).ToList();
    }

    /// <summary>Dentes alternados ao longo do contorno, com entalhes mais fundos de vez em quando (bordas de folhas).</summary>
    private static List<Point2> Serrate(List<Point2> poly, double tooth, DeterministicRandom random)
    {
        double cx = poly.Average(p => p.X), cy = poly.Average(p => p.Y);
        var result = new List<Point2>();
        bool outward = true;
        for (int i = 0; i < poly.Count; i++)
        {
            Point2 a = poly[i], b = poly[(i + 1) % poly.Count];
            double dx = b.X - a.X, dy = b.Y - a.Y, length = Math.Sqrt(dx * dx + dy * dy);
            result.Add(a);
            if (length < 1e-9)
            {
                continue;
            }

            double nx = dy / length, ny = -dx / length;
            if (nx * ((a.X + b.X) / 2 - cx) + ny * ((a.Y + b.Y) / 2 - cy) < 0)
            {
                nx = -nx;
                ny = -ny;
            }

            int steps = Math.Max(1, (int)(length / (tooth * 1.5)));
            for (int s = 1; s <= steps; s++)
            {
                double t = (double)s / (steps + 1);
                double amp = tooth * (0.4 + 0.7 * random.NextDouble());
                double offset = outward ? amp : -amp * (random.NextDouble() < 0.15 ? 1.6 : 0.6);
                outward = !outward;
                result.Add(new Point2(a.X + dx * t + nx * offset, a.Y + dy * t + ny * offset));
            }
        }

        return result;
    }

    internal static double Area(IReadOnlyList<Point2> poly)
    {
        double a = 0;
        for (int i = 0; i < poly.Count; i++)
        {
            Point2 p = poly[i], q = poly[(i + 1) % poly.Count];
            a += p.X * q.Y - q.X * p.Y;
        }

        return Math.Abs(a) / 2;
    }

    private static List<Point2> Hull(List<Point2> points)
    {
        List<Point2> sorted = points.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        if (sorted.Count < 3)
        {
            return sorted;
        }

        double Cross(Point2 o, Point2 a, Point2 b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        var lower = new List<Point2>();
        foreach (Point2 p in sorted)
        {
            while (lower.Count >= 2 && Cross(lower[^2], lower[^1], p) <= 0)
            {
                lower.RemoveAt(lower.Count - 1);
            }

            lower.Add(p);
        }

        var upper = new List<Point2>();
        for (int i = sorted.Count - 1; i >= 0; i--)
        {
            Point2 p = sorted[i];
            while (upper.Count >= 2 && Cross(upper[^2], upper[^1], p) <= 0)
            {
                upper.RemoveAt(upper.Count - 1);
            }

            upper.Add(p);
        }

        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }
}
