using Viridian.Core.Colors;
using Viridian.Core.Geometry;
using Viridian.Core.Model3D.Generation;
using Viridian.Core.Model3D.Textures;

namespace Viridian.Core.Paving;

/// <summary>
/// Textura de pedras e coberturas soltas, gerada aqui (sem fotos de terceiros), que se repete sem emenda. Cada
/// material ganha a sua imagem a partir dos próprios dados: a forma das pedras pelo tipo (seixo rolado e arredondado,
/// brita e pedrisco angulosos com faces, pedra de mão em blocos grandes, pó de pedra, areia e saibro em grão fino,
/// argila expandida em bolinhas porosas, cobertura orgânica em lascas de casca), o tamanho pela granulometria, as
/// cores pela cor do material e do grão (com variação de pedra para pedra) e luz vinda do alto à esquerda, com sombra
/// entre as pedras. A imagem cobre <see cref="TileMeters"/> metros no mundo real.
/// </summary>
public static class StoneTexture
{
    /// <summary>Versão do desenho: aumente quando a textura mudar (as imagens gravadas são refeitas).</summary>
    public const int Version = 2;

    public const int DefaultSize = 512;

    /// <summary>Tamanho real da imagem (m): umas 14 pedras médias de lado, entre 12 cm e 1,6 m.</summary>
    public static double TileMeters(LooseMaterial m)
    {
        ArgumentNullException.ThrowIfNull(m);
        return Math.Clamp(Mean(m) * 14 / 1000.0, 0.12, 1.6);
    }

    /// <summary>Assinatura dos dados que mudam a textura (para o nome do arquivo e o cache).</summary>
    public static string Signature(LooseMaterial m)
    {
        ArgumentNullException.ThrowIfNull(m);
        return GeometryHash.Text(string.Join("|", Version, m.Id, m.Kind, m.Origin, m.Rock, m.GrainMinMm, m.GrainMaxMm, m.Color, m.GrainColor))[..10];
    }

    /// <summary>Cor média da textura (para o material nos estilos sem textura).</summary>
    public static Rgb Average(RgbaImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        long r = 0, g = 0, b = 0;
        int n = image.Width * image.Height;
        for (int i = 0; i < image.Pixels.Length; i += 4)
        {
            r += image.Pixels[i];
            g += image.Pixels[i + 1];
            b += image.Pixels[i + 2];
        }

        return new Rgb((byte)(r / n), (byte)(g / n), (byte)(b / n));
    }

    public static RgbaImage Render(LooseMaterial m, int size = DefaultSize)
    {
        ArgumentNullException.ThrowIfNull(m);
        Rgb main = Rgb.TryParse(m.Color, out Rgb c1) ? c1 : new Rgb(0xC9, 0xC2, 0xB4);
        Rgb grain = Rgb.TryParse(m.GrainColor, out Rgb c2) ? c2 : main.Shade(0.35);
        var random = new DeterministicRandom(DeterministicRandom.StableHash("pedra#" + m.Id + "#" + Version));
        var canvas = new Canvas(size);
        double pxPerMm = size / (TileMeters(m) * 1000);

        switch (m.Kind)
        {
            case LooseKind.PoDePedra or LooseKind.Areia or LooseKind.Saibro:
                FineGrain(canvas, main, grain, m.Kind, pxPerMm, random);
                break;
            case LooseKind.CoberturaOrganica:
                canvas.Clear(grain.Shade(0.4));
                Stones(canvas, m, main, grain, pxPerMm, random, Shape.Chip);
                break;
            case LooseKind.ArgilaExpandida:
                canvas.Clear(grain.Shade(0.35));
                Stones(canvas, m, main, grain, pxPerMm, random, Shape.Ball);
                break;
            case LooseKind.Seixo:
                canvas.Clear(grain.Shade(0.3));
                Stones(canvas, m, main, grain, pxPerMm, random, Shape.Pebble);
                break;
            default:
                // Britas, pedriscos, cascalho, pedra triturada e pedra de mão: pedras angulosas com faces.
                canvas.Clear(grain.Shade(0.35));
                Stones(canvas, m, main, grain, pxPerMm, random, Shape.Angular);
                break;
        }

        return canvas.ToImage();
    }

    private enum Shape
    {
        Pebble,
        Angular,
        Ball,
        Chip,
    }

    private static double Mean(LooseMaterial m) => Math.Max(0.5, (Math.Max(0.1, m.GrainMinMm) + Math.Max(m.GrainMinMm, m.GrainMaxMm)) / 2);

