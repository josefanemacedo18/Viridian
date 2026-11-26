using Viridian.Core.Geometry;
using Viridian.Core.Model3D;

namespace Viridian.Core.Solar;

/// <summary>Classe de luz de um trecho do chão num horário.</summary>
public enum ShadeClass
{
    /// <summary>Embaixo de uma edificação, muro ou cobertura (não é chão do jardim).</summary>
    Edificacao = 0,

    /// <summary>Sombra: chega menos de 35% da luz direta.</summary>
    Sombra = 1,

    /// <summary>Meia-luz: de 35% a 75% (sob a borda das copas ou copas ralas).</summary>
    MeiaLuz = 2,

    /// <summary>Sol: 75% ou mais.</summary>
    Sol = 3,
}

/// <summary>
/// Contorno fechado de uma mancha de luz. Desenhados do maior para o menor, cada um pintado com
/// <see cref="Fill"/>, os contornos reproduzem o mapa de luz completo, com furos e manchas dentro de manchas.
/// </summary>
public sealed record ShadeRing(IReadOnlyList<Point2> Points, ShadeClass Fill, double Area, double Level, bool DarkInside);

/// <summary>Campo de luz de um horário numa malha fina e as manchas de sombra já contornadas.</summary>
public sealed class LightField
{
    /// <summary>Níveis que separam as classes: edificação | sombra | meia-luz | sol.</summary>
    public static readonly double[] Levels = { -0.25, 0.35, 0.75 };

    /// <summary>Valor dos nós sob edificações (abaixo de todos os níveis).</summary>
    public const double BuildingValue = -0.5;

    public LightField(double x0, double y0, double resolution, int nx, int ny)
    {
        X0 = x0;
        Y0 = y0;
        Resolution = resolution;
        Nx = nx;
        Ny = ny;
        Values = new double[nx * ny];
        Inside = new bool[nx * ny];
        Array.Fill(Values, 1.0);
    }

    public double X0 { get; }

    public double Y0 { get; }

    public double Resolution { get; }

    public int Nx { get; }

    public int Ny { get; }

    /// <summary>Luz em cada nó (0 a 1; <see cref="BuildingValue"/> sob edificação). Fora da região vale 1.</summary>
    public double[] Values { get; }

    /// <summary>Nó dentro da região estudada.</summary>
    public bool[] Inside { get; }

    public Point2 Node(int i, int j) => new(X0 + i * Resolution, Y0 + j * Resolution);

    public double this[int i, int j] => Values[j * Nx + i];

    /// <summary>Parte do chão do jardim (sem as edificações) com menos de metade da luz.</summary>
    public double ShadedFraction()
    {
        int ground = 0, shaded = 0;
        for (int k = 0; k < Values.Length; k++)
        {
            if (!Inside[k] || Values[k] < 0)
            {
                continue;
            }

            ground++;
            if (Values[k] < 0.5)
            {
                shaded++;
            }
        }

        return ground == 0 ? 0 : shaded / (double)ground;
    }

    /// <summary>Classe de luz de um valor do campo.</summary>
    public static ShadeClass ClassOf(double value) =>
        value < Levels[0] ? ShadeClass.Edificacao : value < Levels[1] ? ShadeClass.Sombra : value < Levels[2] ? ShadeClass.MeiaLuz : ShadeClass.Sol;
}

/// <summary>
/// Sombras realistas de um horário: as edificações (geometria real) projetadas no plano do jardim pela direção do sol,
/// as copas como elipsoides que filtram a luz pela espessura de folhagem atravessada e os troncos. O resultado é um
/// campo de luz numa malha fina, contornado com linhas suaves (marching squares) em vez de quadradinhos.
/// </summary>
public static class ShadowField
{
    /// <summary>Altura mínima (m) acima do plano do jardim para uma parte de edificação fazer sombra.</summary>
    public const double MinCasterHeightM = 0.2;

    /// <summary>Resolução (m) adequada à região: cerca de 320 nós no lado maior, entre 4 cm e 50 cm.</summary>
    public static double ResolutionFor(Bounds2 region) => Math.Clamp(Math.Max(region.Width, region.Height) / 320.0, 0.04, 0.5);

