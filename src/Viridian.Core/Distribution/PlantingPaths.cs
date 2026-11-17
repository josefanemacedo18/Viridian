using Viridian.Core.Geometry;

namespace Viridian.Core.Distribution;

/// <summary>Eixo de plantio: uma polilinha aberta ou fechada, em metros.</summary>
public sealed record PlantingPath(IReadOnlyList<Point2> Points, bool Closed)
{
    /// <summary>Comprimento do eixo, incluindo o trecho de fechamento quando fechado.</summary>
    public double Length
    {
        get
        {
            double length = Geo.PolylineLength(Points);
            return Closed && Points.Count > 2 ? length + Points[^1].DistanceTo(Points[0]) : length;
        }
    }
}

/// <summary>Montagem de eixos de plantio a partir de linhas desenhadas ou existentes no projeto.</summary>
public static class PathTools
{
    /// <summary>Tolerância padrão para considerar duas extremidades como o mesmo ponto: 1 cm.</summary>
    public const double DefaultTolerance = 0.01;

    /// <summary>
    /// Encadeia trechos soltos (por exemplo, linhas e arcos selecionados no projeto, já subdivididos)
    /// em eixos contínuos. Trechos que se tocam pelas extremidades formam um único eixo, invertendo o
    /// sentido quando necessário; um eixo que volta ao ponto inicial é marcado como fechado.
    /// Trechos que não se tocam geram eixos separados, na ordem em que foram informados.
    /// </summary>
    public static IReadOnlyList<PlantingPath> Chain(IEnumerable<IReadOnlyList<Point2>> pieces, double tolerance = DefaultTolerance)
    {
        tolerance = Math.Max(1e-6, tolerance);
        List<List<Point2>> open = pieces
            .Select(Clean)
            .Where(p => p.Count >= 2)
            .ToList();

        var result = new List<PlantingPath>();
        while (open.Count > 0)
        {
            List<Point2> chain = open[0];
            open.RemoveAt(0);

            bool grown = true;
            while (grown && !IsClosed(chain, tolerance))
            {
                grown = false;
                for (int i = 0; i < open.Count; i++)
                {
                    List<Point2> piece = open[i];
                    if (chain[^1].DistanceTo(piece[0]) <= tolerance)
                    {
                        chain.AddRange(piece.Skip(1));
                    }
                    else if (chain[^1].DistanceTo(piece[^1]) <= tolerance)
                    {
                        piece.Reverse();
                        chain.AddRange(piece.Skip(1));
                    }
                    else if (chain[0].DistanceTo(piece[^1]) <= tolerance)
                    {
                        chain.InsertRange(0, piece.Take(piece.Count - 1));
                    }
                    else if (chain[0].DistanceTo(piece[0]) <= tolerance)
                    {
                        piece.Reverse();
                        chain.InsertRange(0, piece.Take(piece.Count - 1));
                    }
                    else
                    {
                        continue;
                    }

                    open.RemoveAt(i);
                    grown = true;
                    break;
                }
            }

            bool closed = IsClosed(chain, tolerance);
            if (closed)
            {
                chain.RemoveAt(chain.Count - 1);
            }

            result.Add(new PlantingPath(chain, closed));
        }

        return result;
    }

    /// <summary>Círculo de centro e raio dados, como polígono fechado com cordas de no máximo 5 cm de desvio.</summary>
    public static PlantingPath Circle(Point2 center, double radius)
    {
        if (!(radius > 0) || !double.IsFinite(radius))
        {
            return new PlantingPath(Array.Empty<Point2>(), true);
        }

        // Quantidade de lados para que a flecha (desvio entre corda e arco) fique abaixo de 5 mm.
        double sagitta = Math.Min(0.005, radius * 0.01);
        double step = 2 * Math.Acos(Math.Clamp(1 - sagitta / radius, -1, 1));
        int sides = Math.Clamp((int)Math.Ceiling(Geo.TwoPi / Math.Max(step, 1e-6)), 24, 720);
        var points = new List<Point2>(sides);
        for (int i = 0; i < sides; i++)
        {
            double angle = Geo.TwoPi * i / sides;
            points.Add(new Point2(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle)));
        }

