using Viridian.Core.Colors;
using Viridian.Core.Geometry;

namespace Viridian.Core.Model3D.Illustration;

/// <summary>Região de uma cor no plano do cartão: contornos fechados (externos e furos), em metros, sem sobreposição.</summary>
public sealed record ColorRegion(int ColorIndex, Rgb Color, List<List<Point2>> Loops);

/// <summary>Arte de um plano já achatada: regiões de cor que não se sobrepõem (o contorno preto é uma delas).</summary>
public sealed class CardRegions
{
    public List<ColorRegion> Regions { get; } = new();

    /// <summary>Tamanho da célula da grade usada no achatamento (m).</summary>
    public double Cell { get; init; }

    public int Loops => Regions.Sum(r => r.Loops.Count);

    public int Edges => Regions.Sum(r => r.Loops.Sum(l => l.Count));
}

/// <summary>
/// Achata a arte de um plano: pinta as operações numa grade fina (as de depois cobrem as de antes, como na pintura)
/// e traça o contorno de cada cor. O resultado são regiões que não se sobrepõem nem se cruzam, prontas para virar
/// extrusões finas na família do Revit. Os contornos são simplificados sem deixar dois deles se cruzarem.
/// </summary>
public static class CardRaster
{
    /// <param name="referenceSize">Porte de referência (o adulto): as fases menores usam célula proporcional, nunca menor que a de uma fração do adulto.</param>
    public static CardRegions Flatten(CardArt art, int cellsAcross = 600, double minCell = 0.0025, double referenceSize = 0)
    {
        ArgumentNullException.ThrowIfNull(art);
        double cell = Math.Max(minCell, CardArtist.Scale(art.Size, referenceSize) / cellsAcross);
        double minU = double.MaxValue, maxU = double.MinValue, minV = double.MaxValue, maxV = double.MinValue;
        foreach (ArtOp op in art.Ops)
        {
            foreach (Point2 p in op.Points)
            {
                minU = Math.Min(minU, p.X);
                maxU = Math.Max(maxU, p.X);
                minV = Math.Min(minV, p.Y);
                maxV = Math.Max(maxV, p.Y);
            }
        }

        var regions = new CardRegions { Cell = cell };
        if (art.Ops.Count == 0)
        {
            return regions;
        }

        double margin = art.Outline * 3 + cell * 3;
        minU -= margin;
        minV -= margin;
        int w = (int)Math.Ceiling((maxU + margin - minU) / cell) + 1;
        int h = (int)Math.Ceiling((maxV + margin - minV) / cell) + 1;
        var grid = new Grid(w, h, minU, minV, cell);
        foreach (ArtOp op in art.Ops)
        {
            byte color = (byte)Math.Min(op.Color, 255);
            double opLine = Math.Max(op.Width > 0 ? op.Width : art.Outline, 1.1 * cell);
            switch (op.Kind)
            {
                case ArtOpKind.Fill:
                    grid.FillPolygon(op.Points, color);
                    if (op.Outline)
                    {
                        for (int i = 0; i < op.Points.Count; i++)
                        {
                            grid.Stroke(op.Points[i], op.Points[(i + 1) % op.Points.Count], opLine, CardArt.Black);
                        }
                    }

                    break;
                case ArtOpKind.Union:
                    // Traço grosso preto em volta de cada triângulo e, por cima, o preenchimento: só a silhueta fica preta.
                    for (int i = 0; op.Outline && i + 2 < op.Points.Count; i += 3)
                    {
                        grid.Stroke(op.Points[i], op.Points[i + 1], 2 * opLine, CardArt.Black);
                        grid.Stroke(op.Points[i + 1], op.Points[i + 2], 2 * opLine, CardArt.Black);
                        grid.Stroke(op.Points[i + 2], op.Points[i], 2 * opLine, CardArt.Black);
                    }

                    for (int i = 0; i + 2 < op.Points.Count; i += 3)
                    {
                        grid.FillPolygon(new[] { op.Points[i], op.Points[i + 1], op.Points[i + 2] }, color);
                    }

                    break;
                case ArtOpKind.Line:
                    grid.Stroke(op.Points[0], op.Points[1], Math.Max(op.Width, 1.2 * cell), color);
                    break;
            }
        }

        for (int c = 1; c < art.Palette.Count && c < 256; c++)
        {
            List<List<Point2>> loops = grid.Trace((byte)c);
            if (loops.Count > 0)
            {
                regions.Regions.Add(new ColorRegion(c, art.Palette[c], loops));
            }
        }

        return regions;
    }

    private sealed class Grid
    {
        private readonly byte[] _cells;

