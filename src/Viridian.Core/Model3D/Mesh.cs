namespace Viridian.Core.Model3D;

/// <summary>Ponto ou vetor no espaço, em metros (Z para cima).</summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    public static readonly Vec3 Zero = new(0, 0, 0);

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static Vec3 operator -(Vec3 a) => new(-a.X, -a.Y, -a.Z);

    public static Vec3 operator *(Vec3 a, double k) => new(a.X * k, a.Y * k, a.Z * k);

    public static Vec3 operator *(double k, Vec3 a) => a * k;

    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

    public double Dot(Vec3 o) => X * o.X + Y * o.Y + Z * o.Z;

    public Vec3 Cross(Vec3 o) => new(Y * o.Z - Z * o.Y, Z * o.X - X * o.Z, X * o.Y - Y * o.X);

    public Vec3 Normalized()
    {
        double length = Length;
        return length > 1e-12 ? this * (1.0 / length) : Zero;
    }

    /// <summary>Gira em torno do eixo Z (radianos).</summary>
    public Vec3 RotateZ(double radians)
    {
        double c = Math.Cos(radians), s = Math.Sin(radians);
        return new Vec3(X * c - Y * s, X * s + Y * c, Z);
    }
}

/// <summary>Parte de uma planta 3D, que define o material usado.</summary>
public enum MeshPart
{
    Folhagem = 0,
    Tronco = 1,
    Flor = 2,

    /// <summary>Madeira dos galhos (um pouco mais clara que o tronco).</summary>
    Galho = 3,

    Fruto = 4,

    /// <summary>Folhagem em tom mais claro (brotações e partes de cima da copa), para variação tonal.</summary>
    FolhagemClara = 5,

    /// <summary>Folhagem em tom mais escuro (interior e partes de baixo da copa).</summary>
    FolhagemEscura = 6,
}

/// <summary>Triângulo por índices de vértice. A ordem anti-horária, vista de fora, define a face externa.</summary>
public readonly record struct Tri(int A, int B, int C);

/// <summary>Malha de triângulos de uma parte da planta. Malhas fechadas viram sólidos no Revit.</summary>
public sealed class MeshPiece
{
    public MeshPiece(MeshPart part, bool closed = true)
    {
        Part = part;
        Closed = closed;
    }

    public MeshPart Part { get; }

    /// <summary>Verdadeiro quando a malha é fechada (cada aresta é compartilhada por dois triângulos).</summary>
    public bool Closed { get; }

    public List<Vec3> Vertices { get; } = new();

    public List<Tri> Triangles { get; } = new();

    public int TriangleCount => Triangles.Count;

    public int Add(Vec3 v)
    {
        Vertices.Add(v);
        return Vertices.Count - 1;
    }

    public void Triangle(int a, int b, int c)
    {
        if (a != b && b != c && a != c)
        {
            Triangles.Add(new Tri(a, b, c));
        }
    }

    /// <summary>Quadrilátero a-b-c-d (anti-horário visto de fora) dividido em dois triângulos.</summary>
    public void Quad(int a, int b, int c, int d)
    {
        Triangle(a, b, c);
        Triangle(a, c, d);
    }

    /// <summary>Gira em torno de Z e desloca todos os vértices.</summary>
    public MeshPiece Transform(double rotationRadians, Vec3 offset)
    {
        var result = new MeshPiece(Part, Closed);
        foreach (Vec3 v in Vertices)
        {
            result.Vertices.Add(v.RotateZ(rotationRadians) + offset);
        }

        result.Triangles.AddRange(Triangles);
        return result;
    }

    public (Vec3 A, Vec3 B, Vec3 C) Corners(Tri t) => (Vertices[t.A], Vertices[t.B], Vertices[t.C]);

    /// <summary>Volume com sinal (positivo quando as faces apontam para fora).</summary>
    public double SignedVolume()
    {
        double volume = 0;
        foreach (Tri t in Triangles)
        {
            (Vec3 a, Vec3 b, Vec3 c) = Corners(t);
            volume += a.Dot(b.Cross(c)) / 6.0;
        }

        return volume;
    }

    /// <summary>Menor aresta da malha, em metros (o Revit recusa arestas muito curtas).</summary>
    public double ShortestEdge()
    {
        double shortest = double.MaxValue;
        foreach (Tri t in Triangles)
        {
            (Vec3 a, Vec3 b, Vec3 c) = Corners(t);
            shortest = Math.Min(shortest, Math.Min((a - b).Length, Math.Min((b - c).Length, (c - a).Length)));
        }

        return Triangles.Count == 0 ? 0 : shortest;
    }

    /// <summary>Cada aresta orientada aparece uma única vez e tem a sua oposta: malha fechada e coerente.</summary>
    public bool IsWatertight()
    {
        var edges = new HashSet<(int, int)>();
        foreach (Tri t in Triangles)
        {
            if (!edges.Add((t.A, t.B)) || !edges.Add((t.B, t.C)) || !edges.Add((t.C, t.A)))
            {
                return false;
            }
        }

        return edges.All(e => edges.Contains((e.Item2, e.Item1)));
    }

    public (Vec3 Min, Vec3 Max) Bounds()
    {
        if (Vertices.Count == 0)
        {
            return (Vec3.Zero, Vec3.Zero);
        }

        return (new Vec3(Vertices.Min(v => v.X), Vertices.Min(v => v.Y), Vertices.Min(v => v.Z)),
                new Vec3(Vertices.Max(v => v.X), Vertices.Max(v => v.Y), Vertices.Max(v => v.Z)));
    }
}
