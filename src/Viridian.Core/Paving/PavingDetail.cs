using System.Globalization;
using Viridian.Core.Colors;
using Viridian.Core.Detailing;
using Viridian.Core.Geometry;

namespace Viridian.Core.Paving;

/// <summary>
/// Detalhe em corte de um piso: revestimento com as juntas, camadas com cotas e chamadas, e o solo compactado.
/// Medidas em metros reais (desenhar em 1:10).
/// </summary>
public static class PavingDetail
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    public const double WidthM = 1.0;

    public static Drawing Draw(PavingType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var g = new Drawing { Title = "Detalhe de piso — " + type.Name };
        double w = WidthM;
        double y = 0;
        double t = Math.Max(0.001, type.ThicknessMm / 1000);
        var notes = new List<(Point2, string)>();

        // Revestimento: peças com as juntas (o corte passa ao longo do comprimento da peça).
        DrawSurface(g, type, t);
        notes.Add((new Point2(w * 0.62, -t / 2), $"{PavingLabels.Of(type.Category).ToUpperInvariant()}: {type.Material}, {Size(type)}, e = {Cm(type.ThicknessMm)}"
                                                  + (type.JointMm > 0 ? $", junta {Mm(type.JointMm)}" : string.Empty)));
        g.Dimension(new Point2(0, 0), new Point2(0, -t), 0.08, Cm(type.ThicknessMm));
        y = -t;

        foreach (PavingLayer layer in type.Layers)
        {
            double h = layer.ThicknessMm / 1000;
            if (layer.Kind == PavingLayerKind.Manta || h <= 0)
            {
                g.Line(new Point2(0, y), new Point2(w, y), dashed: true, heavy: true);
                notes.Add((new Point2(w * 0.45, y), layer.Name.ToUpperInvariant()));
                continue;
            }

            Rgb color = PavingLabels.LayerColor(layer.Kind);
            if (layer.Kind == PavingLayerKind.Estrutura && layer.SpacingMm > 0)
            {
                // Barrotes em corte, com o vão entre eles.
                double spacing = layer.SpacingMm / 1000;
                double bw = Math.Min(h, spacing * 0.5);
                for (double x = spacing / 2; x + bw / 2 <= w; x += spacing)
                {
                    List<Point2> beam = Box(x - bw / 2, y - h, x + bw / 2, y);
                    g.Fills.Add(new DrawFill(beam, color));
                    g.Polyline(beam, true);
                }

                notes.Add((new Point2(spacing / 2, y - h / 2), $"{layer.Name.ToUpperInvariant()} A CADA {Cm(layer.SpacingMm)}"));
            }
            else
            {
                List<Point2> box = Box(0, y - h, w, y);
                g.Fills.Add(new DrawFill(box, color));
                g.Polyline(box, true);
                Texture(g, layer.Kind, y - h, y, w);
                notes.Add((new Point2(w * (0.3 + 0.08 * (notes.Count % 3)), y - h / 2), $"{layer.Name.ToUpperInvariant()}, e = {Cm(layer.ThicknessMm)}"));
            }

            g.Dimension(new Point2(0, y), new Point2(0, y - h), 0.08, Cm(layer.ThicknessMm));
            y -= h;
        }

        // Solo compactado.
        g.Line(new Point2(0, y), new Point2(w, y), heavy: true);
        for (double x = 0.02; x < w; x += 0.06)
        {
            g.Line(new Point2(x, y), new Point2(x - 0.03, y - 0.03));
        }

        notes.Add((new Point2(w * 0.8, y - 0.015), "SOLO COMPACTADO"));
        g.Line(new Point2(0, 0), new Point2(0, y));
        g.Line(new Point2(w, 0), new Point2(w, y));

        g.Notes(notes, w + 0.12, Math.Max(0.045, -y / Math.Max(4, notes.Count) * 1.15));
        double top = 0.12;
        g.Text(new Point2(0, top + 0.06), ("Detalhe de piso — " + type.Name).ToUpperInvariant(), -1, 1.4);
        g.Text(new Point2(0, top), $"Paginação: {PavingLabels.Of(type.Pattern)} · espessura total {Cm(type.TotalThicknessMm)}"
                                   + (type.SlopePercent > 0 ? $" · caimento mínimo {type.SlopePercent.ToString("0.#", Pt)}%" : string.Empty), -1, 0.95);
        double bottom = y - 0.12;
        if (!string.IsNullOrWhiteSpace(type.Notes))
        {
            foreach (string line in TextLayout.Wrap(type.Notes, 110))
            {
                g.Text(new Point2(0, bottom), line, -1, 0.9);
                bottom -= 0.035;
            }
        }

        g.Text(new Point2(0, bottom - 0.01), "Medidas de referência: confira com o fornecedor, com as normas aplicáveis e com o responsável técnico.", -1, 0.8);
        return g;
    }

    private static void DrawSurface(Drawing g, PavingType type, double t)
    {
        double w = WidthM;
        Rgb color = type.SurfaceRgb;
        double joint = type.JointMm / 1000;
        bool granular = PavingLabels.IsGranular(type.Pattern);
        if (granular)
        {
            List<Point2> layer = Box(0, -t, w, 0);
            g.Fills.Add(new DrawFill(layer, color.Tint(0.35)));
            g.Polyline(layer, true);
            double r = Math.Max(0.004, type.LengthMm / 2000);
            var random = new DeterministicRandom(7);
            for (double x = r; x < w - r; x += r * 2.3)
            {
                for (double yy = -r; yy > -t + r; yy -= r * 2.1)
                {
                    g.Fills.Add(new DrawFill(Drawing.Ellipse(new Point2(x + random.Range(-r, r) * 0.3, yy), r * 0.9, r * 0.75, 10), color.Shade(0.15)));
                }
            }

            return;
        }

        double piece = type.Pattern == LayingPattern.Reguas ? type.WidthMm / 1000 : type.LengthMm / 1000;
        if (type.CastInPlace || piece <= 0)
        {
            piece = Math.Max(type.LengthMm / 1000, w);
        }

        double x0 = 0;
        while (x0 < w - 1e-6)
        {
            double x1 = Math.Min(w, x0 + piece);
            List<Point2> p = Box(x0, -t, x1, 0);
            g.Fills.Add(new DrawFill(p, color));
            g.Polyline(p, true, heavy: true);
            x0 = x1 + joint;
        }
    }

    /// <summary>Textura simples de cada camada: pontos para areias e britas, traços para argamassas e concretos.</summary>
    private static void Texture(Drawing g, PavingLayerKind kind, double bottom, double top, double w)
    {
        double h = top - bottom;
        if (h < 0.008)
        {
            return;
        }

        var random = new DeterministicRandom((int)kind + 3);
        switch (kind)
        {
            case PavingLayerKind.Base or PavingLayerKind.SubBase:
                for (int i = 0; i < (int)(w * h * 900); i++)
                {
                    var c = new Point2(random.Range(0.01, w - 0.01), random.Range(bottom + 0.006, top - 0.006));
                    double r = random.Range(0.003, 0.006);
                    g.Polyline(Drawing.Ellipse(c, r, r * 0.8, 6), true);
                }

                break;

            case PavingLayerKind.Assentamento:
                for (int i = 0; i < (int)(w * h * 2500); i++)
                {
                    var c = new Point2(random.Range(0.01, w - 0.01), random.Range(bottom + 0.002, top - 0.002));
                    g.Line(c, c + new Point2(0.0015, 0));
                }

                break;

            case PavingLayerKind.Contrapiso or PavingLayerKind.Argamassa:
                for (double x = 0.015; x < w; x += 0.05)
                {
                    double yy = bottom + h * (0.3 + 0.4 * random.NextDouble());
                    g.Line(new Point2(x, yy), new Point2(x + 0.012, yy + Math.Min(0.006, h * 0.2)));
                }

                break;
        }
    }

    private static List<Point2> Box(double x0, double y0, double x1, double y1) =>
        new() { new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1) };

    public static string Cm(double mm) => (mm / 10).ToString(mm % 10 == 0 ? "0" : "0.#", Pt) + " cm";

    public static string Mm(double mm) => mm.ToString("0.#", Pt) + " mm";

    public static string Size(PavingType t) => t.Pattern switch
    {
        LayingPattern.Granular or LayingPattern.Seixos or LayingPattern.Britas => $"grãos de cerca de {Mm(t.LengthMm)}",
        LayingPattern.Lascas => $"lascas de cerca de {Mm(t.LengthMm)}",
        LayingPattern.Cacos or LayingPattern.Mosaico => $"peças de cerca de {Cm(t.LengthMm)}",
        LayingPattern.Hexagonal => $"hexágono de {Cm(t.LengthMm)}",
        _ when t.CastInPlace => $"quadros de {Cm(t.LengthMm)} × {Cm(t.WidthMm)}",
        _ => $"{Cm(t.LengthMm)} × {Cm(t.WidthMm)}",
    };
}
