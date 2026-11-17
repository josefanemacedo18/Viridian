using Viridian.Core.Geometry;

namespace Viridian.Core.Distribution;

/// <summary>Distribuições que seguem formas: eixo linear, bordadura, círculos, agrupamentos e degradê.</summary>
public static partial class PlantDistributor
{
    /// <summary>Espaçamento entre fileiras quando não informado.</summary>
    private static double DefaultRowSpacing(DistributionRequest request, double spacing) =>
        request.RowSpacingM > 0 ? request.RowSpacingM : request.RowArrangement == RowArrangement.Alternadas ? spacing * Math.Sqrt(3.0) / 2 : spacing;

    // ------------------------------------------------------------------ plantio linear

    private static DistributionResult DistributeLinear(Polygon2? region, DistributionRequest request)
    {
        List<Point2> path = CleanPath(request.Path);
        if (path.Count < 2)
        {
            return new DistributionResult { Message = "Defina o eixo do plantio linear com pelo menos dois pontos." };
        }

        bool closed = request.PathClosed && path.Count >= 3;
        var axis = new PathSampler(path, closed);
        double length = axis.Length;
        if (length < MinSpacing)
        {
            return new DistributionResult { Message = "O eixo do plantio é curto demais." };
        }

        List<double>? stations = Stations(request, length, closed, out double step, out string problem);
        if (stations is null)
        {
            return new DistributionResult { Message = problem };
        }

        int rows = Math.Clamp(request.Rows, 1, 5);
        double rowSpacing = Math.Max(MinSpacing, DefaultRowSpacing(request, step));
        int limit = request.MaxCount > 0 ? Math.Min(request.MaxCount, HardLimit) : HardLimit;
        long expected = (long)stations.Count * rows;
        if (expected > limit)
        {
            return new DistributionResult
            {
                ComputedCount = (int)Math.Min(int.MaxValue, expected),
                Exceeded = true,
                EffectiveSpacingM = step,
                Message = $"O plantio resultaria em {expected} exemplares, acima do máximo de {limit}.",
            };
        }

        var accepted = new SpacingGrid(Math.Max(MinSpacing, Math.Min(step, rowSpacing) * 0.5));
        var points = new List<Point2>();
        for (int row = 0; row < rows; row++)
        {
            double lateral = request.PathOffsetM + (row - (rows - 1) / 2.0) * rowSpacing;
            double shift = request.RowArrangement == RowArrangement.Alternadas && row % 2 == 1 ? step / 2 : 0;
            foreach (double station in stations)
            {
                double d = station + shift;
                if (closed)
                {
                    d %= length;
                }
                else if (d > length + 1e-9)
                {
                    continue;
                }

                (Point2 p, Point2 tangent) = axis.Sample(d);
                Point2 q = p + tangent.Perp() * lateral;
                if (region is not null && !region.IsEmpty && !region.ContainsWithMargin(q, request.EdgeMarginM))
                {
                    continue;
                }

                if (Blocked(request, q) || !accepted.IsFree(q))
                {
                    continue;
                }

                accepted.Add(q);
                points.Add(q);
            }
        }

        return new DistributionResult
        {
            Points = points,
            ComputedCount = points.Count,
            EffectiveSpacingM = step,
            Message = points.Count == 0 ? "Nenhum ponto do eixo ficou dentro da região." : string.Empty,
        };
    }