    /// <summary>
    /// Campo de luz da região no plano <paramref name="planeZ"/> com o sol na direção <paramref name="toSun"/>
    /// (coordenadas do modelo, unitária, apontando para o sol).
    /// </summary>
    /// <param name="casters">Partes das edificações acima do plano (<see cref="ObstacleMesh.Above"/>), ou nulo.</param>
    public static LightField Compute(Polygon2 region, double resolution, double planeZ, Vec3 toSun, int month,
        IReadOnlyList<CrownObstacle> crowns, IReadOnlyList<List<Vec3>>? casters)
    {
        ArgumentNullException.ThrowIfNull(region);
        ArgumentNullException.ThrowIfNull(crowns);
        double res = Math.Max(0.01, resolution);
        Bounds2 b = region.Bounds;
        // Uma fileira de nós "ao sol" em volta: todos os contornos fecham.
        double x0 = b.MinX - res, y0 = b.MinY - res;
        int nx = (int)Math.Ceiling(b.Width / res) + 3, ny = (int)Math.Ceiling(b.Height / res) + 3;
        var field = new LightField(x0, y0, res, nx, ny);
        for (int j = 0; j < ny; j++)
        {
            for (int i = 0; i < nx; i++)
            {
                field.Inside[j * nx + i] = region.Contains(field.Node(i, j));
            }
        }

        if (toSun.Z <= 0.01)
        {
            for (int k = 0; k < field.Values.Length; k++)
            {
                field.Values[k] = field.Inside[k] ? 0 : 1;
            }

            return field;
        }

        double[] v = field.Values;
        if (casters is not null)
        {
            // Planta das edificações (projeção vertical) e a sombra delas (projeção pela direção do sol).
            var up = new Vec3(0, 0, 1);
            foreach (List<Vec3> poly in casters)
            {
                Fill(field, Project(poly, up, planeZ), building: true);
            }

            foreach (List<Vec3> poly in casters)
            {
                Fill(field, Project(poly, toSun, planeZ), building: false);
            }
        }

        // Copas (luz filtrada) e troncos (opacos).
        foreach (CrownObstacle c in crowns)
        {
            IReadOnlyList<Point2> hull = c.ShadowOnGround(toSun, planeZ);
            if (hull.Count < 3)
            {
                continue;
            }

            Bounds2 hb = Bounds2.FromPoints(hull);
            ForNodes(field, hb, (i, j, k) =>
            {
                if (v[k] <= 0)
                {
                    return;
                }

                Point2 p = field.Node(i, j);
                v[k] *= c.Transmission(new Vec3(p.X, p.Y, planeZ), toSun, month);
            });

            if (c.TrunkRadius > 0 && c.TrunkTop > planeZ)
            {
                double base0 = Math.Max(c.Ground, planeZ);
                Point2 a = new(c.Center.X, c.Center.Y);
                double t = (c.TrunkTop - planeZ) / toSun.Z;
                Point2 top = new(c.Center.X - toSun.X * t, c.Center.Y - toSun.Y * t);
                double t0 = (base0 - planeZ) / toSun.Z;
                Point2 start = new(a.X - toSun.X * t0, a.Y - toSun.Y * t0);
                double r = c.TrunkRadius;
                var tb = new Bounds2(Math.Min(start.X, top.X) - r, Math.Min(start.Y, top.Y) - r, Math.Max(start.X, top.X) + r, Math.Max(start.Y, top.Y) + r);
                ForNodes(field, tb, (i, j, k) =>
                {
                    if (v[k] > 0 && Geo.DistancePointSegment(field.Node(i, j), start, top) <= r)
                    {
                        v[k] = 0;
                    }
                });
            }
        }

        for (int k = 0; k < v.Length; k++)
        {
            if (!field.Inside[k])
            {
                v[k] = 1;
            }
        }

        return field;
    }

    private static List<Point2> Project(List<Vec3> poly, Vec3 dir, double planeZ)
    {
        var result = new List<Point2>(poly.Count);
        foreach (Vec3 p in poly)
        {
            double t = (p.Z - planeZ) / dir.Z;
            result.Add(new Point2(p.X - dir.X * t, p.Y - dir.Y * t));
        }

        return result;
    }

    /// <summary>Pinta os nós dentro do polígono convexo (triângulo recortado): edificação ou sombra total.</summary>
    private static void Fill(LightField field, List<Point2> poly, bool building)
    {
        if (poly.Count < 3 || Math.Abs(Geo.SignedArea(poly)) < 1e-10)
        {
            return;
        }

        double[] v = field.Values;
        double sign = Geo.SignedArea(poly) > 0 ? 1 : -1;
        ForNodes(field, Bounds2.FromPoints(poly), (i, j, k) =>
        {
            if (building ? v[k] <= LightField.BuildingValue : v[k] <= 0)
            {
                return;
            }

            Point2 p = field.Node(i, j);
            for (int e = 0; e < poly.Count; e++)
            {
                Point2 a = poly[e], c = poly[(e + 1) % poly.Count];
                if (sign * ((c.X - a.X) * (p.Y - a.Y) - (c.Y - a.Y) * (p.X - a.X)) < -1e-12)
                {
                    return;
                }
            }

            v[k] = building ? LightField.BuildingValue : Math.Min(v[k], 0);
        });
    }

