namespace Viridian.Core.Geometry;

/// <summary>
/// Polígono 2D formado por um ou mais anéis fechados. O interior segue a regra par-ímpar,
/// de modo que anéis internos funcionam como vazios (áreas não plantáveis) sem exigir orientação específica.
/// </summary>
public sealed class Polygon2
{
    private const double DuplicateTolerance = 1e-9;
    private readonly List<IReadOnlyList<Point2>> _rings = new();
    private double? _area;

    public Polygon2(IEnumerable<IEnumerable<Point2>> rings)
    {
        ArgumentNullException.ThrowIfNull(rings);
        foreach (IEnumerable<Point2> ring in rings)
        {
            List<Point2> cleaned = CleanRing(ring);
            if (cleaned.Count >= 3 && Math.Abs(Geo.SignedArea(cleaned)) > 1e-12)
            {
                _rings.Add(cleaned);
            }
        }

        Bounds = Bounds2.FromPoints(_rings.SelectMany(r => r));
    }

    public Polygon2(IEnumerable<Point2> singleRing)
        : this(new[] { singleRing })
    {
    }

    public IReadOnlyList<IReadOnlyList<Point2>> Rings => _rings;

    public bool IsEmpty => _rings.Count == 0;

    public Bounds2 Bounds { get; }

    public int VertexCount => _rings.Sum(r => r.Count);

    /// <summary>Área líquida em m², descontando os vazios.</summary>
    public double Area => _area ??= ComputeArea();

    /// <summary>Soma dos comprimentos de todos os anéis.</summary>
    public double Perimeter
    {
        get
        {
            double total = 0;
            foreach (IReadOnlyList<Point2> ring in _rings)
            {
                for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
                {
                    total += ring[j].DistanceTo(ring[i]);
                }
            }

            return total;
        }
    }

    public bool Contains(Point2 p)
    {
        if (!Bounds.Contains(p))
        {
            return false;
        }

        bool inside = false;
        foreach (IReadOnlyList<Point2> ring in _rings)
        {
            if (Geo.PointInRing(ring, p))
            {
                inside = !inside;
            }
        }

        return inside;
    }

    /// <summary>Menor distância do ponto a qualquer aresta do polígono.</summary>
    public double DistanceToBoundary(Point2 p)
    {
        double best = double.PositiveInfinity;
        foreach (IReadOnlyList<Point2> ring in _rings)
        {
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                double d = Geo.DistancePointSegment(p, ring[j], ring[i]);
                if (d < best)
                {
                    best = d;
                }
            }
        }

