using Viridian.Core.Geometry;
using Viridian.Core.Hatch;

namespace Viridian.Core.Paving;

/// <summary>
/// Motivo periódico de uma paginação: peças (polígonos), traços avulsos e os dois vetores de repetição. Medidas em mm.
/// </summary>
public sealed class PavingMotif
{
    public List<IReadOnlyList<Point2>> Tiles { get; } = new();

    /// <summary>Traços que não são lados de peças (pontos de pedrisco, vazados).</summary>
    public List<(Point2 A, Point2 B)> Extra { get; } = new();

    public Point2 T1 { get; set; }

    public Point2 T2 { get; set; }

    /// <summary>Verdadeiro quando as peças cobrem o plano sem sobra nem sobreposição.</summary>
    public bool IsTiling { get; set; } = true;

    public double CellArea => Math.Abs(T1.Cross(T2));
}

/// <summary>
/// Gera a hachura de modelo (escala real) de cada paginação. Cada lado de peça vira uma família de linhas tracejadas
/// cuja repetição segue a malha do motivo, o que reproduz espinha de peixe, trama, hexágonos e cacos sem distorção.
/// </summary>
public static class PavingPatterns
{
    private const double Eps = 1e-6;

    public static PavingMotif Motif(PavingType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        double joint = Math.Max(0, double.IsFinite(type.JointMm) ? type.JointMm : 0);
        double a = Math.Max(5, type.LengthMm) + joint; // passo com a junta
        double b = Math.Max(5, type.WidthMm) + joint;
        PavingMotif m = type.Pattern switch
        {
            LayingPattern.AmarracaoMeia => Bond(a, b, 0.5),
            LayingPattern.AmarracaoTerco => Bond(a, b, 1.0 / 3),
            LayingPattern.Reguas => Bond(a, b, 0.4),
            // A espinha de peixe só fecha com peças de comprimento múltiplo da largura (com a junta): o passo é arredondado.
            LayingPattern.EspinhaDePeixe or LayingPattern.EspinhaDePeixe45 => Herringbone(Math.Max(2, Math.Round(a / b)) * b, b),
            LayingPattern.Trama => Basket(a),
            LayingPattern.Hexagonal => Hexagon(a),
            LayingPattern.Opus => Opus(b),
            LayingPattern.Cacos => Crazy(a, 3, 4, Jitter3, split: true),
            LayingPattern.Mosaico => Crazy(a, 4, 4, Jitter4, split: false),
            LayingPattern.Granular => Granular(a),
            LayingPattern.Vazado => Grass(a, b),
            _ => Bond(a, b, 0),
        };

        double angle = type.AngleDeg + type.Pattern switch
        {
            LayingPattern.EspinhaDePeixe45 or LayingPattern.Diagonal => 45,
            _ => 0,
        };
        return Math.Abs(angle % 360) < 1e-9 ? m : Rotate(m, Geo.DegToRad(angle));
    }

    /// <summary>Hachura de modelo do Revit para o tipo de piso (medidas reais em mm).</summary>
    public static HatchPattern Build(PavingType type, string? name = null)
    {
        PavingMotif motif = Motif(type);
        var pattern = new HatchPattern
        {
            Key = "piso-" + type.Id,
            Name = name ?? $"VIR Piso {type.Name} {type.Signature[..6]}",
            Application = "Piso: " + PavingLabels.Of(type.Pattern),
            Target = HatchTarget.Model,
        };
        foreach ((Point2 a, Point2 b) in Edges(motif))
        {
            HatchGrid? grid = Family(a, b, motif.T1, motif.T2);
            if (grid is not null)
            {
                pattern.Grids.Add(grid);
            }
        }

        return pattern;
    }