        public Grid(int width, int height, double originU, double originV, double cell)
        {
            Width = width;
            Height = height;
            OriginU = originU;
            OriginV = originV;
            Cell = cell;
            _cells = new byte[width * height];
        }

        public int Width { get; }

        public int Height { get; }

        public double OriginU { get; }

        public double OriginV { get; }

        public double Cell { get; }

        /// <summary>Preenche as células cujo centro está dentro do polígono (regra par-ímpar).</summary>
        public void FillPolygon(IReadOnlyList<Point2> poly, byte color)
        {
            if (poly.Count < 3)
            {
                return;
            }

            double lo = poly.Min(p => p.Y), hi = poly.Max(p => p.Y);
            int j0 = Math.Max(0, (int)Math.Floor((lo - OriginV) / Cell - 0.5)), j1 = Math.Min(Height - 1, (int)Math.Ceiling((hi - OriginV) / Cell - 0.5));
            var xs = new List<double>();
            for (int j = j0; j <= j1; j++)
            {
                double y = OriginV + (j + 0.5) * Cell;
                xs.Clear();
                for (int i = 0; i < poly.Count; i++)
                {
                    Point2 a = poly[i], b = poly[(i + 1) % poly.Count];
                    if ((a.Y <= y && b.Y > y) || (b.Y <= y && a.Y > y))
                    {
                        xs.Add(a.X + (y - a.Y) / (b.Y - a.Y) * (b.X - a.X));
                    }
                }

                xs.Sort();
                for (int k = 0; k + 1 < xs.Count; k += 2)
                {
                    int i0 = Math.Max(0, (int)Math.Ceiling((xs[k] - OriginU) / Cell - 0.5));
                    int i1 = Math.Min(Width - 1, (int)Math.Floor((xs[k + 1] - OriginU) / Cell - 0.5));
                    for (int i = i0; i <= i1; i++)
                    {
                        _cells[j * Width + i] = color;
                    }
                }
            }
        }

        /// <summary>Traço reto de espessura dada, com pontas arredondadas.</summary>
        public void Stroke(Point2 a, Point2 b, double width, byte color)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, length = Math.Sqrt(dx * dx + dy * dy);
            double r = width / 2;
            if (length > 1e-12)
            {
                double nx = -dy / length * r, ny = dx / length * r;
                FillPolygon(new[] { new Point2(a.X + nx, a.Y + ny), new Point2(b.X + nx, b.Y + ny), new Point2(b.X - nx, b.Y - ny), new Point2(a.X - nx, a.Y - ny) }, color);
            }

            Disc(a, r, color);
            Disc(b, r, color);
        }

        private void Disc(Point2 c, double r, byte color) =>
            FillPolygon(Enumerable.Range(0, 8).Select(i => new Point2(c.X + Math.Cos(i * Math.PI / 4) * r, c.Y + Math.Sin(i * Math.PI / 4) * r)).ToList(), color);

        /// <summary>Contornos da cor: arestas de célula entre dentro e fora, ligadas em laços (externos anti-horários, furos horários).</summary>
        public List<List<Point2>> Trace(byte color)
        {
            var mask = new bool[Width * Height];
            bool any = false;
            for (int i = 0; i < _cells.Length; i++)
            {
                mask[i] = _cells[i] == color;
                any |= mask[i];
            }

            if (!any)
            {
                return new List<List<Point2>>();
            }

            bool M(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height && mask[y * Width + x];

            // Células que só se tocam pela quina formariam laços encostados: uma das células vazias entra na máscara.
            for (int pass = 0, changed = 1; changed > 0 && pass < 6; pass++)
            {
                changed = 0;
                for (int y = 0; y + 1 < Height; y++)
                {
                    for (int x = 0; x + 1 < Width; x++)
                    {
                        bool a = M(x, y), b = M(x + 1, y), c = M(x, y + 1), d = M(x + 1, y + 1);
                        if ((a && d && !b && !c) || (b && c && !a && !d))
                        {
                            mask[y * Width + (a ? x + 1 : x)] = true;
                            changed++;
                        }
                    }
                }
            }

            long Key(int x, int y) => (long)y * (Width + 1) + x;
            var next = new Dictionary<long, long>();
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    if (!M(x, y))
                    {
                        continue;
                    }

                    if (!M(x, y - 1))
                    {
                        next[Key(x, y)] = Key(x + 1, y);
                    }

                    if (!M(x + 1, y))
                    {
                        next[Key(x + 1, y)] = Key(x + 1, y + 1);
                    }

                    if (!M(x, y + 1))
                    {
                        next[Key(x + 1, y + 1)] = Key(x, y + 1);
                    }

                    if (!M(x - 1, y))
                    {
                        next[Key(x, y + 1)] = Key(x, y);
                    }
                }
            }

