using Viridian.Core.Geometry;

namespace Viridian.Core.Distribution;

/// <summary>Padrões de distribuição de plantio.</summary>
public enum DistributionPattern
{
    Retangular = 0,
    Triangular = 1,
    Linear = 2,
    Natural = 3,
    Densidade = 4,
    Manual = 5,

    /// <summary>Fileiras que acompanham o contorno da região (bordaduras e cercas vivas de canteiro).</summary>
    Bordadura = 6,

    /// <summary>Anéis concêntricos dentro da região, ou um círculo definido por centro e raio.</summary>
    Circular = 7,

    /// <summary>Manchas naturalistas com quantidades ímpares de plantas (3, 5, 7...).</summary>
    Agrupamentos = 8,

    /// <summary>Distribuição natural com densidade decrescente de um lado para o outro (degradê).</summary>
    Gradiente = 9,
}

/// <summary>Como o plantio linear define a quantidade de exemplares.</summary>
public enum LinearMode
{
    /// <summary>Pelo espaçamento informado.</summary>
    Espacamento = 0,

    /// <summary>Por uma quantidade fixa, dividida igualmente ao longo do eixo.</summary>
    Quantidade = 1,
}

/// <summary>Posição da primeira planta no plantio linear.</summary>
public enum LinearAlignment
{
    /// <summary>Começa no primeiro ponto do eixo.</summary>
    Inicio = 0,

    /// <summary>Distribui a sobra igualmente nas duas extremidades.</summary>
    Centralizado = 1,

    /// <summary>Ajusta o espaçamento para que as duas extremidades recebam plantas.</summary>
    Ajustado = 2,
}

/// <summary>Disposição das fileiras no plantio linear.</summary>
public enum RowArrangement
{
    /// <summary>Plantas alinhadas entre fileiras.</summary>
    Paralelas = 0,

    /// <summary>Fileiras desencontradas meio espaçamento (zigue-zague).</summary>
    Alternadas = 1,
}

/// <summary>Obstáculo circular (por exemplo, um exemplar existente) que os novos pontos devem evitar.</summary>
public readonly record struct Obstacle(Point2 Center, double Radius);

/// <summary>Parâmetros de uma distribuição.</summary>
public sealed class DistributionRequest
{
    public DistributionPattern Pattern { get; set; } = DistributionPattern.Triangular;

    /// <summary>Distância entre plantas, em metros.</summary>
    public double SpacingM { get; set; } = 1.0;

    /// <summary>Distância entre linhas, em metros. Zero = automática conforme o padrão.</summary>
    public double RowSpacingM { get; set; }

    /// <summary>Densidade em unidades por m² (padrão Densidade).</summary>
    public double DensityPerM2 { get; set; }

    /// <summary>Afastamento mínimo das bordas da região, em metros.</summary>
    public double EdgeMarginM { get; set; }

    /// <summary>Variação de posição, em porcentagem do espaçamento (0 a 50).</summary>
    public double JitterPercent { get; set; }

    /// <summary>Rotação da malha, em graus.</summary>
    public double AngleDeg { get; set; }

    /// <summary>Quantidade máxima aceita. Zero = sem limite (vale apenas o limite de segurança).</summary>
    public int MaxCount { get; set; }

    /// <summary>Semente persistente: a mesma semente reproduz exatamente a mesma distribuição.</summary>
    public long Seed { get; set; } = 1;

    /// <summary>Eixo do plantio linear (polilinha aberta).</summary>
    public IReadOnlyList<Point2> Path { get; set; } = Array.Empty<Point2>();

    /// <summary>Deslocamento lateral em relação ao eixo do plantio linear, em metros (positivo = à esquerda).</summary>
    public double PathOffsetM { get; set; }

    /// <summary>O eixo é fechado (o último ponto se liga ao primeiro).</summary>
    public bool PathClosed { get; set; }

    public LinearMode LinearMode { get; set; } = LinearMode.Espacamento;

    public LinearAlignment LinearAlignment { get; set; } = LinearAlignment.Inicio;

