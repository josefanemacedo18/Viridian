using Viridian.Core.Geometry;

namespace Viridian.Core.Symbols;

/// <summary>
/// Acumula as primitivas de um símbolo garantindo geometria válida para o Revit:
/// nada de segmentos menores que o comprimento mínimo e nada de arcos quase retos.
/// </summary>
internal sealed class SymbolBuilder
{
    private readonly List<SymbolPrimitive> _items = new();
    private readonly double _minLength;

    public SymbolBuilder(double minLength)
    {
        _minLength = minLength;
    }

    public IReadOnlyList<SymbolPrimitive> Items => _items;

    public void Line(SymbolTier tier, Point2 a, Point2 b)
    {
        if (!IsFinite(a) || !IsFinite(b) || a.DistanceTo(b) < _minLength)
        {
            return;
        }

        _items.Add(new SymbolLine(tier, a, b));
    }

    public void Polyline(SymbolTier tier, IReadOnlyList<Point2> points, bool closed = false)
    {
        if (points.Count < 2)
        {
            return;
        }

        // Funde vértices próximos demais para que cada trecho tenha comprimento válido.
        var merged = new List<Point2> { points[0] };
        for (int i = 1; i < points.Count; i++)
        {
            if (merged[^1].DistanceTo(points[i]) >= _minLength)
            {
                merged.Add(points[i]);
            }
        }

        if (closed && merged.Count > 2 && merged[0].DistanceTo(merged[^1]) < _minLength)
        {
            merged.RemoveAt(merged.Count - 1);
        }

        for (int i = 1; i < merged.Count; i++)
        {
            Line(tier, merged[i - 1], merged[i]);
        }

        if (closed && merged.Count > 2)
        {
            Line(tier, merged[^1], merged[0]);
        }
    }

    /// <summary>Arco por três pontos. Quando a curvatura é desprezível, vira um segmento de reta.</summary>
    public void Arc(SymbolTier tier, Point2 start, Point2 mid, Point2 end)
    {
        if (!IsFinite(start) || !IsFinite(mid) || !IsFinite(end))
        {
            return;
        }

        double chord = start.DistanceTo(end);
        if (chord < _minLength)
        {
            return;
        }

        double sagitta = Math.Abs((end - start).Cross(mid - start)) / chord;
        if (sagitta < chord * 0.02 || sagitta < _minLength * 0.25)
        {
            Line(tier, start, end);
            return;
        }

        if (start.DistanceTo(mid) < _minLength * 0.5 || mid.DistanceTo(end) < _minLength * 0.5)
        {
            Line(tier, start, end);
            return;
        }

        _items.Add(new SymbolArc(tier, start, mid, end));
    }

    /// <summary>Arco de círculo entre dois ângulos (de a0 até a1). Varreduras grandes são divididas.</summary>
    public void ArcByAngles(SymbolTier tier, Point2 center, double radius, double a0, double a1)
    {
        double sweep = a1 - a0;
        if (radius <= 0 || Math.Abs(sweep) < 1e-9)
        {
            return;
        }

        int parts = Math.Max(1, (int)Math.Ceiling(Math.Abs(sweep) / (Math.PI * 1.0001)));
        for (int i = 0; i < parts; i++)
        {
            double s = a0 + sweep * i / parts;
            double e = a0 + sweep * (i + 1) / parts;
            double m = (s + e) / 2;
            Arc(tier, center + Point2.Polar(radius, s), center + Point2.Polar(radius, m), center + Point2.Polar(radius, e));
        }
    }

    public void Circle(SymbolTier tier, Point2 center, double radius) =>
        ArcByAngles(tier, center, radius, 0, Geo.TwoPi);

    private static bool IsFinite(Point2 p) => double.IsFinite(p.X) && double.IsFinite(p.Y);
}

/// <summary>Curva de três pontos avaliável (arco, ou reta quando os pontos são colineares).</summary>
internal readonly struct Curve3
{
    private readonly Point2 _start;
    private readonly Point2 _end;
    private readonly ArcInfo _arc;
    private readonly bool _isArc;

    public Curve3(Point2 start, Point2 mid, Point2 end)
    {
        _start = start;
        _end = end;
        _isArc = ArcInfo.TryFromThreePoints(start, mid, end, out _arc);
    }

    public Point2 PointAt(double t) => _isArc ? _arc.PointAt(t) : Point2.Lerp(_start, _end, t);

    /// <summary>Ângulo da tangente no parâmetro t, no sentido do percurso.</summary>
    public double TangentAngleAt(double t)
    {
        if (!_isArc)
        {
            return (_end - _start).Angle;
        }

        double radial = _arc.StartAngle + _arc.Sweep * t;
        return radial + (_arc.Sweep > 0 ? Math.PI / 2 : -Math.PI / 2);
    }
}