    private static void ForNodes(LightField f, Bounds2 box, Action<int, int, int> visit)
    {
        int i0 = Math.Max(0, (int)Math.Ceiling((box.MinX - f.X0) / f.Resolution));
        int i1 = Math.Min(f.Nx - 1, (int)Math.Floor((box.MaxX - f.X0) / f.Resolution));
        int j0 = Math.Max(0, (int)Math.Ceiling((box.MinY - f.Y0) / f.Resolution));
        int j1 = Math.Min(f.Ny - 1, (int)Math.Floor((box.MaxY - f.Y0) / f.Resolution));
        for (int j = j0; j <= j1; j++)
        {
            for (int i = i0; i <= i1; i++)
            {
                int k = j * f.Nx + i;
                if (f.Inside[k])
                {
                    visit(i, j, k);
                }
            }
        }
    }

    /// <summary>
    /// Contornos de todas as classes, prontos para pintar do maior para o menor (ordem da lista). Cada contorno leva a
    /// cor do lado de dentro; contornos minúsculos (ruído da malha) são descartados e os demais simplificados.
    /// </summary>
    public static List<ShadeRing> Rings(LightField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        var rings = new List<ShadeRing>();
        double minArea = field.Resolution * field.Resolution * 1.5;
        for (int l = 0; l < LightField.Levels.Length; l++)
        {
            double level = LightField.Levels[l];
            foreach (List<Point2> ring in IsoRings(field, level))
            {
                List<Point2> simple = Simplify(ring, field.Resolution * 0.3);
                if (simple.Count < 3)
                {
                    continue;
                }

                double area = Geo.SignedArea(simple);
                if (Math.Abs(area) < minArea)
                {
                    continue;
                }

                bool dark = area > 0; // anti-horário: o lado escuro (abaixo do nível) fica dentro
                ShadeClass fill = (ShadeClass)(dark ? l : l + 1);
                rings.Add(new ShadeRing(simple, fill, Math.Abs(area), level, dark));
            }
        }

        rings.Sort((a, b) => b.Area.CompareTo(a.Area));
        return rings;
    }