    /// <summary>
    /// Lados das peças que precisam de uma família própria: um lado já coberto pelas repetições (na malha) de lados
    /// aceitos antes — inclusive quando coberto por pedaços de dois lados, como o topo de uma peça amarrada — é descartado.
    /// </summary>
    public static List<(Point2 A, Point2 B)> Edges(PavingMotif motif)
    {
        var all = new List<(Point2, Point2)>();
        foreach (IReadOnlyList<Point2> tile in motif.Tiles)
        {
            for (int i = 0; i < tile.Count; i++)
            {
                all.Add((tile[i], tile[(i + 1) % tile.Count]));
            }
        }

        all.AddRange(motif.Extra);
        var accepted = new List<(Point2 A, Point2 B)>();
        foreach ((Point2 a, Point2 b) in all)
        {
            if (a.DistanceTo(b) < 0.01)
            {
                continue;
            }

            bool covered = true;
            for (int k = 0; k <= 8 && covered; k++)
            {
                Point2 p = Point2.Lerp(a, b, 0.02 + 0.96 * k / 8);
                covered = accepted.Any(e => OnTranslate(p, e, motif.T1, motif.T2));
            }

            if (!covered)
            {
                // Leva o lado para perto da origem (mesma família, origem mais estável).
                Point2 mid = (a + b) / 2;
                Point2 shift = Reduce(mid, motif.T1, motif.T2) - mid;
                accepted.Add((a + shift, b + shift));
            }
        }

        return accepted;
    }

