using Viridian.Core.Geometry;
using Viridian.Core.Model3D;

namespace Viridian.Core.Solar;

/// <summary>Triângulo em metros nas coordenadas do modelo.</summary>
public readonly record struct Tri3(Vec3 A, Vec3 B, Vec3 C);

/// <summary>
/// Geometria real das edificações, muros, coberturas e do terreno, em triângulos. Serve para as duas contas do
/// estudo solar: o raio de cada ponto até o sol (horas de sol) e a projeção das sombras no plano do jardim (quadros
/// de sombra). As duas usam a mesma geometria, então o desenho e os números concordam.
/// </summary>
public sealed class ObstacleMesh
{
    private readonly Tri3[] _tris;
    private readonly bool[] _terrain;
    private readonly Dictionary<(int, int), List<int>> _bins = new();
    private readonly double _bin;
    private readonly int[] _stamp;
    private int _stampValue;

    /// <param name="triangles">Triângulos; <c>Terrain</c> marca o terreno, que bloqueia o sol mas não é desenhado como edificação.</param>
    /// <param name="binSize">Lado (m) das células de busca em planta.</param>
    public ObstacleMesh(IEnumerable<(Tri3 Tri, bool Terrain)> triangles, double binSize = 2.0)
    {
        ArgumentNullException.ThrowIfNull(triangles);
        var list = new List<Tri3>();
        var terrain = new List<bool>();
        foreach ((Tri3 t, bool isTerrain) in triangles)
        {
            Vec3 n = (t.B - t.A).Cross(t.C - t.A);
            if (n.Length < 1e-10 || !Finite(t.A) || !Finite(t.B) || !Finite(t.C))
            {
                continue; // degenerado
            }

            list.Add(t);
            terrain.Add(isTerrain);
        }

        _tris = list.ToArray();
        _terrain = terrain.ToArray();
        _stamp = new int[_tris.Length];
        _bin = Math.Max(0.25, binSize);
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        for (int i = 0; i < _tris.Length; i++)
        {
            Tri3 t = _tris[i];
            double x0 = Math.Min(t.A.X, Math.Min(t.B.X, t.C.X)), x1 = Math.Max(t.A.X, Math.Max(t.B.X, t.C.X));
            double y0 = Math.Min(t.A.Y, Math.Min(t.B.Y, t.C.Y)), y1 = Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y));
            minX = Math.Min(minX, x0);
            minY = Math.Min(minY, y0);
            maxX = Math.Max(maxX, x1);
            maxY = Math.Max(maxY, y1);
            maxZ = Math.Max(maxZ, Math.Max(t.A.Z, Math.Max(t.B.Z, t.C.Z)));
            for (int ix = Cell(x0); ix <= Cell(x1); ix++)
            {
                for (int iy = Cell(y0); iy <= Cell(y1); iy++)
                {
                    if (!_bins.TryGetValue((ix, iy), out List<int>? bucket))
                    {
                        _bins[(ix, iy)] = bucket = new List<int>();
                    }

                    bucket.Add(i);
                }
            }
        }

        Bounds = _tris.Length == 0 ? new Bounds2(0, 0, 0, 0) : new Bounds2(minX, minY, maxX, maxY);
        MaxZ = _tris.Length == 0 ? double.MinValue : maxZ;
    }

    public int Count => _tris.Length;

    public bool IsEmpty => _tris.Length == 0;

    public Bounds2 Bounds { get; }

    public double MaxZ { get; }

    public IReadOnlyList<Tri3> Triangles => _tris;

    public bool IsTerrain(int index) => _terrain[index];

    private static bool Finite(Vec3 v) => double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);

    private int Cell(double v) => (int)Math.Floor(v / _bin);

    /// <summary>
    /// Verdadeiro quando o raio que sai de <paramref name="origin"/> na direção <paramref name="dir"/> (para o sol)
    /// atravessa algum triângulo. Não é seguro para uso simultâneo em várias linhas de execução.
    /// </summary>
    public bool Blocks(Vec3 origin, Vec3 dir)
    {
        if (_tris.Length == 0 || dir.Z <= 1e-6 || origin.Z >= MaxZ)
        {
            return false;
        }

        double length = (MaxZ - origin.Z) / dir.Z; // acima de MaxZ não há mais nada
        double horizontal = Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y);
        if (horizontal * length > 2000)
        {
            length = 2000 / Math.Max(horizontal, 1e-9);
        }

        if (++_stampValue == int.MaxValue)
        {
            Array.Clear(_stamp);
            _stampValue = 1;
        }

        // Percorre as células de busca atravessadas pela projeção do raio em planta (Amanatides–Woo).
        double x = origin.X, y = origin.Y;
        int ix = Cell(x), iy = Cell(y);
        double ex = x + dir.X * length, ey = y + dir.Y * length;
        int endX = Cell(ex), endY = Cell(ey);
        int stepX = dir.X > 0 ? 1 : -1, stepY = dir.Y > 0 ? 1 : -1;
        double tDeltaX = Math.Abs(dir.X) < 1e-12 ? double.MaxValue : _bin / Math.Abs(dir.X);
        double tDeltaY = Math.Abs(dir.Y) < 1e-12 ? double.MaxValue : _bin / Math.Abs(dir.Y);
        double nextX = Math.Abs(dir.X) < 1e-12 ? double.MaxValue : ((dir.X > 0 ? (ix + 1) * _bin : ix * _bin) - x) / dir.X;
        double nextY = Math.Abs(dir.Y) < 1e-12 ? double.MaxValue : ((dir.Y > 0 ? (iy + 1) * _bin : iy * _bin) - y) / dir.Y;
        int guard = 0;
        while (true)
        {
            if (_bins.TryGetValue((ix, iy), out List<int>? bucket))
            {
                foreach (int i in bucket)
                {
                    if (_stamp[i] == _stampValue)
                    {
                        continue;
                    }

                    _stamp[i] = _stampValue;
                    if (Hit(_tris[i], origin, dir))
                    {
                        return true;
                    }
                }
            }

            if ((ix == endX && iy == endY) || ++guard > 100000)
            {
                return false;
            }

            if (nextX < nextY)
            {
                if (nextX > length)
                {
                    return false;
                }

                nextX += tDeltaX;
                ix += stepX;
            }
            else
            {
                if (nextY > length)
                {
                    return false;
                }

                nextY += tDeltaY;
                iy += stepY;
            }
        }
    }

    /// <summary>Interseção raio × triângulo (Möller–Trumbore), à frente da origem.</summary>
    private static bool Hit(Tri3 t, Vec3 o, Vec3 d)
    {
        Vec3 e1 = t.B - t.A, e2 = t.C - t.A;
        Vec3 p = d.Cross(e2);
        double det = e1.Dot(p);
        if (Math.Abs(det) < 1e-12)
        {
            return false;
        }

        double inv = 1 / det;
        Vec3 s = o - t.A;
        double u = s.Dot(p) * inv;
        if (u < 0 || u > 1)
        {
            return false;
        }

        Vec3 q = s.Cross(e1);
        double v = d.Dot(q) * inv;
        if (v < 0 || u + v > 1)
        {
            return false;
        }

        return e2.Dot(q) * inv > 1e-4;
    }

    /// <summary>
    /// Partes dos triângulos (sem o terreno) acima da cota <paramref name="z"/>: só elas fazem sombra no plano do jardim
    /// (o piso em que o jardim está não faz sombra sobre si mesmo).
    /// </summary>
    public List<List<Vec3>> Above(double z, Bounds2 near)
    {
        var result = new List<List<Vec3>>();
        for (int i = 0; i < _tris.Length; i++)
        {
            if (_terrain[i])
            {
                continue;
            }

            Tri3 t = _tris[i];
            if (Math.Max(t.A.Z, Math.Max(t.B.Z, t.C.Z)) <= z
                || Math.Max(t.A.X, Math.Max(t.B.X, t.C.X)) < near.MinX || Math.Min(t.A.X, Math.Min(t.B.X, t.C.X)) > near.MaxX
                || Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y)) < near.MinY || Math.Min(t.A.Y, Math.Min(t.B.Y, t.C.Y)) > near.MaxY)
            {
                continue;
            }

            List<Vec3> clipped = ClipAbove(new List<Vec3> { t.A, t.B, t.C }, z);
            if (clipped.Count >= 3)
            {
                result.Add(clipped);
            }
        }

        return result;
    }

    /// <summary>Recorte de um polígono 3D pelo semiespaço Z ≥ <paramref name="z"/>.</summary>
    private static List<Vec3> ClipAbove(List<Vec3> poly, double z)
    {
        var result = new List<Vec3>(poly.Count + 2);
        for (int i = 0; i < poly.Count; i++)
        {
            Vec3 a = poly[i], b = poly[(i + 1) % poly.Count];
            bool ina = a.Z >= z, inb = b.Z >= z;
            if (ina)
            {
                result.Add(a);
            }

            if (ina != inb)
            {
                double t = (z - a.Z) / (b.Z - a.Z);
                result.Add(a + (b - a) * t);
            }
        }

        return result;
    }
}
