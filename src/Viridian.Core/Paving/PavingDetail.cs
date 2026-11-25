using System.Globalization;
using Viridian.Core.Colors;
using Viridian.Core.Detailing;
using Viridian.Core.Geometry;
using Viridian.Core.Hatch;

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
                string? pattern = layer.Kind switch
                {
                    PavingLayerKind.Base or PavingLayerKind.SubBase => MaterialHatches.Gravel,
                    PavingLayerKind.Assentamento => MaterialHatches.Sand,
                    PavingLayerKind.Contrapiso or PavingLayerKind.Argamassa => MaterialHatches.Concrete,
                    _ => null,
                };
                g.Box(0, y - h, w, y, color, pattern, DrawingStyle.HatchInk(color), LinePen.Fina);
                notes.Add((new Point2(w * (0.3 + 0.08 * (notes.Count % 3)), y - h / 2), $"{layer.Name.ToUpperInvariant()}, e = {Cm(layer.ThicknessMm)}"));
            }

            g.Dimension(new Point2(0, y), new Point2(0, y - h), 0.08, Cm(layer.ThicknessMm));
            y -= h;
        }

        // Solo compactado.
        g.Box(0, y - 0.06, w, y, DrawingStyle.Compacted(), MaterialHatches.Compacted, DrawingStyle.HatchInk(DrawingStyle.Compacted()));
        g.Stroke(new Point2(0, y), new Point2(w, y), LinePen.Grossa);
        notes.Add((new Point2(w * 0.8, y - 0.03), "SOLO COMPACTADO"));
        g.Stroke(new Point2(0, 0), new Point2(0, y), LinePen.Media);
        g.Stroke(new Point2(w, 0), new Point2(w, y), LinePen.Media);

        g.Notes(notes, w + 0.12, Math.Max(0.045, -y / Math.Max(4, notes.Count) * 1.15));

        // Amostra da paginação em planta, acima do corte.
        PlanSample(g, type, new Point2(0, 0.10), w, 0.40);
        g.Text(new Point2(0, 0.10 + 0.40 + 0.035), $"PAGINAÇÃO EM PLANTA — {PavingLabels.Of(type.Pattern).ToUpperInvariant()} (amostra 1,00 × 0,40 m)", TextStyle.Subtitulo, -1, 1.0);
        g.Text(new Point2(0, 0.06), "CORTE", TextStyle.Subtitulo, -1, 1.0);

        double bottom = y - 0.13;
        g.Text(new Point2(0, bottom), $"Espessura total {Cm(type.TotalThicknessMm)}" + (type.SlopePercent > 0 ? $" · caimento mínimo {type.SlopePercent.ToString("0.#", Pt)}%" : string.Empty),
            TextStyle.Subtitulo, -1, 0.95);
        bottom -= 0.035;
        if (!string.IsNullOrWhiteSpace(type.Notes))
        {
            foreach (string line in TextLayout.Wrap(type.Notes, 110))
            {
                g.Text(new Point2(0, bottom), line, -1, 0.9);
                bottom -= 0.03;
            }
        }

        g.Text(new Point2(0, bottom - 0.01), "Medidas de referência: confira com o fornecedor, com as normas aplicáveis e com o responsável técnico.", TextStyle.Nota, -1, 0.8);
        return g;
    }

    /// <summary>
    /// Amostra em planta da paginação: o motivo repetido na malha dentro de um retângulo, peças com leve variação de
    /// tom (como o lote real) e a moldura.
    /// </summary>
    private static void PlanSample(Drawing g, PavingType type, Point2 origin, double width, double height)
    {
        PavingMotif motif = PavingPatterns.Motif(type);
        Rgb color = type.SurfaceRgb;
        g.Box(origin.X, origin.Y, origin.X + width, origin.Y + height, PavingLabels.IsGranular(type.Pattern) ? color.Tint(0.35) : color.Shade(0.25));
        double k = 0.001; // mm → m
        Point2 t1 = motif.T1 * k, t2 = motif.T2 * k;
        double det = t1.Cross(t2);
        if (Math.Abs(det) < 1e-12 || (motif.Tiles.Count == 0 && motif.Extra.Count == 0))
        {
            g.Strokes(new List<Point2> { origin, origin + new Point2(width, 0), origin + new Point2(width, height), origin + new Point2(0, height) }, true, LinePen.Grossa);
            return;
        }

        // Índices da malha que cobrem o retângulo.
        var corners = new[] { new Point2(0, 0), new Point2(width, 0), new Point2(width, height), new Point2(0, height) };
        double Si(Point2 p) => p.Cross(t2) / det;
        double Tj(Point2 p) => t1.Cross(p) / det;
        // Folga pelo tamanho do motivo: as peças podem estar longe da origem da malha.
        double reach = motif.Tiles.SelectMany(t => t).Concat(motif.Extra.SelectMany(e => new[] { e.A, e.B })).Select(p => (p * k).Length).DefaultIfEmpty(0).Max();
        int extra = (int)Math.Ceiling(reach / Math.Max(1e-6, Math.Min(t1.Length, t2.Length))) + 2;
        int i0 = (int)Math.Floor(corners.Min(Si)) - extra, i1 = (int)Math.Ceiling(corners.Max(Si)) + extra;
        int j0 = (int)Math.Floor(corners.Min(Tj)) - extra, j1 = (int)Math.Ceiling(corners.Max(Tj)) + extra;
        if ((long)(i1 - i0) * (j1 - j0) * Math.Max(1, motif.Tiles.Count) > 6000)
        {
            g.Strokes(new List<Point2> { origin, origin + new Point2(width, 0), origin + new Point2(width, height), origin + new Point2(0, height) }, true, LinePen.Grossa);
            return;
        }

        var random = new DeterministicRandom(type.Id.Length + 17);
        bool Inside(Point2 p) => p.X >= -1e-9 && p.X <= width + 1e-9 && p.Y >= -1e-9 && p.Y <= height + 1e-9;
        for (int i = i0; i <= i1; i++)
        {
            for (int j = j0; j <= j1; j++)
            {
                Point2 move = t1 * i + t2 * j;
                foreach (IReadOnlyList<Point2> tile in motif.Tiles)
                {
                    // Peças da borda recortadas no limite da amostra, como na obra.
                    List<Point2> points = ClipToRect(tile.Select(p => p * k + move).ToList(), width, height);
                    if (points.Count < 3)
                    {
                        continue;
                    }

                    double shade = random.Range(-0.06, 0.08);
                    g.Fill(points.Select(p => p + origin).ToList(), shade >= 0 ? color.Shade(shade) : color.Tint(-shade));
                    g.Strokes(points.Select(p => p + origin).ToList(), true, LinePen.Fina);
                }

                foreach ((Point2 a, Point2 b) in motif.Extra)
                {
                    Point2 pa = a * k + move, pb = b * k + move;
                    if (Inside(pa) && Inside(pb))
                    {
                        g.Stroke(pa + origin, pb + origin, LinePen.Media);
                    }
                }
            }
        }

        g.Strokes(new List<Point2> { origin, origin + new Point2(width, 0), origin + new Point2(width, height), origin + new Point2(0, height) }, true, LinePen.Grossa);
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

    /// <summary>Recorte de um polígono pelo retângulo [0, w] × [0, h] (Sutherland–Hodgman).</summary>
    private static List<Point2> ClipToRect(List<Point2> polygon, double w, double h)
    {
        List<Point2> Clip(List<Point2> poly, Func<Point2, double> inside)
        {
            var result = new List<Point2>();
            for (int i = 0; i < poly.Count; i++)
            {
                Point2 a = poly[i], b = poly[(i + 1) % poly.Count];
                double da = inside(a), db = inside(b);
                if (da >= 0)
                {
                    result.Add(a);
                }

                if ((da >= 0) != (db >= 0))
                {
                    result.Add(Point2.Lerp(a, b, da / (da - db)));
                }
            }

            return result;
        }

        List<Point2> r = Clip(polygon, p => p.X);
        r = Clip(r, p => w - p.X);
        r = Clip(r, p => p.Y);
        return Clip(r, p => h - p.Y);
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