    /// <summary>Distâncias ao longo do eixo em que as plantas de uma fileira são posicionadas.</summary>
    private static List<double>? Stations(DistributionRequest request, double length, bool closed, out double step, out string problem)
    {
        problem = string.Empty;
        step = 0;
        var stations = new List<double>();
        if (request.LinearMode == LinearMode.Quantidade)
        {
            int n = request.Count;
            if (n < 1)
            {
                problem = "Informe a quantidade de exemplares por fileira.";
                return null;
            }

            if (n > HardLimit)
            {
                problem = $"A quantidade não pode passar de {HardLimit}.";
                return null;
            }

            if (closed)
            {
                step = length / n;
                for (int i = 0; i < n; i++)
                {
                    stations.Add(i * step);
                }
            }
            else if (n == 1)
            {
                step = length;
                stations.Add(length / 2);
            }
            else
            {
                step = length / (n - 1);
                for (int i = 0; i < n; i++)
                {
                    stations.Add(Math.Min(length, i * step));
                }
            }

            if (step < MinSpacing)
            {
                problem = "A quantidade é grande demais para o comprimento do eixo.";
                return null;
            }

            return stations;
        }

        double spacing = request.SpacingM;
        if (!(spacing >= MinSpacing))
        {
            problem = $"O espaçamento deve ser de pelo menos {MinSpacing * 100:0} cm.";
            return null;
        }

        if (length / spacing > HardLimit)
        {
            step = spacing;
            problem = $"O plantio resultaria em mais de {HardLimit} exemplares por fileira.";
            return null;
        }

        if (closed)
        {
            int n = Math.Max(1, (int)Math.Round(length / spacing));
            step = length / n;
            for (int i = 0; i < n; i++)
            {
                stations.Add(i * step);
            }

            return stations;
        }

        switch (request.LinearAlignment)
        {
            case LinearAlignment.Ajustado:
            {
                int n = Math.Max(2, (int)Math.Round(length / spacing) + 1);
                step = length / (n - 1);
                for (int i = 0; i < n; i++)
                {
                    stations.Add(Math.Min(length, i * step));
                }

                break;
            }

            case LinearAlignment.Centralizado:
            {
                int n = (int)Math.Floor(length / spacing + 1e-9) + 1;
                step = spacing;
                double offset = (length - (n - 1) * spacing) / 2;
                for (int i = 0; i < n; i++)
                {
                    stations.Add(offset + i * spacing);
                }

                break;
            }

            default:
            {
                int n = (int)Math.Floor(length / spacing + 1e-9) + 1;
                step = spacing;
                for (int i = 0; i < n; i++)
                {
                    stations.Add(Math.Min(length, i * spacing));
                }

                break;
            }
        }

        return stations;
    }

    // ------------------------------------------------------------------ círculo por centro e raio

    private static DistributionResult DistributeCircle(DistributionRequest request)
    {
        double spacing = request.SpacingM;
        if (!(spacing >= MinSpacing))
        {
            return new DistributionResult { Message = $"O espaçamento deve ser de pelo menos {MinSpacing * 100:0} cm." };
        }

        if (!(request.CircleRadiusM > 0) || !request.CircleCenter.HasValue)
        {
            return new DistributionResult { Message = "Defina o centro e o raio do círculo." };
        }

        Point2 center = request.CircleCenter.Value;
        int rows = Math.Clamp(request.Rows, 1, 10);
        double rowSpacing = Math.Max(MinSpacing, DefaultRowSpacing(request, spacing));
        int limit = request.MaxCount > 0 ? Math.Min(request.MaxCount, HardLimit) : HardLimit;
        double start = Geo.DegToRad(request.AngleDeg);

        long expected = 0;
        for (int row = 0; row < rows; row++)
        {
            double radius = request.CircleRadiusM - row * rowSpacing;
            expected += radius > MinSpacing / 2 ? Math.Max(3, (long)Math.Round(Geo.TwoPi * radius / spacing)) : 1;
        }

        if (expected > limit)
        {
            return new DistributionResult
            {
                ComputedCount = (int)Math.Min(int.MaxValue, expected),
                Exceeded = true,
                EffectiveSpacingM = spacing,
                Message = $"O plantio resultaria em {expected} exemplares, acima do máximo de {limit}.",
            };
        }

        var points = new List<Point2>();
        var accepted = new SpacingGrid(Math.Max(MinSpacing, Math.Min(spacing, rowSpacing) * 0.5));
        for (int row = 0; row < rows; row++)
        {
            double radius = request.CircleRadiusM - row * rowSpacing;
            if (radius <= MinSpacing / 2)
            {
                if (radius > -rowSpacing / 2 && !Blocked(request, center) && accepted.IsFree(center))
                {
                    accepted.Add(center);
                    points.Add(center);
                }

                break;
            }

            int n = Math.Max(3, (int)Math.Round(Geo.TwoPi * radius / spacing));
            double phase = start + (request.RowArrangement == RowArrangement.Alternadas && row % 2 == 1 ? Math.PI / n : 0);
            for (int i = 0; i < n; i++)
            {
                Point2 p = center + Point2.Polar(radius, phase + Geo.TwoPi * i / n);
                if (!Blocked(request, p) && accepted.IsFree(p))
                {
                    accepted.Add(p);
                    points.Add(p);
                }
            }
        }

        return new DistributionResult { Points = points, ComputedCount = points.Count, EffectiveSpacingM = spacing };
    }

    // ------------------------------------------------------------------ bordadura

