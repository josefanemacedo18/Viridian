using Viridian.Core.Colors;
using Viridian.Core.Geometry;

namespace Viridian.Core.Detailing;

/// <summary>
/// Identidade visual dos desenhos do Viridian: uma única paleta (verdes de paisagismo, terra e água) usada nos
/// detalhes, cortes, quadros, folha A1 e memorial, para que todas as peças da prancha conversem entre si.
/// </summary>
public static class DrawingStyle
{
    /// <summary>Verde-escuro principal (faixas de título, traço grosso).</summary>
    public static readonly Rgb Primary = new(0x2E, 0x4A, 0x36);

    /// <summary>Verde médio de apoio.</summary>
    public static readonly Rgb Secondary = new(0x5E, 0x82, 0x4E);

    /// <summary>Verde-claro de faixas e cabeçalhos.</summary>
    public static readonly Rgb Tint = new(0xE3, 0xEC, 0xDC);

    /// <summary>Fundo muito claro de quadros (linhas alternadas, painéis).</summary>
    public static readonly Rgb Paper = new(0xF6, 0xF8, 0xF2);

    /// <summary>Destaque quente (terracota) para marcadores e setas de referência.</summary>
    public static readonly Rgb Accent = new(0xC0, 0x6A, 0x3A);

    public static readonly Rgb Ink = new(0x2B, 0x31, 0x2D);

    public static readonly Rgb Muted = new(0x6B, 0x74, 0x6D);

    // Materiais em corte (fundo das hachuras).
    public static readonly Rgb Earth = new(0xC9, 0xB0, 0x8C);
    public static readonly Rgb EarthDark = new(0x8A, 0x6E, 0x4E);
    public static readonly Rgb NaturalSoil = new(0xD9, 0xCC, 0xB4);

    /// <summary>Solo compactado (subleito).</summary>
    public static Rgb Compacted() => new(0xCF, 0xC2, 0xA8);
    public static readonly Rgb Gravel = new(0xB8, 0xB4, 0xAA);
    public static readonly Rgb Sand = new(0xE8, 0xD9, 0xAE);
    public static readonly Rgb Concrete = new(0xC8, 0xC6, 0xC0);
    public static readonly Rgb Mulch = new(0x9A, 0x74, 0x4E);
    public static readonly Rgb RootBall = new(0xA8, 0x8C, 0x6A);
    public static readonly Rgb Water = new(0xBF, 0xDB, 0xEE);
    public static readonly Rgb WaterInk = new(0x3D, 0x7F, 0xB8);
    public static readonly Rgb Bark = new(0x6E, 0x55, 0x42);
    public static readonly Rgb Person = new(0x9A, 0xA3, 0x9C);

    /// <summary>Cor da hachura sobre um fundo: o próprio fundo escurecido.</summary>
    public static Rgb HatchInk(Rgb background) => background.Shade(0.45);
}

/// <summary>
/// Desenhos de apoio em corte e elevação: copa orgânica com sombra, tronco afinado com casca, palmeira, arbusto,
/// tufos de capim e a figura humana de escala. Medidas em metros reais.
/// </summary>
public static class Sketch
{
    /// <summary>
    /// Contorno orgânico de copa (lóbulos irregulares) em volta de uma elipse. A mesma semente dá sempre o mesmo
    /// contorno, para o desenho não mudar a cada geração.
    /// </summary>
    public static List<Point2> CrownOutline(Point2 center, double rx, double ry, int seed, double lobes = 11, double depth = 0.09)
    {
        var rng = new DeterministicRandom(seed);
        int n = Math.Max(48, (int)(lobes * 8));
        double phase = rng.Range(0, Math.PI * 2);
        var amps = Enumerable.Range(0, 3).Select(_ => rng.Range(0.4, 1.0)).ToArray();
        var points = new List<Point2>(n);
        for (int i = 0; i < n; i++)
        {
            double t = 2 * Math.PI * i / n;
            double bump = Math.Abs(Math.Sin(t * lobes / 2 + phase)) * amps[0]
                          + 0.35 * Math.Abs(Math.Sin(t * lobes + phase * 1.7)) * amps[1];
            double k = 1 - depth + depth * bump * 1.1 + rng.Range(-0.012, 0.012);
            // Base da copa mais achatada (as folhas pendem menos embaixo).
            double flatten = Math.Sin(t) < 0 ? 0.92 : 1.0;
            points.Add(new Point2(center.X + rx * k * Math.Cos(t), center.Y + ry * k * flatten * Math.Sin(t)));
        }

        // Os lóbulos não podem passar da copa real: a largura e a altura desenhadas são as da espécie.
        double maxX = points.Max(p => Math.Abs(p.X - center.X)), maxY = points.Max(p => Math.Abs(p.Y - center.Y));
        double sx = maxX > 1e-9 ? rx / maxX : 1, sy = maxY > 1e-9 ? ry / maxY : 1;
        return points.Select(p => new Point2(center.X + (p.X - center.X) * sx, center.Y + (p.Y - center.Y) * sy)).ToList();
    }