    /// <summary>Quantidade de exemplares por fileira no modo Quantidade.</summary>
    public int Count { get; set; }

    /// <summary>Quantidade de fileiras (plantio linear e bordadura).</summary>
    public int Rows { get; set; } = 1;

    public RowArrangement RowArrangement { get; set; } = RowArrangement.Paralelas;

    /// <summary>Centro do plantio circular sem região.</summary>
    public Point2? CircleCenter { get; set; }

    /// <summary>Raio do plantio circular sem região, em metros.</summary>
    public double CircleRadiusM { get; set; }

    /// <summary>Menor quantidade de plantas por mancha (agrupamentos).</summary>
    public int GroupSizeMin { get; set; } = 3;

    /// <summary>Maior quantidade de plantas por mancha (agrupamentos).</summary>
    public int GroupSizeMax { get; set; } = 7;

    /// <summary>Distância livre entre manchas, em metros. Zero = um espaçamento.</summary>
    public double GroupGapM { get; set; }

    /// <summary>Densidade no fim do degradê, em porcentagem da densidade inicial (0 a 100).</summary>
    public double GradientEndPercent { get; set; } = 15;

    public IReadOnlyList<Obstacle> Obstacles { get; set; } = Array.Empty<Obstacle>();
}

/// <summary>Resultado de uma distribuição.</summary>
public sealed class DistributionResult
{
    public IReadOnlyList<Point2> Points { get; init; } = Array.Empty<Point2>();

    /// <summary>Quantidade calculada (mesmo quando a distribuição foi recusada por exceder o máximo).</summary>
    public int ComputedCount { get; init; }

    /// <summary>Verdadeiro quando a quantidade calculada ultrapassa o máximo configurado ou o limite de segurança.</summary>
    public bool Exceeded { get; init; }

    public double EffectiveSpacingM { get; init; }

    public string Message { get; init; } = string.Empty;

    public bool HasPoints => Points.Count > 0;
}

/// <summary>Calcula os pontos de plantio dentro de uma região respeitando limites, afastamentos e espaçamentos.</summary>
public static partial class PlantDistributor
{
    /// <summary>Limite de segurança de exemplares por operação.</summary>
    public const int HardLimit = 20000;

    public const double MinSpacing = 0.02;

    private const long MaxCandidates = 6_000_000;

    public static DistributionResult Distribute(Polygon2? region, DistributionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Pattern == DistributionPattern.Manual)
        {
            return new DistributionResult { Message = "No padrão manual os exemplares são posicionados um a um." };
        }

        if (request.Pattern == DistributionPattern.Linear)
        {
            return DistributeLinear(region, request);
        }

        if (request.Pattern == DistributionPattern.Circular && (region is null || region.IsEmpty) && request.CircleCenter.HasValue)
        {
            return DistributeCircle(request);
        }

        if (region is null || region.IsEmpty || region.Area <= 0)
        {
            return new DistributionResult { Message = "A região selecionada não possui área válida." };
        }

        double spacing = EffectiveSpacing(request);
        if (!(spacing >= MinSpacing))
        {
            return new DistributionResult { Message = $"O espaçamento deve ser de pelo menos {MinSpacing * 100:0} cm." };
        }

        int limit = request.MaxCount > 0 ? Math.Min(request.MaxCount, HardLimit) : HardLimit;
        int computed;
        List<Point2>? points = request.Pattern switch
        {
            DistributionPattern.Natural => Poisson(region, request, spacing, limit, out computed),
            DistributionPattern.Bordadura => Border(region, request, spacing, limit, out computed),
            DistributionPattern.Circular => Rings(region, request, spacing, limit, out computed),
            DistributionPattern.Agrupamentos => Groups(region, request, spacing, limit, out computed),
            DistributionPattern.Gradiente => Gradient(region, request, spacing, limit, out computed),
            _ => Lattice(region, request, spacing, out computed),
        };