    private static List<Point2>? Border(Polygon2 region, DistributionRequest request, double spacing, int limit, out int computed)
    {
        int rows = Math.Clamp(request.Rows, 1, 10);
        double rowSpacing = Math.Max(MinSpacing, DefaultRowSpacing(request, spacing));
        double fine = Math.Max(0.005, spacing / 8);
        double probe = Math.Max(0.002, fine / 4);
        var accepted = new SpacingGrid(Math.Min(spacing, rows > 1 ? rowSpacing : spacing) * 0.9);
        var points = new List<Point2>();
        computed = 0;

        double perimeter = region.Perimeter;
        if (perimeter / fine * rows > MaxCandidates)
        {
            computed = (int)Math.Min(int.MaxValue, perimeter / spacing * rows);
            return null;
        }

        foreach (IReadOnlyList<Point2> ring in region.Rings)
        {
            var sampler = new PathSampler(ring.ToList(), closed: true);
            int samples = Math.Max(12, (int)Math.Ceiling(sampler.Length / fine));
            for (int row = 0; row < rows; row++)
            {
                double inset = Math.Max(request.EdgeMarginM + row * rowSpacing, probe * 2);

                // Contorno deslocado para dentro: amostragem fina do limite, mantendo só os pontos à distância correta.
                var chain = new List<Point2>();
                var chains = new List<List<Point2>>();
                for (int i = 0; i < samples; i++)
                {
                    (Point2 p, Point2 tangent) = sampler.Sample(sampler.Length * i / samples);
                    Point2 normal = tangent.Perp();
                    Point2 inward = region.Contains(p + normal * probe) ? normal : -normal;
                    Point2 q = p + inward * inset;
                    bool valid = region.Contains(q) && region.DistanceToBoundary(q) >= inset * 0.985;
                    if (valid && (chain.Count == 0 || chain[^1].DistanceTo(q) <= fine * 4 + inset * 0.1))
                    {
                        chain.Add(q);
                    }
                    else
                    {
                        if (chain.Count > 1)
                        {
                            chains.Add(chain);
                        }

                        chain = valid ? new List<Point2> { q } : new List<Point2>();
                    }
                }

                if (chain.Count > 1)
                {
                    // Une o fim ao começo quando o contorno deslocado é contínuo.
                    if (chains.Count > 0 && chain[^1].DistanceTo(chains[0][0]) <= fine * 4 + inset * 0.1)
                    {
                        chain.AddRange(chains[0]);
                        chains[0] = chain;
                    }
                    else
                    {
                        chains.Add(chain);
                    }
                }

                double shift = request.RowArrangement == RowArrangement.Alternadas && row % 2 == 1 ? spacing / 2 : 0;
                foreach (List<Point2> path in chains)
                {
                    var walker = new PathSampler(path, closed: false);
                    for (double d = shift; d <= walker.Length + 1e-9; d += spacing)
                    {
                        Point2 q = walker.Sample(d).Point;
                        if (Blocked(request, q) || !accepted.IsFree(q))
                        {
                            continue;
                        }

                        accepted.Add(q);
                        points.Add(q);
                        if (points.Count > limit)
                        {
                            computed = points.Count;
                            return points.Count > HardLimit ? null : points;
                        }
                    }
                }
            }
        }

        computed = points.Count;
        return points;
    }

    // ------------------------------------------------------------------ anéis concêntricos

    private static List<Point2>? Rings(Polygon2 region, DistributionRequest request, double spacing, int limit, out int computed)
    {
        Point2 center = request.CircleCenter ?? CenterOf(region);
        double ringSpacing = Math.Max(MinSpacing, request.RowSpacingM > 0 ? request.RowSpacingM : spacing);
        Bounds2 b = region.Bounds;
        double reach = new[]
        {
            new Point2(b.MinX, b.MinY), new Point2(b.MaxX, b.MinY), new Point2(b.MaxX, b.MaxY), new Point2(b.MinX, b.MaxY),
        }.Max(c => c.DistanceTo(center));

        computed = 0;
        long candidates = (long)Math.Ceiling(Math.PI * reach * reach / (spacing * ringSpacing)) + 1;
        if (candidates > MaxCandidates)
        {
            computed = (int)Math.Min(int.MaxValue, region.Area / (spacing * ringSpacing));
            return null;
        }

        var points = new List<Point2>();
        double start = Geo.DegToRad(request.AngleDeg);
        if (region.ContainsWithMargin(center, request.EdgeMarginM) && !Blocked(request, center))
        {
            points.Add(center);
        }

        int ringIndex = 1;
        for (double radius = ringSpacing; radius <= reach + 1e-9; radius += ringSpacing, ringIndex++)
        {
            int n = Math.Max(3, (int)Math.Round(Geo.TwoPi * radius / spacing));
            double phase = start + (request.RowArrangement == RowArrangement.Alternadas && ringIndex % 2 == 1 ? Math.PI / n : 0);
            for (int i = 0; i < n; i++)
            {
                Point2 p = center + Point2.Polar(radius, phase + Geo.TwoPi * i / n);
                if (region.ContainsWithMargin(p, request.EdgeMarginM) && !Blocked(request, p))
                {
                    points.Add(p);
                    if (points.Count > limit)
                    {
                        computed = points.Count;
                        return points.Count > HardLimit ? null : points;
                    }
                }
            }
        }

        computed = points.Count;
        return points;
    }