    /// <summary>Verdadeiro quando o ponto está sobre alguma translação do lado pela malha.</summary>
    private static bool OnTranslate(Point2 p, (Point2 A, Point2 B) edge, Point2 t1, Point2 t2)
    {
        Point2 mid = (edge.A + edge.B) / 2;
        double det = t1.Cross(t2);
        Point2 rel = p - mid;
        double s = rel.Cross(t2) / det;
        double t = t1.Cross(rel) / det;
        double reach = edge.A.DistanceTo(edge.B) / Math.Min(t1.Length, t2.Length) + 2;
        int r = (int)Math.Ceiling(reach);
        for (int i = (int)Math.Floor(s) - r; i <= (int)Math.Ceiling(s) + r; i++)
        {
            for (int j = (int)Math.Floor(t) - r; j <= (int)Math.Ceiling(t) + r; j++)
            {
                Point2 move = t1 * i + t2 * j;
                if (Geo.DistancePointSegment(p, edge.A + move, edge.B + move) < 1e-3)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Família de linhas que repete o lado (a, b) em toda a malha; nula quando não há vetor paralelo na malha.</summary>
    public static HatchGrid? Family(Point2 a, Point2 b, Point2 t1, Point2 t2)
    {
        double length = a.DistanceTo(b);
        Point2 d = (b - a) / length;
        Point2 n = d.Perp();
        double det = Math.Abs(t1.Cross(t2));
        double scale = Math.Max(t1.Length, t2.Length);

        Point2? u = null;
        for (int r = 1; r <= 24 && u is null; r++)
        {
            for (int i = -r; i <= r; i++)
            {
                for (int j = -r; j <= r; j++)
                {
                    if (Math.Max(Math.Abs(i), Math.Abs(j)) != r)
                    {
                        continue;
                    }

                    Point2 v = t1 * i + t2 * j;
                    if (v.Dot(d) > Eps && Math.Abs(v.Cross(d)) < 1e-6 * scale && (u is null || v.Length < u.Value.Length - Eps))
                    {
                        u = v;
                    }
                }
            }
        }

        if (u is null)
        {
            return null;
        }

        double period = u.Value.Length;
        Point2? best = null;
        for (int i = -24; i <= 24; i++)
        {
            for (int j = -24; j <= 24; j++)
            {
                Point2 v = t1 * i + t2 * j;
                if (v.Dot(n) <= Eps || Math.Abs(Math.Abs(u.Value.Cross(v)) - det) > 1e-6 * det)
                {
                    continue;
                }

                if (best is null || v.Length < best.Value.Length - Eps)
                {
                    best = v;
                }
            }
        }

        if (best is null)
        {
            return null;
        }

        double shift = best.Value.Dot(d) % period;
        if (shift < 0)
        {
            shift += period;
        }

        var grid = new HatchGrid
        {
            AngleDeg = Geo.RadToDeg(Math.Atan2(d.Y, d.X)),
            OriginX = a.X,
            OriginY = a.Y,
            Shift = shift,
            Offset = best.Value.Dot(n),
        };
        if (period - length > 0.01)
        {
            grid.Segments.Add(length);
            grid.Segments.Add(period - length);
        }

        return grid;
    }

    private static Point2 Reduce(Point2 p, Point2 t1, Point2 t2)
    {
        double det = t1.Cross(t2);
        double s = p.Cross(t2) / det;
        double t = t1.Cross(p) / det;
        double fs = s - Math.Floor(s + 1e-9);
        double ft = t - Math.Floor(t + 1e-9);
        return t1 * fs + t2 * ft;
    }

    private static PavingMotif Rotate(PavingMotif m, double radians)
    {
        var r = new PavingMotif { T1 = m.T1.Rotate(radians), T2 = m.T2.Rotate(radians), IsTiling = m.IsTiling };
        foreach (IReadOnlyList<Point2> tile in m.Tiles)
        {
            r.Tiles.Add(tile.Select(p => p.Rotate(radians)).ToList());
        }

        foreach ((Point2 a, Point2 b) in m.Extra)
        {
            r.Extra.Add((a.Rotate(radians), b.Rotate(radians)));
        }

        return r;
    }

    private static List<Point2> Rect(double x0, double y0, double x1, double y1) =>
        new() { new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1) };

    /// <summary>Grade (fração 0) ou amarração: cada fiada deslocada da anterior pela fração do comprimento.</summary>
    private static PavingMotif Bond(double a, double b, double fraction)
    {
        var m = new PavingMotif { T1 = new Point2(a, 0), T2 = new Point2(a * fraction, b) };
        m.Tiles.Add(Rect(0, 0, a, b));
        return m;
    }

    /// <summary>Espinha de peixe 90°: peça deitada e peça em pé encaixadas em degrau.</summary>
    private static PavingMotif Herringbone(double a, double b)
    {
        var m = new PavingMotif { T1 = new Point2(b, b), T2 = new Point2(a, -a) };
        m.Tiles.Add(Rect(0, 0, a, b));
        m.Tiles.Add(Rect(0, b, b, b + a));
        return m;
    }

    /// <summary>Trama: blocos quadrados de duas peças (proporção 2:1), alternando a direção.</summary>
    private static PavingMotif Basket(double a)
    {
        double h = a / 2;
        var m = new PavingMotif { T1 = new Point2(a, a), T2 = new Point2(a, -a) };
        m.Tiles.Add(Rect(0, 0, a, h));
        m.Tiles.Add(Rect(0, h, a, a));
        m.Tiles.Add(Rect(a, 0, a + h, a));
        m.Tiles.Add(Rect(a + h, 0, 2 * a, a));
        return m;
    }

    /// <summary>Hexágono com o comprimento informado entre lados opostos.</summary>
    private static PavingMotif Hexagon(double flat)
    {
        double s = flat / Math.Sqrt(3);
        var m = new PavingMotif { T1 = new Point2(flat, 0), T2 = new Point2(flat / 2, 1.5 * s) };
        m.Tiles.Add(Enumerable.Range(0, 6).Select(i => Point2.Polar(s, Geo.DegToRad(30 + 60 * i))).ToList());
        return m;
    }

    /// <summary>Opus: quatro tamanhos (2×2, 2×1, 1×1, 1×2 módulos) num bloco 4×4 com fiadas defasadas.</summary>
    private static PavingMotif Opus(double u)
    {
        var m = new PavingMotif { T1 = new Point2(4 * u, 0), T2 = new Point2(u, 4 * u) };
        (double, double, double, double)[] r =
        {
            (0, 0, 2, 2), (2, 0, 4, 1), (2, 1, 3, 2), (3, 1, 4, 2),
            (0, 2, 1, 4), (1, 2, 3, 3), (1, 3, 3, 4), (3, 2, 4, 4),
        };
        foreach ((double x0, double y0, double x1, double y1) in r)
        {
            m.Tiles.Add(Rect(x0 * u, y0 * u, x1 * u, y1 * u));
        }

        return m;
    }

    private static readonly (int X, int Y)[] Jitter3 = { (0, 0), (1, -1), (-1, 1), (1, 1), (0, -1), (-1, 0), (1, 0), (-1, -1), (0, 1) };

    private static readonly (int X, int Y)[] Jitter4 =
    {
        (0, 0), (1, 0), (0, 1), (-1, 0), (1, 1), (0, -1), (-1, 1), (1, -1),
        (0, 1), (-1, -1), (1, 0), (0, 0), (-1, 0), (1, 1), (0, -1), (-1, 1),
    };

    /// <summary>
    /// Cacos e mosaico: grade de vértices deslocados em posições inteiras (a hachura só repete lados com direção
    /// "inteira" na malha), com parte das células divididas na diagonal para formar polígonos irregulares.
    /// </summary>
    private static PavingMotif Crazy(double size, int n, int step, (int X, int Y)[] jitter, bool split)
    {
        int units = n * step;
        double unit = size * n / units; // célula com n peças por lado
        var m = new PavingMotif { T1 = new Point2(units * unit, 0), T2 = new Point2(0, units * unit) };
        Point2 V(int i, int j)
        {
            int wi = ((i % n) + n) % n, wj = ((j % n) + n) % n;
            (int jx, int jy) = jitter[(wi * n + wj) % jitter.Length];
            int ox = (i - wi) / n * units, oy = (j - wj) / n * units;
            return new Point2((wi * step + jx + ox) * unit, (wj * step + jy + oy) * unit);
        }

        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                Point2 p00 = V(i, j), p10 = V(i + 1, j), p11 = V(i + 1, j + 1), p01 = V(i, j + 1);
                if (split && (i + 2 * j) % 3 == 0)
                {
                    m.Tiles.Add(new List<Point2> { p00, p10, p11 });
                    m.Tiles.Add(new List<Point2> { p00, p11, p01 });
                }
                else
                {
                    m.Tiles.Add(new List<Point2> { p00, p10, p11, p01 });
                }
            }
        }

        return m;
    }

