using Viridian.Core.Geometry;

namespace Viridian.Core.Model3D;

/// <summary>
/// Triangulação de polígonos com furos por recorte de orelhas. Os anéis são limpos antes (pontos repetidos e
/// colineares) e os furos são ligados ao contorno externo por pontes. Os índices se referem a <see cref="Points"/>.
/// </summary>
public sealed class Triangulation
{
    public List<Point2> Points { get; } = new();

    /// <summary>Anéis limpos (externo anti-horário, furos horários), em índices de <see cref="Points"/>.</summary>
    public List<List<int>> Rings { get; } = new();

    public List<Tri> Triangles { get; } = new();

    public double Area => Triangles.Sum(t => Math.Abs(Cross(Points[t.A], Points[t.B], Points[t.C])) / 2);

    internal static double Cross(Point2 a, Point2 b, Point2 c) => (b - a).Cross(c - a);
}

public static class Triangulator
{
    private const double Eps = 1e-9;

    /// <summary>Remove pontos repetidos (até <paramref name="tolerance"/>) e colineares de um anel fechado.</summary>
    public static List<Point2> CleanRing(IReadOnlyList<Point2> ring, double tolerance = 0.005)
    {
        var points = new List<Point2>();
        foreach (Point2 p in ring)
        {
            if (points.Count == 0 || points[^1].DistanceTo(p) > tolerance)
            {
                points.Add(p);
            }
        }

        while (points.Count > 1 && points[0].DistanceTo(points[^1]) <= tolerance)
        {
            points.RemoveAt(points.Count - 1);
        }

        bool changed = true;
        while (changed && points.Count >= 3)
        {
            changed = false;
            for (int i = 0; i < points.Count && points.Count >= 3; i++)
            {
                Point2 prev = points[(i - 1 + points.Count) % points.Count];
                Point2 next = points[(i + 1) % points.Count];
                double baseLength = prev.DistanceTo(next);
                double height = baseLength < Eps ? 0 : Math.Abs(Triangulation.Cross(prev, points[i], next)) / baseLength;
                if (height <= tolerance * 0.2)
                {
                    points.RemoveAt(i);
                    changed = true;
                    i--;
                }
            }
        }

        return points.Count >= 3 ? points : new List<Point2>();
    }

    public static Triangulation Triangulate(Polygon2 polygon)
    {
        var result = new Triangulation();
        List<List<Point2>> rings = polygon.Rings.Select(r => CleanRing(r)).Where(r => r.Count >= 3).ToList();
        if (rings.Count == 0)
        {
            return result;
        }

        // O maior anel em área é o contorno externo; os demais anéis dentro dele são furos.
        int outerIndex = 0;
        for (int i = 1; i < rings.Count; i++)
        {
            if (Math.Abs(Geo.SignedArea(rings[i])) > Math.Abs(Geo.SignedArea(rings[outerIndex])))
            {
                outerIndex = i;
            }
        }

        List<Point2> outer = rings[outerIndex];
        if (Geo.SignedArea(outer) < 0)
        {
            outer.Reverse();
        }

        var holes = new List<List<Point2>>();
        for (int i = 0; i < rings.Count; i++)
        {
            if (i == outerIndex || !Geo.PointInRing(outer, rings[i][0]))
            {
                continue;
            }

            List<Point2> hole = rings[i];
            if (Geo.SignedArea(hole) > 0)
            {
                hole.Reverse();
            }

            holes.Add(hole);
        }

        result.Rings.Add(AddRing(result, outer));
        foreach (List<Point2> hole in holes)
        {
            result.Rings.Add(AddRing(result, hole));
        }

        List<int> merged = new(result.Rings[0]);
        foreach (List<int> hole in result.Rings.Skip(1).OrderByDescending(h => h.Max(i => result.Points[i].X)))
        {
            merged = Bridge(result.Points, merged, hole);
        }

        EarClip(result.Points, merged, result.Triangles);
        return result;
    }

    private static List<int> AddRing(Triangulation result, List<Point2> ring)
    {
        var indices = new List<int>(ring.Count);
        foreach (Point2 p in ring)
        {
            result.Points.Add(p);
            indices.Add(result.Points.Count - 1);
        }

        return indices;
    }