    /// <summary>Centro de uma região: o centroide quando está dentro dela; senão, um ponto interior.</summary>
    public static Point2 CenterOf(Polygon2 region)
    {
        Point2 centroid = region.Centroid();
        return region.Contains(centroid) ? centroid : region.InteriorPoint();
    }

    // ------------------------------------------------------------------ agrupamentos naturalistas

    private static List<Point2>? Groups(Polygon2 region, DistributionRequest request, double spacing, int limit, out int computed)
    {
        int minSize = Math.Clamp(request.GroupSizeMin, 1, 25);
        int maxSize = Math.Clamp(Math.Max(request.GroupSizeMax, minSize), 1, 25);
        var sizes = Enumerable.Range(minSize, maxSize - minSize + 1).Where(k => k % 2 == 1).ToList();
        if (sizes.Count == 0)
        {
            sizes.Add(minSize);
        }

        double gap = request.GroupGapM > 0 ? request.GroupGapM : spacing;
        double averageSize = sizes.Average();
        double groupRadius = spacing * (0.55 * Math.Sqrt(averageSize) * 1.3 + 0.3);
        var centersRequest = new DistributionRequest
        {
            Pattern = DistributionPattern.Natural,
            EdgeMarginM = request.EdgeMarginM,
            Seed = DeterministicRandom.Combine(request.Seed, 7001),
            Obstacles = request.Obstacles,
        };

        List<Point2>? centers = Poisson(region, centersRequest, 2 * groupRadius + gap, HardLimit, out computed);
        if (centers is null)
        {
            return null;
        }

        var rng = new DeterministicRandom(DeterministicRandom.Combine(request.Seed, 7013));
        var accepted = new SpacingGrid(spacing * 0.92);
        var points = new List<Point2>();

        // Malha hexagonal de candidatos ao redor do centro de cada mancha.
        var lattice = new List<Point2>();
        int span = (int)Math.Ceiling(Math.Sqrt(maxSize)) + 3;
        double rowStep = spacing * Math.Sqrt(3.0) / 2;
        for (int j = -span; j <= span; j++)
        {
            for (int i = -span; i <= span; i++)
            {
                lattice.Add(new Point2((i + ((j & 1) != 0 ? 0.5 : 0)) * spacing, j * rowStep));
            }
        }

        foreach (Point2 c in centers)
        {
            int size = sizes[rng.NextInt(0, sizes.Count)];
            double rotation = rng.Range(0, Math.PI);
            double elongation = rng.Range(1.0, 1.7);
            var candidates = lattice
                .Select(v => (Point: c + v.Rotate(rotation), Order: Math.Sqrt(v.X * v.X / (elongation * elongation) + v.Y * v.Y) + rng.Range(0, spacing * 0.25)))
                .OrderBy(t => t.Order)
                .ToList();
            int placed = 0;
            foreach ((Point2 p, double _) in candidates)
            {
                if (placed >= size)
                {
                    break;
                }

                if (!region.ContainsWithMargin(p, request.EdgeMarginM) || Blocked(request, p) || !accepted.IsFree(p))
                {
                    continue;
                }

                accepted.Add(p);
                points.Add(p);
                placed++;
                if (points.Count > limit)
                {
                    computed = points.Count;
                    return points.Count > HardLimit ? null : points;
                }
            }
        }

        computed = points.Count;
        return points;
    }

    // ------------------------------------------------------------------ degradê

