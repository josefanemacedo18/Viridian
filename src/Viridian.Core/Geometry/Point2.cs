namespace Viridian.Core.Geometry;

/// <summary>Ponto ou vetor 2D. Em todo o núcleo as coordenadas estão em metros.</summary>
public readonly record struct Point2(double X, double Y)
{
    public static readonly Point2 Zero = new(0, 0);

    public double Length => Math.Sqrt(X * X + Y * Y);

    public double LengthSquared => X * X + Y * Y;

    /// <summary>Ângulo do vetor em radianos, no intervalo (-π, π].</summary>
    public double Angle => Math.Atan2(Y, X);

    public static Point2 operator +(Point2 a, Point2 b) => new(a.X + b.X, a.Y + b.Y);

    public static Point2 operator -(Point2 a, Point2 b) => new(a.X - b.X, a.Y - b.Y);

    public static Point2 operator -(Point2 a) => new(-a.X, -a.Y);

    public static Point2 operator *(Point2 a, double k) => new(a.X * k, a.Y * k);

    public static Point2 operator *(double k, Point2 a) => new(a.X * k, a.Y * k);

    public static Point2 operator /(Point2 a, double k) => new(a.X / k, a.Y / k);

    public double DistanceTo(Point2 other) => (this - other).Length;

    public double Dot(Point2 other) => X * other.X + Y * other.Y;

    /// <summary>Componente Z do produto vetorial (positivo quando <paramref name="other"/> está à esquerda).</summary>
    public double Cross(Point2 other) => X * other.Y - Y * other.X;

    public Point2 Rotate(double radians)
    {
        double c = Math.Cos(radians);
        double s = Math.Sin(radians);
        return new Point2(X * c - Y * s, X * s + Y * c);
    }

    public Point2 Normalized()
    {
        double length = Length;
        return length < 1e-12 ? Zero : new Point2(X / length, Y / length);
    }

    /// <summary>Vetor perpendicular, girado 90° no sentido anti-horário.</summary>
    public Point2 Perp() => new(-Y, X);

    public static Point2 Polar(double radius, double angle) => new(radius * Math.Cos(angle), radius * Math.Sin(angle));

    public static Point2 Lerp(Point2 a, Point2 b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
}

/// <summary>Retângulo envolvente alinhado aos eixos.</summary>
public readonly record struct Bounds2(double MinX, double MinY, double MaxX, double MaxY)
{
    public double Width => MaxX - MinX;

    public double Height => MaxY - MinY;

    public Point2 Center => new((MinX + MaxX) / 2, (MinY + MaxY) / 2);

    public bool IsValid => MaxX >= MinX && MaxY >= MinY;

    public bool Contains(Point2 p) => p.X >= MinX && p.X <= MaxX && p.Y >= MinY && p.Y <= MaxY;

    public Bounds2 Expand(double margin) => new(MinX - margin, MinY - margin, MaxX + margin, MaxY + margin);

    public static Bounds2 FromPoints(IEnumerable<Point2> points)
    {
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        foreach (Point2 p in points)
        {
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
        }

        return double.IsInfinity(minX) ? new Bounds2(0, 0, 0, 0) : new Bounds2(minX, minY, maxX, maxY);
    }
}
