using Viridian.Core.Geometry;

namespace Viridian.Core.Model3D.Generation;

/// <summary>
/// Primitivas botânicas: galhos em tubo, fitas e placas de folha, grupos de folhagem orgânicos e curvas suaves. Peças
/// fechadas (sólidos) e abertas (superfícies de folha, dupla face) são marcadas, porque o Revit trata cada uma de um jeito.
/// </summary>
public static class MeshBuild
{
    /// <summary>Menor largura ou raio usado nas malhas (m): evita arestas que o Revit recusa.</summary>
    public const double MinFeature = 0.004;

    /// <summary>Curva de Bézier cúbica amostrada em <paramref name="segments"/> trechos.</summary>
    public static List<Vec3> Bezier(Vec3 p0, Vec3 p1, Vec3 p2, Vec3 p3, int segments)
    {
        segments = Math.Max(1, segments);
        var points = new List<Vec3>(segments + 1);
        for (int i = 0; i <= segments; i++)
        {
            double t = (double)i / segments, u = 1 - t;
            points.Add(p0 * (u * u * u) + p1 * (3 * u * u * t) + p2 * (3 * u * t * t) + p3 * (t * t * t));
        }

        return points;
    }

    /// <summary>
    /// Tubo fechado ao longo de um caminho (galho, tronco, haste), com raio variável e seção de <paramref name="sides"/>
    /// lados. Os referenciais seguem o caminho sem torção (transporte paralelo).
    /// </summary>
    public static MeshPiece Tube(MeshPart part, IReadOnlyList<Vec3> path, IReadOnlyList<double> radii, int sides)
    {
        var mesh = new MeshPiece(part);
        int n = path.Count;
        if (n < 2 || radii.Count != n)
        {
            return mesh;
        }

        sides = Math.Clamp(sides, 3, 16);
        List<(Vec3 T, Vec3 U, Vec3 V)> frames = Frames(path);
        var start = new int[n];
        for (int i = 0; i < n; i++)
        {
            (Vec3 _, Vec3 u, Vec3 v) = frames[i];
            double r = Math.Max(MinFeature, radii[i]);
            start[i] = mesh.Vertices.Count;
            for (int s = 0; s < sides; s++)
            {
                double a = Geo.TwoPi * s / sides;
                mesh.Add(path[i] + u * (Math.Cos(a) * r) + v * (Math.Sin(a) * r));
            }
        }

        int V(int ring, int s) => start[ring] + ((s % sides) + sides) % sides;
        for (int i = 0; i < n - 1; i++)
        {
            for (int s = 0; s < sides; s++)
            {
                mesh.Quad(V(i, s), V(i, s + 1), V(i + 1, s + 1), V(i + 1, s)); // normal para fora
            }
        }

        int c0 = mesh.Add(path[0]);
        int c1 = mesh.Add(path[n - 1]);
        for (int s = 0; s < sides; s++)
        {
            mesh.Triangle(c0, V(0, s + 1), V(0, s));
            mesh.Triangle(c1, V(n - 1, s), V(n - 1, s + 1));
        }

        return mesh;
    }

    /// <summary>
    /// Fita de folha ao longo de um caminho: superfície com largura variável, dobrada levemente na nervura central
    /// (<paramref name="fold"/>, fração da largura) e orientada pela direção <paramref name="up"/>. Aberta e de face única
    /// (o Revit mostra os dois lados de superfícies abertas); <paramref name="solid"/> gera uma lâmina fechada fina no lugar.
    /// </summary>
    public static MeshPiece Ribbon(MeshPart part, IReadOnlyList<Vec3> path, IReadOnlyList<double> widths, Vec3 up, double fold, bool solid, double thickness = 0.004)
    {
        int n = path.Count;
        if (n < 2 || widths.Count != n)
        {
            return new MeshPiece(part, closed: solid);
        }

        if (solid)
        {
            return Primitives.Blade(part, path, widths.Select(w => Math.Max(MinFeature * 2, w)).ToList(), Math.Max(MinFeature, thickness));
        }

        var mesh = new MeshPiece(part, closed: false);
        var left = new int[n];
        var mid = new int[n];
        var right = new int[n];
        for (int i = 0; i < n; i++)
        {
            Vec3 tangent = (path[Math.Min(n - 1, i + 1)] - path[Math.Max(0, i - 1)]).Normalized();
            Vec3 side = tangent.Cross(up).Normalized();
            if (side.Length < 0.5)
            {
                side = tangent.Cross(new Vec3(0, 1, 0)).Normalized();
            }

            Vec3 normal = side.Cross(tangent).Normalized();
            double w = Math.Max(MinFeature, widths[i]) / 2;
            Vec3 lift = normal * (w * fold);
            left[i] = mesh.Add(path[i] - side * w + lift);
            mid[i] = mesh.Add(path[i]);
            right[i] = mesh.Add(path[i] + side * w + lift);
        }

        for (int i = 0; i < n - 1; i++)
        {
            mesh.Quad(left[i], left[i + 1], mid[i + 1], mid[i]);
            mesh.Quad(mid[i], mid[i + 1], right[i + 1], right[i]);
        }

        return mesh;
    }