    /// <summary>
    /// Copa em corte: contorno orgânico na cor da folhagem, sombra na metade de baixo (do lado oposto ao sol) e
    /// pequenos tufos de folhas no contorno, em traço de textura.
    /// </summary>
    public static void Crown(Drawing d, Point2 center, double rx, double ry, Rgb foliage, int seed, Rgb? flower = null)
    {
        List<Point2> outline = CrownOutline(center, rx, ry, seed);
        d.Fill(outline, foliage);

        // Sombra: a mesma forma, menor e deslocada para baixo e à direita, um tom mais escura.
        // (forma curva, contida na copa: os traços do Revit ficam sempre por cima das áreas, então nada de recortes retos).
        List<Point2> shade = CrownOutline(center + new Point2(rx * 0.12, -ry * 0.24), rx * 0.7, ry * 0.6, seed + 7, 9, 0.07);
        d.Fill(shade, foliage.Shade(0.16));

        // Luz no alto à esquerda.
        List<Point2> light = CrownOutline(center + new Point2(-rx * 0.3, ry * 0.36), rx * 0.34, ry * 0.26, seed + 11, 9, 0.14);
        d.Fill(light, foliage.Tint(0.1));

        if (flower is Rgb f)
        {
            var rng = new DeterministicRandom(seed + 23);
            int count = (int)Math.Clamp(rx * ry * 10, 14, 40);
            for (int i = 0; i < count; i++)
            {
                double t = rng.Range(0, Math.PI * 2), r = Math.Sqrt(rng.Range(0.05, 0.85));
                var p = new Point2(center.X + rx * r * Math.Cos(t), center.Y + ry * r * Math.Sin(t));
                double s = Math.Min(rx, ry) * rng.Range(0.03, 0.05);
                d.Fill(Drawing.Ellipse(p, s, s * 0.8, 10), f);
            }
        }

        d.Strokes(outline, true, LinePen.Media);

        // Tufos de folhas: pequenos arcos de textura dentro da copa.
        var leaves = new DeterministicRandom(seed + 31);
        int tufts = (int)Math.Clamp(rx * ry * 5, 6, 40);
        for (int i = 0; i < tufts; i++)
        {
            double t = leaves.Range(0, Math.PI * 2), r = Math.Sqrt(leaves.Range(0.1, 0.8));
            var p = new Point2(center.X + rx * r * Math.Cos(t), center.Y + ry * r * Math.Sin(t));
            double s = Math.Min(rx, ry) * 0.1;
            d.Stroke(p, p + new Point2(s, s * 0.55), LinePen.Textura);
            d.Stroke(p + new Point2(s * 0.5, -s * 0.15), p + new Point2(s * 1.4, s * 0.25), LinePen.Textura);
        }
    }

    /// <summary>Tronco afinado (mais largo na base), com a casca em traços de textura.</summary>
    public static void Trunk(Drawing d, Point2 baseCenter, double height, double baseWidth, int seed, Rgb? color = null)
    {
        double top = baseWidth * 0.55;
        var outline = new List<Point2>
        {
            baseCenter + new Point2(-baseWidth / 2, 0),
            baseCenter + new Point2(baseWidth / 2, 0),
            baseCenter + new Point2(top / 2, height),
            baseCenter + new Point2(-top / 2, height),
        };
        d.Fill(outline, color ?? DrawingStyle.Bark);
        d.Strokes(outline, true, LinePen.Media);
        var rng = new DeterministicRandom(seed + 5);
        int marks = (int)Math.Clamp(height / 0.18, 3, 30);
        for (int i = 0; i < marks; i++)
        {
            double y = rng.Range(0.05, 0.95) * height;
            double w = baseWidth + (top - baseWidth) * (y / height);
            double x = rng.Range(-0.3, 0.3) * w;
            d.Stroke(baseCenter + new Point2(x, y), baseCenter + new Point2(x + rng.Range(-0.02, 0.02), y + Math.Min(0.12, height * 0.08)), LinePen.Textura);
        }
    }