        return best;
    }

    /// <summary>Verifica se o ponto está dentro do polígono e afastado das bordas pelo menos <paramref name="margin"/>.</summary>
    public bool ContainsWithMargin(Point2 p, double margin)
    {
        if (!Contains(p))
        {
            return false;
        }

        return margin <= 0 || DistanceToBoundary(p) >= margin;
    }

    /// <summary>Indica se alguma aresta cruza outra (limite inválido para plantio).</summary>
    public bool HasSelfIntersections()
    {
        var segments = new List<(Point2 A, Point2 B, int Ring, int Index, int Count)>();
        for (int r = 0; r < _rings.Count; r++)
        {
            IReadOnlyList<Point2> ring = _rings[r];
            for (int i = 0; i < ring.Count; i++)
            {
                segments.Add((ring[i], ring[(i + 1) % ring.Count], r, i, ring.Count));
            }
        }

        if (segments.Count > 4000)
        {
            // Limite de segurança: polígonos enormes não são verificados para não travar a interface.
            return false;
        }

        for (int i = 0; i < segments.Count; i++)
        {
            for (int j = i + 1; j < segments.Count; j++)
            {
                var s1 = segments[i];
                var s2 = segments[j];
                if (s1.Ring == s2.Ring)
                {
                    int diff = Math.Abs(s1.Index - s2.Index);
                    if (diff == 1 || diff == s1.Count - 1)
                    {
                        continue; // arestas vizinhas compartilham um vértice
                    }
                }

                if (Geo.SegmentsCross(s1.A, s1.B, s2.A, s2.B))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Um ponto garantidamente interior, adequado para posicionar etiquetas.</summary>
    public Point2 InteriorPoint()
    {
        if (IsEmpty)
        {
            return Point2.Zero;
        }

        Point2 centroid = Centroid();
        if (Contains(centroid))
        {
            return centroid;
        }

        // Varre linhas horizontais e usa o meio do maior trecho interior encontrado.
        Point2 best = _rings[0][0];
        double bestSpan = -1;
        const int scanLines = 15;
        for (int k = 1; k <= scanLines; k++)
        {
            double y = Bounds.MinY + Bounds.Height * k / (scanLines + 1);
            var crossings = new List<double>();
            foreach (IReadOnlyList<Point2> ring in _rings)
            {
                for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
                {
                    Point2 a = ring[i];
                    Point2 b = ring[j];
                    if ((a.Y > y) != (b.Y > y))
                    {
                        crossings.Add(a.X + (y - a.Y) * (b.X - a.X) / (b.Y - a.Y));
                    }
                }
            }

            crossings.Sort();
            for (int i = 0; i + 1 < crossings.Count; i += 2)
            {
                double span = crossings[i + 1] - crossings[i];
                if (span > bestSpan)
                {
                    bestSpan = span;
                    best = new Point2((crossings[i] + crossings[i + 1]) / 2, y);
                }
            }
        }

        return best;
    }

    /// <summary>Centroide ponderado pela área (pode ficar fora de polígonos côncavos).</summary>
    public Point2 Centroid()
    {
        double areaSum = 0, cx = 0, cy = 0;
        for (int r = 0; r < _rings.Count; r++)
        {
            IReadOnlyList<Point2> ring = _rings[r];
            double sign = RingSign(r);
            double a = 0, x = 0, y = 0;
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                double cross = ring[j].X * ring[i].Y - ring[i].X * ring[j].Y;
                a += cross;
                x += (ring[j].X + ring[i].X) * cross;
                y += (ring[j].Y + ring[i].Y) * cross;
            }

            if (Math.Abs(a) < 1e-12)
            {
                continue;
            }

            double ringArea = Math.Abs(a) / 2;
            double ringCx = x / (3 * a);
            double ringCy = y / (3 * a);
            areaSum += sign * ringArea;
            cx += sign * ringArea * ringCx;
            cy += sign * ringArea * ringCy;
        }

        return Math.Abs(areaSum) < 1e-12 ? Bounds.Center : new Point2(cx / areaSum, cy / areaSum);
    }

    private double ComputeArea()
    {
        double total = 0;
        for (int r = 0; r < _rings.Count; r++)
        {
            total += RingSign(r) * Math.Abs(Geo.SignedArea(_rings[r]));
        }

        return Math.Max(0, total);
    }

    /// <summary>+1 para anéis externos, -1 para vazios, conforme o nível de aninhamento.</summary>
    private double RingSign(int ringIndex)
    {
        Point2 probe = _rings[ringIndex][0];
        int depth = 0;
        for (int other = 0; other < _rings.Count; other++)
        {
            if (other != ringIndex && Geo.PointInRing(_rings[other], probe))
            {
                depth++;
            }
        }

        return depth % 2 == 0 ? 1 : -1;
    }

    private static List<Point2> CleanRing(IEnumerable<Point2> ring)
    {
        var cleaned = new List<Point2>();
        foreach (Point2 p in ring)
        {
            if (double.IsNaN(p.X) || double.IsNaN(p.Y) || double.IsInfinity(p.X) || double.IsInfinity(p.Y))
            {
                continue;
            }

            if (cleaned.Count == 0 || cleaned[^1].DistanceTo(p) > DuplicateTolerance)
            {
                cleaned.Add(p);
            }
        }

        if (cleaned.Count > 1 && cleaned[0].DistanceTo(cleaned[^1]) <= DuplicateTolerance)
        {
            cleaned.RemoveAt(cleaned.Count - 1);
        }

        return cleaned;
    }
}