            var originals = new List<List<(int X, int Y)>>();
            var visited = new HashSet<long>();
            foreach (long start in next.Keys)
            {
                if (visited.Contains(start))
                {
                    continue;
                }

                var loop = new List<(int, int)>();
                long current = start;
                while (visited.Add(current))
                {
                    loop.Add(((int)(current % (Width + 1)), (int)(current / (Width + 1))));
                    current = next[current];
                }

                List<(int X, int Y)> corners = Corners(loop);
                if (corners.Count >= 3 && Math.Abs(SignedArea(corners)) >= 1.5)
                {
                    originals.Add(corners);
                }
            }

            List<List<(double X, double Y)>> loops = SimplifySafely(originals);
            return loops.Select(l => l.Select(p => new Point2(OriginU + p.X * Cell, OriginV + p.Y * Cell)).ToList()).ToList();
        }

        private static List<(int X, int Y)> Corners(List<(int X, int Y)> loop)
        {
            var result = new List<(int X, int Y)>();
            int n = loop.Count;
            for (int i = 0; i < n; i++)
            {
                (int X, int Y) prev = loop[(i - 1 + n) % n], cur = loop[i], nxt = loop[(i + 1) % n];
                if ((cur.X - prev.X) * (nxt.Y - cur.Y) - (cur.Y - prev.Y) * (nxt.X - cur.X) != 0)
                {
                    result.Add(cur);
                }
            }

            return result;
        }

        private static double SignedArea(List<(int X, int Y)> loop) => SignedArea(loop.Select(p => ((double)p.X, (double)p.Y)).ToList());

        private static double SignedArea(List<(double X, double Y)> loop)
        {
            double a = 0;
            for (int i = 0; i < loop.Count; i++)
            {
                (double x0, double y0) = loop[i];
                (double x1, double y1) = loop[(i + 1) % loop.Count];
                a += x0 * y1 - x1 * y0;
            }

            return a / 2;
        }

        /// <summary>Douglas–Peucker em cada laço; laços que passariam a se cruzar voltam à forma exata da grade.</summary>
        private static List<List<(double X, double Y)>> SimplifySafely(List<List<(int X, int Y)>> originals)
        {
            var exact = originals.Select(l => l.Select(p => ((double)p.X, (double)p.Y)).ToList()).ToList();
            var current = exact.Select(l => Simplify(l, 0.95)).ToList();
            for (int i = 0; i < current.Count; i++)
            {
                if (current[i].Count < 3 || Math.Abs(SignedArea(current[i])) < 1.0)
                {
                    current[i] = exact[i];
                }
            }

            for (int pass = 0; pass < 6; pass++)
            {
                HashSet<int> conflicts = Conflicts(current);
                conflicts.RemoveWhere(i => ReferenceEquals(current[i], exact[i]));
                if (conflicts.Count == 0)
                {
                    return current;
                }

                foreach (int i in conflicts)
                {
                    current[i] = exact[i];
                }
            }

            return exact;
        }

        private static List<(double X, double Y)> Simplify(List<(double X, double Y)> loop, double epsilon)
        {
            if (loop.Count <= 4)
            {
                return loop;
            }

            // Divide o laço no ponto mais distante do primeiro e simplifica as duas metades.
            int far = 0;
            double best = -1;
            for (int i = 1; i < loop.Count; i++)
            {
                double d = Math.Pow(loop[i].X - loop[0].X, 2) + Math.Pow(loop[i].Y - loop[0].Y, 2);
                if (d > best)
                {
                    best = d;
                    far = i;
                }
            }

            var keep = new bool[loop.Count + 1];
            keep[0] = keep[far] = true;
            var ring = loop.Concat(new[] { loop[0] }).ToList();
            Mark(ring, 0, far, epsilon, keep);
            Mark(ring, far, loop.Count, epsilon, keep);
            return Enumerable.Range(0, loop.Count).Where(i => keep[i]).Select(i => loop[i]).ToList();
        }

        private static void Mark(List<(double X, double Y)> ring, int first, int last, double epsilon, bool[] keep)
        {
            var stack = new Stack<(int, int)>();
            stack.Push((first, last));
            while (stack.Count > 0)
            {
                (int a, int b) = stack.Pop();
                if (b - a < 2)
                {
                    continue;
                }

                (double ax, double ay) = ring[a];
                (double bx, double by) = ring[b];
                double dx = bx - ax, dy = by - ay, len = Math.Sqrt(dx * dx + dy * dy);
                int index = -1;
                double max = 0;
                for (int i = a + 1; i < b; i++)
                {
                    double d = len < 1e-12
                        ? Math.Sqrt(Math.Pow(ring[i].X - ax, 2) + Math.Pow(ring[i].Y - ay, 2))
                        : Math.Abs(dy * ring[i].X - dx * ring[i].Y + bx * ay - by * ax) / len;
                    if (d > max)
                    {
                        max = d;
                        index = i;
                    }
                }

                if (index >= 0 && max > epsilon)
                {
                    keep[index % (keep.Length - 1)] = true;
                    stack.Push((a, index));
                    stack.Push((index, b));
                }
            }
        }

        /// <summary>Laços cujos segmentos se cruzam ou se encostam (no próprio laço ou com outro laço da mesma cor).</summary>
        private static HashSet<int> Conflicts(List<List<(double X, double Y)>> loops)
        {
            const double bucket = 6;
            var segments = new List<(int Loop, int Index, int Count, double X0, double Y0, double X1, double Y1)>();
            for (int l = 0; l < loops.Count; l++)
            {
                List<(double X, double Y)> loop = loops[l];
                for (int i = 0; i < loop.Count; i++)
                {
                    (double x0, double y0) = loop[i];
                    (double x1, double y1) = loop[(i + 1) % loop.Count];
                    segments.Add((l, i, loop.Count, x0, y0, x1, y1));
                }
            }

            var buckets = new Dictionary<(int, int), List<int>>();
            for (int s = 0; s < segments.Count; s++)
            {
                var seg = segments[s];
                int bx0 = (int)Math.Floor(Math.Min(seg.X0, seg.X1) / bucket), bx1 = (int)Math.Floor(Math.Max(seg.X0, seg.X1) / bucket);
                int by0 = (int)Math.Floor(Math.Min(seg.Y0, seg.Y1) / bucket), by1 = (int)Math.Floor(Math.Max(seg.Y0, seg.Y1) / bucket);
                for (int bx = bx0; bx <= bx1; bx++)
                {
                    for (int by = by0; by <= by1; by++)
                    {
                        if (!buckets.TryGetValue((bx, by), out List<int>? list))
                        {
                            list = new List<int>();
                            buckets[(bx, by)] = list;
                        }

                        list.Add(s);
                    }
                }
            }

            var conflicts = new HashSet<int>();
            var tested = new HashSet<(int, int)>();
            foreach (List<int> list in buckets.Values)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        int a = Math.Min(list[i], list[j]), b = Math.Max(list[i], list[j]);
                        if (!tested.Add((a, b)))
                        {
                            continue;
                        }

                        var s1 = segments[a];
                        var s2 = segments[b];
                        if (s1.Loop == s2.Loop)
                        {
                            int diff = Math.Abs(s1.Index - s2.Index);
                            if (diff == 1 || diff == s1.Count - 1)
                            {
                                continue; // vizinhos no mesmo laço compartilham o vértice
                            }
                        }

                        if (Touch(s1.X0, s1.Y0, s1.X1, s1.Y1, s2.X0, s2.Y0, s2.X1, s2.Y1))
                        {
                            conflicts.Add(s1.Loop);
                            conflicts.Add(s2.Loop);
                        }
                    }
                }
            }

            return conflicts;
        }

        private static bool Touch(double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy)
        {
            const double eps = 1e-9;
            double O(double px, double py, double qx, double qy, double rx, double ry) => (qx - px) * (ry - py) - (qy - py) * (rx - px);
            bool On(double px, double py, double qx, double qy, double rx, double ry) =>
                Math.Min(px, qx) - eps <= rx && rx <= Math.Max(px, qx) + eps && Math.Min(py, qy) - eps <= ry && ry <= Math.Max(py, qy) + eps;
            double o1 = O(ax, ay, bx, by, cx, cy), o2 = O(ax, ay, bx, by, dx, dy), o3 = O(cx, cy, dx, dy, ax, ay), o4 = O(cx, cy, dx, dy, bx, by);
            if (((o1 > eps && o2 < -eps) || (o1 < -eps && o2 > eps)) && ((o3 > eps && o4 < -eps) || (o3 < -eps && o4 > eps)))
            {
                return true;
            }

            return (Math.Abs(o1) <= eps && On(ax, ay, bx, by, cx, cy)) || (Math.Abs(o2) <= eps && On(ax, ay, bx, by, dx, dy))
                   || (Math.Abs(o3) <= eps && On(cx, cy, dx, dy, ax, ay)) || (Math.Abs(o4) <= eps && On(cx, cy, dx, dy, bx, by));
        }
    }
}
