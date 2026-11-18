using Viridian.Core.Geometry;

namespace Viridian.Core.Paving;

/// <summary>Lado da faixa de borda em relação ao eixo.</summary>
public enum StripSide
{
    /// <summary>Centralizada no eixo.</summary>
    Centro = 0,

    /// <summary>Para fora do contorno fechado; à esquerda de uma linha aberta (no sentido em que foi desenhada).</summary>
    Fora = 1,

    /// <summary>Para dentro do contorno fechado; à direita de uma linha aberta.</summary>
    Dentro = 2,
}

/// <summary>
/// Faixa de largura constante ao longo de um eixo (borda de caminho ou de canteiro): o eixo é deslocado com cantos em
/// esquadro (chanfrados quando muito agudos) e o contorno da faixa é devolvido em anéis, em metros.
/// </summary>
public static class BorderStrip
{
    /// <summary>
    /// Anéis da faixa. Eixo aberto: um anel. Eixo fechado: dois anéis (contorno externo e furo).
    /// <paramref name="isInside"/> indica, para eixos fechados, de que lado fica o elemento de origem (o "dentro").
    /// </summary>
    public static List<IReadOnlyList<Point2>> Build(IReadOnlyList<Point2> axis, bool closed, double width, StripSide side, Func<Point2, bool>? isInside = null)
    {
        List<Point2> points = Clean(axis, closed);
        var rings = new List<IReadOnlyList<Point2>>();
        if (points.Count < (closed ? 3 : 2) || !(width > 0) || !double.IsFinite(width))
        {
            return rings;
        }

        // Sinal do lado "fora": esquerda (+) por padrão; em contornos fechados, o lado oposto ao "dentro".
        double outSign = 1;
        if (closed)
        {
            Point2 a = points[0], b = points[1];
            Point2 left = (a + b) / 2 + (b - a).Normalized().Perp() * Math.Min(0.05, width / 4);
            bool leftIsInside = isInside?.Invoke(left) ?? SignedArea(points) > 0;
            outSign = leftIsInside ? -1 : 1;
        }

        (double d1, double d2) = side switch
        {
            StripSide.Fora => (0, width),
            StripSide.Dentro => (-width, 0),
            _ => (-width / 2, width / 2),
        };
        d1 *= outSign;
        d2 *= outSign;

        List<Point2> o1 = Offset(points, closed, d1);
        List<Point2> o2 = Offset(points, closed, d2);
        if (closed)
        {
            rings.Add(o1);
            rings.Add(o2);
        }
        else
        {
            var ring = new List<Point2>(o1);
            o2.Reverse();
            ring.AddRange(o2);
            rings.Add(ring);
        }

        return rings;
    }

    /// <summary>Polilinha deslocada de <paramref name="distance"/> para a esquerda (negativo: direita), com esquadro limitado.</summary>
    public static List<Point2> Offset(IReadOnlyList<Point2> points, bool closed, double distance)
    {
        var result = new List<Point2>();
        if (Math.Abs(distance) < 1e-12)
        {
            result.AddRange(points);
            return result;
        }

        int n = points.Count;
        for (int i = 0; i < n; i++)
        {
            bool hasPrev = closed || i > 0;
            bool hasNext = closed || i < n - 1;
            Point2 p = points[i];
            Point2? nPrev = hasPrev ? Normal(points[(i - 1 + n) % n], p) : null;
            Point2? nNext = hasNext ? Normal(p, points[(i + 1) % n]) : null;
            if (nPrev is null)
            {
                result.Add(p + nNext!.Value * distance);
                continue;
            }

            if (nNext is null)
            {
                result.Add(p + nPrev.Value * distance);
                continue;
            }

            Point2 bisector = nPrev.Value + nNext.Value;
            double cos = bisector.Length / 2; // cosseno da metade do ângulo entre as normais
            if (bisector.Length < 1e-9 || cos < 0.35)
            {
                // Canto muito agudo (ou retorno): chanfro com dois pontos.
                result.Add(p + nPrev.Value * distance);
                result.Add(p + nNext.Value * distance);
                continue;
            }

            result.Add(p + bisector.Normalized() * (distance / cos));
        }

        return result;
    }

    private static Point2 Normal(Point2 a, Point2 b) => (b - a).Normalized().Perp();

    private static double SignedArea(IReadOnlyList<Point2> ring)
    {
        double sum = 0;
        for (int i = 0; i < ring.Count; i++)
        {
            Point2 a = ring[i], b = ring[(i + 1) % ring.Count];
            sum += a.Cross(b);
        }

        return sum / 2;
    }

    private static List<Point2> Clean(IReadOnlyList<Point2> axis, bool closed)
    {
        var points = new List<Point2>();
        foreach (Point2 p in axis)
        {
            if (points.Count == 0 || points[^1].DistanceTo(p) > 1e-4)
            {
                points.Add(p);
            }
        }

        if (closed)
        {
            while (points.Count > 2 && points[^1].DistanceTo(points[0]) <= 1e-4)
            {
                points.RemoveAt(points.Count - 1);
            }
        }

        // Pontos alinhados não mudam a faixa: são removidos para o contorno ficar limpo.
        for (int i = points.Count - 2; i >= 1; i--)
        {
            Point2 a = points[i - 1], b = points[i], c = points[i + 1];
            if (Math.Abs((b - a).Normalized().Cross((c - b).Normalized())) < 1e-6 && (b - a).Dot(c - b) > 0)
            {
                points.RemoveAt(i);
            }
        }

        return points;
    }
}