        return new PlantingPath(points, true);
    }

    private static bool IsClosed(List<Point2> chain, double tolerance) =>
        chain.Count >= 4 && chain[0].DistanceTo(chain[^1]) <= tolerance;

    private static List<Point2> Clean(IReadOnlyList<Point2> piece)
    {
        var clean = new List<Point2>();
        foreach (Point2 p in piece ?? Array.Empty<Point2>())
        {
            if (double.IsFinite(p.X) && double.IsFinite(p.Y) && (clean.Count == 0 || clean[^1].DistanceTo(p) > 1e-6))
            {
                clean.Add(p);
            }
        }

        return clean;
    }
}

public static partial class PlantDistributor
{
    /// <summary>
    /// Plantio linear ao longo de um ou mais eixos (por exemplo, várias linhas selecionadas no projeto).
    /// Cada eixo é distribuído com as mesmas opções; plantas de eixos diferentes que ficariam mais próximas
    /// que meio espaçamento (em cruzamentos e emendas) são descartadas.
    /// </summary>
    public static DistributionResult DistributeAlongPaths(Polygon2? region, IReadOnlyList<PlantingPath> paths, DistributionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        List<PlantingPath> usable = (paths ?? Array.Empty<PlantingPath>()).Where(p => p.Points.Count >= 2).ToList();
        if (usable.Count == 0)
        {
            return new DistributionResult { Message = "Defina o eixo do plantio linear com pelo menos dois pontos." };
        }

        int limit = request.MaxCount > 0 ? Math.Min(request.MaxCount, HardLimit) : HardLimit;
        var all = new List<Point2>();
        var messages = new List<string>();
        double step = 0;
        SpacingGrid? grid = null;
        foreach (PlantingPath path in usable)
        {
            DistributionRequest single = Copy(request);
            single.Pattern = DistributionPattern.Linear;
            single.Path = path.Points;
            single.PathClosed = path.Closed || (request.PathClosed && usable.Count == 1);
            single.MaxCount = 0;
            DistributionResult partial = DistributeLinear(region, single);
            if (partial.Exceeded)
            {
                return new DistributionResult
                {
                    ComputedCount = partial.ComputedCount,
                    Exceeded = true,
                    EffectiveSpacingM = partial.EffectiveSpacingM,
                    Message = partial.Message,
                };
            }

            if (partial.Points.Count == 0)
            {
                if (partial.Message.Length > 0)
                {
                    messages.Add(partial.Message);
                }

                continue;
            }

            step = step == 0 ? partial.EffectiveSpacingM : Math.Min(step, partial.EffectiveSpacingM);
            grid ??= new SpacingGrid(Math.Max(MinSpacing, partial.EffectiveSpacingM * 0.5));
            foreach (Point2 p in partial.Points)
            {
                if (usable.Count == 1 || grid.IsFree(p))
                {
                    grid.Add(p);
                    all.Add(p);
                }
            }
        }

        if (all.Count > limit)
        {
            return new DistributionResult
            {
                ComputedCount = all.Count,
                Exceeded = true,
                EffectiveSpacingM = step,
                Message = $"O plantio resultaria em {all.Count} exemplares, acima do máximo de {limit}.",
            };
        }

        return new DistributionResult
        {
            Points = all,
            ComputedCount = all.Count,
            EffectiveSpacingM = step,
            Message = all.Count == 0 ? (messages.FirstOrDefault() ?? "Nenhum ponto coube nos eixos informados.") : string.Empty,
        };
    }

    private static DistributionRequest Copy(DistributionRequest r) => new()
    {
        Pattern = r.Pattern,
        SpacingM = r.SpacingM,
        RowSpacingM = r.RowSpacingM,
        DensityPerM2 = r.DensityPerM2,
        EdgeMarginM = r.EdgeMarginM,
        JitterPercent = r.JitterPercent,
        AngleDeg = r.AngleDeg,
        MaxCount = r.MaxCount,
        Seed = r.Seed,
        Path = r.Path,
        PathOffsetM = r.PathOffsetM,
        PathClosed = r.PathClosed,
        LinearMode = r.LinearMode,
        LinearAlignment = r.LinearAlignment,
        Count = r.Count,
        Rows = r.Rows,
        RowArrangement = r.RowArrangement,
        CircleCenter = r.CircleCenter,
        CircleRadiusM = r.CircleRadiusM,
        GroupSizeMin = r.GroupSizeMin,
        GroupSizeMax = r.GroupSizeMax,
        GroupGapM = r.GroupGapM,
        GradientEndPercent = r.GradientEndPercent,
        Obstacles = r.Obstacles,
    };
}
