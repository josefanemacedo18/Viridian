using Viridian.Core.Geometry;

namespace Viridian.Core.Model3D;

/// <summary>
/// Sólidos simples em malha fechada, com as faces voltadas para fora. Um leve ruído determinístico
/// quebra a regularidade das copas sem abrir a malha (cada vértice é deslocado uma única vez).
/// </summary>
public static class Primitives
{
    /// <summary>Elipsoide com centro e semieixos; <paramref name="noise"/> é a variação radial relativa (0 a 0,2).</summary>
    public static MeshPiece Ellipsoid(MeshPart part, Vec3 center, double rx, double ry, double rz, int segments, int rings, double noise = 0, long seed = 1)
    {
        segments = Math.Max(5, segments);
        rings = Math.Max(3, rings);
        var mesh = new MeshPiece(part);
        var random = new DeterministicRandom(seed);
        double Jitter() => noise <= 0 ? 1.0 : 1.0 + random.Range(-noise, noise);

        int top = mesh.Add(center + new Vec3(0, 0, rz * Jitter()));
        var ringStart = new int[rings - 1];
        for (int i = 1; i < rings; i++)
        {
            double phi = Math.PI * i / rings;
            double ringRadius = Math.Sin(phi);
            double z = Math.Cos(phi);
            ringStart[i - 1] = mesh.Vertices.Count;
            for (int s = 0; s < segments; s++)
            {
                double theta = Geo.TwoPi * s / segments;
                double k = Jitter();
                mesh.Add(center + new Vec3(rx * ringRadius * Math.Cos(theta) * k, ry * ringRadius * Math.Sin(theta) * k, rz * z * k));
            }
        }

        int bottom = mesh.Add(center - new Vec3(0, 0, rz * Jitter()));
        int V(int ring, int s) => ringStart[ring] + ((s % segments) + segments) % segments;

        for (int s = 0; s < segments; s++)
        {
            mesh.Triangle(top, V(0, s), V(0, s + 1));
        }

        for (int i = 0; i < rings - 2; i++)
        {
            for (int s = 0; s < segments; s++)
            {
                mesh.Quad(V(i, s), V(i + 1, s), V(i + 1, s + 1), V(i, s + 1));
            }
        }

        for (int s = 0; s < segments; s++)
        {
            mesh.Triangle(bottom, V(rings - 2, s + 1), V(rings - 2, s));
        }

        return mesh;
    }

    /// <summary>Meio elipsoide apoiado no chão (z = base), com fundo plano: arbustos, forrações e touceiras.</summary>
    public static MeshPiece Dome(MeshPart part, Vec3 baseCenter, double rx, double ry, double height, int segments, int rings, double noise = 0, long seed = 1)
    {
        segments = Math.Max(5, segments);
        rings = Math.Max(2, rings);
        var mesh = new MeshPiece(part);
        var random = new DeterministicRandom(seed);
        double Jitter() => noise <= 0 ? 1.0 : 1.0 + random.Range(-noise, noise);

        int top = mesh.Add(baseCenter + new Vec3(0, 0, height * Jitter()));
        var ringStart = new int[rings];
        for (int i = 1; i <= rings; i++)
        {
            double phi = Math.PI / 2 * i / rings;
            double ringRadius = Math.Sin(phi);
            double z = Math.Cos(phi);
            bool ground = i == rings;
            ringStart[i - 1] = mesh.Vertices.Count;
            for (int s = 0; s < segments; s++)
            {
                double theta = Geo.TwoPi * s / segments;
                double k = Jitter();
                mesh.Add(baseCenter + new Vec3(rx * ringRadius * Math.Cos(theta) * k, ry * ringRadius * Math.Sin(theta) * k, ground ? 0 : height * z * k));
            }
        }

        int bottom = mesh.Add(baseCenter);
        int V(int ring, int s) => ringStart[ring] + ((s % segments) + segments) % segments;

        for (int s = 0; s < segments; s++)
        {
            mesh.Triangle(top, V(0, s), V(0, s + 1));
        }

        for (int i = 0; i < rings - 1; i++)
        {
            for (int s = 0; s < segments; s++)
            {
                mesh.Quad(V(i, s), V(i + 1, s), V(i + 1, s + 1), V(i, s + 1));
            }
        }

        for (int s = 0; s < segments; s++)
        {
            mesh.Triangle(bottom, V(rings - 1, s + 1), V(rings - 1, s));
        }

        return mesh;
    }