    /// <summary>Pedras soltas: lançadas ao acaso até cobrir a imagem, as de depois por cima das de antes.</summary>
    private static void Stones(Canvas canvas, LooseMaterial m, Rgb main, Rgb grain, double pxPerMm, DeterministicRandom random, Shape shape)
    {
        int size = canvas.Size;
        double minR = Math.Max(1.5, Math.Max(0.1, m.GrainMinMm) * pxPerMm / 2);
        double maxR = Math.Max(minR, Math.Max(m.GrainMinMm, m.GrainMaxMm) * pxPerMm / 2);
        bool varied = m.Origin == LooseOrigin.NaturalDeRio || m.Rock.Contains("diversas", StringComparison.OrdinalIgnoreCase);
        bool speckled = m.Rock.Contains("granito", StringComparison.OrdinalIgnoreCase);
        double covered = 0, target = size * size * (shape == Shape.Chip ? 2.6 : shape == Shape.Ball ? 2.0 : 2.4);
        for (int count = 0; covered < target && count < 12000; count++)
        {
            double r = minR + (maxR - minR) * Math.Pow(random.NextDouble(), 1.3);
            double aspect = shape switch
            {
                Shape.Chip => 2.2 + 1.8 * random.NextDouble(),
                Shape.Ball => 1 + 0.08 * random.NextDouble(),
                Shape.Pebble => 1.1 + 0.45 * random.NextDouble(),
                _ => 1.05 + 0.5 * random.NextDouble(),
            };
            double angle = random.NextDouble() * Math.PI;
            var center = new Point2(random.NextDouble() * size, random.NextDouble() * size);
            List<Point2> outline = Outline(center, r, aspect, angle, shape, random);

            Rgb color = StoneColor(main, grain, varied, shape, random);
            var stone = new StoneDraw(outline, center, r, aspect, angle, color, shape, speckled, random.NextInt(0, int.MaxValue));
            canvas.DrawWrapped(stone);
            covered += Math.PI * r * r / aspect * (shape == Shape.Chip ? 1.0 : 1.15);
        }
    }

    private static Rgb StoneColor(Rgb main, Rgb grain, bool varied, Shape shape, DeterministicRandom random)
    {
        double t = random.NextDouble();
        Rgb color = varied
            ? (t < 0.33 ? main : t < 0.66 ? main.Mix(grain, 0.45 + 0.3 * random.NextDouble()) : grain.Tint(0.25 + 0.25 * random.NextDouble()))
            : main.Mix(grain, 0.28 * Math.Pow(random.NextDouble(), 1.5));
        if (shape == Shape.Chip)
        {
            color = main.Mix(grain, 0.6 * random.NextDouble());
        }

        double light = (random.NextDouble() - 0.5) * 0.22;
        return light > 0 ? color.Tint(light) : color.Shade(-light);
    }

    private static List<Point2> Outline(Point2 center, double r, double aspect, double angle, Shape shape, DeterministicRandom random)
    {
        int n = shape switch
        {
            Shape.Angular => 5 + random.NextInt(0, 4),
            Shape.Chip => 6 + random.NextInt(0, 3),
            _ => 20,
        };
        double irregular = shape switch
        {
            Shape.Angular => 0.32,
            Shape.Chip => 0.25,
            Shape.Ball => 0.04,
            _ => 0.12,
        };
        double phase = random.NextDouble() * Geo.TwoPi;
        double c = Math.Cos(angle), s = Math.Sin(angle);
        var points = new List<Point2>(n);
        for (int i = 0; i < n; i++)
        {
            double a = phase + (i + (shape == Shape.Angular ? (random.NextDouble() - 0.5) * 0.6 : 0)) * Geo.TwoPi / n;
            double radius = r * (1 - irregular / 2 + irregular * random.NextDouble());
            double x = Math.Cos(a) * radius, y = Math.Sin(a) * radius / aspect;
            points.Add(new Point2(center.X + x * c - y * s, center.Y + x * s + y * c));
        }

        return points;
    }