    /// <summary>Larguras de folha ao longo do comprimento: afunilada na base e no ápice conforme a forma.</summary>
    public static List<double> LeafWidths(int count, double maxWidth, double widestAt = 0.4, double tip = 0.12, double baseWidth = 0.18)
    {
        var widths = new List<double>(count);
        for (int i = 0; i < count; i++)
        {
            double t = count == 1 ? 0.5 : (double)i / (count - 1);
            double profile = t <= widestAt
                ? baseWidth + (1 - baseWidth) * Math.Sin(t / widestAt * Math.PI / 2)
                : tip + (1 - tip) * Math.Cos((t - widestAt) / (1 - widestAt) * Math.PI / 2);
            widths.Add(Math.Max(MinFeature, maxWidth * profile));
        }

        return widths;
    }

    /// <summary>
    /// Grupo de folhagem orgânico: elipsoide de baixa resolução com ruído e base achatada, para que a copa seja feita de
    /// massas sobrepostas e não de esferas perfeitas.
    /// </summary>
    public static MeshPiece Cluster(MeshPart part, Vec3 center, double radius, double flatten, int segments, int rings, long seed, double noise = 0.18)
    {
        double r = Math.Max(MinFeature * 4, radius);
        MeshPiece blob = Primitives.Ellipsoid(part, center, r, r * 0.92, r * Math.Clamp(flatten, 0.4, 1.2), Math.Max(5, segments), Math.Max(3, rings), noise, seed);

        // Base um pouco achatada (folhagem pesa para baixo): os vértices abaixo do centro sobem.
        for (int i = 0; i < blob.Vertices.Count; i++)
        {
            Vec3 v = blob.Vertices[i];
            if (v.Z < center.Z)
            {
                blob.Vertices[i] = new Vec3(v.X, v.Y, center.Z + (v.Z - center.Z) * 0.75);
            }
        }

        return blob;
    }

    /// <summary>
    /// Placa de folha (ou de grupo de folhas): losango dobrado na nervura, face única, com a ponta em
    /// <paramref name="tip"/> a partir de <paramref name="basePoint"/>.
    /// </summary>
    public static void LeafPlate(MeshPiece target, Vec3 basePoint, Vec3 tip, double width, Vec3 up, double fold = 0.18)
    {
        Vec3 axis = tip - basePoint;
        double length = axis.Length;
        if (length < MinFeature * 2)
        {
            return;
        }

        Vec3 t = axis * (1 / length);
        Vec3 side = t.Cross(up).Normalized();
        if (side.Length < 0.5)
        {
            side = t.Cross(new Vec3(1, 0, 0)).Normalized();
        }

        Vec3 normal = side.Cross(t).Normalized();
        double w = Math.Max(MinFeature, width) / 2;
        Vec3 middle = basePoint + axis * 0.42;
        int b = target.Add(basePoint);
        int l = target.Add(middle - side * w + normal * (w * fold));
        int r = target.Add(middle + side * w + normal * (w * fold));
        int e = target.Add(tip);
        target.Triangle(b, r, e);
        target.Triangle(b, e, l);
    }

    /// <summary>Esfera de baixa resolução (frutos, botões, flores pequenas).</summary>
    public static MeshPiece Sphere(MeshPart part, Vec3 center, double radius, int segments = 6, int rings = 4) =>
        Primitives.Ellipsoid(part, center, Math.Max(MinFeature, radius), Math.Max(MinFeature, radius), Math.Max(MinFeature, radius), segments, rings);

    /// <summary>Junta peças abertas da mesma parte numa só (menos objetos no Revit). Peças fechadas ficam separadas.</summary>
    public static List<MeshPiece> MergeOpen(IEnumerable<MeshPiece> pieces)
    {
        var result = new List<MeshPiece>();
        var open = new Dictionary<MeshPart, MeshPiece>();
        foreach (MeshPiece piece in pieces.Where(p => p.TriangleCount > 0))
        {
            if (piece.Closed)
            {
                result.Add(piece);
                continue;
            }

            if (!open.TryGetValue(piece.Part, out MeshPiece? merged))
            {
                merged = new MeshPiece(piece.Part, closed: false);
                open[piece.Part] = merged;
            }

            int offset = merged.Vertices.Count;
            merged.Vertices.AddRange(piece.Vertices);
            merged.Triangles.AddRange(piece.Triangles.Select(t => new Tri(t.A + offset, t.B + offset, t.C + offset)));
        }

        result.AddRange(open.Values);
        return result;
    }

    /// <summary>Referenciais ao longo do caminho (tangente e dois eixos normais) por transporte paralelo.</summary>
    private static List<(Vec3 T, Vec3 U, Vec3 V)> Frames(IReadOnlyList<Vec3> path)
    {
        int n = path.Count;
        var frames = new List<(Vec3, Vec3, Vec3)>(n);
        Vec3 Tangent(int i) => (path[Math.Min(n - 1, i + 1)] - path[Math.Max(0, i - 1)]).Normalized();
        Vec3 t0 = Tangent(0);
        Vec3 reference = Math.Abs(t0.Z) < 0.9 ? new Vec3(0, 0, 1) : new Vec3(1, 0, 0);
        Vec3 u = t0.Cross(reference).Normalized();
        Vec3 v = t0.Cross(u).Normalized();
        frames.Add((t0, u, v));
        for (int i = 1; i < n; i++)
        {
            Vec3 t = Tangent(i);
            // Projeta o eixo anterior no plano normal à nova tangente.
            Vec3 projected = u - t * u.Dot(t);
            u = projected.Length > 1e-9 ? projected.Normalized() : u;
            v = t.Cross(u).Normalized();
            frames.Add((t, u, v));
        }

        return frames;
    }
}
