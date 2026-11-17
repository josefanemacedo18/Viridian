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
    /// <param name="depthM">Profundidade de vista além da linha: plantas até essa distância também aparecem (atrás, em traço fino).</param>
    public static Drawing Build(Point2 a, Point2 b, IReadOnlyList<SectionPlant> plants, IReadOnlyList<SectionMassing> massings, ColorSettings colors, double depthM = 0)
    {
        double length = a.DistanceTo(b);
        var drawing = new Drawing { Title = "Corte esquemático da vegetação" };
        if (length < 0.1)
        {
            return drawing;
        }

        Point2 dir = (b - a) / length;
        Point2 normal = dir.Perp();

        // Linha do terreno.
        drawing.Line(new Point2(0, 0), new Point2(length, 0), heavy: true);
        for (double x = 0.1; x < length; x += 0.3)
        {
            drawing.Line(new Point2(x, -0.02), new Point2(x - 0.12, -0.14));
        }

        // Maciços: faixas onde a linha de corte atravessa o polígono.
        foreach (SectionMassing m in massings)
        {
            double h = Math.Clamp(m.Species.AdultHeightM > 0 ? m.Species.AdultHeightM : 0.3, 0.05, 3.0);
            Rgb color = VegetationPalette.CrownFill(m.Species, colors);
            foreach ((double from, double to) in Spans(m.Boundary, a, b))
            {
                var band = new List<Point2> { new(from, 0), new(to, 0), new(to, h * 0.8), new((from + to) / 2, h), new(from, h * 0.8) };
                drawing.Fills.Add(new DrawFill(band, color));
                drawing.Polyline(band, true);
                drawing.Text(new Point2((from + to) / 2, h + 0.25), m.Code, 0, 0.9);
            }
        }

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

        foreach ((SectionPlant p, double u, double distance) in placed.OrderByDescending(x => x.Distance))
        {
            bool cut = distance <= p.CrownDiameterM / 2;
            Silhouette(drawing, p, u, cut ? distance : 0, colors, behind: !cut);
        }

        // Escala de alturas e nomes.
        double maxHeight = placed.Count == 0 ? 3 : placed.Max(x => x.Plant.HeightM);
        double step = maxHeight > 15 ? 5 : maxHeight > 6 ? 2 : 1;
        for (double y = step; y <= maxHeight + 1e-9; y += step)
        {
            drawing.Line(new Point2(-0.6, y), new Point2(-0.3, y));
            drawing.Text(new Point2(-0.7, y), y.ToString("0", Pt) + " m", 1, 0.85);
        }

        drawing.Line(new Point2(-0.45, 0), new Point2(-0.45, Math.Ceiling(maxHeight / step) * step));
        foreach ((SectionPlant p, double u, double distance) in placed.Where(x => x.Distance <= x.Plant.CrownDiameterM / 2).OrderBy(x => x.U))
        {
            drawing.Text(new Point2(u, -0.45), p.Code, 0, 0.9);
            drawing.Text(new Point2(u, -0.85), p.Species.PopularName, 0, 0.75);
        }

        drawing.Text(new Point2(length / 2, -1.35), "Alturas e copas na fase indicada; silhuetas esquemáticas", 0, 0.75);
        return drawing;
    }

    /// <summary>Silhueta conforme a categoria e a forma da copa, com a largura do corte à distância informada do eixo.</summary>
    private static void Silhouette(Drawing g, SectionPlant p, double u, double offset, ColorSettings colors, bool behind)
    {
        double height = Math.Max(0.1, p.HeightM);
        double radius = Math.Max(0.05, p.CrownDiameterM / 2);
        double half = Math.Sqrt(Math.Max(0, radius * radius - offset * offset));
        half = Math.Max(half, radius * 0.25);
        Rgb color = VegetationPalette.CrownFill(p.Species, colors);
        if (behind)
        {
            color = color.Tint(0.45);
        }

        Species s = p.Species;
        switch (s.Category)
        {
            case PlantCategory.Palmeira:
            {
                // Estipe levemente afinada e coroa de folhas arqueadas.
                double top = height * 0.8;
                double stipe = Math.Clamp(height * 0.018, 0.08, 0.3);
                g.Line(new Point2(u - stipe, 0), new Point2(u - stipe * 0.7, top), heavy: !behind);
                g.Line(new Point2(u + stipe, 0), new Point2(u + stipe * 0.7, top), heavy: !behind);
                double length = Math.Max(half, (height - top) * 1.6);
                Point2 crown = new(u, top);
                for (int i = 0; i < 11; i++)
                {
                    double angle = Math.PI * (0.08 + 0.84 * i / 10.0);
                    var frond = new List<Point2>();
                    for (int k = 0; k <= 10; k++)
                    {
                        double t = k / 10.0;
                        double reach = length * t;
                        frond.Add(crown + new Point2(Math.Cos(angle) * reach * (half / length), Math.Sin(angle) * reach * 0.55 - length * 0.35 * t * t));
                    }

                    g.Polyline(frond, false, heavy: !behind);
                }

                return;
            }

            case PlantCategory.Arvore:
            {
                double crownBase = height * (s.CrownForm == CrownForm.Colunar ? 0.15 : 0.4);
                double trunk = Math.Clamp(height * 0.012, 0.05, 0.3);
                g.Line(new Point2(u - trunk, 0), new Point2(u - trunk * 0.6, crownBase), heavy: !behind);
                g.Line(new Point2(u + trunk, 0), new Point2(u + trunk * 0.6, crownBase), heavy: !behind);
                List<Point2> crown = CrownShape(s.CrownForm, u, crownBase, height, half);
                g.Fills.Add(new DrawFill(crown, color));
                g.Polyline(crown, true, heavy: !behind);
                return;
            }

            default:
            {
                // Arbustos, herbáceas e forrações: touceira em meia-elipse a partir do solo.
                var mound = new List<Point2>();
                for (int k = 0; k <= 24; k++)
                {
                    double t = Math.PI * k / 24;
                    mound.Add(new Point2(u + half * Math.Cos(t), height * Math.Sin(t)));
                }

                g.Fills.Add(new DrawFill(mound, color));
                g.Polyline(mound, true, heavy: !behind);
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