    /// <summary>Pedrisco e seixo: pequenos traços espalhados, sem desenho de peças.</summary>
    private static PavingMotif Granular(double size)
    {
        const int units = 12;
        double unit = size * 1.6;
        var m = new PavingMotif { T1 = new Point2(units * unit, 0), T2 = new Point2(0, units * unit), IsTiling = false };
        (int X, int Y, int Dir)[] dots =
        {
            (1, 2, 0), (4, 1, 1), (7, 3, 2), (10, 1, 0), (2, 6, 1), (5, 5, 0), (8, 7, 1), (11, 6, 2),
            (1, 10, 2), (4, 9, 0), (7, 11, 1), (10, 9, 0), (3, 8, 2), (9, 4, 1),
        };
        double len = size * 0.45;
        foreach ((int x, int y, int dir) in dots)
        {
            var p = new Point2(x * unit, y * unit);
            Point2 d = dir switch { 1 => new Point2(0, 1), 2 => new Point2(1, 1).Normalized(), _ => new Point2(1, 0) };
            m.Extra.Add((p, p + d * len));
        }

        return m;
    }

    /// <summary>Grama-concreto: placas com quatro vazados quadrados.</summary>
    private static PavingMotif Grass(double a, double b)
    {
        var m = new PavingMotif { T1 = new Point2(a, 0), T2 = new Point2(0, b), IsTiling = false };
        m.Tiles.Add(Rect(0, 0, a, b));
        double hx = a * 0.28, hy = b * 0.28;
        foreach ((double cx, double cy) in new[] { (0.25, 0.25), (0.75, 0.25), (0.25, 0.75), (0.75, 0.75) })
        {
            List<Point2> hole = Rect(a * cx - hx / 2, b * cy - hy / 2, a * cx + hx / 2, b * cy + hy / 2);
            for (int i = 0; i < 4; i++)
            {
                m.Extra.Add((hole[i], hole[(i + 1) % 4]));
            }
        }

        return m;
    }
}
