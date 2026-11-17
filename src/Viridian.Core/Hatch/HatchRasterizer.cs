using Viridian.Core.Geometry;

namespace Viridian.Core.Hatch;

/// <summary>
/// Converte a definição de uma hachura em segmentos de reta dentro de um retângulo.
/// Usado nas pré-visualizações; segue a mesma semântica do padrão de preenchimento do Revit
/// (origem, ângulo, deslocamento ao longo da linha e distância entre linhas).
/// </summary>
public static class HatchRasterizer
{
    /// <summary>Segmentos da hachura dentro da área, nas mesmas unidades da definição (milímetros).</summary>
    public static IReadOnlyList<(Point2 A, Point2 B)> Segments(HatchPattern pattern, Bounds2 area, int maxSegments = 20000)
    {
        var result = new List<(Point2, Point2)>();
        if (!area.IsValid || area.Width <= 0 || area.Height <= 0)
        {
            return result;
        }

        var corners = new[]
        {
            new Point2(area.MinX, area.MinY), new Point2(area.MaxX, area.MinY),
            new Point2(area.MaxX, area.MaxY), new Point2(area.MinX, area.MaxY),
        };

        foreach (HatchGrid grid in pattern.Grids)
        {
            if (Math.Abs(grid.Offset) < 1e-9)
            {
                continue;
            }

            double angle = Geo.DegToRad(grid.AngleDeg);
            var u = new Point2(Math.Cos(angle), Math.Sin(angle));
            Point2 n = u.Perp();
            var origin = new Point2(grid.OriginX, grid.OriginY);

            double min = double.PositiveInfinity, max = double.NegativeInfinity;
            foreach (Point2 corner in corners)
            {
                double k = (corner - origin).Dot(n) / grid.Offset;
                min = Math.Min(min, k);
                max = Math.Max(max, k);
            }

            double period = grid.Segments.Sum();
            for (long k = (long)Math.Floor(min); k <= (long)Math.Ceiling(max); k++)
            {
                Point2 start = origin + n * (grid.Offset * k) + u * (grid.Shift * k);
                if (!ClipLine(start, u, area, out double t0, out double t1))
                {
                    continue;
                }

                if (grid.Segments.Count == 0 || period <= 1e-9)
                {
                    result.Add((start + u * t0, start + u * t1));
                }
                else
                {
                    for (long m = (long)Math.Floor(t0 / period); m <= (long)Math.Ceiling(t1 / period); m++)
                    {
                        double position = m * period;
                        for (int i = 0; i < grid.Segments.Count; i++)
                        {
                            double length = grid.Segments[i];
                            if (i % 2 == 0)
                            {
                                double a = Math.Max(position, t0);
                                double b = Math.Min(position + length, t1);
                                if (b > a)
                                {
                                    result.Add((start + u * a, start + u * b));
                                }
                            }

                            position += length;
                        }
                    }
                }

                if (result.Count >= maxSegments)
                {
                    return result;
                }
            }
        }

        return result;
    }

    /// <summary>Intervalo do parâmetro t em que a reta start + t·direction cruza o retângulo.</summary>
    private static bool ClipLine(Point2 start, Point2 direction, Bounds2 area, out double t0, out double t1)
    {
        t0 = double.NegativeInfinity;
        t1 = double.PositiveInfinity;
        return ClipAxis(start.X, direction.X, area.MinX, area.MaxX, ref t0, ref t1)
            && ClipAxis(start.Y, direction.Y, area.MinY, area.MaxY, ref t0, ref t1)
            && t1 > t0;
    }

    private static bool ClipAxis(double origin, double direction, double min, double max, ref double t0, ref double t1)
    {
        if (Math.Abs(direction) < 1e-12)
        {
            return origin >= min && origin <= max;
        }

        double a = (min - origin) / direction;
        double b = (max - origin) / direction;
        if (a > b)
        {
            (a, b) = (b, a);
        }

        t0 = Math.Max(t0, a);
        t1 = Math.Min(t1, b);
        return t1 > t0;
    }
}
