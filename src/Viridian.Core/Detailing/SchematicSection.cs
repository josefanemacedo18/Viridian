using System.Globalization;
using Viridian.Core.Colors;
using Viridian.Core.Geometry;
using Viridian.Core.Model;

namespace Viridian.Core.Detailing;

/// <summary>Exemplar considerado no corte: posição em planta e dimensões no estágio desejado.</summary>
public sealed record SectionPlant(Species Species, string Code, Point2 Location, double HeightM, double CrownDiameterM);

/// <summary>Maciço considerado no corte.</summary>
public sealed record SectionMassing(Species Species, string Code, Polygon2 Boundary);

/// <summary>
/// Corte esquemático da vegetação: os exemplares cuja copa é atravessada pela linha de corte são projetados no
/// plano vertical com a silhueta da sua forma de copa, em altura e largura reais. Os maciços cortados aparecem
/// como faixas na altura da espécie. Representação 2D, sem modelo 3D.
/// </summary>
public static class SchematicSection
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    /// <param name="a">Início da linha de corte (em planta).</param>
    /// <param name="b">Fim da linha de corte.</param>
    /// <param name="depthM">Profundidade de vista além da linha: plantas até essa distância também aparecem (atrás, em tom claro).</param>
    /// <param name="scale">Escala de apresentação (1:scale), para o tamanho dos rótulos; zero = estimada pelo comprimento.</param>
    public static Drawing Build(Point2 a, Point2 b, IReadOnlyList<SectionPlant> plants, IReadOnlyList<SectionMassing> massings, ColorSettings colors, double depthM = 0, int scale = 0)
    {
        double length = a.DistanceTo(b);
        var drawing = new Drawing { Title = "Corte esquemático da vegetação" };
        if (length < 0.1)
        {
            return drawing;
        }

        // 1 mm do papel em metros: rótulos e réguas com tamanho constante na prancha.
        double mm = (scale > 0 ? scale : Math.Clamp(length * 1000 / 500, 20, 500)) / 1000.0;
        Point2 dir = (b - a) / length;
        Point2 normal = dir.Perp();

        // Terreno em corte (com a figura humana de escala à esquerda).
        double left = -2.2;
        Sketch.Ground(drawing, left, length + 0.5, 0, Math.Max(0.35, 6 * mm));
        Sketch.Person(drawing, new Point2(-1.2, 0));

        // Exemplares: os mais afastados primeiro, para os da frente ficarem por cima.
        var placed = new List<(SectionPlant Plant, double U, double Distance)>();
        foreach (SectionPlant p in plants)
        {
            Point2 v = p.Location - a;
            double u = v.Dot(dir);
            double d = Math.Abs(v.Dot(normal));
            double radius = p.CrownDiameterM / 2;
            if (u < -radius || u > length + radius || d > radius + Math.Max(0, depthM))
            {
                continue;
            }

            placed.Add((p, u, d));
        }

        foreach ((SectionPlant p, double u, double distance) in placed.Where(x => x.Distance > x.Plant.CrownDiameterM / 2).OrderByDescending(x => x.Distance))
        {
            Silhouette(drawing, p, u, 0, colors, behind: true);
        }

        var labels = new List<(double U, string Code, Species Species)>();

        // Maciços cortados: touceiras recortadas na altura da espécie, por cima das plantas de trás.
        foreach (SectionMassing m in massings)
        {
            double h = Math.Clamp(m.Species.AdultHeightM > 0 ? m.Species.AdultHeightM : 0.3, 0.05, 3.0);
            Rgb color = VegetationPalette.Foliage(m.Species, colors);
            Rgb? flower = VegetationPalette.Flower(m.Species, colors);
            foreach ((double from, double to) in Spans(m.Boundary, a, b))
            {
                double step = Math.Clamp(h * 0.9, 0.15, 1.2);
                int i = 0;
                for (double x = from + step / 2; x < to; x += step * 0.8, i++)
                {
                    double w = Math.Min(step, (to - from));
                    Sketch.Shrub(drawing, new Point2(Math.Min(x, to - w / 2), 0), h * (0.85 + 0.15 * ((i * 7) % 3) / 2.0), w, i % 2 == 0 ? color : color.Shade(0.08), m.Species.EffectiveSymbolSeed + i, flower);
                }

                labels.Add(((from + to) / 2, m.Code, m.Species));
            }
        }

        foreach ((SectionPlant p, double u, double distance) in placed.Where(x => x.Distance <= x.Plant.CrownDiameterM / 2).OrderByDescending(x => x.Distance))
        {
            Silhouette(drawing, p, u, distance, colors, behind: false);
        }

        // Régua de alturas.
        double maxHeight = placed.Count == 0 ? 3 : Math.Max(2, placed.Max(x => x.Plant.HeightM));
        double rule = maxHeight > 15 ? 5 : maxHeight > 6 ? 2 : 1;
        double rx = left - 0.4;
        double top = Math.Ceiling(maxHeight / rule) * rule;
        drawing.Stroke(new Point2(rx, 0), new Point2(rx, top), LinePen.Cota);
        for (double y = 0; y <= top + 1e-9; y += rule)
        {
            drawing.Stroke(new Point2(rx - 1.5 * mm, y), new Point2(rx + 1.5 * mm, y), LinePen.Media);
            drawing.Text(new Point2(rx - 2.5 * mm, y), y.ToString("0", Pt) + " m", TextStyle.Nota, 1, 0.85);
            if (y > 0)
            {
                drawing.Stroke(new Point2(rx + 2 * mm, y), new Point2(length + 0.5, y), LinePen.Textura, dashed: true);
            }
        }

        labels.AddRange(placed.Where(x => x.Distance <= x.Plant.CrownDiameterM / 2).Select(x => (x.U, x.Plant.Code, x.Plant.Species)));

        // Rótulos em duas linhas alternadas quando ficariam encostados (menos de 22 mm no papel).
        double last = double.NegativeInfinity;
        int row = 0;
        foreach ((double u, string code, Species species) in labels.OrderBy(l => l.U))
        {
            row = u - last < 22 * mm ? 1 - row : 0;
            Label(drawing, u, mm, code, species, row);
            last = u;
        }

        drawing.Text(new Point2(left, -Math.Max(0.35, 6 * mm) - 19 * mm), "Alturas e copas na fase indicada; silhuetas esquemáticas. Figura humana de 1,70 m como referência de escala.", TextStyle.Nota, -1, 0.85);
        return drawing;
    }

    /// <summary>Rótulo abaixo do terreno: código em destaque e o nome popular.</summary>
    private static void Label(Drawing d, double u, double mm, string code, Species species, int row = 0)
    {
        double y = -Math.Max(0.35, 6 * mm) - 3 * mm - row * 8 * mm;
        d.Text(new Point2(u, y), code, TextStyle.Subtitulo, 0, 0.95);
        d.Text(new Point2(u, y - 3.4 * mm), species.PopularName, TextStyle.Nota, 0, 0.8);
        d.Stroke(new Point2(u, 0), new Point2(u, -Math.Max(0.35, 6 * mm) + 0.5 * mm), LinePen.Textura, dashed: true);
    }

    /// <summary>Silhueta conforme a categoria e a forma da copa, com a largura do corte à distância informada do eixo.</summary>
    private static void Silhouette(Drawing g, SectionPlant p, double u, double offset, ColorSettings colors, bool behind)
    {
        double height = Math.Max(0.1, p.HeightM);
        double radius = Math.Max(0.05, p.CrownDiameterM / 2);
        double half = Math.Sqrt(Math.Max(0, radius * radius - offset * offset));
        half = Math.Max(half, radius * 0.25);
        Species s = p.Species;
        int seed = s.EffectiveSymbolSeed + (int)(u * 100);
        Rgb color = VegetationPalette.Foliage(s, colors);
        Rgb? flower = VegetationPalette.Flower(s, colors);
        if (behind)
        {
            // Planta atrás do plano de corte: silhueta lisa, clara e sem detalhes.
            color = color.Tint(0.5);
            flower = null;
        }

        switch (s.Category)
        {
            case PlantCategory.Palmeira:
            {
                if (behind)
                {
                    double top = height * 0.86;
                    g.Fill(new List<Point2> { new(u - 0.08, 0), new(u + 0.08, 0), new(u + 0.06, top), new(u - 0.06, top) }, color);
                    g.Fill(Drawing.Ellipse(new Point2(u, top), half, (height - top) * 1.2 + 0.3, 30), color);
                    return;
                }

                Sketch.Palm(g, new Point2(u, 0), height * 0.86, Math.Max(half * 2, height * 0.35), color, seed);
                return;
            }

            case PlantCategory.Arvore:
            {
                double crownBase = height * (s.CrownForm == CrownForm.Colunar ? 0.15 : 0.4);
                double trunk = Math.Clamp(height * 0.025, 0.08, 0.6);
                double cy = crownBase + (height - crownBase) / 2;
                double ry = (height - crownBase) / 2;
                if (behind)
                {
                    g.Fill(new List<Point2> { new(u - trunk / 2, 0), new(u + trunk / 2, 0), new(u + trunk * 0.3, cy), new(u - trunk * 0.3, cy) }, color);
                    g.Fill(CrownShape(s.CrownForm, u, crownBase, height, half), color);
                    return;
                }

                Sketch.Trunk(g, new Point2(u, 0), crownBase + ry * 0.2, trunk, seed);
                switch (s.CrownForm)
                {
                    case CrownForm.Piramidal:
                    case CrownForm.Colunar:
                    case CrownForm.Espalhada:
                    {
                        List<Point2> crown = CrownShape(s.CrownForm, u, crownBase, height, half);
                        g.Fill(crown, color);
                        g.Fill(CrownShape(s.CrownForm, u + half * 0.12, crownBase, crownBase + (height - crownBase) * 0.55, half * 0.75), color.Shade(0.18));
                        g.Strokes(crown, true, LinePen.Media);
                        break;
                    }

                    default:
                        Sketch.Crown(g, new Point2(u, cy), half, ry, color, seed, flower);
                        break;
                }

                return;
            }

            default:
            {
                // Arbustos, herbáceas e forrações: touceira a partir do solo.
                if (behind)
                {
                    g.Fill(Drawing.Ellipse(new Point2(u, height * 0.5), half, height * 0.5, 30), color);
                    return;
                }

                Sketch.Shrub(g, new Point2(u, 0), height, half * 2, color, seed, flower);
                return;
            }
        }
    }

    private static List<Point2> CrownShape(CrownForm form, double u, double bottom, double top, double half)
    {
        double h = top - bottom;
        double cy = bottom + h / 2;
        switch (form)
        {
            case CrownForm.Piramidal:
                return new List<Point2> { new(u - half, bottom), new(u + half, bottom), new(u, top) };
            case CrownForm.Colunar:
            {
                var p = Drawing.Ellipse(new Point2(u, cy), Math.Min(half, h * 0.2), h / 2);
                return p;
            }

            case CrownForm.Espalhada:
            {
                // Umbelífera: base estreita e topo largo e achatado.
                var points = new List<Point2>();
                for (int i = 0; i <= 24; i++)
                {
                    double t = Math.PI * i / 24;
                    points.Add(new Point2(u + half * Math.Cos(t), bottom + h * 0.45 + h * 0.55 * Math.Sin(t)));
                }

                points.Add(new Point2(u - half * 0.3, bottom));
                points.Add(new Point2(u + half * 0.3, bottom));
                return points;
            }

            case CrownForm.Irregular:
            {
                var points = new List<Point2>();
                for (int i = 0; i < 40; i++)
                {
                    double t = 2 * Math.PI * i / 40;
                    double k = 1 + 0.08 * Math.Sin(5 * t) + 0.05 * Math.Sin(3 * t + 1);
                    points.Add(new Point2(u + half * k * Math.Cos(t), cy + h / 2 * k * Math.Sin(t)));
                }

                return points;
            }

            default:
                return Drawing.Ellipse(new Point2(u, cy), half, h / 2);
        }
    }

    /// <summary>Trechos (distância ao longo da linha) em que o segmento AB atravessa o polígono.</summary>
    public static IReadOnlyList<(double From, double To)> Spans(Polygon2 polygon, Point2 a, Point2 b)
    {
        double length = a.DistanceTo(b);
        var cuts = new List<double> { 0, length };
        foreach (IReadOnlyList<Point2> ring in polygon.Rings)
        {
            for (int i = 0; i < ring.Count; i++)
            {
                Point2 p = ring[i];
                Point2 q = ring[(i + 1) % ring.Count];
                if (Intersect(a, b, p, q, out double t))
                {
                    cuts.Add(t * length);
                }
            }
        }

        cuts.Sort();
        var spans = new List<(double, double)>();
        Point2 dir = (b - a) / length;
        for (int i = 1; i < cuts.Count; i++)
        {
            double from = cuts[i - 1];
            double to = cuts[i];
            if (to - from < 1e-6)
            {
                continue;
            }

            if (polygon.Contains(a + dir * ((from + to) / 2)))
            {
                if (spans.Count > 0 && Math.Abs(spans[^1].Item2 - from) < 1e-6)
                {
                    spans[^1] = (spans[^1].Item1, to);
                }
                else
                {
                    spans.Add((from, to));
                }
            }
        }

        return spans;
    }

    private static bool Intersect(Point2 a, Point2 b, Point2 p, Point2 q, out double t)
    {
        t = 0;
        Point2 r = b - a;
        Point2 s = q - p;
        double denominator = r.Cross(s);
        if (Math.Abs(denominator) < 1e-12)
        {
            return false;
        }

        t = (p - a).Cross(s) / denominator;
        double w = (p - a).Cross(r) / denominator;
        return t >= 0 && t <= 1 && w >= 0 && w <= 1;
    }
}