        if (points is null || computed > limit)
        {
            string reason = computed > HardLimit || points is null
                ? $"A distribuição resultaria em mais de {HardLimit} exemplares. Aumente o espaçamento ou use hachura."
                : $"A distribuição resultaria em {computed} exemplares, acima do máximo de {limit}.";
            return new DistributionResult
            {
                ComputedCount = computed,
                Exceeded = true,
                EffectiveSpacingM = spacing,
                Message = reason,
            };
        }

        return new DistributionResult
        {
            Points = points,
            ComputedCount = points.Count,
            EffectiveSpacingM = spacing,
            Message = points.Count == 0 ? "Nenhum ponto coube na região com o espaçamento e os afastamentos informados." : string.Empty,
        };
    }

    /// <summary>Espaçamento efetivo: no padrão Densidade deriva da densidade em malha triangular.</summary>
    public static double EffectiveSpacing(DistributionRequest request)
    {
        if (request.Pattern == DistributionPattern.Densidade)
        {
            return request.DensityPerM2 > 0 ? Math.Sqrt(2.0 / (Math.Sqrt(3.0) * request.DensityPerM2)) : 0;
        }

        return request.SpacingM;
    }

    /// <summary>Ângulos de rotação determinísticos, um por exemplar, para quebrar a repetição do desenho.</summary>
    public static IReadOnlyList<double> RandomRotations(int count, long seed)
    {
        var rng = new DeterministicRandom(DeterministicRandom.Combine(seed, 4242));
        var result = new double[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = rng.Range(0, Geo.TwoPi);
        }

        return result;
    }

    // ------------------------------------------------------------------ malhas regulares

    private static List<Point2>? Lattice(Polygon2 region, DistributionRequest request, double spacing, out int computed)
    {
        bool triangular = request.Pattern != DistributionPattern.Retangular;
        double rowSpacing = request.RowSpacingM > 0
            ? request.RowSpacingM
            : triangular ? spacing * Math.Sqrt(3.0) / 2 : spacing;
        rowSpacing = Math.Max(rowSpacing, MinSpacing);

        Bounds2 bounds = region.Bounds;
        Point2 center = bounds.Center;
        double half = Math.Sqrt(bounds.Width * bounds.Width + bounds.Height * bounds.Height) / 2 + spacing;
        int nx = (int)Math.Ceiling(half / spacing) + 1;
        int ny = (int)Math.Ceiling(half / rowSpacing) + 1;

        computed = 0;
        long candidates = (2L * nx + 1) * (2L * ny + 1);
        if (candidates > MaxCandidates)
        {
            computed = EstimateCount(region.Area, spacing, rowSpacing);
            return null;
        }

        double angle = Geo.DegToRad(request.AngleDeg);
        double jitter = Math.Clamp(request.JitterPercent, 0, 50) / 100.0 * spacing;
        var rng = new DeterministicRandom(DeterministicRandom.Combine(request.Seed, 1009));
        var result = new List<Point2>();

        for (int j = -ny; j <= ny; j++)
        {
            double shift = triangular && (j & 1) != 0 ? spacing / 2 : 0;
            for (int i = -nx; i <= nx; i++)
            {
                Point2 p = center + new Point2(i * spacing + shift, j * rowSpacing).Rotate(angle);

                // O sorteio acontece para todo candidato, o que mantém a distribuição estável
                // quando só os limites da região mudam.
                Point2 offset = jitter > 0 ? Point2.Polar(jitter * Math.Sqrt(rng.NextDouble()), rng.Range(0, Geo.TwoPi)) : Point2.Zero;

                if (!IsAllowed(region, request, p))
                {
                    continue;
                }

                Point2 moved = p + offset;
                result.Add(jitter > 0 && IsAllowed(region, request, moved) ? moved : p);
                if (result.Count > HardLimit)
                {
                    computed = Math.Max(result.Count, EstimateCount(region.Area, spacing, rowSpacing));
                    return null;
                }
            }
        }

        computed = result.Count;
        return result;
    }

    private static int EstimateCount(double area, double spacing, double rowSpacing) =>
        (int)Math.Min(int.MaxValue, Math.Ceiling(area / (spacing * rowSpacing)));

    private static bool IsAllowed(Polygon2 region, DistributionRequest request, Point2 p)
    {
        if (!region.ContainsWithMargin(p, request.EdgeMarginM))
        {
            return false;
        }

        foreach (Obstacle obstacle in request.Obstacles)
        {
            if (p.DistanceTo(obstacle.Center) < obstacle.Radius)
            {
                return false;
            }
        }

        return true;
    }

    // ------------------------------------------------------------------ distribuição natural (disco de Poisson)

    private static List<Point2>? Poisson(Polygon2 region, DistributionRequest request, double spacing, int limit, out int computed)
    {
        Bounds2 bounds = region.Bounds;
        double cell = spacing / Math.Sqrt(2.0);
        long gridW = (long)Math.Ceiling(bounds.Width / cell) + 1;
        long gridH = (long)Math.Ceiling(bounds.Height / cell) + 1;
        computed = 0;
        if (gridW * gridH > MaxCandidates)
        {
            computed = (int)Math.Min(int.MaxValue, Math.Ceiling(region.Area * 0.7 / (spacing * spacing)));
            return null;
        }

        int w = (int)gridW;
        int h = (int)gridH;
        var grid = new int[w * h];
        Array.Fill(grid, -1);
        var points = new List<Point2>();
        var active = new List<int>();
        var rng = new DeterministicRandom(DeterministicRandom.Combine(request.Seed, 2003));
        double spacingSquared = spacing * spacing;

        bool Fits(Point2 p)
        {
            if (!IsAllowed(region, request, p))
            {
                return false;
            }

            int gx = (int)((p.X - bounds.MinX) / cell);
            int gy = (int)((p.Y - bounds.MinY) / cell);
            for (int y = Math.Max(0, gy - 2); y <= Math.Min(h - 1, gy + 2); y++)
            {
                for (int x = Math.Max(0, gx - 2); x <= Math.Min(w - 1, gx + 2); x++)
                {
                    int index = grid[y * w + x];
                    if (index >= 0 && (points[index] - p).LengthSquared < spacingSquared)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        void Add(Point2 p)
        {
            int gx = Math.Clamp((int)((p.X - bounds.MinX) / cell), 0, w - 1);
            int gy = Math.Clamp((int)((p.Y - bounds.MinY) / cell), 0, h - 1);
            grid[gy * w + gx] = points.Count;
            active.Add(points.Count);
            points.Add(p);
        }

        bool Grow()
        {
            while (active.Count > 0)
            {
                int pick = rng.NextInt(0, active.Count);
                Point2 origin = points[active[pick]];
                bool placed = false;
                for (int attempt = 0; attempt < 24; attempt++)
                {
                    Point2 candidate = origin + Point2.Polar(spacing * rng.Range(1.0, 2.0), rng.Range(0, Geo.TwoPi));
                    if (Fits(candidate))
                    {
                        Add(candidate);
                        placed = true;
                        if (points.Count > limit)
                        {
                            return false;
                        }

                        break;
                    }
                }

                if (!placed)
                {
                    active[pick] = active[^1];
                    active.RemoveAt(active.Count - 1);
                }
            }

            return true;
        }

        // Sementes em uma malha embaralhada: garante o preenchimento de regiões desconexas e de sobras.
        double step = spacing * 0.9;
        int sx = Math.Max(1, (int)Math.Ceiling(bounds.Width / step));
        int sy = Math.Max(1, (int)Math.Ceiling(bounds.Height / step));
        for (int j = 0; j <= sy; j++)
        {
            for (int i = 0; i <= sx; i++)
            {
                var candidate = new Point2(
                    bounds.MinX + (i + rng.Range(0.05, 0.95)) * step,
                    bounds.MinY + (j + rng.Range(0.05, 0.95)) * step);
                if (!Fits(candidate))
                {
                    continue;
                }

                Add(candidate);
                if (points.Count > limit || !Grow())
                {
                    computed = Math.Max(points.Count, (int)Math.Ceiling(region.Area * 0.7 / spacingSquared));
                    return points.Count > HardLimit ? null : new List<Point2>(0);
                }
            }
        }

        computed = points.Count;
        return points;
    }
}