    private static List<Point2>? Gradient(Polygon2 region, DistributionRequest request, double spacing, int limit, out int computed)
    {
        List<Point2>? dense = Poisson(region, request, spacing, HardLimit, out computed);
        if (dense is null || dense.Count == 0)
        {
            return dense;
        }

        double angle = Geo.DegToRad(request.AngleDeg);
        var dir = new Point2(Math.Cos(angle), Math.Sin(angle));
        double min = dense.Min(p => p.Dot(dir));
        double max = dense.Max(p => p.Dot(dir));
        double span = Math.Max(1e-9, max - min);
        double end = Math.Clamp(request.GradientEndPercent, 0, 100) / 100.0;
        var rng = new DeterministicRandom(DeterministicRandom.Combine(request.Seed, 9001));
        var kept = new List<Point2>(dense.Count);
        foreach (Point2 p in dense)
        {
            double t = (p.Dot(dir) - min) / span;
            double keep = 1 - (1 - end) * t;
            if (rng.NextDouble() < keep)
            {
                kept.Add(p);
            }
        }

        computed = kept.Count;
        return kept.Count > limit ? (kept.Count > HardLimit ? null : kept) : kept;
    }

    // ------------------------------------------------------------------ utilitários

    private static bool Blocked(DistributionRequest request, Point2 p)
    {
        foreach (Obstacle obstacle in request.Obstacles)
        {
            if (p.DistanceTo(obstacle.Center) < obstacle.Radius)
            {
                return true;
            }
        }

        return false;
    }

    private static List<Point2> CleanPath(IReadOnlyList<Point2> path)
    {
        var clean = new List<Point2>();
        foreach (Point2 p in path ?? Array.Empty<Point2>())
        {
            if (double.IsFinite(p.X) && double.IsFinite(p.Y) && (clean.Count == 0 || clean[^1].DistanceTo(p) > 0.001))
            {
                clean.Add(p);
            }
        }

        return clean;
    }

    /// <summary>Percorre uma polilinha pelo comprimento acumulado, devolvendo ponto e direção.</summary>
    private sealed class PathSampler
    {
        private readonly List<Point2> _points;
        private readonly double[] _cumulative;

        public PathSampler(List<Point2> points, bool closed)
        {
            _points = new List<Point2>(points);
            if (closed && _points.Count > 2 && _points[0].DistanceTo(_points[^1]) > 1e-9)
            {
                _points.Add(_points[0]);
            }

            _cumulative = new double[_points.Count];
            for (int i = 1; i < _points.Count; i++)
            {
                _cumulative[i] = _cumulative[i - 1] + _points[i - 1].DistanceTo(_points[i]);
            }
        }

        public double Length => _cumulative[^1];

        public (Point2 Point, Point2 Tangent) Sample(double distance)
        {
            double d = Math.Clamp(distance, 0, Length);
            int lo = 0;
            int hi = _cumulative.Length - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (_cumulative[mid] <= d)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }

            Point2 a = _points[lo];
            Point2 b = _points[hi];
            double segment = _cumulative[hi] - _cumulative[lo];
            double t = segment < 1e-12 ? 0 : (d - _cumulative[lo]) / segment;
            return (Point2.Lerp(a, b, t), (b - a).Normalized());
        }
    }

    /// <summary>Malha espacial que garante uma distância mínima entre os pontos aceitos.</summary>
    private sealed class SpacingGrid
    {
        private readonly double _min;
        private readonly Dictionary<(long, long), List<Point2>> _cells = new();

        public SpacingGrid(double minDistance)
        {
            _min = Math.Max(1e-6, minDistance);
        }

        private (long, long) Cell(Point2 p) => ((long)Math.Floor(p.X / _min), (long)Math.Floor(p.Y / _min));

        public bool IsFree(Point2 p)
        {
            (long cx, long cy) = Cell(p);
            for (long dx = -1; dx <= 1; dx++)
            {
                for (long dy = -1; dy <= 1; dy++)
                {
                    if (_cells.TryGetValue((cx + dx, cy + dy), out List<Point2>? bucket) && bucket.Any(q => q.DistanceTo(p) < _min - 1e-9))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        public void Add(Point2 p)
        {
            (long, long) key = Cell(p);
            if (!_cells.TryGetValue(key, out List<Point2>? bucket))
            {
                bucket = new List<Point2>();
                _cells[key] = bucket;
            }

            bucket.Add(p);
        }
    }
}
