using Viridian.Core.Geometry;

namespace Viridian.Core.Model3D;

/// <summary>
/// Volume de um maciço baixo (forração, herbáceas, arbustos, gramado): o contorno é extrudado até a altura das
/// plantas, acompanhando o terreno em cada vértice do contorno.
/// </summary>
public static class BedModels
{
    /// <summary>Quanto a base fica enterrada, para não aparecer fresta sobre o terreno.</summary>
    public const double Embed = 0.05;

    /// <param name="ground">Cota do terreno (m) em cada ponto da planta.</param>
    public static MeshPiece Build(Polygon2 boundary, Func<Point2, double> ground, double heightM)
    {
        ArgumentNullException.ThrowIfNull(boundary);
        ArgumentNullException.ThrowIfNull(ground);
        var mesh = new MeshPiece(MeshPart.Folhagem);
        Triangulation tri = Triangulator.Triangulate(boundary);
        if (tri.Triangles.Count == 0)
        {
            return mesh;
        }

        double height = Math.Clamp(double.IsFinite(heightM) ? heightM : 0.1, 0.03, 6);
        int n = tri.Points.Count;
        var groundZ = new double[n];
        for (int i = 0; i < n; i++)
        {
            double z = ground(tri.Points[i]);
            groundZ[i] = double.IsFinite(z) ? z : 0;
        }

        for (int i = 0; i < n; i++)
        {
            mesh.Add(new Vec3(tri.Points[i].X, tri.Points[i].Y, groundZ[i] + height));
        }

        for (int i = 0; i < n; i++)
        {
            mesh.Add(new Vec3(tri.Points[i].X, tri.Points[i].Y, groundZ[i] - Embed));
        }

        foreach (Tri t in tri.Triangles)
        {
            mesh.Triangle(t.A, t.B, t.C);
            mesh.Triangle(n + t.A, n + t.C, n + t.B);
        }

        foreach (List<int> ring in tri.Rings)
        {
            for (int k = 0; k < ring.Count; k++)
            {
                int a = ring[k], b = ring[(k + 1) % ring.Count];
                mesh.Quad(n + a, n + b, b, a);
            }
        }

        return mesh;
    }

    /// <summary>Pontos sobre o topo do maciço para as manchas de flor (no máximo <paramref name="max"/>).</summary>
    public static IReadOnlyList<Vec3> FlowerPoints(MeshPiece bed, Polygon2 boundary, double spacing, int max, long seed)
    {
        var result = new List<Vec3>();
        if (bed.Triangles.Count == 0 || boundary.IsEmpty || max <= 0)
        {
            return result;
        }

        double area = boundary.Area;
        spacing = Math.Max(spacing, Math.Sqrt(area / max));
        var random = new DeterministicRandom(seed);
        Bounds2 b = boundary.Bounds;
        int attempts = max * 6;
        double topZ(Point2 p)
        {
            foreach (Tri t in bed.Triangles)
            {
                (Vec3 a, Vec3 v, Vec3 c) = bed.Corners(t);
                double d = (v.Y - c.Y) * (a.X - c.X) + (c.X - v.X) * (a.Y - c.Y);
                if (Math.Abs(d) < 1e-12)
                {
                    continue;
                }

                double l1 = ((v.Y - c.Y) * (p.X - c.X) + (c.X - v.X) * (p.Y - c.Y)) / d;
                double l2 = ((c.Y - a.Y) * (p.X - c.X) + (a.X - c.X) * (p.Y - c.Y)) / d;
                double l3 = 1 - l1 - l2;
                Vec3 normal = (v - a).Cross(c - a);
                if (normal.Z > 0 && l1 >= -1e-9 && l2 >= -1e-9 && l3 >= -1e-9)
                {
                    return l1 * a.Z + l2 * v.Z + l3 * c.Z;
                }
            }

            return double.NaN;
        }

        for (int i = 0; i < attempts && result.Count < max; i++)
        {
            var p = new Point2(random.Range(b.MinX, b.MaxX), random.Range(b.MinY, b.MaxY));
            if (!boundary.ContainsWithMargin(p, spacing * 0.3) || result.Any(q => new Point2(q.X, q.Y).DistanceTo(p) < spacing))
            {
                continue;
            }

            double z = topZ(p);
            if (double.IsFinite(z))
            {
                result.Add(new Vec3(p.X, p.Y, z));
            }
        }

        return result;
    }
}