    /// <summary>Liga o furo ao polígono pelo vértice do furo mais à direita e um vértice visível do contorno.</summary>
    private static List<int> Bridge(List<Point2> pts, List<int> polygon, List<int> hole)
    {
        int holeStart = 0;
        for (int i = 1; i < hole.Count; i++)
        {
            if (pts[hole[i]].X > pts[hole[holeStart]].X)
            {
                holeStart = i;
            }
        }

        Point2 h = pts[hole[holeStart]];
        int best = -1;
        double bestDistance = double.MaxValue;
        for (int i = 0; i < polygon.Count; i++)
        {
            Point2 candidate = pts[polygon[i]];
            double distance = candidate.DistanceTo(h);
            if (distance >= bestDistance || !Visible(pts, polygon, hole, h, candidate, polygon[i]))
            {
                continue;
            }

            best = i;
            bestDistance = distance;
        }

        if (best < 0)
        {
            // Sem vértice visível (geometria degenerada): usa o mais próximo, o que mantém a área correta na maioria dos casos.
            for (int i = 0; i < polygon.Count; i++)
            {
                double distance = pts[polygon[i]].DistanceTo(h);
                if (distance < bestDistance)
                {
                    best = i;
                    bestDistance = distance;
                }
            }
        }

        var merged = new List<int>(polygon.Count + hole.Count + 2);
        for (int i = 0; i <= best; i++)
        {
            merged.Add(polygon[i]);
        }

        for (int k = 0; k <= hole.Count; k++)
        {
            merged.Add(hole[(holeStart + k) % hole.Count]);
        }

        for (int i = best; i < polygon.Count; i++)
        {
            merged.Add(polygon[i]);
        }

        return merged;
    }

    private static bool Visible(List<Point2> pts, List<int> polygon, List<int> hole, Point2 from, Point2 to, int toIndex)
    {
        bool Crosses(List<int> ring)
        {
            for (int i = 0; i < ring.Count; i++)
            {
                int a = ring[i], b = ring[(i + 1) % ring.Count];
                if (a == toIndex || b == toIndex || pts[a] == from || pts[b] == from)
                {
                    continue;
                }

                if (Geo.SegmentsCross(from, to, pts[a], pts[b]))
                {
                    return true;
                }
            }

            return false;
        }

        return !Crosses(polygon) && !Crosses(hole);
    }

    private static void EarClip(List<Point2> pts, List<int> polygon, List<Tri> triangles)
    {
        var remaining = new List<int>(polygon);
        int guard = remaining.Count * remaining.Count + 10;
        bool strict = true;
        while (remaining.Count > 3 && guard-- > 0)
        {
            bool clipped = false;
            for (int i = 0; i < remaining.Count; i++)
            {
                int prev = remaining[(i - 1 + remaining.Count) % remaining.Count];
                int cur = remaining[i];
                int next = remaining[(i + 1) % remaining.Count];
                double cross = Triangulation.Cross(pts[prev], pts[cur], pts[next]);
                if (cross <= Eps)
                {
                    continue;
                }

                if (strict && ContainsAny(pts, remaining, prev, cur, next))
                {
                    continue;
                }

                triangles.Add(new Tri(prev, cur, next));
                remaining.RemoveAt(i);
                clipped = true;
                strict = true;
                break;
            }

            if (!clipped)
            {
                if (!strict)
                {
                    break;
                }

                // Polígono degenerado: relaxa o teste de pontos internos para garantir o término.
                strict = false;
            }
        }

        if (remaining.Count == 3 && Triangulation.Cross(pts[remaining[0]], pts[remaining[1]], pts[remaining[2]]) > Eps)
        {
            triangles.Add(new Tri(remaining[0], remaining[1], remaining[2]));
        }
    }

    private static bool ContainsAny(List<Point2> pts, List<int> ring, int a, int b, int c)
    {
        Point2 pa = pts[a], pb = pts[b], pc = pts[c];
        foreach (int index in ring)
        {
            if (index == a || index == b || index == c)
            {
                continue;
            }

            Point2 p = pts[index];
            if (p == pa || p == pb || p == pc)
            {
                continue;
            }

            if (Triangulation.Cross(pa, pb, p) >= -Eps && Triangulation.Cross(pb, pc, p) >= -Eps && Triangulation.Cross(pc, pa, p) >= -Eps)
            {
                return true;
            }
        }

        return false;
    }
}