    /// <summary>
    /// Contornos fechados do nível <paramref name="level"/> (marching squares com interpolação linear), orientados com
    /// o lado abaixo do nível à esquerda: anti-horário quando a mancha escura está dentro.
    /// </summary>
    public static List<List<Point2>> IsoRings(LightField f, double level)
    {
        int nx = f.Nx, ny = f.Ny;
        double V(int i, int j) => f.Values[j * nx + i];
        bool In(int i, int j) => V(i, j) < level;

        // Chave de cada aresta da malha: horizontais (i,j)-(i+1,j) pares, verticais (i,j)-(i,j+1) ímpares.
        long H(int i, int j) => 2L * ((long)j * nx + i);
        long Vt(int i, int j) => 2L * ((long)j * nx + i) + 1;
        Point2 Cross(int ia, int ja, int ib, int jb)
        {
            double va = V(ia, ja), vb = V(ib, jb);
            double t = Math.Abs(vb - va) < 1e-12 ? 0.5 : Math.Clamp((level - va) / (vb - va), 0, 1);
            Point2 a = f.Node(ia, ja), b = f.Node(ib, jb);
            return new Point2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
        }

        var next = new Dictionary<long, (long To, Point2 From)>();
        for (int j = 0; j < ny - 1; j++)
        {
            for (int i = 0; i < nx - 1; i++)
            {
                bool bl = In(i, j), br = In(i + 1, j), tr = In(i + 1, j + 1), tl = In(i, j + 1);
                if (bl == br && br == tr && tr == tl)
                {
                    continue;
                }

                // Arestas cortadas: 0 baixo, 1 direita, 2 cima, 3 esquerda.
                var edges = new List<(int Edge, long Key, Point2 P)>(4);
                if (bl != br)
                {
                    edges.Add((0, H(i, j), Cross(i, j, i + 1, j)));
                }

                if (br != tr)
                {
                    edges.Add((1, Vt(i + 1, j), Cross(i + 1, j, i + 1, j + 1)));
                }

                if (tr != tl)
                {
                    edges.Add((2, H(i, j + 1), Cross(i, j + 1, i + 1, j + 1)));
                }

                if (tl != bl)
                {
                    edges.Add((3, Vt(i, j), Cross(i, j, i, j + 1)));
                }

                // Cada par de cortes forma um segmento; "corner" é um canto do lado escuro (ou claro) que decide o sentido.
                Point2 cBl = f.Node(i, j), cBr = f.Node(i + 1, j), cTr = f.Node(i + 1, j + 1), cTl = f.Node(i, j + 1);
                var pairs = new List<((int, long, Point2) A, (int, long, Point2) B, Point2 Corner, bool CornerDark)>(2);
                if (edges.Count == 2)
                {
                    (Point2 corner, bool dark) = bl ? (cBl, true) : br ? (cBr, true) : tr ? (cTr, true) : (cTl, true);
                    pairs.Add((edges[0], edges[1], corner, dark));
                }
                else if (edges.Count == 4)
                {
                    // Sela: o valor do centro decide se os cantos escuros se ligam.
                    double center = (V(i, j) + V(i + 1, j) + V(i + 1, j + 1) + V(i, j + 1)) / 4;
                    bool centerIn = center < level;
                    if (centerIn == bl)
                    {
                        // bl e tr ligados pelo centro: os segmentos cortam br e tl.
                        pairs.Add((edges[0], edges[1], cBr, br));
                        pairs.Add((edges[2], edges[3], cTl, tl));
                    }
                    else
                    {
                        pairs.Add((edges[3], edges[0], cBl, bl));
                        pairs.Add((edges[1], edges[2], cTr, tr));
                    }
                }

                foreach (((int _, long ka, Point2 pa), (int _, long kb, Point2 pb), Point2 corner, bool cornerDark) in pairs)
                {
                    // Lado escuro à esquerda: o canto de referência fica à esquerda (se escuro) ou à direita (se claro).
                    double cross = (pb.X - pa.X) * (corner.Y - pa.Y) - (pb.Y - pa.Y) * (corner.X - pa.X);
                    bool forward = cross > 0 == cornerDark;
                    (long from, Point2 start, long to) = forward ? (ka, pa, kb) : (kb, pb, ka);
                    next[from] = (to, start);
                }
            }
        }

        // Encadeia os segmentos pelas arestas compartilhadas.
        var rings = new List<List<Point2>>();
        var used = new HashSet<long>();
        foreach (long first in next.Keys)
        {
            if (used.Contains(first))
            {
                continue;
            }

            var ring = new List<Point2>();
            long key = first;
            int guard = 0;
            while (used.Add(key) && next.TryGetValue(key, out (long To, Point2 From) seg) && guard++ < 10_000_000)
            {
                ring.Add(seg.From);
                key = seg.To;
            }

            if (ring.Count >= 3 && key == first)
            {
                rings.Add(ring);
            }
        }

        return rings;
    }

    /// <summary>Douglas–Peucker num contorno fechado (mantém a orientação).</summary>
    public static List<Point2> Simplify(IReadOnlyList<Point2> ring, double tolerance)
    {
        if (ring.Count < 5)
        {
            return ring.ToList();
        }

        // Começa pelos dois pontos mais distantes entre si (aprox.: o primeiro e o mais longe dele).
        int far = 0;
        double best = -1;
        for (int i = 1; i < ring.Count; i++)
        {
            double d = ring[0].DistanceTo(ring[i]);
            if (d > best)
            {
                best = d;
                far = i;
            }
        }

        var keep = new bool[ring.Count];
        keep[0] = keep[far] = true;
        Reduce(ring, 0, far, tolerance, keep);
        Reduce(ring, far, ring.Count, tolerance, keep);
        var result = new List<Point2>();
        for (int i = 0; i < ring.Count; i++)
        {
            if (keep[i])
            {
                result.Add(ring[i]);
            }
        }

        return result;
    }

    private static void Reduce(IReadOnlyList<Point2> ring, int from, int to, double tolerance, bool[] keep)
    {
        // Índices circulares: "to" pode ser ring.Count (volta ao 0).
        var stack = new Stack<(int, int)>();
        stack.Push((from, to));
        while (stack.Count > 0)
        {
            (int a, int b) = stack.Pop();
            if (b - a < 2)
            {
                continue;
            }

            Point2 pa = ring[a % ring.Count], pb = ring[b % ring.Count];
            int index = -1;
            double max = tolerance;
            for (int i = a + 1; i < b; i++)
            {
                double d = Geo.DistancePointSegment(ring[i % ring.Count], pa, pb);
                if (d > max)
                {
                    max = d;
                    index = i;
                }
            }

            if (index >= 0)
            {
                keep[index % ring.Count] = true;
                stack.Push((a, index));
                stack.Push((index, b));
            }
        }
    }
}