    /// <summary>Tronco de cone (cilindro quando os raios são iguais; cone quando o raio do topo é zero).</summary>
    public static MeshPiece Frustum(MeshPart part, Vec3 baseCenter, double height, double baseRadius, double topRadius, int sides)
    {
        sides = Math.Max(5, sides);
        var mesh = new MeshPiece(part);
        int bottomCenter = mesh.Add(baseCenter);
        int bottomStart = mesh.Vertices.Count;
        for (int s = 0; s < sides; s++)
        {
            double theta = Geo.TwoPi * s / sides;
            mesh.Add(baseCenter + new Vec3(baseRadius * Math.Cos(theta), baseRadius * Math.Sin(theta), 0));
        }

        int B(int s) => bottomStart + ((s % sides) + sides) % sides;
        for (int s = 0; s < sides; s++)
        {
            mesh.Triangle(bottomCenter, B(s + 1), B(s));
        }

        if (topRadius <= 1e-6)
        {
            int apex = mesh.Add(baseCenter + new Vec3(0, 0, height));
            for (int s = 0; s < sides; s++)
            {
                mesh.Triangle(apex, B(s), B(s + 1));
            }

            return mesh;
        }

        int topStart = mesh.Vertices.Count;
        for (int s = 0; s < sides; s++)
        {
            double theta = Geo.TwoPi * s / sides;
            mesh.Add(baseCenter + new Vec3(topRadius * Math.Cos(theta), topRadius * Math.Sin(theta), height));
        }

        int T(int s) => topStart + ((s % sides) + sides) % sides;
        int topCenter = mesh.Add(baseCenter + new Vec3(0, 0, height));
        for (int s = 0; s < sides; s++)
        {
            mesh.Quad(B(s), B(s + 1), T(s + 1), T(s));
            mesh.Triangle(topCenter, T(s), T(s + 1));
        }

        return mesh;
    }

    /// <summary>Octaedro: manchas de flor sobre a copa.</summary>
    public static MeshPiece Blob(MeshPart part, Vec3 center, double radius, double rotation = 0)
    {
        var mesh = new MeshPiece(part);
        int px = mesh.Add(center + new Vec3(radius, 0, 0).RotateZ(rotation));
        int py = mesh.Add(center + new Vec3(0, radius, 0).RotateZ(rotation));
        int nx = mesh.Add(center + new Vec3(-radius, 0, 0).RotateZ(rotation));
        int ny = mesh.Add(center + new Vec3(0, -radius, 0).RotateZ(rotation));
        int pz = mesh.Add(center + new Vec3(0, 0, radius * 0.8));
        int nz = mesh.Add(center - new Vec3(0, 0, radius * 0.8));
        mesh.Triangle(pz, px, py);
        mesh.Triangle(pz, py, nx);
        mesh.Triangle(pz, nx, ny);
        mesh.Triangle(pz, ny, px);
        mesh.Triangle(nz, py, px);
        mesh.Triangle(nz, nx, py);
        mesh.Triangle(nz, ny, nx);
        mesh.Triangle(nz, px, ny);
        return mesh;
    }

    /// <summary>
    /// Lâmina fechada ao longo de um caminho (folha de palmeira): seção retangular de largura variável e
    /// espessura constante. O caminho deve ter pontos distintos.
    /// </summary>
    public static MeshPiece Blade(MeshPart part, IReadOnlyList<Vec3> path, IReadOnlyList<double> widths, double thickness)
    {
        var mesh = new MeshPiece(part);
        int n = path.Count;
        if (n < 2 || widths.Count != n)
        {
            return mesh;
        }

        var start = new int[n];
        for (int i = 0; i < n; i++)
        {
            Vec3 tangent = (path[Math.Min(n - 1, i + 1)] - path[Math.Max(0, i - 1)]).Normalized();
            Vec3 side = tangent.Cross(new Vec3(0, 0, 1)).Normalized();
            if (side.Length < 0.5)
            {
                side = new Vec3(1, 0, 0);
            }

            Vec3 up = side.Cross(tangent).Normalized();
            double w = Math.Max(0.01, widths[i]) / 2;
            double t = thickness / 2;
            start[i] = mesh.Vertices.Count;
            mesh.Add(path[i] - side * w - up * t); // 0: esquerda, baixo
            mesh.Add(path[i] + side * w - up * t); // 1: direita, baixo
            mesh.Add(path[i] + side * w + up * t); // 2: direita, cima
            mesh.Add(path[i] - side * w + up * t); // 3: esquerda, cima
        }

        for (int i = 0; i < n - 1; i++)
        {
            int a = start[i], b = start[i + 1];
            mesh.Quad(a + 3, a + 2, b + 2, b + 3); // face superior
            mesh.Quad(a + 0, b + 0, b + 1, a + 1); // face inferior
            mesh.Quad(a + 1, b + 1, b + 2, a + 2); // lado direito
            mesh.Quad(a + 0, a + 3, b + 3, b + 0); // lado esquerdo
        }

        int s0 = start[0], e = start[n - 1];
        mesh.Quad(s0 + 0, s0 + 1, s0 + 2, s0 + 3);
        mesh.Quad(e + 0, e + 3, e + 2, e + 1);
        return mesh;
    }
}
