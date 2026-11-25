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

        var woods = new List<(double Depth, Rgb Color, List<Point2> Tris, bool Stem)>();
        var leaves = new List<(double Depth, Rgb Color, double Bias, List<Point2> Tris, bool Fine)>();
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
                    woods.Add((depth, WoodTone(part.Color), TrisOf(piece.Triangles), piece.Part == MeshPart.Tronco && !top));
                }
                else if (!decoration && Elongated(piece) && Extent(piece) < 0.25 * Math.Max(height, 2 * reach))
                {
                    // Folha alongada e fechada (agave, rosetas): forma própria na cor da folhagem, não massa nem madeira.
                    leaves.Add((depth, part.Color, bias, TrisOf(piece.Triangles), false));
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
                    woods.Add((depth, WoodTone(part.Color), pts, false));
                }
                else if (Math.Max(w, h) < 40 * px)
                {
                    small.AddRange(comp);
                }
                else
                {
                    // Folhas longas e finas (dracena, gramíneas) recebem o mesmo traço leve dos folíolos.
                    double area = 0;
                    for (int i = 0; i + 2 < pts.Count; i += 3)
                    {
                        area += Math.Abs((pts[i + 1].X - pts[i].X) * (pts[i + 2].Y - pts[i].Y) - (pts[i + 1].Y - pts[i].Y) * (pts[i + 2].X - pts[i].X)) / 2;
                    }

                    bool thin = area < 0.08 * Math.Max(w, h) * Math.Max(w, h) || Math.Min(w, h) < 14 * px || height < 0.6;
                    leaves.Add((depth, part.Color, bias, pts, thin));
                }
            }

            if (small.Count > 0)
            {
                leaves.Add((piece.Vertices.Average(v => v.Dot(toEye)), part.Color, bias, TrisOf(small), true));
            }
        }

        double plantWidth = 2 * reach;
        double massRadius = Math.Clamp(plantWidth * 0.08, 0.026 * unit, 0.075 * unit);
        double tooth = 12 * px;

        // Traço com peso variado: madeira mais marcada, folhas e flores mais leves.
        double woodLine = 1.5 * px, massLine = 1.05 * px, leafLine = 0.95 * px, flowerLine = 0.8 * px;

        // Tronco e galhos, com um realce claro ao longo do tronco (dá volume ao cilindro).
        foreach ((double _, Rgb color, List<Point2> tris, bool stem) in woods.OrderBy(w => w.Depth))
        {
            art.Ops.Add(new ArtOp(ArtOpKind.Union, art.ColorIndex(color), tris, woodLine, true));
            if (stem && StemHighlight(tris) is List<Point2> highlight)
            {
                art.Ops.Add(new ArtOp(ArtOpKind.Union, art.ColorIndex(color.Tint(0.32)), highlight, 0, false));
            }
        }

        // Grupos de copa achatados e baixos (gramados, tapetes): tapete de folhas de grama, não massas.
        var carpets = leaves.Count == 0 ? clusters.Where(c => IsCarpet(c.Points, height, top)).ToList() : new List<(Rgb Color, double Bias, List<Point2> Points)>();
        clusters = clusters.Except(carpets).ToList();
        foreach ((Rgb color, double _, List<Point2> points) in carpets)
        {
            DrawCarpet(art, points, color, top, px, random);
        }

        // Palmeiras, rosetas e herbáceas (com folhas inteiras): os grupos fechados são o miolo da coroa, desenhados
        // como uma forma escura recortada, sem massas nem flores por cima.
        double plantSize = Math.Max(height, 2 * reach);
        bool crownCores = leaves.Count > 0 && clusters.Count > 0
            && clusters.All(c => Math.Max(c.Points.Max(p => p.X) - c.Points.Min(p => p.X), c.Points.Max(p => p.Y) - c.Points.Min(p => p.Y)) < 0.2 * plantSize);
        if (crownCores)
        {
            foreach ((Rgb color, double _, List<Point2> points) in clusters)
            {
                List<Point2> hull = Hull(points);
                if (hull.Count >= 3)
                {
                    art.Ops.Add(new ArtOp(ArtOpKind.Fill, art.ColorIndex(color.Shade(0.3)), Serrate(Densify(hull, tooth * 2), tooth * 0.7, random), leafLine, true));
                }
            }

            clusters.Clear();
        }

        // Fundo escuro (sem contorno, borda recortada): fecha os vãos entre as massas. Grupos vizinhos ganham um fundo
        // comum; grupos distantes (touceiras, colunares), cada um o seu.
        foreach (List<int> group in NearbyGroups(clusters, massRadius))
        {
            List<Point2> hull = Hull(group.SelectMany(i => clusters[i].Points).ToList());
            if (hull.Count < 3)
            {
                continue;
            }

            double cx = hull.Average(p => p.X), cy = hull.Average(p => p.Y);
            Rgb deep = group.Select(i => clusters[i].Color).OrderBy(c => c.Luminance).First().Shade(0.4);
            List<Point2> shrunk = hull.Select(p => new Point2(cx + (p.X - cx) * 0.8, cy + (p.Y - cy) * 0.8)).ToList();
            art.Ops.Add(new ArtOp(ArtOpKind.Fill, art.ColorIndex(deep), Serrate(Densify(shrunk, tooth * 2), tooth, random), 0, false));
        }

        // Folhas inteiras (palmeiras, rosetas, herbáceas), das mais distantes às mais próximas.
        foreach ((double _, Rgb color, double bias, List<Point2> tris, bool fine) in leaves.OrderBy(l => l.Depth))
        {
            Rgb leaf = Tone(color, bias, tone, random);
            if (!fine)
            {
                art.Ops.Add(new ArtOp(ArtOpKind.Union, art.ColorIndex(leaf), tris, leafLine, true));
                AddVeins(art, tris, leaf, px, Math.Max(height, 2 * reach));
                continue;
            }

            // Folíolos miúdos (palmeiras, dracena): contorno verde-escuro em vez de preto, para não virar um rabisco.
            int edge = art.ColorIndex(leaf.Shade(0.5));
            for (int i = 0; i + 2 < tris.Count; i += 3)
            {
                art.Ops.Add(new ArtOp(ArtOpKind.Line, edge, new[] { tris[i], tris[i + 1] }, 2 * leafLine, false));
                art.Ops.Add(new ArtOp(ArtOpKind.Line, edge, new[] { tris[i + 1], tris[i + 2] }, 2 * leafLine, false));
                art.Ops.Add(new ArtOp(ArtOpKind.Line, edge, new[] { tris[i + 2], tris[i] }, 2 * leafLine, false));
            }

            art.Ops.Add(new ArtOp(ArtOpKind.Union, art.ColorIndex(leaf.Tint(0.1)), tris, 0, false));
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

        double crownTop = masses.Count > 0 ? masses.Max(m => m.Center.Y + m.Radius) : 0;
        double crownBottom = masses.Count > 0 ? masses.Min(m => m.Center.Y - m.Radius) : 0;
        foreach ((Point2 center, double radius, Rgb color, double bias, bool flower) in masses.OrderByDescending(m => m.Center.Y))
        {
            List<Point2> outline = Serrate(Blob(center, radius, random), tooth, random);
            if (flower)
            {
                Rgb petal = tone > 0 ? color.Tint(0.08) : color.Shade(0.12);
                art.Ops.Add(new ArtOp(ArtOpKind.Fill, art.ColorIndex(petal.Shade(0.16)), outline, massLine, true));
                art.Ops.Add(new ArtOp(ArtOpKind.Fill, art.ColorIndex(petal), Inset(outline, center, radius, 0.78, 0.12), 0, false));
                continue;
            }

            // Volume em três camadas: a massa inteira no tom de sombra (com o contorno), o tom médio um pouco acima e,
            // nas massas mais claras, um brilho no alto. As camadas de dentro repetem o recorte da borda.
            Rgb mid = Vary(Tone(color, bias, tone, random), random);
            if (height < 0.6)
            {
                // Plantas baixas (forrações): contorno no tom escuro da folhagem, sem o preto que pesaria no conjunto.
                art.Ops.Add(new ArtOp(ArtOpKind.Fill, art.ColorIndex(mid.Shade(0.5)), Inset(outline, center, radius, 1.08, 0), 0, false));
                art.Ops.Add(new ArtOp(ArtOpKind.Fill, art.ColorIndex(mid.Shade(0.2)), outline, 0, false));
            }
            else
            {
                art.Ops.Add(new ArtOp(ArtOpKind.Fill, art.ColorIndex(mid.Shade(0.2)), outline, massLine, true));
            }

            art.Ops.Add(new ArtOp(ArtOpKind.Fill, art.ColorIndex(mid), Inset(outline, center, radius, 0.8, 0.13), 0, false));

            // Mais luz no alto da copa, menos embaixo.
            double height01 = crownTop > crownBottom ? (center.Y - crownBottom) / (crownTop - crownBottom) : 0.5;
            if (mid.Luminance > 0.22 && random.NextDouble() < 0.35 + 0.5 * height01)
            {
                art.Ops.Add(new ArtOp(ArtOpKind.Fill, art.ColorIndex(mid.Tint(0.2)), Inset(outline, new Point2(center.X - radius * 0.08, center.Y), radius, 0.46, 0.3), 0, false));
            }

            // Marcas de folhas soltas na parte de baixo da massa (no lugar das hachuras retas).
            if (random.NextDouble() < 0.4)
            {
                int marks = 2 + random.NextInt(0, 3);
                int markColor = art.ColorIndex(mid.Shade(0.34));
                for (int i = 0; i < marks; i++)
                {
                    double ang = -Math.PI / 2 + (random.NextDouble() - 0.5) * 1.8;
                    double rho = radius * (0.28 + 0.32 * random.NextDouble());
                    var at = new Point2(center.X + Math.Cos(ang) * rho, center.Y + Math.Sin(ang) * rho * 0.86);
                    art.Ops.Add(new ArtOp(ArtOpKind.Fill, markColor, Leaf(at, radius * (0.16 + 0.08 * random.NextDouble()), random.NextDouble() * Math.PI), 0, false));
                }
            }

            // Flores em cachos pequenos, mais nas massas de cima e da borda.
            if (flowerColor is Rgb petals && random.NextDouble() < 0.45 + 0.45 * height01)
            {
                int bunches = 1 + (random.NextDouble() < 0.3 ? 1 : 0);
                for (int b = 0; b < bunches; b++)
                {
                    double rho = Math.Sqrt(random.NextDouble()) * radius * 0.62;
                    double theta = random.NextDouble() * Geo.TwoPi;
                    var c0 = new Point2(center.X + Math.Cos(theta) * rho, center.Y + Math.Sin(theta) * rho);
                    Rgb dotColor = mid.Luminance < 0.3 ? petals.Shade(0.1) : petals;
                    int dotsInBunch = 2 + random.NextInt(0, 3);
                    for (int d = 0; d < dotsInBunch; d++)
                    {
                        double a = random.NextDouble() * Geo.TwoPi, r = d == 0 ? 0 : 5.5 * px * (0.8 + 0.5 * random.NextDouble());
                        var at = new Point2(c0.X + Math.Cos(a) * r, c0.Y + Math.Sin(a) * r);
                        art.Ops.Add(new ArtOp(ArtOpKind.Fill, art.ColorIndex(d == 0 ? dotColor.Tint(0.18) : dotColor), Pentagon(at, (4.2 + 1.6 * random.NextDouble()) * px, random), flowerLine, true));
                    }
                }
            }
        }

        // Flores miúdas soltas (espigas, inflorescências): contorno no tom escuro da própria flor, para não virarem
        // manchas pretas quando estão juntas.
        foreach ((Point2 center, Rgb color) in dots)
        {
            List<Point2> petal = Pentagon(center, 5.5 * px, random);
            art.Ops.Add(new ArtOp(ArtOpKind.Fill, art.ColorIndex(color.Shade(0.42)), petal.Select(p => new Point2(center.X + (p.X - center.X) * 1.3, center.Y + (p.Y - center.Y) * 1.3)).ToList(), 0, false));
            art.Ops.Add(new ArtOp(ArtOpKind.Fill, art.ColorIndex(color), petal, 0, false));
        }

        return art;
    }

    /// <summary>Cópia reduzida do contorno, deslocada para cima (fica sempre dentro da forma original).</summary>
    private static List<Point2> Inset(List<Point2> outline, Point2 center, double radius, double scale, double lift) =>
        outline.Select(p => new Point2(center.X + (p.X - center.X) * scale, center.Y + radius * lift + (p.Y - center.Y) * scale)).ToList();

    /// <summary>Folha pequena (losango alongado) para as marcas dentro das massas.</summary>
    private static List<Point2> Leaf(Point2 center, double length, double angle)
    {
        double c = Math.Cos(angle), s = Math.Sin(angle), w = length * 0.32;
        Point2 At(double u, double v) => new(center.X + u * c - v * s, center.Y + u * s + v * c);
        return new List<Point2> { At(-length / 2, 0), At(-length * 0.1, w / 2), At(length / 2, 0), At(-length * 0.1, -w / 2) };
    }

    /// <summary>Variação sutil de matiz entre massas (umas mais quentes, outras mais frias), para a copa não parecer repetida.</summary>
    private static Rgb Vary(Rgb color, DeterministicRandom random)
    {
        double r = random.NextDouble();
        return r < 0.3 ? color.Mix(new Rgb(0xC9, 0xC2, 0x4A), 0.1) : r < 0.55 ? color.Mix(new Rgb(0x2F, 0x6E, 0x6A), 0.08) : color;
    }

    /// <summary>Realce do tronco: uma faixa estreita à esquerda do eixo, dentro da silhueta. Nulo em troncos curtos.</summary>
    private static List<Point2>? StemHighlight(List<Point2> tris)
    {
        double minX = tris.Min(p => p.X), maxX = tris.Max(p => p.X), minY = tris.Min(p => p.Y), maxY = tris.Max(p => p.Y);
        double width = maxX - minX, height = maxY - minY;
        if (width <= 0 || height < 2.5 * width)
        {
            return null;
        }

        // Só a parte de baixo do tronco (onde ele é reto); a faixa vai até 80% da altura.
        double cx = (minX + maxX) / 2;
        var result = new List<Point2>();
        for (int i = 0; i + 2 < tris.Count; i += 3)
        {
            Point2 a = tris[i], b = tris[i + 1], c = tris[i + 2];
            if (Math.Max(a.Y, Math.Max(b.Y, c.Y)) > minY + height * 0.8)
            {
                continue;
            }

            Point2 F(Point2 p) => new(cx - width * 0.16 + (p.X - cx) * 0.28, p.Y);
            result.Add(F(a));
            result.Add(F(b));
            result.Add(F(c));
        }

        return result.Count >= 3 ? result : null;
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
        double area = Math.Max(1e-9, Area(hull));
        double radius = Math.Sqrt(area / Math.PI);
        if (radius < massRadius * 1.1)
        {
            masses.Add((new Point2(cx, cy), radius * 1.05, color, bias, flower));
            return;
        }

        // As massas ficam dentro da forma do grupo (copa cônica, colunar, espalhada...), não num círculo médio.
        int count = Math.Clamp((int)Math.Round(2.2 * area / (Math.PI * massRadius * massRadius)), 2, 30);
        List<Point2> inner = hull.Select(p => new Point2(cx + (p.X - cx) * 0.82, cy + (p.Y - cy) * 0.82)).ToList();
        double minX = inner.Min(p => p.X), maxX = inner.Max(p => p.X), minY = inner.Min(p => p.Y), maxY = inner.Max(p => p.Y);
        masses.Add((new Point2(cx, cy), Math.Min(radius, massRadius * 1.05), color, bias, flower));
        for (int j = 1, tries = 0; j < count && tries < count * 40; tries++)
        {
            var at = new Point2(minX + random.NextDouble() * (maxX - minX), minY + random.NextDouble() * (maxY - minY));
            if (!Geo.PointInRing(inner, at))
            {
                continue;
            }

            double rm = Math.Min(radius, massRadius * (0.85 + 0.4 * random.NextDouble()));
            masses.Add((at, rm, color, bias, flower));
            j++;
        }
    }

    /// <summary>Junta os grupos de copa que se tocam (ou quase) para receberem um fundo comum.</summary>
    private static List<List<int>> NearbyGroups(List<(Rgb Color, double Bias, List<Point2> Points)> clusters, double gap)
    {
        var boxes = clusters.Select(c => (MinX: c.Points.Min(p => p.X) - gap, MaxX: c.Points.Max(p => p.X) + gap, MinY: c.Points.Min(p => p.Y) - gap, MaxY: c.Points.Max(p => p.Y) + gap)).ToList();
        var parent = Enumerable.Range(0, clusters.Count).ToArray();
        int Find(int x) => parent[x] == x ? x : parent[x] = Find(parent[x]);
        for (int i = 0; i < clusters.Count; i++)
        {
            for (int j = i + 1; j < clusters.Count; j++)
            {
                if (boxes[i].MinX <= boxes[j].MaxX && boxes[j].MinX <= boxes[i].MaxX && boxes[i].MinY <= boxes[j].MaxY && boxes[j].MinY <= boxes[i].MaxY)
                {
                    parent[Find(i)] = Find(j);
                }
            }
        }

        return Enumerable.Range(0, clusters.Count).GroupBy(Find).Select(g => g.ToList()).ToList();
    }

    /// <summary>Insere pontos nas arestas longas (para o recorte da borda acompanhar todo o contorno).</summary>
    private static List<Point2> Densify(List<Point2> poly, double step)
    {
        var result = new List<Point2>();
        for (int i = 0; i < poly.Count; i++)
        {
            Point2 a = poly[i], b = poly[(i + 1) % poly.Count];
            result.Add(a);
            double length = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            int n = (int)(length / Math.Max(step, 1e-6));
            for (int k = 1; k < n; k++)
            {
                result.Add(new Point2(a.X + (b.X - a.X) * k / n, a.Y + (b.Y - a.Y) * k / n));
            }
        }

        return result;
    }

    /// <summary>Grupo achatado e baixo (gramado, tapete de forração): vira tapete de grama.</summary>
    private static bool IsCarpet(List<Point2> points, double plantHeight, bool top)
    {
        if (plantHeight > 0.4)
        {
            return false;
        }

        double w = points.Max(p => p.X) - points.Min(p => p.X), h = points.Max(p => p.Y) - points.Min(p => p.Y);
        return top || h < 0.5 * w;
    }

    /// <summary>
    /// Tapete de grama: de lado, uma faixa com folhas finas de grama em três tons subindo da base; de cima, a área com
    /// manchas claras e escuras.
    /// </summary>
    private static void DrawCarpet(CardArt art, List<Point2> points, Rgb color, bool top, double px, DeterministicRandom random)
    {
        List<Point2> hull = Hull(points);
        if (hull.Count < 3)
        {
            return;
        }

        double minX = hull.Min(p => p.X), maxX = hull.Max(p => p.X), minY = hull.Min(p => p.Y), maxY = hull.Max(p => p.Y);
        Rgb[] tones = { color.Shade(0.25), color, color.Tint(0.18) };
        if (top)
        {
            art.Ops.Add(new ArtOp(ArtOpKind.Fill, art.ColorIndex(color), Serrate(Densify(hull, 10 * px), 4 * px, random), 0.9 * px, true));
            int specks = Math.Clamp((int)(Area(hull) / Math.Pow(14 * px, 2)), 6, 60);
            for (int i = 0; i < specks; i++)
            {
                var at = new Point2(minX + random.NextDouble() * (maxX - minX), minY + random.NextDouble() * (maxY - minY));
                if (Geo.PointInRing(hull, at))
                {
                    art.Ops.Add(new ArtOp(ArtOpKind.Fill, art.ColorIndex(tones[random.NextInt(0, 3)]), Leaf(at, 9 * px, random.NextDouble() * Math.PI), 0, false));
                }
            }

            return;
        }

        // Faixa de base e folhas de grama por cima, das de trás (escuras) às da frente (claras).
        double baseTop = minY + (maxY - minY) * 0.35;
        art.Ops.Add(new ArtOp(ArtOpKind.Fill, art.ColorIndex(tones[0]), new List<Point2> { new(minX, minY), new(maxX, minY), new(maxX, baseTop), new(minX, baseTop) }, 0.9 * px, true));
        for (int layer = 0; layer < 3; layer++)
        {
            int index = art.ColorIndex(tones[layer]);
            for (double x = minX + random.NextDouble() * 3 * px; x < maxX; x += (3.2 + 2.5 * random.NextDouble()) * px)
            {
                double tipY = baseTop + (maxY - baseTop) * (0.45 + 0.55 * random.NextDouble()) * (1.6 - 0.25 * layer);
                double lean = (random.NextDouble() - 0.5) * 6 * px;
                art.Ops.Add(new ArtOp(ArtOpKind.Fill, index, new List<Point2> { new(x - 1.3 * px, minY), new(x + 1.3 * px, minY), new(x + lean, tipY) }, 0, false));
            }
        }
    }

    /// <summary>Nervuras finas nas folhas grandes: da base da folha (o ponto mais perto do eixo da planta) às pontas.</summary>
    private static void AddVeins(CardArt art, List<Point2> tris, Rgb leaf, double px, double plantSize)
    {
        List<Point2> hull = Hull(tris);
        if (hull.Count < 3)
        {
            return;
        }

        double w = hull.Max(p => p.X) - hull.Min(p => p.X), h = hull.Max(p => p.Y) - hull.Min(p => p.Y);
        if (Math.Max(w, h) < 45 * px)
        {
            return;
        }

        // Base: o ponto do contorno mais próximo do eixo vertical da planta e mais baixo.
        Point2 basePoint = hull.OrderBy(p => Math.Abs(p.X) + 0.3 * p.Y).First();
        int color = art.ColorIndex(leaf.Shade(0.28));
        List<Point2> tips = hull.Where(p => p != basePoint).OrderByDescending(p => (p.X - basePoint.X) * (p.X - basePoint.X) + (p.Y - basePoint.Y) * (p.Y - basePoint.Y)).Take(7).ToList();
        foreach (Point2 tip in tips)
        {
            var end = new Point2(basePoint.X + (tip.X - basePoint.X) * 0.82, basePoint.Y + (tip.Y - basePoint.Y) * 0.82);
            art.Ops.Add(new ArtOp(ArtOpKind.Line, color, new[] { basePoint, end }, 0.7 * px, false));
        }
    }

    private static double Extent(MeshPiece piece)
    {
        (Vec3 min, Vec3 max) = piece.Bounds();
        return Math.Max(max.X - min.X, Math.Max(max.Y - min.Y, max.Z - min.Z));
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