    /// <summary>Grão fino (pó de pedra, areia, saibro): ruído em duas escalas entre as duas cores, e grãos soltos.</summary>
    private static void FineGrain(Canvas canvas, Rgb main, Rgb grain, LooseKind kind, double pxPerMm, DeterministicRandom random)
    {
        int size = canvas.Size;
        var coarse = new ValueNoise(size, 24, random.NextInt(0, int.MaxValue));
        var fine = new ValueNoise(size, 6, random.NextInt(0, int.MaxValue));
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double v = 0.6 * coarse.At(x, y) + 0.4 * fine.At(x, y) + (random.NextDouble() - 0.5) * 0.35;
                Rgb color = main.Mix(grain, Math.Clamp(0.15 + v * 0.45, 0, 1));
                canvas.Set(x, y, color);
            }
        }

        // Grãos maiores soltos (claros e escuros), do tamanho da granulometria (no mínimo 1 pixel).
        int specks = size * size / (kind == LooseKind.Saibro ? 60 : 40);
        double r = Math.Clamp(pxPerMm * 1.2, 0.8, 3.5);
        for (int i = 0; i < specks; i++)
        {
            double x = random.NextDouble() * size, y = random.NextDouble() * size;
            Rgb color = random.NextDouble() < 0.5 ? grain.Shade(0.3) : main.Tint(0.35);
            canvas.Disc(x, y, r * (0.6 + 0.8 * random.NextDouble()), color, 0.85);
        }
    }

    private sealed record StoneDraw(List<Point2> Outline, Point2 Center, double Radius, double Aspect, double Angle, Rgb Color, Shape Shape, bool Speckled, int Seed);

    /// <summary>Tela em ponto flutuante, com desenho que dá a volta nas bordas (imagem sem emenda).</summary>
    private sealed class Canvas
    {
        private static readonly (double X, double Y, double Z) Light = Normalize(-0.55, -0.62, 0.56);
        private readonly double[] _r, _g, _b;

        public Canvas(int size)
        {
            Size = size;
            _r = new double[size * size];
            _g = new double[size * size];
            _b = new double[size * size];
        }

        public int Size { get; }

        public void Clear(Rgb color)
        {
            Array.Fill(_r, color.R);
            Array.Fill(_g, color.G);
            Array.Fill(_b, color.B);
        }

        public void Set(int x, int y, Rgb color)
        {
            int i = Wrap(y) * Size + Wrap(x);
            _r[i] = color.R;
            _g[i] = color.G;
            _b[i] = color.B;
        }

        private void Blend(int x, int y, double r, double g, double b, double alpha)
        {
            int i = Wrap(y) * Size + Wrap(x);
            _r[i] += (r - _r[i]) * alpha;
            _g[i] += (g - _g[i]) * alpha;
            _b[i] += (b - _b[i]) * alpha;
        }

        private void Darken(int x, int y, double factor)
        {
            int i = Wrap(y) * Size + Wrap(x);
            _r[i] *= factor;
            _g[i] *= factor;
            _b[i] *= factor;
        }

        private int Wrap(int v) => ((v % Size) + Size) % Size;

        public void Disc(double cx, double cy, double r, Rgb color, double alpha)
        {
            for (int y = (int)Math.Floor(cy - r); y <= (int)Math.Ceiling(cy + r); y++)
            {
                for (int x = (int)Math.Floor(cx - r); x <= (int)Math.Ceiling(cx + r); x++)
                {
                    double d = Math.Sqrt((x + 0.5 - cx) * (x + 0.5 - cx) + (y + 0.5 - cy) * (y + 0.5 - cy));
                    if (d <= r)
                    {
                        Blend(x, y, color.R, color.G, color.B, alpha * Math.Clamp(r - d + 0.5, 0, 1));
                    }
                }
            }
        }

        /// <summary>Desenha a pedra com as cópias deslocadas de uma imagem (as que cruzam a borda reaparecem do outro lado).</summary>
        public void DrawWrapped(StoneDraw stone)
        {
            double minX = stone.Outline.Min(p => p.X), maxX = stone.Outline.Max(p => p.X);
            double minY = stone.Outline.Min(p => p.Y), maxY = stone.Outline.Max(p => p.Y);
            double pad = stone.Radius * 0.4 + 2;
            foreach (int ox in new[] { -Size, 0, Size })
            {
                foreach (int oy in new[] { -Size, 0, Size })
                {
                    if (maxX + ox + pad < 0 || minX + ox - pad > Size || maxY + oy + pad < 0 || minY + oy - pad > Size)
                    {
                        continue;
                    }

                    Draw(stone, ox, oy);
                }
            }
        }

        private void Draw(StoneDraw stone, int ox, int oy)
        {
            List<Point2> poly = stone.Outline.Select(p => new Point2(p.X + ox, p.Y + oy)).ToList();
            var center = new Point2(stone.Center.X + ox, stone.Center.Y + oy);
            double r = stone.Radius;

            // Sombra para baixo e à direita (luz do alto à esquerda): escurece o que já está embaixo.
            var shadow = poly.Select(p => new Point2(p.X + r * 0.16 + 0.8, p.Y + r * 0.22 + 0.8)).ToList();
            Fill(shadow, (x, y, _) => Darken(x, y, 0.74), false);

            var random = new DeterministicRandom(stone.Seed);
            int n = poly.Count;
            var facets = Enumerable.Range(0, n).Select(_ => (Tilt: 0.35 + 0.45 * random.NextDouble(), Bias: (random.NextDouble() - 0.5) * 0.5)).ToArray();
            double c = Math.Cos(stone.Angle), s = Math.Sin(stone.Angle);
            Fill(poly, (x, y, edge) =>
            {
                double px = x + 0.5 - center.X, py = y + 0.5 - center.Y;
                double nx, ny, nz;
                if (stone.Shape is Shape.Angular or Shape.Chip)
                {
                    // Faces: cada fatia entre o centro e uma aresta tem a sua inclinação.
                    double ang = Math.Atan2(py, px);
                    int facet = FacetOf(poly, center, ang);
                    Point2 a = poly[facet], b = poly[(facet + 1) % n];
                    double mx = (a.X + b.X) / 2 - center.X, my = (a.Y + b.Y) / 2 - center.Y;
                    double len = Math.Max(1e-6, Math.Sqrt(mx * mx + my * my));
                    double tilt = facets[facet].Tilt;
                    nx = mx / len * tilt;
                    ny = my / len * tilt;
                    nz = 1;
                    double top = 1 - Math.Min(1, Math.Sqrt(px * px + py * py) / Math.Max(1, len) * 0.9);
                    nz += top * 0.6;
                    (nx, ny, nz) = Normalize(nx, ny, nz);
                }
                else
                {
                    // Cúpula (seixo, bolinha): normal a partir da posição na elipse.
                    double u = (px * c + py * s) / r, v = (-px * s + py * c) / (r / stone.Aspect);
                    double d2 = Math.Min(0.98, u * u + v * v);
                    double h = Math.Sqrt(1 - d2);
                    double gx = (u * c - v * s) * 0.9, gy = (u * s + v * c) * 0.9;
                    (nx, ny, nz) = Normalize(gx, gy, h + 0.25);
                }

                double lambert = Math.Max(0, nx * Light.X + ny * Light.Y + nz * Light.Z);
                double shade = 0.62 + 0.55 * lambert;
                if (stone.Shape == Shape.Pebble || stone.Shape == Shape.Ball)
                {
                    // Brilho suave nas pedras roladas.
                    double spec = Math.Pow(Math.Max(0, 2 * lambert * nz - Light.Z), 18) * 0.35;
                    shade += spec;
                }

                double noise = (Hash(x, y, stone.Seed) - 0.5) * 0.1;
                double edgeDark = edge < 1.2 ? 0.8 + 0.16 * edge : 1;
                double k = shade * edgeDark * (1 + noise);
                double rr = stone.Color.R * k, gg = stone.Color.G * k, bb = stone.Color.B * k;
                if (stone.Speckled && Hash(x * 7, y * 3, stone.Seed) < 0.09)
                {
                    double dark = Hash(x, y * 5, stone.Seed) < 0.5 ? 0.35 : 1.45;
                    rr *= dark;
                    gg *= dark;
                    bb *= dark;
                }

                if (stone.Shape == Shape.Ball && Hash(x * 3, y * 7, stone.Seed) < 0.06)
                {
                    rr *= 0.55;
                    gg *= 0.55;
                    bb *= 0.55;
                }

                if (stone.Shape == Shape.Chip)
                {
                    // Fibras da casca ao longo da lasca.
                    double along = (px * -s + py * c);
                    double fiber = Math.Sin(along * 1.9 + Hash((int)(px * c + py * s), 1, stone.Seed) * 3) * 0.08;
                    rr *= 1 + fiber;
                    gg *= 1 + fiber;
                    bb *= 1 + fiber;
                }

                Blend(x, y, Math.Clamp(rr, 0, 255), Math.Clamp(gg, 0, 255), Math.Clamp(bb, 0, 255), 1);
            }, true);
        }

        private static int FacetOf(List<Point2> poly, Point2 center, double angle)
        {
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                double a0 = Math.Atan2(poly[i].Y - center.Y, poly[i].X - center.X);
                double a1 = Math.Atan2(poly[(i + 1) % n].Y - center.Y, poly[(i + 1) % n].X - center.X);
                double span = Norm(a1 - a0), rel = Norm(angle - a0);
                if (rel <= span)
                {
                    return i;
                }
            }

            return 0;
        }

        private static double Norm(double a)
        {
            while (a < 0)
            {
                a += Geo.TwoPi;
            }

            while (a >= Geo.TwoPi)
            {
                a -= Geo.TwoPi;
            }

            return a;
        }

        /// <summary>Percorre os pixels dentro do polígono; a ação recebe a distância (px) até a borda, quando pedida.</summary>
        private void Fill(List<Point2> poly, Action<int, int, double> action, bool withEdge)
        {
            // Só os pixels dentro da imagem: o que passa da borda é desenhado pela cópia deslocada da pedra.
            int y0 = Math.Max(0, (int)Math.Floor(poly.Min(p => p.Y))), y1 = Math.Min(Size - 1, (int)Math.Ceiling(poly.Max(p => p.Y)));
            var xs = new List<double>();
            for (int y = y0; y <= y1; y++)
            {
                double cy = y + 0.5;
                xs.Clear();
                for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                {
                    Point2 a = poly[i], b = poly[j];
                    if ((a.Y > cy) != (b.Y > cy))
                    {
                        xs.Add(a.X + (cy - a.Y) * (b.X - a.X) / (b.Y - a.Y));
                    }
                }

                xs.Sort();
                for (int k = 0; k + 1 < xs.Count; k += 2)
                {
                    for (int x = Math.Max(0, (int)Math.Ceiling(xs[k] - 0.5)); x <= Math.Min(Size - 1, (int)Math.Floor(xs[k + 1] - 0.5)); x++)
                    {
                        action(x, y, withEdge ? EdgeDistance(poly, x + 0.5, cy) : 9);
                    }
                }
            }
        }

        private static double EdgeDistance(List<Point2> poly, double x, double y)
        {
            double best = double.MaxValue;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                Point2 a = poly[j], b = poly[i];
                double dx = b.X - a.X, dy = b.Y - a.Y, len2 = dx * dx + dy * dy;
                double t = len2 < 1e-12 ? 0 : Math.Clamp(((x - a.X) * dx + (y - a.Y) * dy) / len2, 0, 1);
                double ex = a.X + t * dx - x, ey = a.Y + t * dy - y;
                best = Math.Min(best, ex * ex + ey * ey);
            }

            return Math.Sqrt(best);
        }

        public RgbaImage ToImage()
        {
            var image = new RgbaImage(Size, Size);
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    int i = y * Size + x;
                    image.Set(x, y, (byte)Math.Clamp(_r[i], 0, 255), (byte)Math.Clamp(_g[i], 0, 255), (byte)Math.Clamp(_b[i], 0, 255));
                }
            }

            return image;
        }
    }

    private static (double X, double Y, double Z) Normalize(double x, double y, double z)
    {
        double l = Math.Sqrt(x * x + y * y + z * z);
        return l < 1e-12 ? (0, 0, 1) : (x / l, y / l, z / l);
    }

    private static double Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (double)0x1000000;
        }
    }

    /// <summary>Ruído de valor que se repete na borda (para o grão fino).</summary>
    private sealed class ValueNoise
    {
        private readonly int _size;
        private readonly int _cells;
        private readonly double[] _grid;

        public ValueNoise(int size, int cellPx, int seed)
        {
            _size = size;
            _cells = Math.Max(2, size / Math.Max(2, cellPx));
            _grid = new double[_cells * _cells];
            var random = new DeterministicRandom(seed);
            for (int i = 0; i < _grid.Length; i++)
            {
                _grid[i] = random.NextDouble();
            }
        }

        public double At(int x, int y)
        {
            double gx = (double)x / _size * _cells, gy = (double)y / _size * _cells;
            int x0 = (int)Math.Floor(gx), y0 = (int)Math.Floor(gy);
            double fx = gx - x0, fy = gy - y0;
            fx = fx * fx * (3 - 2 * fx);
            fy = fy * fy * (3 - 2 * fy);
            double V(int i, int j) => _grid[((j % _cells + _cells) % _cells) * _cells + ((i % _cells + _cells) % _cells)];
            double top = V(x0, y0) + (V(x0 + 1, y0) - V(x0, y0)) * fx;
            double bottom = V(x0, y0 + 1) + (V(x0 + 1, y0 + 1) - V(x0, y0 + 1)) * fx;
            return top + (bottom - top) * fy;
        }
    }
}
