namespace Viridian.Core.Geometry;

/// <summary>Funções geométricas elementares em 2D.</summary>
public static class Geo
{
    public const double TwoPi = Math.PI * 2;

    public static double DegToRad(double degrees) => degrees * Math.PI / 180.0;

    public static double RadToDeg(double radians) => radians * 180.0 / Math.PI;

    /// <summary>Área com sinal de um anel (positiva quando anti-horário).</summary>
    public static double SignedArea(IReadOnlyList<Point2> ring)
    {
        double sum = 0;
        int n = ring.Count;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            sum += ring[j].X * ring[i].Y - ring[i].X * ring[j].Y;
        }

        return sum / 2;
    }

    /// <summary>Teste de ponto em anel pelo método do raio (regra par-ímpar).</summary>
    public static bool PointInRing(IReadOnlyList<Point2> ring, Point2 p)
    {
        bool inside = false;
        int n = ring.Count;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            Point2 a = ring[i];
            Point2 b = ring[j];
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    /// <summary>Distância de um ponto a um segmento.</summary>
    public static double DistancePointSegment(Point2 p, Point2 a, Point2 b)
    {
        Point2 ab = b - a;
        double lengthSquared = ab.LengthSquared;
        if (lengthSquared < 1e-18)
        {
            return p.DistanceTo(a);
        }

        double t = Math.Clamp((p - a).Dot(ab) / lengthSquared, 0, 1);
        return p.DistanceTo(a + ab * t);
    }

    /// <summary>Verifica se dois segmentos se cruzam em um ponto interior a ambos.</summary>
    public static bool SegmentsCross(Point2 a1, Point2 a2, Point2 b1, Point2 b2)
    {
        double d1 = (a2 - a1).Cross(b1 - a1);
        double d2 = (a2 - a1).Cross(b2 - a1);
        double d3 = (b2 - b1).Cross(a1 - b1);
        double d4 = (b2 - b1).Cross(a2 - b1);
        const double eps = 1e-12;
        return ((d1 > eps && d2 < -eps) || (d1 < -eps && d2 > eps))
            && ((d3 > eps && d4 < -eps) || (d3 < -eps && d4 > eps));
    }

    /// <summary>Comprimento total de uma polilinha aberta.</summary>
    public static double PolylineLength(IReadOnlyList<Point2> points)
    {
        double total = 0;
        for (int i = 1; i < points.Count; i++)
        {
            total += points[i - 1].DistanceTo(points[i]);
        }

        return total;
    }

    /// <summary>Normaliza um ângulo para o intervalo [0, 2π).</summary>
    public static double NormalizeAngle(double radians)
    {
        double a = radians % TwoPi;
        return a < 0 ? a + TwoPi : a;
    }
}

/// <summary>Arco circular definido por centro, raio, ângulo inicial e varredura com sinal.</summary>
public readonly record struct ArcInfo(Point2 Center, double Radius, double StartAngle, double Sweep)
{
    public bool IsCounterClockwise => Sweep > 0;

    /// <summary>Ponto do arco no parâmetro t (0 = início, 1 = fim).</summary>
    public Point2 PointAt(double t) => Center + Point2.Polar(Radius, StartAngle + Sweep * t);

    public double Length => Math.Abs(Sweep) * Radius;

    /// <summary>
    /// Calcula o arco que passa por três pontos. Retorna false quando os pontos são (quase) colineares.
    /// </summary>
    public static bool TryFromThreePoints(Point2 start, Point2 mid, Point2 end, out ArcInfo arc)
    {
        arc = default;
        double ax = start.X, ay = start.Y, bx = mid.X, by = mid.Y, cx = end.X, cy = end.Y;
        double d = 2 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));
        double scale = Math.Max(start.DistanceTo(end), start.DistanceTo(mid));
        if (Math.Abs(d) < 1e-12 * Math.Max(1.0, scale * scale))
        {
            return false;
        }

        double a2 = ax * ax + ay * ay;
        double b2 = bx * bx + by * by;
        double c2 = cx * cx + cy * cy;
        double ux = (a2 * (by - cy) + b2 * (cy - ay) + c2 * (ay - by)) / d;
        double uy = (a2 * (cx - bx) + b2 * (ax - cx) + c2 * (bx - ax)) / d;
        var center = new Point2(ux, uy);
        double radius = center.DistanceTo(start);
        double a0 = (start - center).Angle;
        double a1 = (mid - center).Angle;
        double a2Angle = (end - center).Angle;

        // Varredura no sentido anti-horário de start até end, passando (ou não) por mid.
        double ccwToEnd = Geo.NormalizeAngle(a2Angle - a0);
        double ccwToMid = Geo.NormalizeAngle(a1 - a0);
        double sweep = ccwToMid <= ccwToEnd ? ccwToEnd : ccwToEnd - Geo.TwoPi;
        if (Math.Abs(sweep) < 1e-9)
        {
            return false;
        }

        arc = new ArcInfo(center, radius, a0, sweep);
        return true;
    }
}