    /// <summary>Palmeira em elevação: estipe com anéis e coroa de folhas arqueadas.</summary>
    public static void Palm(Drawing d, Point2 baseCenter, double height, double crown, Rgb foliage, int seed)
    {
        double stem = Math.Clamp(height * 0.035, 0.12, 0.45);
        Trunk(d, baseCenter, height, stem, seed, new Rgb(0x8C, 0x7F, 0x6C));
        for (double y = 0.35; y < height - 0.2; y += 0.35)
        {
            double w = stem * (1 - 0.45 * y / height);
            d.Stroke(baseCenter + new Point2(-w / 2, y), baseCenter + new Point2(w / 2, y + 0.04), LinePen.Textura);
        }

        Point2 top = baseCenter + new Point2(0, height);
        var rng = new DeterministicRandom(seed);
        int fronds = 11;
        for (int i = 0; i < fronds; i++)
        {
            double a = Math.PI * (0.06 + 0.88 * i / (fronds - 1)) + rng.Range(-0.06, 0.06);
            double len = crown / 2 * rng.Range(0.85, 1.05);
            var spine = new List<Point2>();
            for (int k = 0; k <= 10; k++)
            {
                double t = k / 10.0;
                // Folhas quase em pé caem menos: com a queda cheia, a nervura voltaria sobre si mesma e a lâmina
                // cruzaria o próprio contorno (o Revit recusa a região).
                double droop = t * t * len * (0.15 + 0.4 * Math.Abs(Math.Cos(a)));
                spine.Add(top + new Point2(Math.Cos(a) * len * t, Math.Sin(a) * len * t * 0.75 - droop));
            }

            // Folha em faixa estreita (lâmina) com a nervura no meio.
            var blade = new List<Point2>();
            for (int k = 0; k <= 10; k++)
            {
                double w = len * 0.07 * Math.Sin(Math.PI * k / 10.0);
                Point2 dir = k < 10 ? (spine[k + 1] - spine[k]).Normalized() : (spine[k] - spine[k - 1]).Normalized();
                blade.Add(spine[k] + dir.Perp() * w);
            }

            for (int k = 9; k >= 1; k--) // pontas (largura zero) já estão no lado de cima
            {
                double w = len * 0.07 * Math.Sin(Math.PI * k / 10.0);
                Point2 dir = k < 10 ? (spine[k + 1] - spine[k]).Normalized() : (spine[k] - spine[k - 1]).Normalized();
                blade.Add(spine[k] - dir.Perp() * w);
            }

            d.Fill(blade, i % 2 == 0 ? foliage : foliage.Shade(0.15));
            d.Strokes(spine, false, LinePen.Fina);
        }
    }

    /// <summary>Arbusto em elevação: monte arredondado de copa.</summary>
    public static void Shrub(Drawing d, Point2 baseCenter, double height, double width, Rgb foliage, int seed, Rgb? flower = null)
    {
        Crown(d, baseCenter + new Point2(0, height * 0.52), width / 2, height * 0.52, foliage, seed, flower);
    }

    /// <summary>Tufo de capim ou forração (folhas finas em leque).</summary>
    public static void Tuft(Drawing d, Point2 baseCenter, double height, double width, int seed, LinePen pen = LinePen.Fina)
    {
        var rng = new DeterministicRandom(seed);
        int blades = 7;
        for (int i = 0; i < blades; i++)
        {
            double t = (i - (blades - 1) / 2.0) / ((blades - 1) / 2.0);
            Point2 tip = baseCenter + new Point2(t * width / 2 + rng.Range(-0.02, 0.02), height * (1 - 0.3 * t * t) * rng.Range(0.8, 1.0));
            Point2 mid = baseCenter + new Point2(t * width * 0.18, height * 0.5);
            d.Stroke(baseCenter, mid, pen);
            d.Stroke(mid, tip, pen);
        }
    }

    /// <summary>Figura humana de escala (1,70 m), em silhueta cinza.</summary>
    public static void Person(Drawing d, Point2 feet, double height = 1.70)
    {
        double k = height / 1.70;
        Point2 P(double x, double y) => feet + new Point2(x * k, y * k);
        d.Fill(Drawing.Ellipse(P(0, 1.58), 0.11 * k, 0.12 * k, 20), DrawingStyle.Person);
        var body = new List<Point2>
        {
            P(-0.19, 1.43), P(0.19, 1.43), P(0.23, 0.86), P(0.15, 0.86), P(0.13, 0.0), P(0.03, 0.0), P(0.0, 0.78),
            P(-0.03, 0.0), P(-0.13, 0.0), P(-0.15, 0.86), P(-0.23, 0.86),
        };
        d.Fill(body, DrawingStyle.Person);
    }

    /// <summary>Faixa de terreno em corte: linha grossa e o solo com hachura de solo natural abaixo.</summary>
    public static void Ground(Drawing d, double x0, double x1, double y, double depth)
    {
        d.Box(x0, y - depth, x1, y, DrawingStyle.NaturalSoil, Hatch.MaterialHatches.NaturalSoil, DrawingStyle.HatchInk(DrawingStyle.NaturalSoil));
        d.Stroke(new Point2(x0, y), new Point2(x1, y), LinePen.Grossa);
    }

    /// <summary>Recorta um contorno abaixo de uma cota (para a sombra ficar na parte de baixo da copa).</summary>
    private static List<Point2> Clip(List<Point2> polygon, double maxY)
    {
        var result = new List<Point2>();
        for (int i = 0; i < polygon.Count; i++)
        {
            Point2 a = polygon[i], b = polygon[(i + 1) % polygon.Count];
            bool ina = a.Y <= maxY, inb = b.Y <= maxY;
            if (ina)
            {
                result.Add(a);
            }

            if (ina != inb)
            {
                double t = (maxY - a.Y) / (b.Y - a.Y);
                result.Add(new Point2(a.X + (b.X - a.X) * t, maxY));
            }
        }

        return result;
    }
}
